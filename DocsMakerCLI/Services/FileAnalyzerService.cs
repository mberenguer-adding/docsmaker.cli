using Microsoft.Extensions.FileSystemGlobbing;

namespace DocsMakerCli.Services;

public class FileAnalyzerService
{
    public IEnumerable<string> GetFilesToDocument(string rootPath)
    {
        var matcher = new Matcher();

        // 1. Por defecto, incluimos todos los archivos
        matcher.AddInclude("**/*");

        // 2. Exclusiones base (las típicas carpetas que NO queremos documentar)
        string[] defaultIgnores = [
            "**/node_modules/**", 
            "**/.git/**", 
            "**/bin/**", 
            "**/obj/**", 
            "**/.vs/**", 
            "**/dist/**", 
            "**/build/**",
            "**/.docsmaker/**",
            "**/.docsmaker-context.md",
            "**/.gitignore"
        ];

        foreach (var ignore in defaultIgnores)
            matcher.AddExclude(ignore);

        // 3. Excluir archivos binarios o irrelevantes (imágenes, dlls, etc.)
        string[] nonCodeExtensions = [
            "**/*.png", "**/*.jpg", "**/*.jpeg", "**/*.gif", "**/*.svg", "**/*.ico",
            "**/*.dll", "**/*.exe", "**/*.pdf", "**/*.zip", "**/*.tar.gz", "**/.DS_Store" // A veces no aportan valor al prompt de la IA
        ];

        foreach (var ext in nonCodeExtensions)
            matcher.AddExclude(ext);

        // 4. Leer ignores personalizados (.docsmakerignore + .gitignore)
        AddIgnorePatternsFromFile(matcher, Path.Combine(rootPath, ".docsmakerignore"));
        AddIgnorePatternsFromFile(matcher, Path.Combine(rootPath, ".gitignore"));

        // 5. Ejecutar la búsqueda en la carpeta raíz que nos pasen
        return matcher.GetResultsInFullPath(rootPath);
    }

    private static void AddIgnorePatternsFromFile(Matcher matcher, string ignoreFilePath)
    {
        if (!File.Exists(ignoreFilePath))
            return;

        var ignorePatterns = File.ReadAllLines(ignoreFilePath);
        foreach (var line in ignorePatterns)
        {
            var cleanLine = line.Trim();

            // Ignoramos líneas vacías, comentarios y reglas de inclusión (no soportadas aquí)
            if (string.IsNullOrEmpty(cleanLine) || cleanLine.StartsWith('#') || cleanLine.StartsWith('!'))
                continue;

            matcher.AddExclude(NormalizeIgnorePattern(cleanLine));
        }
    }

    private static string NormalizeIgnorePattern(string pattern)
    {
        var normalized = pattern.Replace('\\', '/').Trim();

        if (normalized.StartsWith('/'))
            normalized = normalized[1..];

        if (normalized.EndsWith('/'))
            return $"**/{normalized.TrimEnd('/')}/**";

        // Si no trae ruta, aplicar a cualquier subcarpeta.
        if (!normalized.Contains('/'))
            return $"**/{normalized}";

        return $"**/{normalized}";
    }
}
