# Contributing

Use a small synthetic reproduction and a focused issue or PR. Read the [design](docs/DESIGN.md), [troubleshooting](docs/TROUBLESHOOTING.md) and [bounded roadmap](ROADMAP.md). Do not share private datasets, company code or credentials. Label/group fields are data too.

With the .NET 10 SDK, from the root:

```sh
dotnet restore
dotnet test -c Release --no-restore
dotnet format --verify-no-changes --no-restore
dotnet pack src/Cli -c Release --no-restore -o artifacts
pwsh -File examples/Smoke.ps1
```

The smoke script requires PowerShell 7, available on GitHub-hosted runners. It installs from the local feed and checks preview/export, group separation, hashes, strict no-write gating and packaged commit metadata. Commit the intended source and repack before its final-commit assertion; a package from an earlier commit should fail that assertion.

Tests cover group/class coverage, independent complement checks, metadata/order/encoding preservation, deterministic artifacts across paths/cultures, warnings, resource/input limits and 0/1/2 exits. Add meaningful regressions for changed behavior. Algorithm changes require an explicit version/compatibility decision: persisted assignments should not change silently. Runtime code uses only the .NET BCL and performs no network or model calls.

The source 0.2.0 [verifier](docs/VERIFY.md) also checks stored bytes against independently reconstructed train/validation content; tests include a changed artifact with a rewritten manifest hash, malformed/unsafe manifests, missing artifacts, source changes and no-write behavior. It is not yet present in published 0.1.0.

Update docs/changelog with observable changes. Contributions use the [MIT license](LICENSE). Source grouping/stratification is an engineering aid, not a basis for invented model-quality claims.
