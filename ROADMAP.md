# Bounded first scope

Scope fixed on 2026-11-23, before initial publication.

- [ ] Usable v0.1.0: strict labeled JSONL input, read-only fold planning, deterministic group-preserving/class-aware assignment, coverage/large-group warnings, explicit fresh train/validation exports with hashes/provenance, examples/docs/tests, three-platform CI and verified release.
- [ ] Independent `verify` workflow for stored artifacts: given the original input and saved manifest/output directory, verify source/algorithm/schema, complete row coverage, group separation, declared counts and actual file hashes. Include modified-file, malformed-manifest and missing-file regressions. It must be read-only and must not trust hash claims as an authenticity proof.
- [ ] Document and validate that verification workflow in a closing release with bundled guides, clean installation/examples, three-platform CI and final-commit/tag/asset checks.

After those items, retain the public repository for bugs/feedback. Additional optimizers, CSV adapters, model training, remote data, temporal splitting, semantic duplicate discovery and resampling are optional, outside this boundary. Required features will not be added indefinitely.

The initial CLI is useful without the planned artifact verifier. The verifier is not available in 0.1.0. See [research](docs/RESEARCH.md) and [design](docs/DESIGN.md) for the problem, alternatives and limits.
