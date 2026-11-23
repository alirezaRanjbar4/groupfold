# Changelog

## 0.1.0 — 2026-11-23

- Preview deterministic whole-group/class-aware cross-validation assignments for labeled JSONL.
- Export explicit fresh train/validation pairs, dataset SHA-256/lengths and source-line provenance while preserving original records/metadata.
- Report rare-class, missing partition coverage and oversized-group warnings; strict warning gates create no output.
- Validate bounded UTF-8 JSONL, unique IDs, recursive duplicate properties and exact group/label identity.
- Include positive/negative synthetic examples, tests, design/research/troubleshooting guides and three-platform installed-tool CI.

The independent stored-artifact verifier in the [roadmap](ROADMAP.md) is future work, not part of this release. No model training, class resampling or optimal-balance guarantee is provided.
