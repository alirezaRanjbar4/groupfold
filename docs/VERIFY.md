# Stored artifact verification

Available in version 0.2.0; the v0.1.0 package has no `verify`. From a checkout, first run the [quick start](../README.md) to create `artifacts/folds-42`, then:

```sh
dotnet run --project src/Cli -- verify --input examples/data.jsonl --manifest artifacts/folds-42/manifest.json --artifact-dir artifacts/folds-42
```

The command reads the original source, a saved manifest and the artifact directory without creating or changing files. It accepts exactly one of each option. Its JSON report contains source and manifest byte SHA-256, fold count, number of artifacts actually opened, and findings. Exit 0 means all checked content is consistent; 1 means mismatch with findings; 2 means invalid input/manifest/options, unsupported schema/algorithm, resource limit or file access failure, with stderr and no report.

Manifest input is bounded to 16 MiB and schema 1 with algorithm `group-sse-v1`. Every expected field is required; duplicate/unknown fields anywhere, unsafe/reordered/unexpected artifact names, invalid SHA-256/lengths or wrong types are rejected. Expected names are exactly `train-01.jsonl`, `validation-01.jsonl`, etc. Only those filenames are opened within the supplied directory; extra files are ignored. Artifact links/reparse points are rejected. This is not a security sandbox: parents may be links and file paths can change concurrently; do not verify adversarially changing directories.

The original bounded source is parsed and a deterministic plan is recomputed using the manifest's fold count and seed. Its source hash, counts, groups, class support, warnings, folds and source-line assignments must match the manifest. For each fold, expected validation and training complement bytes are independently reconstructed from source rows using that assignment and the export's UTF-8/LF format. Actual files are read and compared byte-for-byte, and their observed lengths/hashes are compared with manifest claims. Changing both a file and its claimed hash still fails. The group and row guarantees follow from the recomputed planner and exact source-byte comparison; this does not independently implement a second assignment algorithm.

Actual artifact reads are capped at 256 MiB combined, source input at 64 MiB, 100,000 rows and 100 labels; see [design](DESIGN.md). Expected bytes for one artifact are held in memory (at most 256 MiB), as is the bounded source/plan. Large but otherwise valid inputs can exceed these fixed limits. Avoid modifying inputs while verification runs; filesystem reads are not a multi-file atomic snapshot.

| Code | Meaning |
| --- | --- |
| `GFV001` | Supplied source hash differs from the manifest |
| `GFV002` | Manifest plan metadata or deterministic assignment differs |
| `GFV003` | Expected artifact is missing or is a link |
| `GFV004` | Artifact content, length or hash differs from source/manifest |

Findings contain no source records, IDs or group names. They contain expected filenames and source/manifest hashes; plan files themselves contain labels, counts and source-line numbers. Review all outputs before sharing. Hashes and this consistency check do **not** establish authenticity, an approved seed, entity independence, absence of hidden leakage or model quality. [Troubleshooting](TROUBLESHOOTING.md) explains common failures.
