# Troubleshooting

## Strict mode returns 1 and creates no output

This is a quality gate. Inspect `warnings` in the JSON plan. `GF001` means a label spans fewer groups than folds; `GF002` identifies actual missing labels in a training/validation partition; `GF003` means a group exceeds the ideal rows per fold. Reduce fold count, obtain more independent groups or review whether this validation design matches your task. Do not split related groups just to hide a warning. Without `--strict`, warnings are advisory and an explicit split exports the reviewed assignment with exit 0.

## Distribution is not 50/50 or not exactly preserved

Stratification aims to retain the observed distribution approximately; it is not over/under-sampling. Large mixed-label groups and sparse classes constrain what is possible. The greedy objective is not globally optimal and a warning-free plan does not establish independence or model validity. Read per-fold counts in the report. [Design](DESIGN.md) defines the objective and tie rules.

## Exit 2, malformed records or existing paths

Read stderr. Each nonblank line must be one complete JSON object with nonblank string `id`, `group` and `label`. IDs must be unique. Duplicate properties anywhere in a record are prohibited. Check field lengths, UTF-8, limits and at least one distinct group per requested fold. No partially valid subset is exported.

Use a fresh `--out-dir`; an existing directory is refused even if empty. Avoid redirecting stdout to a source file or dataset destination: shell redirection happens before the tool reads input. After an I/O failure, inspect the destination; cleanup of newly created files is best effort and directories may remain. Existing data is preserved by exclusive file creation.

## Hashes differ or records look reordered

Source hashes describe exact original bytes. Output uses UTF-8 without BOM and LF, so a BOM/CRLF source will have a different output hash. Every individual output retains original relative row order. Validation assignments are group-based, so files are not necessarily contiguous source chunks. Compare `validationSourceLines` and the artifact hashes in `manifest.json`.

The same bytes/seed/fold count/algorithm version reproduce the same assignment. Preview and export are separate invocations with their own snapshots; compare `sourceSha256` if input can change. No `verify` command is available in 0.1.0; the read-only stored-artifact verifier is [planned](../ROADMAP.md).

## Data still leaks or a classifier rejects it

Groups are caller-supplied, exact strings. Different customer-key spelling, case, whitespace or Unicode form denotes a different group. Text duplicates or shared entities across different keys are not detected. Validate your grouping policy separately. The optional `text` field is not interpreted by GroupFold; a downstream model tool may impose text, label, duplicate or provider constraints.

Train every learned preprocessor on each training fold only. A fold partition is not a model and does not guarantee provider acceptance or high scores. These fixtures are synthetic demonstrations; see [examples](../examples/README.md).

## Install or report a bug

Use the .NET 10 SDK, place the GitHub Release nupkg in `artifacts/` and use the local-only config from the [README](../README.md). Missing assets are not fetched from NuGet.org by that config. The executable is `groupfold.exe` on Windows and `groupfold` on Linux/macOS.

For a bug, include tool/runtime version, command, exit code, expected result and a minimal synthetic case. Reports contain label names and source line numbers; review before sharing. Never attach private datasets. See [contributing](../CONTRIBUTING.md).
