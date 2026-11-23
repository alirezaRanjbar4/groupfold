# Problem and alternatives

Reviewed on 2026-11-23. In cross-validation, records from one person, conversation thread, device or other related entity can leak across train/validation if rows are split independently. Keeping groups intact can also leave rare classes absent from a fold. The practical goal here is explicit reusable fold artifacts, approximate preservation of observed class proportions, and visible coverage limits in a small offline .NET tool.

Primary sources:

- [ML.NET CrossValidationSplit](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.dataoperationscatalog.crossvalidationsplit) already supports a sampling key that keeps related rows together. This tool does not claim to introduce group preservation to ML.NET.
- [ML.NET's cookbook](https://github.com/dotnet/machinelearning/blob/main/docs/code/MlNetCookBook.md) describes group leakage and fitting learned preprocessing within each training fold.
- [ML.NET issue #4082](https://github.com/dotnet/machinelearning/issues/4082) requests stratification, distinct from grouping; it was open at review. [Issue #4396](https://github.com/dotnet/machinelearning/issues/4396) also clarifies that preserving a 90/10 distribution is different from resampling to 50/50.
- [scikit-learn StratifiedGroupKFold](https://scikit-learn.org/stable/modules/generated/sklearn.model_selection.StratifiedGroupKFold.html) already combines non-overlapping groups and approximate class stratification in Python, with limitations for large indivisible groups. Use it if that fits your stack.

The inferred useful niche is a .NET-only JSONL command with preview, ready-to-consume train/validation files, hashes/source-line provenance, coverage warnings and strict no-write gating. This is not a uniqueness claim or an ML.NET API replacement. The implementation is an independently written transparent greedy squared-deviation heuristic, not a port or behavioral clone of scikit-learn.

Class proportions are preserved approximately. No up/down-sampling, class equalization, optimality or improved model accuracy is promised. Source grouping must be chosen by the caller; shared entities not represented by `group` remain outside the guarantee. See the [fixed roadmap](../ROADMAP.md).
