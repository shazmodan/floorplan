# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## C# support (Phase 7)

Structure analysis is done; see [docs/csharp-support.md](docs/csharp-support.md).

- C# compiler-output recognizers (`Floorplan.IL.Codegen.CSharp`): string `switch`, string equality, collection
  access, format and interpolation templates, readable lambda names.
- C# entry-point fixtures (minimal APIs, controllers, hand-written routing) with golden files.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
