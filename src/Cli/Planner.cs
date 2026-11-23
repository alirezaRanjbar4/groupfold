using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GroupFold;

public sealed class InputException(string message) : Exception(message);
public sealed record Warning(string Code, string Message, string? Label = null, int? Fold = null, string? Partition = null);
public sealed record LabelSummary(string Label, int Rows, int Groups);
public sealed record FoldSummary(int Fold, int ValidationRows, int TrainingRows, int ValidationGroups,
    IReadOnlyDictionary<string, int> ValidationLabels, IReadOnlyDictionary<string, int> TrainingLabels, int[] ValidationSourceLines);
public sealed record Artifact(string Name, string Sha256, long Bytes);
public sealed record Report(int SchemaVersion, string Algorithm, int FoldCount, int Seed, string SourceSha256,
    int SourceRows, int Groups, LabelSummary[] Labels, FoldSummary[] Folds, Warning[] Warnings, Artifact[] Artifacts)
{
    public bool Exported => Artifacts.Length > 0;
}
internal sealed record Row(string Group, string Label, string Raw, int Line);
internal sealed record Group(string Key, Row[] Rows, (int Index, int Count)[] Counts, double Priority, string Rank);
public sealed class PlanResult
{
    internal Row[] Rows { get; }
    internal int[] Assignments { get; }
    public Report Report { get; }
    internal PlanResult(Row[] rows, int[] assignments, Report report) => (Rows, Assignments, Report) = (rows, assignments, report);
}

