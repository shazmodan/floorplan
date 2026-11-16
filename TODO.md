# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Analysis

- **Saturn: the OPTIONS route is unresolved.** `forward "/orders" (OPTIONS >=> ...)` comes after a large router and
  is never explored. Not the 8M total cap: one 150k sub-budget on an outer library call bounds the whole request
  run, and raising it does not help (hits grow with the budget: 50, 116, 276 at 150k, 400k, 1M). The cause is
  path explosion in Giraffe's recursive `chooseHttpFunc`: a failed alternative returns several outcomes that are
  not merged, and each re-explores every later alternative (identical `remove` hits 24 times).
  Work in progress on branch `claude/saturn-options-route-budget` (not merged):
  - Done, checked on the four framework fixtures (unchanged results): closures nested under
    `<StartupCode$FSharp-Core>` (FSharp.Core's own `seq { }`, as in `Seq.append`) are no longer opaque. Removes
    437 dropped threads in `GeneratedSequenceBase.MoveNextImpl`.
  - Not yet run: outcomes are merged on their predicates as a set, not as an ordered list.
  - Next suspect if that is not enough: `LastUserObject` differs between equivalent failed outcomes.
  - Then: full test suite and the fsharp-ballast regression run before merging.
- **MVC conventional routing** (`MapControllerRoute`, `MapDefaultControllerRoute`): actions of controllers
  without attribute routes are reported without a route. Also not read: areas (`[area]`), route prefixes from
  application model conventions, and `[Route]` on base controller classes. See `docs/controller-routing.md`.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
- CI: `actions/checkout@v4` and `actions/setup-dotnet@v4` run on Node 20, which GitHub has deprecated.
