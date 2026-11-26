# GroupFold

[![CI](https://github.com/alirezaRanjbar4/groupfold/actions/workflows/ci.yml/badge.svg)](https://github.com/alirezaRanjbar4/groupfold/actions/workflows/ci.yml)

**Keep related records together when preparing cross-validation data.** An offline C#/.NET 10 CLI that previews group-preserving, class-aware folds and explicitly exports ready-to-use train/validation JSONL pairs, hashes and source-line provenance.

Messages from the same customer or thread should not land on both sides of a validation split just because their text differs. Rare classes and large groups also make some fold distributions impossible. GroupFold keeps each supplied group intact, tries to preserve observed class proportions, and reports actual missing-class coverage before you fit a model.

It prepares data; it does not train a model, discover related entities, resample classes or guarantee balanced/optimal folds or better accuracy. See [research and existing tools](docs/RESEARCH.md), including ML.NET's existing group support and Python's StratifiedGroupKFold.

Version 0.2.0 adds read-only verification of stored artifacts against the original source and manifest. The v0.1.0 release does not have `verify`.

## Quick start

Requires the .NET 10 SDK. From a checkout:

```sh
dotnet test -c Release
dotnet run --project src/Cli -- plan --input examples/data.jsonl --folds 3 --seed 42 --strict
dotnet run --project src/Cli -- split --input examples/data.jsonl --out-dir artifacts/folds-42 --folds 3 --seed 42 --strict
dotnet run --project src/Cli -- verify --input examples/data.jsonl --manifest artifacts/folds-42/manifest.json --artifact-dir artifacts/folds-42
```

The synthetic example has 12 records from six customers and two labels. It produces three validation folds, each with four rows (two of each label) and two whole customer groups; each training complement has eight rows. Use a **fresh output directory** for another export. The fixture demonstrates partition behavior, not model quality.

Output files are `train-01.jsonl`, `validation-01.jsonl`, corresponding pairs for each fold, and `manifest.json`. Each row appears in validation exactly once across folds and in training `k-1` times. For fold `i`, fit all learned preprocessing and the model on `train-i` only, then evaluate on `validation-i`. Do not concatenate all validation files to train the pipeline before evaluation.

For a warning example:

```sh
dotnet run --project src/Cli -- plan --input examples/rare.jsonl --folds 3 --strict
```

It exits `1`: a label supported by one group cannot occur in all three validation folds. `split ... --strict` returns that same warning report **without creating output directories/files**. Without `--strict`, warnings are advisory and an explicitly requested split is exported with exit `0`.

## Input contract

One complete UTF-8 JSON object per physical line:

```json
{"id":"ticket-01","group":"customer-01","label":"billing","text":"Please check this invoice","metadata":{"source":"synthetic"}}
```

`id`, `group` and `label` are required nonblank strings with no control characters. IDs are unique. ID/group strings are limited to 256 UTF-16 code units and labels to 128. Identity is exact ordinal text: case, spaces and Unicode forms are preserved. Extra fields, including `text` and nested metadata, are allowed and preserved in exported records. Duplicate JSON properties anywhere in a record, blank lines, invalid JSON/UTF-8, empty inputs and fewer groups than folds are rejected.

Choose the group yourself: customer, thread, source document, device or other entity appropriate for your validation question. The tool cannot detect hidden relationships or text duplicates across different group keys. Groups may contain multiple labels. There is no class up/down-sampling: approximately preserving a 90/10 class distribution does not turn it into 50/50.

See [examples and C# training integration](examples/README.md), [design/limits](docs/DESIGN.md) and [troubleshooting](docs/TROUBLESHOOTING.md).

## Install a release

Packages and `SHA256SUMS.txt` are distributed through [GitHub Releases](https://github.com/alirezaRanjbar4/groupfold/releases), **not NuGet.org**. Once the v0.2.0 assets are available, from a checkout download them into `artifacts/`, compare the package SHA-256 to the checksum file, then install:

```sh
gh release download v0.2.0 --repo alirezaRanjbar4/groupfold --dir artifacts
dotnet tool install GroupFold.Tool --version 0.2.0 --tool-path artifacts/tool --configfile nuget.local.config --no-cache
./artifacts/tool/groupfold --version
```

The GitHub CLI download command is optional; browser downloads work too. Use `Get-FileHash -Algorithm SHA256` in PowerShell or `sha256sum` on Linux. The supplied config reads only the local `artifacts/` feed. Installation needs an SDK; the installed tool runs on the .NET 10 runtime. It has no runtime package dependency, network call, API key or paid-service requirement.

## Reports, warnings and exits

JSON goes to stdout. Reports use `schemaVersion: 1`, assignment algorithm `group-sse-v1`, source byte SHA-256, seed, counts, per-label group support, per-fold training/validation label counts and one-based validation source lines. Export adds relative artifact filenames, byte lengths and SHA-256; the same report is written as `manifest.json` with a final LF. It contains no absolute source/output paths, record IDs, group names or message bodies. **Label names, counts, hashes and source line numbers are present**; review reports before sharing. Dataset exports contain the complete supplied records.

| Code | Warning |
| --- | --- |
| `GF001` | A class occurs in fewer distinct groups than folds; complete validation coverage is impossible under the supplied groups |
| `GF002` | A class is actually absent from a particular training or validation partition |
| `GF003` | An indivisible group is larger than the ideal number of rows per validation fold |

| Exit | Meaning |
| --- | --- |
| `0` | Valid plan/export; warnings are advisory by default |
| `1` | Warnings with `--strict`; report on stdout and no export |
| `2` | Input, option, path or file-operation failure; message on stderr, no report |

Fold count is 2–20 (default 5); seed is any signed 32-bit integer (default 0). Repeated/unknown options are rejected. `plan` is read-only; only `split` accepts/requires `--out-dir`.

In version 0.2.0, `verify` also returns `1` when stored artifacts or manifest content do not match. It returns `0` on a consistent set or `2` for an invalid manifest/options or inaccessible input. No output files are created. Read the [verification contract](docs/VERIFY.md) for precise checks, bounds and privacy limitations.

Inputs are limited to 64 MiB, 100,000 rows and 100 labels; a line is limited to one Mi UTF-16 code units. Combined train/validation dataset outputs are limited to 256 MiB because k-fold pairs duplicate training rows. The full snapshot/group state is kept in memory. Existing destination files or directories are refused; files are created exclusively with best-effort cleanup after failed writes. This is not a multi-file atomic transaction. See [design](docs/DESIGN.md) for the fixed heuristic, resource bounds and filesystem limits.

## Development and scope

[Contributing](CONTRIBUTING.md), [changelog](CHANGELOG.md), [MIT license](LICENSE) and the [bounded roadmap](ROADMAP.md) describe validation and the completion boundary. CI tests, formats, packs, installs and exercises examples on Windows/Linux/macOS.
