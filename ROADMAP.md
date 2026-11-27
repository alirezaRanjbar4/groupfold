# Bounded first scope

Scope fixed on 2026-11-23, before initial publication.

- [x] Usable v0.1.0: strict labeled JSONL input, read-only fold planning, deterministic group-preserving/class-aware assignment, coverage/large-group warnings, explicit fresh train/validation exports with hashes/provenance, examples/docs/tests, three-platform CI and verified release.
- [x] Read-only `verify` workflow implemented in source 0.2.0: check the original source and strict saved manifest against a recomputed deterministic plan and every actual artifact byte/hash. Modified-file, rewritten-hash, malformed-manifest and missing-file regressions are included. See [verification](docs/VERIFY.md).
- [ ] Document and validate that verification workflow in a closing release with bundled guides, clean installation/examples, three-platform CI and final-commit/tag/asset checks.

After those items, retain the public repository for bugs/feedback. Additional optimizers, CSV adapters, model training, remote data, temporal splitting, semantic duplicate discovery and resampling are optional, outside this boundary. Required features will not be added indefinitely.

Version 0.1.0 has no verifier; version 0.2.0 includes it. See [research](docs/RESEARCH.md) and [design](docs/DESIGN.md) for the problem, alternatives and limits.
