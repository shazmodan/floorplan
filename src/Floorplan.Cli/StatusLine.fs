/// Progress on the console: every step reported is a line with the time since the start, and while a step runs,
/// a spinner line under it says how long it has taken so far. The spinner is drawn only on a terminal; when
/// stderr goes to a file or a pipe, the lines alone are written.
module Floorplan.Cli.StatusLine

open System
open System.Diagnostics
open System.Threading

type Display(quiet: bool) =
    let clock = Stopwatch.StartNew()
    let interactive = not quiet && not Console.IsErrorRedirected
    let gate = obj ()

    // Braille frames where the console writes UTF-8, else ASCII ones every console can show.
    let frames =
        if Console.OutputEncoding.CodePage = 65001 then
            [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]
        else
            [| "|"; "/"; "-"; "\\" |]

    let mutable frame = 0
    let mutable current = ""
    let mutable since = TimeSpan.Zero
    // How many characters of the spinner line are on the screen.
    let mutable shown = 0

    let clear () =
        if shown > 0 then
            eprintf "\r%s\r" (String(' ', shown))
            shown <- 0

    let draw () =
        if interactive && current <> "" then
            let width =
                try
                    max 20 (Console.WindowWidth - 1)
                with _ ->
                    79

            let text = sprintf "%s %s (%.0fs)" frames[frame] current (clock.Elapsed - since).TotalSeconds
            // A line that wraps cannot be redrawn in place.
            let text = if text.Length > width then text.Substring(0, width - 1) + "…" else text
            eprintf "\r%s%s" text (String(' ', max 0 (shown - text.Length)))
            shown <- text.Length

    let timer =
        new Timer(
            (fun _ ->
                lock gate (fun () ->
                    frame <- (frame + 1) % frames.Length
                    draw ())),
            null,
            100,
            100
        )

    /// A step that is starting: a line of its own, and the step the spinner shows from now on.
    member _.Report(message: string) =
        if not quiet then
            lock gate (fun () ->
                clear ()
                eprintfn "[%5.1fs] %s" clock.Elapsed.TotalSeconds message
                current <- message
                since <- clock.Elapsed
                draw ())

    /// Removes the spinner line, before the final output.
    member _.Finish() =
        lock gate (fun () ->
            timer.Change(Timeout.Infinite, Timeout.Infinite) |> ignore
            clear ()
            current <- "")

    interface IDisposable with
        member this.Dispose() =
            this.Finish()
            timer.Dispose()
