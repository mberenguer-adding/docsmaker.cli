using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;
using DocsMakerCLI.Prompts;

namespace DocsMakerCli.Services;

public class AiDocumentationService
{
    private readonly IChatClient _chatClient;
    private readonly string _projectRootPath;
    private readonly string _docsBasePath;
    
    public Action<string>? OnProgress { get; set; }

    public AiDocumentationService(string apiKey, string projectRootPath)
    {
        _projectRootPath = projectRootPath;
        _docsBasePath = Path.Combine(projectRootPath, "src", "content", "docs");

        // Configuramos el cliente base
        var openAiClient = new OpenAIClient(apiKey).GetChatClient("gpt-5.4").AsIChatClient();

        // LA MAGIA DEL AGENTE: Usamos el Builder para que resuelva las llamadas a herramientas automáticamente
        _chatClient = new ChatClientBuilder(openAiClient)
            .UseFunctionInvocation() // Esto hace que el LLM ejecute el bucle de herramientas él solo
            .Build();
    }

    public async Task GenerateDocumentationAsync(IEnumerable<string> files, string userContext)
    {
        // 1. Construimos el árbol de archivos como texto plano para el prompt
        var treeBuilder = new StringBuilder();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(_projectRootPath, file);
    
            // Nivel Pro: Leemos las primeras 15 líneas para capturar namespace y firmas de clase
            var lines = await File.ReadAllLinesAsync(file);
            var preview = string.Join(" ", lines.Take(15)
                .Where(l => l.Contains("class") || l.Contains("interface") || l.Contains("record")));

            treeBuilder.AppendLine($"- {relativePath} (Contiene: {preview.Trim()})");
        }
        var fileTree = treeBuilder.ToString();

        // 2. Registramos las herramientas
        var writeTool = AIFunctionFactory.Create(WriteDocFile);
        var readTool = AIFunctionFactory.Create(ReadFile);
        
        var chatOptions = new ChatOptions
        {
            Tools = [writeTool, readTool],
            Temperature = 0.2f
        };

        // 3. Preparamos el Prompt inicial con todo el contexto
        string systemPrompt = SystemPrompts.GetMasterPrompt(userContext, fileTree);
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, "Inicia tu análisis del proyecto y genera la documentación. Puedes empezar leyendo los archivos que consideres el punto de entrada o la base de la arquitectura.")
        };

        // 4. UNA ÚNICA LLAMADA AL AGENTE. 
        // El UseFunctionInvocation() interceptará las peticiones de herramientas, ejecutará el código C#
        // y le devolverá el resultado al LLM de forma invisible hasta que el LLM decida que ha terminado.
        var response = await _chatClient.GetResponseAsync(chatMessages, chatOptions);

        // Opcional: imprimir lo que dice el LLM al terminar (el mensaje de resumen)
        Console.WriteLine($"\n[IA]: {response.Text}");
    }

    // --- HERRAMIENTAS PARA LA IA ---

    [Description("Lee el contenido de un archivo del proyecto. Usa la ruta relativa que viste en el árbol de archivos.")]
    private string ReadFile(
        [Description("Ruta relativa del archivo a leer (ej. 'src/Services/Auth.cs')")] string relativeFilePath)
    {
        try
        {
            var fullPath = Path.Combine(_projectRootPath, relativeFilePath);
            var normalizedTarget = Path.GetFullPath(fullPath);
            
            if (File.Exists(normalizedTarget))
            {
                // Podríamos imprimir por consola aquí para que el usuario vea qué está leyendo la IA
                OnProgress?.Invoke($"[yellow]Fichero leído:[/] {relativeFilePath}");
                return File.ReadAllText(normalizedTarget);
            }
            return $"Error: El archivo {relativeFilePath} no existe.";
        }
        catch (Exception ex)
        {
            OnProgress?.Invoke($"[red]Error al leer archivo:[/] {relativeFilePath} - {ex.Message}");
            return $"Error al leer el archivo: {ex.Message}";
        }
    }

    [Description("Guarda un archivo de documentación en formato Markdown. Organiza bien la ruta.")]
    private void WriteDocFile(
        [Description("Ruta relativa donde guardar el .md (ej. 'modelos/usuario.md').")] string relativePath,
        [Description("Contenido en Markdown con Frontmatter.")] string markdownContent)
    {
        if (!relativePath.EndsWith(".md") && !relativePath.EndsWith(".mdx")) relativePath += ".md";

        var fullPath = Path.Combine(_docsBasePath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(directory) && directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        OnProgress?.Invoke($"[green]Fichero guardado:[/] {relativePath}");
        File.WriteAllText(fullPath, markdownContent);
    }
}