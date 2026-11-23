# Examples and consuming the folds

`data.jsonl` contains 12 synthetic English/Persian ticket records, six customer groups and billing/support labels. Each customer has one row of each label. The documented three-fold example gives each validation fold two whole customers, four rows and two rows per class; its training complement has eight rows. `rare.jsonl` demonstrates a label supported by one group and the resulting coverage warnings.

Run the [README commands](../README.md) from the repository root. `plan` writes no files. `split` needs a fresh directory. With `--strict`, `rare.jsonl` returns 1 and creates no outputs. After packing, `pwsh -File examples/Smoke.ps1` verifies the installed CLI and hand-checked artifacts on CI.

## Optional C# text-training workflow

The exported objects preserve `text`/`label` and the extra metadata, so a JSONL loader that ignores extra properties can consume them directly. For example, with the separate [.NET/ML.NET Text Train Kit](https://github.com/alirezaRanjbar4/text-train-kit) v0.3.0 installed:

```sh
groupfold split --input examples/data.jsonl --out-dir artifacts/folds-42 --folds 3 --seed 42 --strict
text-train-kit train --train artifacts/folds-42/train-01.jsonl --validation artifacts/folds-42/validation-01.jsonl --model artifacts/model-01.zip --report artifacts/metrics-01.json
```

That optional trainer is not a GroupFold runtime/test dependency. It fits a traditional multiclass text classifier on CPU, not an LLM. Repeat with fresh model/report paths and the corresponding pair for each fold, fitting the entire learned preprocessing pipeline on that training partition only. Report validation metrics per fold; choose any final refit strategy separately and preserve a truly unseen test set when required by your experiment.

These tiny synthetic texts demonstrate input compatibility and partition behavior. They are not an accuracy benchmark, evidence of improved generalization, or production training data. GroupFold checks supplied entity groups; the downstream trainer has its own text-duplicate/label checks and may reject other datasets. See [research](../docs/RESEARCH.md) for ML.NET's existing grouping features and Python alternatives.
