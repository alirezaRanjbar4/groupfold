using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GroupFold;

public static class Exporter
{
    public static Report Write(PlanResult plan, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new InputException("Pass a fresh output directory.");
        var destination = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new InputException("Output directory already exists; use a fresh path.");
        var outputBytes = plan.Rows.Sum(row => (long)Encoding.UTF8.GetByteCount(row.Raw) + 1) * plan.Report.FoldCount;
        if (outputBytes > 256L * 1024 * 1024) throw new InputException("Combined train/validation dataset outputs exceed 256 MiB; reduce folds or input size.");
        var created = new List<string>();
        var artifacts = new List<Artifact>();
        try
        {
            Directory.CreateDirectory(destination);
            for (var fold = 0; fold < plan.Report.FoldCount; fold++)
            {
                WriteDataset($"train-{fold + 1:00}.jsonl", fold, validation: false);
                WriteDataset($"validation-{fold + 1:00}.jsonl", fold, validation: true);
            }
            var report = plan.Report with { Artifacts = artifacts.ToArray() };
            var manifest = Path.Combine(destination, "manifest.json");
            using var output = new FileStream(manifest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created.Add(manifest);
            output.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, Planner.JsonOptions) + "\n"));
            return report;
        }
        catch
        {
            foreach (var file in created)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }

        void WriteDataset(string name, int fold, bool validation)
        {
            var path = Path.Combine(destination, name);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created.Add(path);
            for (var i = 0; i < plan.Rows.Length; i++)
                if ((plan.Assignments[i] == fold) == validation)
                    file.Write(Encoding.UTF8.GetBytes(plan.Rows[i].Raw + "\n"));
            file.Flush();
            file.Position = 0;
            artifacts.Add(new(name, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), file.Length));
        }
    }
}
