namespace DocsMakerCli.Models;

public class ProjectMetadata
{
    public string LastUpdated { get; set; } = DateTime.UtcNow.ToString("O");
    public List<FileMetadata> Files { get; set; } = new();
}

public class FileMetadata
{
    public string RelativePath { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public List<string> GeneratedDocFiles { get; set; } = new();
}