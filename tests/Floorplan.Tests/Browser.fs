/// Headless browser smoke tests (plan section 9): open a rendered site and read back the DOM after scripts ran.
module Floorplan.Tests.Browser

open System
open System.Diagnostics
open System.IO

let private candidates =
    [ Environment.GetEnvironmentVariable "FLOORPLAN_BROWSER"
      @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
      @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
      @"C:\Program Files\Google\Chrome\Application\chrome.exe"
      "/usr/bin/google-chrome"
      "/usr/bin/chromium"
      "/usr/bin/chromium-browser"
      "/usr/bin/microsoft-edge" ]

let find () =
    candidates |> List.tryFind (fun c -> not (String.IsNullOrEmpty c) && File.Exists c)

/// The DOM of the page at file `path` (with an optional #hash) after load.
let dumpDom (browser: string) (path: string) (hash: string) =
    let url = Uri(Path.GetFullPath path).AbsoluteUri + hash
    let profile = Path.Combine(Path.GetTempPath(), "floorplan-browser-" + string (Guid.NewGuid()))

    let info =
        ProcessStartInfo(browser, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)

    for a in
        [ "--headless"
          "--disable-gpu"
          "--no-first-run"
          "--no-sandbox"
          $"--user-data-dir={profile}"
          "--virtual-time-budget=3000"
          "--dump-dom"
          url ] do
        info.ArgumentList.Add a

    use p = Process.Start info
    let output = p.StandardOutput.ReadToEndAsync()
    p.StandardError.ReadToEndAsync() |> ignore

    if not (p.WaitForExit 60000) then
        p.Kill true

    try Directory.Delete(profile, true) with _ -> ()
    output.Result
