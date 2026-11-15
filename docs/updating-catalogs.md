# Updating the boundary catalog for a new .NET version

.NET ships one major version per year in November. Floorplan handles versions it does not know yet by using the
newest catalog and saying so (plan section 8.2), so a catalog update is never urgent, but it should follow each
release. This is the procedure of plan section 8.3, as it was carried out for .NET 11 on 2026-11-15.

## 1. Bump the compiler service

Update `FSharp.Compiler.Service`, `FSharp.Core` and `Ionide.ProjInfo` in `Directory.Packages.props` to the
versions that understand the new F# language version, and run the tests.

## 2. Diff the reference packs

```
dotnet run --project tools/CatalogDiff -- --old 10.0.15 --new 11.0.0 --out docs/catalog-diffs/net10.0-net11.0.md
```

`--old` and `--new` are pack versions of `Microsoft.NETCore.App.Ref` and `Microsoft.AspNetCore.App.Ref`; the tool
uses the installed SDK's packs folder when it has them and downloads them from NuGet otherwise. `--old-dir` and
`--new-dir` take reference folders directly.

The report lists every public member added or removed in the watched namespaces (`System.Net.*`, `System.IO.*`,
`System.Data.*`, `System.Diagnostics`, `Microsoft.AspNetCore.Http.*`, `Microsoft.AspNetCore.Routing.*`,
`Microsoft.AspNetCore.Builder`) and marks the ones a catalog rule already covers.

## 3. Write the delta catalog

Review the uncovered members. Add a rule for each side-effect, request source or handler registration to
`src/Floorplan.Catalog/drafts/net<version>.json`, which `extends` the previous catalog. A rule with the id of an
inherited rule replaces it, which is how a member list is extended. Most new API is not a primitive: types and
options, compression codecs, diagnostics listeners.

Check the draft with the same diff:

```
dotnet run --project tools/CatalogDiff -- --old 10.0.15 --new 11.0.0 --catalog-file src/Floorplan.Catalog/drafts/net11.0.json
```

The review can also reveal gaps that predate the new version. For .NET 11 it found that zip archives on disk
(`ZipFile`, `ZipFileExtensions`) were never covered; those rules went into `net8.0.json`.

## 4. Add the version to the test matrix

Add the new target framework to the fixtures and the CI matrix (`.github/workflows/ci.yml`), and fix any codegen
recognizer tests that fail because the compiler emits something differently.

## 5. Ship

Move the reviewed draft from `drafts/` to `catalogs/`, where it is embedded and chosen for projects that target
that version, and release.

## Status

| Catalog | State |
|---|---|
| net8.0 | Built in, full catalog |
| net10.0 | Built in, delta on net8.0 |
| net11.0 | Draft, delta on net10.0; report in `docs/catalog-diffs/net10.0-net11.0.md` |
