using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GroupFold;

namespace Tests;

public sealed class VerificationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "groupfold-verify-" + Guid.NewGuid().ToString("N"));
    public VerificationTests() => Directory.CreateDirectory(root);
    private (string Source, string Directory, string Manifest) Fixture()
    {
        var source = Path.Combine(root, Guid.NewGuid() + ".jsonl");
        var records = Enumerable.Range(0, 12).Select(i => JsonSerializer.Serialize(new { id = "private-id-" + i, group = "private-group-" + (i / 2), label = i % 2 == 0 ? "A" : "B", message = "private-message" }));
        File.WriteAllText(source, string.Join('\n', records) + "\n", new UTF8Encoding(false));
        var output = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Exporter.Write(Planner.Plan(source, 3, 42), output);
        return (source, output, Path.Combine(output, "manifest.json"));
    }
    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var stdout = new StringWriter(); using var stderr = new StringWriter();
        var code = Application.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string[] Arguments((string Source, string Directory, string Manifest) f) => ["verify", "--input", f.Source, "--manifest", f.Manifest, "--artifact-dir", f.Directory];

    [Fact]
    public void ValidArtifactsAreReadOnlyAndReportContainsNoPrivateRows()
    {
        var f = Fixture();
        var files = Directory.GetFiles(f.Directory).Append(f.Source).ToDictionary(path => path, Hash);
        var result = Run(Arguments(f));
        Assert.Equal(0, result.Code); Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(6, json.RootElement.GetProperty("artifactsChecked").GetInt32());
        Assert.Equal(Hash(f.Manifest), json.RootElement.GetProperty("manifestSha256").GetString());
        Assert.DoesNotContain("private-id", result.Output); Assert.DoesNotContain("private-group", result.Output); Assert.DoesNotContain("private-message", result.Output);
        Assert.Equal(files.Count, Directory.GetFiles(f.Directory).Length + 1);
        foreach (var file in files) Assert.Equal(file.Value, Hash(file.Key));
    }

    [Fact]
    public void RewrittenHashCannotHideModifiedArtifactBytes()
    {
        var f = Fixture(); var file = Path.Combine(f.Directory, "validation-01.jsonl");
        File.AppendAllText(file, "tampered\n");
        var manifest = JsonNode.Parse(File.ReadAllText(f.Manifest))!;
        var artifact = manifest["artifacts"]!.AsArray().Single(a => (string?)a!["name"] == "validation-01.jsonl")!;
        artifact["sha256"] = Hash(file); artifact["bytes"] = new FileInfo(file).Length;
        File.WriteAllText(f.Manifest, manifest.ToJsonString());
        var result = Run(Arguments(f));
        Assert.Equal(1, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains(json.RootElement.GetProperty("findings").EnumerateArray(), x => x.GetProperty("code").GetString() == "GFV004");
        Assert.DoesNotContain("tampered", result.Output);
    }

    [Fact]
    public void MissingArtifactAndChangedSourceProduceFindings()
    {
        var f = Fixture(); File.Delete(Path.Combine(f.Directory, "train-01.jsonl"));
        var missing = Run(Arguments(f)); Assert.Equal(1, missing.Code); Assert.Contains("GFV003", missing.Output);
        File.AppendAllText(f.Source, " ");
        var invalid = Run(Arguments(f)); Assert.Equal(2, invalid.Code); Assert.Empty(invalid.Output);
        var f2 = Fixture();
        var source = File.ReadAllText(f2.Source).Replace("private-message", "different-message"); File.WriteAllText(f2.Source, source);
        var changed = Run(Arguments(f2)); Assert.Equal(1, changed.Code); Assert.Contains("GFV001", changed.Output);
    }

    [Fact]
    public void ChangedAssignmentsOrCountsAreDetectedEvenWhenInputsStayValid()
    {
        var f = Fixture(); var node = JsonNode.Parse(File.ReadAllText(f.Manifest))!;
        node["folds"]![0]!["validationRows"] = 1;
        File.WriteAllText(f.Manifest, node.ToJsonString());
        var result = Run(Arguments(f)); Assert.Equal(1, result.Code); Assert.Contains("GFV002", result.Output);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("unsafe")]
    [InlineData("hash")]
    [InlineData("version")]
    [InlineData("algorithm")]
    [InlineData("truncated")]
    [InlineData("missing-seed")]
    [InlineData("false-exported")]
    public void MalformedOrUnsafeManifestReturnsTwoWithoutJson(string kind)
    {
        var f = Fixture(); var text = File.ReadAllText(f.Manifest); var node = JsonNode.Parse(text)!;
        switch (kind)
        {
            case "unknown": node["unknown"] = 1; text = node.ToJsonString(); break;
            case "duplicate": text = text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"); break;
            case "unsafe": node["artifacts"]![0]!["name"] = "../outside.jsonl"; text = node.ToJsonString(); break;
            case "hash": node["artifacts"]![0]!["sha256"] = "bad"; text = node.ToJsonString(); break;
            case "version": node["schemaVersion"] = 2; text = node.ToJsonString(); break;
            case "algorithm": node["algorithm"] = "unknown"; text = node.ToJsonString(); break;
            case "truncated": text = "{"; break;
            case "missing-seed": node.AsObject().Remove("seed"); text = node.ToJsonString(); break;
            case "false-exported": node["exported"] = false; text = node.ToJsonString(); break;
        }
        File.WriteAllText(f.Manifest, text);
        var result = Run(Arguments(f)); Assert.Equal(2, result.Code); Assert.Empty(result.Output); Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void VerifyOptionsAndManifestSizeAreBounded()
    {
        var f = Fixture(); Assert.Equal(2, Run("verify", "--input", f.Source).Code);
        Assert.Equal(2, Run(Arguments(f).Concat(["--strict"]).ToArray()).Code);
        using (var file = new FileStream(f.Manifest, FileMode.Create)) file.SetLength(16L * 1024 * 1024 + 1);
        var result = Run(Arguments(f)); Assert.Equal(2, result.Code); Assert.Empty(result.Output);
    }
    public void Dispose() => Directory.Delete(root, true);
}
