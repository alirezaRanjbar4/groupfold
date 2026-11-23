# Design and limits

## Dataset snapshot and identity

`Planner` reads one bounded byte snapshot from a read-only handle, hashes it, decodes strict UTF-8 (optional UTF-8 BOM), and validates every physical JSONL record before producing a plan. The JSON parser has its default maximum nesting depth of 64. Duplicate properties are rejected recursively, including in extra metadata. Required ID/group/label strings are checked without trimming, normalization or case folding. Record IDs must be unique. Group equality and class equality are ordinal.

Original JSON record strings are retained. Output preserves their order and text, including extra metadata and whitespace, while normalizing separators to LF with a final LF and UTF-8 without BOM. Therefore source/output byte hashes can differ without changing record content. Source-line provenance refers to the original physical lines, starting at 1.

## Assignment algorithm `group-sse-v1`

This is an independently written greedy heuristic, not scikit-learn SGKF parity. Let `N` be rows, `k` folds, and `N_c` the observed number of rows for class `c`. Targets for each validation fold are `N/k` total rows and `N_c/k` rows per class. Minimize the **change** in the sum of squared deviations normalized by each target squared, including one row-count term and one term per class. No label is given an externally supplied target proportion, and rows are neither copied nor removed to rebalance classes.

1. Make indivisible groups with their class histograms.
2. Sort groups by decreasing maximum `groupClassRows / allClassRows`, then decreasing row count, then ordinal SHA-256 rank of invariant decimal seed, LF and the exact group key; use group key as the final collision tie breaker. Control characters in group keys are prohibited.
3. Greedily place each group in the validation fold with the lowest objective increase. Scores within `1e-10` tie; choose lower current row count, then lower fold number. Iterate class indices in ordinal label order.
4. When the remaining groups equal the number of empty folds, restrict candidates to empty folds. This ensures each validation fold is nonempty whenever distinct groups are at least `k`. Each training complement is also nonempty for `k >= 2`.
5. Report actual class coverage in both training and validation, and necessary impossibility warnings from class group support and oversized groups. Training is the complement of that validation fold.

The heuristic can miss feasible class coverage or a better-balanced partition. `GF001` establishes a necessary group-support limitation; its absence does not prove a valid stratification exists. `GF002` reports what this assignment actually produced. Read counts and warnings rather than treating an exit 0 as statistical validation. Seed affects tied group ordering; it need not change every dataset's assignment.

Same source bytes, seed, fold count and algorithm version produce the same plan and output bytes across destination paths. The manifest has no timestamp, absolute path or machine identity. Hashes establish byte equality, not authenticity. Do not modify inputs concurrently; the read handle/snapshot is not a cross-platform filesystem snapshot guarantee.

## Explicit export and file failure behavior

`Application` validates options and requests a plan. With strict warnings, it emits that plan and exits 1 before invoking `Exporter`. Otherwise `split` alone writes files. Invalid input never produces a partial dataset.

`Exporter` checks the combined dataset byte limit before making directories. A destination must not already exist. Every dataset/manifest file is opened with `FileMode.CreateNew`; already-existing files are never overwritten. On an exception, only files successfully created by that invocation are removed, best effort. Parent/output directories can remain. Directory preflight and multi-file writes are not atomic; concurrent directory creation, permission changes or filesystems can still cause failure. There is no general symlink-isolation/security guarantee.

Each dataset file is hashed from its open stream after flushing. The manifest/console report lists the `2k` dataset artifacts; it does not include a self-referential hash for `manifest.json`. Every original row occurs once across validation files and `k-1` times across training files. Combining training sets from different folds defeats the intended holdout separation.

## Bounds and privacy

- Input bytes: 64 MiB, checked before and during reading.
- Rows: 100,000; distinct labels: 100; folds: 2–20.
- Line length: 1,048,576 UTF-16 code units. String limits: ID/group 256, label 128, no controls.
- Combined dataset output bytes: 256 MiB, excluding the manifest. Full snapshot, strings, indexes and serialized report consume additional memory.

These are application bounds, not a hostile-input sandbox or a guaranteed peak memory cap. Reports expose label names and aggregate counts, hashes and row positions. They omit message content, IDs and group names. Exported datasets retain all source data, including arbitrary extra fields; keep them private when the input is private.

Only the supplied group key defines independence. Hidden entity relations, temporal order, semantic/text duplication, privacy detection, model fitting and model-quality claims are outside scope. Fit learned preprocessing on each training partition only. See [research](RESEARCH.md), [troubleshooting](TROUBLESHOOTING.md) and [the roadmap](../ROADMAP.md).
