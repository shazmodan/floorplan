Throwaway Phase 0 spike code. Not part of the product. See `docs/phase-0-spike-findings.md`.

    dotnet build RouterSample && dotnet build IlProbe
    dotnet IlProbe/bin/Debug/net10.0/IlProbe.dll RouterSample/bin/Debug/net8.0/RouterSample.dll "Router::route"
