using System.Globalization;
using System.Text.Json;

namespace GroupFold;

public static class Application
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["--version"]) { output.WriteLine("0.1.0"); return 0; }
        if (args is ["--help"] or ["-h"])
        {
            output.WriteLine("groupfold plan --input data.jsonl [--folds 5] [--seed 0] [--strict]\ngroupfold split --input data.jsonl --out-dir fresh/path [--folds 5] [--seed 0] [--strict]\nRows require nonblank string id/group/label; optional fields are preserved. Group-preserving class-aware heuristic; no model is trained. JSON stdout. Exit 0 success, 1 warnings with --strict (no export), 2 input/argument/file error.");
            return 0;
        }
        try
        {
            if (args.Length == 0 || args[0] is not ("plan" or "split")) throw new InputException("Use plan or split; see --help.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var strict = false;
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                if (key == "--strict") { if (strict) throw new InputException("Repeated option."); strict = true; continue; }
                if (key is not ("--input" or "--out-dir" or "--folds" or "--seed")) throw new InputException("Unknown option; see --help.");
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal)) throw new InputException("Option needs a nonblank value.");
                if (!options.TryAdd(key, args[i])) throw new InputException("Repeated option.");
            }
            if (!options.TryGetValue("--input", out var input)) throw new InputException("--input is required.");
            var hasOutput = options.TryGetValue("--out-dir", out var destination);
            if ((args[0] == "split") != hasOutput) throw new InputException("Only split requires and accepts --out-dir.");
            int Number(string key, int fallback) => !options.TryGetValue(key, out var value) ? fallback :
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new InputException("Fold count and seed must be 32-bit integers.");
            var plan = Planner.Plan(input, Number("--folds", 5), Number("--seed", 0));
            var blocked = strict && plan.Report.Warnings.Length > 0;
            var report = args[0] == "split" && !blocked ? Exporter.Write(plan, destination!) : plan.Report;
            output.WriteLine(JsonSerializer.Serialize(report, Planner.JsonOptions));
            return blocked ? 1 : 0;
        }
        catch (InputException exception) { error.WriteLine(exception.Message); return 2; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { error.WriteLine("Cannot read input or create fresh outputs. Check paths, access and encoding."); return 2; }
    }
}
