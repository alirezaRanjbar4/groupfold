# Changelog

## 0.2.0

- Add read-only `verify` for a saved manifest and artifacts against original input and a recomputed `group-sse-v1` plan. It validates strict manifest structure and expected filenames, then checks actual bytes, lengths and hashes against the source assignment, including modified files with rewritten manifest hashes.
- Report stable `GFV001`–`GFV004` findings with 0/1/2 exits; include manifest byte hash, bounded processing, privacy limits, tests and installed examples. See [verification](docs/VERIFY.md).

## 0.1.0 — 2026-11-23

- Preview deterministic whole-group/class-aware cross-validation assignments for labeled JSONL.
- Export explicit fresh train/validation pairs, dataset SHA-256/lengths and source-line provenance while preserving original records/metadata.
- Report rare-class, missing partition coverage and oversized-group warnings; strict warning gates create no output.
- Validate bounded UTF-8 JSONL, unique IDs, recursive duplicate properties and exact group/label identity.
- Include positive/negative synthetic examples, tests, design/research/troubleshooting guides and three-platform installed-tool CI.

The independent stored-artifact verifier in the [roadmap](ROADMAP.md) is future work, not part of this release. No model training, class resampling or optimal-balance guarantee is provided.
