using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;
using DocsMakerCLI.Prompts;
using System.ClientModel;

namespace DocsMakerCli.Services;

public class AiDocumentationService
{
    private const string CompletionToken = "DOCSMAKER_COMPLETE";
    private const int MaxAutonomousRounds = 8;

    private readonly IChatClient _chatClient;
    private readonly string _projectRootPath;
    private readonly string _docsBasePath;
    
    public Action<string>? OnProgress { get; set; }

    public int FilesReadCount { get; private set; }
    public int FilesWrittenCount { get; private set; }

    public AiDocumentationService(string apiKey, string projectRootPath)
    {
        _projectRootPath = projectRootPath;
        _docsBasePath = Path.Combine(projectRootPath, "src", "content", "docs");

        // Configuramos timeout de red más alto para proyectos grandes (el default suele ser 100s).
        var openAiOptions = new OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromMinutes(10)
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), openAiOptions)
            .GetChatClient("gpt-5.4")
            .AsIChatClient();

        // LA MAGIA DEL AGENTE: Usamos el Builder para que resuelva las llamadas a herramientas automáticamente
        _chatClient = new ChatClientBuilder(openAiClient)
            .UseFunctionInvocation() // Esto hace que el LLM ejecute el bucle de herramientas él solo
            .Build();
    }

    private void UpdateProgress(string detail)
    {
        OnProgress?.Invoke($"[blue]Leídos:[/] {FilesReadCount} | [green]Escritos:[/] {FilesWrittenCount} | {detail}");
    }

    public async Task GenerateDocumentationAsync(IEnumerable<string> files, string userContext)
    {
        var projectFiles = files.ToList();

        // 1. Construimos el árbol de archivos como texto plano para el prompt
        var treeBuilder = new StringBuilder();
        foreach (var file in projectFiles)
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
        string systemPrompt = SystemPrompts.GetMasterPrompt(userContext, fileTree, CompletionToken);
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, "Inicia tu análisis del proyecto y genera la documentación. Puedes empezar leyendo los archivos que consideres el punto de entrada o la base de la arquitectura.")
        };

        // 4. Ejecutamos rondas autónomas para evitar que un único mensaje "corte" la sesión en proyectos grandes.
        var completed = false;
        for (var round = 1; round <= MaxAutonomousRounds && !completed; round++)
        {
            UpdateProgress($"[blue]Ronda autónoma {round}/{MaxAutonomousRounds}[/]: analizando y generando docs...");

            var response = await _chatClient.GetResponseAsync(chatMessages, chatOptions);
            var responseText = response.Text?.Trim() ?? string.Empty;

            UpdateProgress($"[blue]Ronda autónoma {round}/{MaxAutonomousRounds}[/]: respuesta recibida, evaluando si continuar...");
            chatMessages.Add(new ChatMessage(ChatRole.Assistant, responseText));

            completed = responseText.Contains(CompletionToken, StringComparison.OrdinalIgnoreCase);
            if (completed)
                break;

            var docsCount = Directory.Exists(_docsBasePath)
                ? Directory.GetFiles(_docsBasePath, "*.md*", SearchOption.AllDirectories).Length
                : 0;

            chatMessages.Add(new ChatMessage(
                ChatRole.User,
                $"Continúa automáticamente. No pidas confirmación al usuario y sigue hasta cubrir lo importante del proyecto. " +
                $"Estado actual: {docsCount} archivos de documentación generados. " +
                $"Cuando termines por completo, responde únicamente con {CompletionToken}."));
        }

        if (!completed)
        {
            UpdateProgress(
                $"[yellow]Límite de rondas alcanzado ({MaxAutonomousRounds}).[/] Se generó documentación parcial; puedes relanzar el comando para seguir ampliándola.");
        }
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
                FilesReadCount++;
                // Podríamos imprimir por consola aquí para que el usuario vea qué está leyendo la IA
                UpdateProgress($"[yellow]Fichero leído:[/] {relativeFilePath}");
                return File.ReadAllText(normalizedTarget);
            }
            return $"Error: El archivo {relativeFilePath} no existe.";
        }
        catch (Exception ex)
        {
            UpdateProgress($"[red]Error al leer archivo:[/] {relativeFilePath} - {ex.Message}");
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

        FilesWrittenCount++;
        UpdateProgress($"[green]Fichero guardado:[/] {relativePath}");
        File.WriteAllText(fullPath, markdownContent);
    }
}
