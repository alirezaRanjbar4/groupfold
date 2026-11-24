using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GroupFold;

namespace Tests;

public sealed class FoldTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "groupfold-" + Guid.NewGuid().ToString("N"));
    public FoldTests() => Directory.CreateDirectory(root);
    private string Fresh(string name) => Path.Combine(root, name);
    private static string Row(int id, string group, string label = "A", string text = "synthetic") =>
        JsonSerializer.Serialize(new { id = id.ToString(CultureInfo.InvariantCulture), group, label, text, metadata = new { source = "fixture" } });
    private string Input(IEnumerable<string>? lines = null)
    {
        var path = Fresh(Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllText(path, string.Join('\n', lines ?? Balanced()) + "\n", new UTF8Encoding(false));
        return path;
    }
    private static IEnumerable<string> Balanced() => Enumerable.Range(0, 12).Select(i => Row(i, "thread-" + i / 2, i % 2 == 0 ? "A" : "B"));
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string[] Groups(string path) => File.ReadLines(path).Select(line =>
    {
        using var json = JsonDocument.Parse(line);
        return json.RootElement.GetProperty("group").GetString()!;
    }).Distinct(StringComparer.Ordinal).ToArray();

    [Fact]
    public void PreviewPreservesSourceAndHasHandCheckedBalancedCoverage()
    {
        var source = Input(); var before = Hash(source);
        var report = Planner.Plan(source, 3, 42).Report;
        Assert.Equal(before, report.SourceSha256);
        Assert.Equal(12, report.SourceRows); Assert.Equal(6, report.Groups);
        Assert.False(report.Exported); Assert.Empty(report.Artifacts); Assert.Empty(report.Warnings);
        Assert.All(report.Folds, fold =>
        {
            Assert.Equal(4, fold.ValidationRows); Assert.Equal(8, fold.TrainingRows);
            Assert.Equal(2, fold.ValidationGroups);
            Assert.Equal(2, fold.ValidationLabels["A"]); Assert.Equal(2, fold.ValidationLabels["B"]);
        });
        Assert.Equal(Enumerable.Range(1, 12), report.Folds.SelectMany(fold => fold.ValidationSourceLines).Order());
        Assert.Equal(before, Hash(source)); Assert.Single(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void ExportedPairsHaveNoGroupOverlapAndValidateEveryRowExactlyOnce()
    {
        var source = Input(); var before = Hash(source);
        var plan = Planner.Plan(source, 3, 42);
        var output = Fresh("folds"); var report = Exporter.Write(plan, output);
        var original = File.ReadAllLines(source);
        var validations = new List<string>();
        for (var fold = 1; fold <= 3; fold++)
        {
            var training = Path.Combine(output, $"train-{fold:00}.jsonl");
            var validation = Path.Combine(output, $"validation-{fold:00}.jsonl");
            Assert.Empty(Groups(training).Intersect(Groups(validation), StringComparer.Ordinal));
            Assert.Equal(12, File.ReadAllLines(training).Length + File.ReadAllLines(validation).Length);
            validations.AddRange(File.ReadAllLines(validation));
            Assert.Equal(report.Folds[fold - 1].ValidationSourceLines.Select(line => original[line - 1]), File.ReadAllLines(validation));
        }
        Assert.Equal(original.Order(), validations.Order());
        Assert.True(report.Exported); Assert.Equal(6, report.Artifacts.Length);
        Assert.Equal(7, Directory.GetFiles(output).Length);
        foreach (var artifact in report.Artifacts)
        {
            var path = Path.Combine(output, artifact.Name);
            Assert.Equal(Hash(path), artifact.Sha256); Assert.Equal(new FileInfo(path).Length, artifact.Bytes);
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            Assert.DoesNotContain((byte)13, bytes); Assert.Equal(10, bytes[^1]);
        }
        Assert.Equal(JsonSerializer.Serialize(report, Planner.JsonOptions), File.ReadAllText(Path.Combine(output, "manifest.json")).TrimEnd('\n'));
        Assert.Equal(before, Hash(source));
    }

    [Fact]
    public void SameSeedAndBytesProduceIdenticalArtifactsAcrossPathsAndCultures()
    {
        var source = Input(); var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = Exporter.Write(Planner.Plan(source, 3, -42), Fresh("first"));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            var second = Exporter.Write(Planner.Plan(source, 3, -42), Fresh("second"));
            Assert.Equal(JsonSerializer.Serialize(first, Planner.JsonOptions), JsonSerializer.Serialize(second, Planner.JsonOptions));
            foreach (var name in Directory.GetFiles(Fresh("first")).Select(Path.GetFileName))
                Assert.Equal(File.ReadAllBytes(Path.Combine(Fresh("first"), name!)), File.ReadAllBytes(Path.Combine(Fresh("second"), name!)));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void GroupAssignmentDoesNotDependOnInputOrdering()
    {
        var lines = Balanced().ToArray();
        var a = Planner.Plan(Input(lines), 3, 42).Report;
        var b = Planner.Plan(Input(lines.Reverse()), 3, 42).Report;
        static Dictionary<string, int> Mapping(Report report, string[] input) => report.Folds
            .SelectMany(fold => fold.ValidationSourceLines.Select(line => (Raw: input[line - 1], fold.Fold)))
            .ToDictionary(row => row.Raw, row => row.Fold, StringComparer.Ordinal);
        var first = Mapping(a, lines); var second = Mapping(b, lines.Reverse().ToArray());
        Assert.All(first, row => Assert.Equal(row.Value, second[row.Key]));
    }

    [Fact]
    public void SeedAffectsTiedGroupAssignments()
    {
        var source = Input(Enumerable.Range(0, 40).Select(i => Row(i, "g" + i)));
        var signatures = Enumerable.Range(0, 4).Select(seed => string.Join(',', Planner.Plan(source, 3, seed).Report.Folds[0].ValidationSourceLines));
        Assert.True(signatures.Distinct().Count() > 1);
    }

    [Fact]
    public void RareLabelWarningsDescribeBothValidationAndTrainingCoverage()
    {
        var report = Planner.Plan(Input([Row(1, "one", "rare"), Row(2, "two"), Row(3, "three"), Row(4, "four")]), 3).Report;
        Assert.Contains(report.Warnings, row => row.Code == "GF001" && row.Label == "rare");
        Assert.Contains(report.Warnings, row => row.Code == "GF002" && row.Label == "rare" && row.Partition == "training");
        Assert.Contains(report.Warnings, row => row.Code == "GF002" && row.Label == "rare" && row.Partition == "validation");
    }

    [Fact]
    public void LargeMixedLabelGroupIsNeverSplitToImproveBalance()
    {
        var lines = Enumerable.Range(0, 7).Select(i => Row(i, "large", i % 2 == 0 ? "A" : "B"))
            .Concat([Row(7, "small-1"), Row(8, "small-2", "B")]).ToArray();
        var report = Planner.Plan(Input(lines), 3).Report;
        Assert.Contains(report.Warnings, row => row.Code == "GF003");
        Assert.Contains(report.Folds, fold => Enumerable.Range(1, 7).All(fold.ValidationSourceLines.Contains));
        Assert.All(report.Folds, fold => Assert.True(fold.ValidationRows > 0 && fold.TrainingRows > 0));
    }

    [Fact]
    public void StrictWarningsBlockExportBeforeCreatingDirectories()
    {
        var source = Input([Row(1, "one", "rare"), Row(2, "two"), Row(3, "three")]);
        var output = Fresh("blocked");
        var result = Run(["split", "--input", source, "--out-dir", output, "--folds", "3", "--strict"]);
        Assert.Equal(1, result.Code); Assert.Empty(result.Error); Assert.False(Directory.Exists(output));
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("exported").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("artifacts").GetArrayLength());
        Assert.Equal(0, Run(["split", "--input", source, "--out-dir", Fresh("advisory"), "--folds", "3"]).Code);
    }

    [Fact]
    public void ExistingFilesAndDirectoriesArePreserved()
    {
        var source = Input(); var directory = Fresh("existing"); Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, "sentinel.txt"); File.WriteAllText(sentinel, "keep");
        Assert.Throws<InputException>(() => Exporter.Write(Planner.Plan(source, 3), directory));
        Assert.Equal("keep", File.ReadAllText(sentinel)); Assert.Single(Directory.GetFiles(directory));
        Assert.Throws<InputException>(() => Exporter.Write(Planner.Plan(source, 3), sentinel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(21)]
    public void FoldLimitsAreEnforced(int count) => Assert.Throws<InputException>(() => Planner.Plan(Input(), count));

    [Fact]
    public void NumberOfGroupsRatherThanRowsBoundsFoldCount()
    {
        var source = Input(Enumerable.Range(0, 12).Select(i => Row(i, "same")));
        Assert.Throws<InputException>(() => Planner.Plan(source, 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"id\":\"1\",\"group\":\"g\"}")]
    [InlineData("{\"id\":1,\"group\":\"g\",\"label\":\"A\"}")]
    [InlineData("{\"id\":\"1\",\"group\":\" \",\"label\":\"A\"}")]
    [InlineData("{\"id\":\"1\",\"group\":\"g\",\"label\":\"A\",\"label\":\"B\"}")]
    [InlineData("{\"id\":\"1\",\"group\":\"g\",\"label\":\"A\",\"meta\":[{\"x\":1,\"x\":2}]}")]
    [InlineData("{\"id\":\"1\",\"group\":\"g\\n\",\"label\":\"A\"}")]
    public void InvalidRowsAreRejectedWithoutEchoingTheirValues(string bad)
    {
        var exception = Assert.Throws<InputException>(() => Planner.Plan(Input([bad, Row(2, "two")]), 2));
        Assert.Contains("line 1", exception.Message);
    }

    [Fact]
    public void DuplicateIdsAndEmptyInputAreRejected()
    {
        Assert.Contains("line 2", Assert.Throws<InputException>(() => Planner.Plan(Input([Row(1, "g1"), Row(1, "g2")]), 2)).Message);
        var empty = Fresh("empty.jsonl"); File.WriteAllText(empty, "");
        Assert.Throws<InputException>(() => Planner.Plan(empty, 2));
    }

    [Fact]
    public void IdentityUsesExactCaseAndUnicodeWithoutSilentNormalization()
    {
        var source = Input([Row(1, "گروه", "الف"), Row(2, "گروه ", "الف"), Row(3, "G", "A"), Row(4, "g", "a")]);
        var report = Planner.Plan(source, 2).Report;
        Assert.Equal(4, report.Groups); Assert.Equal(3, report.Labels.Length);
    }

    [Fact]
    public void Utf8BomAndCrLfAreAcceptedWhileMalformedUtf8IsRejected()
    {
        var bom = Fresh("bom.jsonl"); File.WriteAllText(bom, string.Join("\r\n", Balanced()) + "\r\n", new UTF8Encoding(true));
        Assert.Equal(Hash(bom), Planner.Plan(bom, 3).Report.SourceSha256);
        var invalid = Fresh("bad.jsonl"); File.WriteAllBytes(invalid, [0xff, 0xfe]);
        Assert.Throws<InputException>(() => Planner.Plan(invalid, 2));
    }

    [Fact]
    public void ResourceBoundsFailBeforeExport()
    {
        var large = Fresh("large.jsonl");
        using (var file = File.Create(large)) file.SetLength(Planner.MaxInputBytes + 1L);
        Assert.Throws<InputException>(() => Planner.Plan(large, 2));
        Assert.Throws<InputException>(() => Planner.Plan(Input([new string(' ', Planner.MaxLineCharacters + 1)]), 2));
        Assert.Throws<InputException>(() => Planner.Plan(Input(Enumerable.Range(0, 101).Select(i => Row(i, "g" + i, "label" + i))), 2));
    }

    [Fact]
    public void ReportsOmitPrivateTextRecordIdsAndGroupNames()
    {
        var source = Input(Enumerable.Range(0, 6).Select(i => Row(i + 918230, "private-group-" + i, "A", "private-message-body")));
        var json = JsonSerializer.Serialize(Planner.Plan(source, 3).Report, Planner.JsonOptions);
        Assert.DoesNotContain("private-message-body", json); Assert.DoesNotContain("private-group", json); Assert.DoesNotContain("918230", json);
        Assert.DoesNotContain(Path.GetFileName(source), json);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--folds")]
    [InlineData("--folds nope")]
    [InlineData("--seed 2147483648")]
    [InlineData("--strict --strict")]
    [InlineData("--folds 3 --folds 3")]
    [InlineData("--out-dir unwanted")]
    public void InvalidCliOptionsReturnTwoWithNoJson(string options)
    {
        var result = Run(new[] { "plan", "--input", Input() }.Concat(options.Split(' ')).ToArray());
        Assert.Equal(2, result.Code); Assert.Empty(result.Output); Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void CliHelpVersionMissingFileAndSuccessContracts()
    {
        Assert.Equal(0, Run(["--help"]).Code); Assert.Equal("0.2.0", Run(["--version"]).Output.Trim());
        Assert.Equal(2, Run([]).Code); Assert.Equal(2, Run(["plan"]).Code);
        Assert.Equal(2, Run(["plan", "--input", Fresh("missing")]).Code);
        Assert.Equal(2, Run(["split", "--input", Input()]).Code);
        var result = Run(["plan", "--input", Input(), "--folds", "3", "--seed", "-42", "--strict"]);
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("group-sse-v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal(-42, json.RootElement.GetProperty("seed").GetInt32());
    }

    private static (int Code, string Output, string Error) Run(string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var code = Application.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }
    public void Dispose() => Directory.Delete(root, recursive: true);
}