public static class Planner
{
    public const int MaxInputBytes = 64 * 1024 * 1024;
    public const int MaxLineCharacters = 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static PlanResult Plan(string path, int folds = 5, int seed = 0)
    {
        if (folds is < 2 or > 20) throw new InputException("Fold count must be between 2 and 20.");
        var bytes = ReadSnapshot(path);
        string text;
        try
        {
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException) { throw new InputException("Input must be valid UTF-8 (an optional UTF-8 BOM is accepted)."); }
        var rows = new List<Row>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var labelSet = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            var line = rows.Count + 1;
            if (rows.Count >= 100_000) throw new InputException("Input exceeds the 100,000-row limit.");
            if (raw.Length > MaxLineCharacters) throw new InputException($"Row exceeds the 1 Mi-character limit at line {line}.");
            try
            {
                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
                RejectDuplicates(root);
                var id = Field(root, "id", 256);
                var group = Field(root, "group", 256);
                var label = Field(root, "label", 128);
                if (!ids.Add(id)) throw new InputException($"Repeated record ID at line {line}.");
                labelSet.Add(label);
                if (labelSet.Count > 100) throw new InputException("Input exceeds the 100-label limit.");
                rows.Add(new(group, label, raw, line));
            }
            catch (JsonException) { throw new InputException($"Invalid JSON object or id/group/label fields at line {line}; duplicate properties are prohibited."); }
        }
        if (rows.Count == 0) throw new InputException("Dataset is empty.");
        var labels = labelSet.Order(StringComparer.Ordinal).ToArray();
        var labelIndex = labels.Select((label, index) => (label, index)).ToDictionary(pair => pair.label, pair => pair.index, StringComparer.Ordinal);
        var totals = new int[labels.Length];
        foreach (var row in rows) totals[labelIndex[row.Label]]++;
        var groups = rows.GroupBy(row => row.Group, StringComparer.Ordinal).Select(group =>
        {
            var items = group.ToArray();
            var counts = items.GroupBy(row => labelIndex[row.Label]).OrderBy(g => g.Key).Select(g => (Index: g.Key, Count: g.Count())).ToArray();
            var priority = counts.Max(pair => (double)pair.Count / totals[pair.Index]);
            var rank = Hash(Encoding.UTF8.GetBytes(seed.ToString(CultureInfo.InvariantCulture) + "\n" + group.Key));
            return new Group(group.Key, items, counts, priority, rank);
        }).OrderByDescending(group => group.Priority).ThenByDescending(group => group.Rows.Length)
          .ThenBy(group => group.Rank, StringComparer.Ordinal).ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
        if (groups.Length < folds) throw new InputException("At least one distinct group per fold is required.");
        var countsByFold = new int[folds, labels.Length];
        var rowsByFold = new int[folds];
        var groupsByFold = new int[folds];
        var assignments = new int[rows.Count];
        var targetRows = (double)rows.Count / folds;
        for (var g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            var empty = groupsByFold.Count(count => count == 0);
            var mustFillEmpty = groups.Length - g == empty;
            var chosen = -1;
            var best = double.PositiveInfinity;
            for (var fold = 0; fold < folds; fold++)
            {
                if (mustFillEmpty && groupsByFold[fold] != 0) continue;
                // Change in global squared deviation from per-class and row targets.
                var score = Delta(rowsByFold[fold], group.Rows.Length, targetRows);
                foreach (var pair in group.Counts)
                    score += Delta(countsByFold[fold, pair.Index], pair.Count, (double)totals[pair.Index] / folds);
                if (score < best - 1e-10 || (Math.Abs(score - best) <= 1e-10 &&
                    (chosen < 0 || rowsByFold[fold] < rowsByFold[chosen])))
                { chosen = fold; best = score; }
            }
            rowsByFold[chosen] += group.Rows.Length;
            groupsByFold[chosen]++;
            foreach (var pair in group.Counts) countsByFold[chosen, pair.Index] += pair.Count;
            foreach (var row in group.Rows) assignments[row.Line - 1] = chosen;
        }
        var warnings = new List<Warning>();
        var summaries = new LabelSummary[labels.Length];
        for (var label = 0; label < labels.Length; label++)
        {
            var support = groups.Count(group => group.Counts.Any(pair => pair.Index == label));
            summaries[label] = new(labels[label], totals[label], support);
            if (support < folds) warnings.Add(new("GF001", "Label occurs in fewer groups than folds; validation coverage in every fold is impossible without breaking groups.", labels[label]));
        }
        if (groups.Any(group => group.Rows.Length > targetRows))
            warnings.Add(new("GF003", "An indivisible group exceeds the ideal rows per fold; exact equal-size validation folds are impossible."));
        var foldSummaries = new FoldSummary[folds];
        for (var fold = 0; fold < folds; fold++)
        {
            var validation = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var training = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (var label = 0; label < labels.Length; label++)
            {
                validation[labels[label]] = countsByFold[fold, label];
                training[labels[label]] = totals[label] - countsByFold[fold, label];
                if (validation[labels[label]] == 0) warnings.Add(new("GF002", "Label is absent from this partition; inspect coverage before fitting or interpreting metrics.", labels[label], fold + 1, "validation"));
                if (training[labels[label]] == 0) warnings.Add(new("GF002", "Label is absent from this partition; inspect coverage before fitting or interpreting metrics.", labels[label], fold + 1, "training"));
            }
            foldSummaries[fold] = new(fold + 1, rowsByFold[fold], rows.Count - rowsByFold[fold], groupsByFold[fold], validation, training,
                rows.Where(row => assignments[row.Line - 1] == fold).Select(row => row.Line).ToArray());
        }
        var report = new Report(1, "group-sse-v1", folds, seed, Hash(bytes), rows.Count, groups.Length, summaries, foldSummaries, warnings.ToArray(), []);
        return new(rows.ToArray(), assignments, report);
    }

    private static double Delta(int current, int added, double target) =>
        (2.0 * added * (current - target) + (double)added * added) / (target * target);

    private static string Field(JsonElement root, string name, int limit)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) throw new JsonException();
        var value = element.GetString()!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl)) throw new JsonException();
        return value;
    }

    private static void RejectDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicates(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) RejectDuplicates(child);
    }

    private static byte[] ReadSnapshot(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxInputBytes) throw new InputException("Input exceeds the 64 MiB limit.");
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = file.Read(buffer)) > 0)
        {
            if (memory.Length + read > MaxInputBytes) throw new InputException("Input exceeds the 64 MiB limit.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
