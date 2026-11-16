# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Analysis

- **MVC conventional routing** (`MapControllerRoute`, `MapDefaultControllerRoute`): actions of controllers
  without attribute routes are reported without a route. Also not read: areas (`[area]`), route prefixes from
  application model conventions, and `[Route]` on base controller classes. See `docs/controller-routing.md`.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
- CI: `actions/checkout@v4` and `actions/setup-dotnet@v4` run on Node 20, which GitHub has deprecated.
