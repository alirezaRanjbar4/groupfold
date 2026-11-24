using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GroupFold;

public sealed record VerificationFinding(string Code, string Message, string? Artifact = null);
public sealed record VerificationReport(int SchemaVersion, bool Passed, string SourceSha256, string ManifestSha256, int FoldCount, int ArtifactsChecked, VerificationFinding[] Findings);

public static class Verifier
{
    private const int MaxManifestBytes = 16 * 1024 * 1024;
    private const long MaxArtifactBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions Strict = new(Planner.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };

    public static VerificationReport Verify(string input, string manifestPath, string artifactDirectory)
    {
        var manifestBytes = ReadBounded(manifestPath, MaxManifestBytes);
        Report manifest;
        try
        {
            var offset = manifestBytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            using var document = JsonDocument.Parse(manifestBytes.AsMemory(offset), new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateFields(document.RootElement);
            RequireFields(document.RootElement, ["schemaVersion", "algorithm", "foldCount", "seed", "sourceSha256", "sourceRows", "groups", "labels", "folds", "warnings", "artifacts", "exported"]);
            if (document.RootElement.GetProperty("exported").ValueKind != JsonValueKind.True) throw new JsonException();
            foreach (var item in document.RootElement.GetProperty("labels").EnumerateArray()) RequireFields(item, ["label", "rows", "groups"]);
            foreach (var item in document.RootElement.GetProperty("folds").EnumerateArray()) RequireFields(item, ["fold", "validationRows", "trainingRows", "validationGroups", "validationLabels", "trainingLabels", "validationSourceLines"]);
            foreach (var item in document.RootElement.GetProperty("warnings").EnumerateArray()) RequireFields(item, ["code", "message", "label", "fold", "partition"]);
            foreach (var item in document.RootElement.GetProperty("artifacts").EnumerateArray()) RequireFields(item, ["name", "sha256", "bytes"]);
            manifest = document.RootElement.Deserialize<Report>(Strict) ?? throw new JsonException();
            if (manifest.SchemaVersion != 1 || manifest.Algorithm != "group-sse-v1" || manifest.FoldCount is < 2 or > 20 ||
                manifest.SourceSha256 is not { Length: 64 } || !manifest.SourceSha256.All(Uri.IsHexDigit) ||
                manifest.Labels is null || manifest.Folds is null || manifest.Warnings is null || manifest.Artifacts is null ||
                manifest.Artifacts.Length != manifest.FoldCount * 2 || manifest.Folds.Length != manifest.FoldCount || !manifest.Exported)
                throw new JsonException();
            for (var i = 0; i < manifest.Artifacts.Length; i++)
            {
                var artifact = manifest.Artifacts[i];
                var expectedName = $"{(i % 2 == 0 ? "train" : "validation")}-{i / 2 + 1:00}.jsonl";
                if (artifact is null || artifact.Name != expectedName || artifact.Bytes < 0 || artifact.Bytes > MaxArtifactBytes ||
                    artifact.Sha256 is not { Length: 64 } || !artifact.Sha256.All(Uri.IsHexDigit)) throw new JsonException();
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        { throw new InputException("Invalid manifest: expected strict schema 1, supported algorithm and ordered safe artifact names."); }

        var plan = Planner.Plan(input, manifest.FoldCount, manifest.Seed);
        var findings = new List<VerificationFinding>();
        var manifestHash = Planner.Hash(manifestBytes);
        if (!string.Equals(plan.Report.SourceSha256, manifest.SourceSha256, StringComparison.OrdinalIgnoreCase))
            findings.Add(new("GFV001", "Source bytes differ from the manifest."));
        var expectedReport = plan.Report with { Artifacts = manifest.Artifacts };
        var expectedNode = JsonSerializer.SerializeToNode(expectedReport, Planner.JsonOptions);
        var actualNode = JsonSerializer.SerializeToNode(manifest, Planner.JsonOptions);
        if (!JsonNode.DeepEquals(expectedNode, actualNode))
            findings.Add(new("GFV002", "Manifest counts, assignments, warnings or source metadata differ from the deterministic plan."));
        var directory = Path.GetFullPath(artifactDirectory);
        if (!Directory.Exists(directory)) throw new InputException("Artifact directory does not exist.");
        var checkedCount = 0; long totalBytes = 0;
        for (var i = 0; i < manifest.Artifacts.Length; i++)
        {
            var declared = manifest.Artifacts[i];
            var path = Path.Combine(directory, declared.Name);
            if (!File.Exists(path)) { findings.Add(new("GFV003", "Artifact is missing.", declared.Name)); continue; }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { findings.Add(new("GFV003", "Artifact must be a regular file, not a link.", declared.Name)); continue; }
            using var actual = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (actual.Length > MaxArtifactBytes || actual.Length > MaxArtifactBytes - totalBytes)
                throw new InputException("Combined artifact bytes exceed the 256 MiB verification limit.");
            using var expected = new MemoryStream();
            var fold = i / 2; var validation = i % 2 == 1;
            for (var row = 0; row < plan.Rows.Length; row++)
                if ((plan.Assignments[row] == fold) == validation)
                {
                    var bytes = Encoding.UTF8.GetBytes(plan.Rows[row].Raw + "\n");
                    if (expected.Length + bytes.Length > MaxArtifactBytes) throw new InputException("Expected artifact exceeds verification limit.");
                    expected.Write(bytes);
                }
            expected.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[8192]; var expectedBuffer = new byte[8192];
            long actualBytes = 0; bool matches = true; int read;
            while ((read = actual.Read(buffer)) > 0)
            {
                actualBytes += read; totalBytes += read;
                if (totalBytes > MaxArtifactBytes) throw new InputException("Combined artifact bytes exceed the 256 MiB verification limit.");
                hash.AppendData(buffer, 0, read);
                var expectedRead = expected.ReadAtLeast(expectedBuffer, read, throwOnEndOfStream: false);
                if (expectedRead != read || !buffer.AsSpan(0, read).SequenceEqual(expectedBuffer.AsSpan(0, expectedRead))) matches = false;
            }
            if (expected.Position != expected.Length) matches = false;
            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!matches || actualBytes != declared.Bytes || !string.Equals(digest, declared.Sha256, StringComparison.OrdinalIgnoreCase))
                findings.Add(new("GFV004", "Artifact bytes, length or SHA-256 differ from the source assignment or manifest.", declared.Name));
            checkedCount++;
        }
        return new(1, findings.Count == 0, plan.Report.SourceSha256, manifestHash, manifest.FoldCount, checkedCount, findings.ToArray());
    }

    private static byte[] ReadBounded(string path, int limit)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > limit) throw new InputException("Manifest exceeds 16 MiB.");
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = input.Read(buffer)) > 0) { if (output.Length + read > limit) throw new InputException("Manifest exceeds 16 MiB."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }

    private static void RejectDuplicateFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new JsonException(); RejectDuplicateFields(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicateFields(item);
    }

    private static void RequireFields(JsonElement value, string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length || names.Any(name => !value.TryGetProperty(name, out _))) throw new JsonException();
    }
}
