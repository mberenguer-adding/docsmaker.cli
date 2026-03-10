using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocsMakerCli.Models;

namespace DocsMakerCli.Services;

public class MetadataService
{
    private readonly string _metadataPath;
    private readonly string _projectRoot;

    public MetadataService(string projectRoot)
    {
        _projectRoot = projectRoot;
        var docsmakerDir = Path.Combine(projectRoot, ".docsmaker");
        if (!Directory.Exists(docsmakerDir))
        {
            Directory.CreateDirectory(docsmakerDir);
        }
        _metadataPath = Path.Combine(docsmakerDir, "metadata.json");
    }

    public ProjectMetadata LoadMetadata()
    {
        if (!File.Exists(_metadataPath))
        {
            return new ProjectMetadata();
        }

        try
        {
            var json = File.ReadAllText(_metadataPath);
            return JsonSerializer.Deserialize<ProjectMetadata>(json) ?? new ProjectMetadata();
        }
        catch
        {
            return new ProjectMetadata();
        }
    }

    public void SaveMetadata(ProjectMetadata metadata)
    {
        metadata.LastUpdated = DateTime.UtcNow.ToString("O");
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_metadataPath, json);
    }

    public string ComputeHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    public List<string> GetChangedFiles(IEnumerable<string> currentFiles, ProjectMetadata oldMetadata)
    {
        var changedFiles = new List<string>();
        var oldFilesDict = oldMetadata.Files.ToDictionary(f => f.RelativePath);

        foreach (var file in currentFiles)
        {
            var relativePath = Path.GetRelativePath(_projectRoot, file);
            var currentHash = ComputeHash(file);

            if (!oldFilesDict.TryGetValue(relativePath, out var oldFile) || oldFile.Hash != currentHash)
            {
                changedFiles.Add(file);
            }
        }

        return changedFiles;
    }
}
