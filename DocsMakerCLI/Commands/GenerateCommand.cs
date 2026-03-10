using System.ComponentModel;
using DocsMakerCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DocsMakerCli.Commands;

// 1. Definimos los argumentos que el usuario puede pasar por consola
public class GenerateSettings : CommandSettings
{
    [CommandArgument(0, "[ProjectPath]")]
    [Description("Ruta del proyecto a documentar. Por defecto es la carpeta actual.")]
    public string ProjectPath { get; set; } = ".";

    [CommandOption("-k|--api-key")]
    [Description("Clave del API de IA (OpenAI, Gemini, etc.). Si no se pasa, intentará leer variables de entorno.")]
    public string? ApiKey { get; set; }
}

// 2. Definimos la lógica del comando
public class GenerateCommand : AsyncCommand<GenerateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, GenerateSettings settings, CancellationToken cancellationToken)
    {
        var targetPath = Path.GetFullPath(settings.ProjectPath);

        if (!Directory.Exists(targetPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] La ruta '{targetPath}' no existe.");
            return -1;
        }

        AnsiConsole.MarkupLine($"[yellow]Analizando proyecto en:[/] {targetPath}");

        // 1. Obtener contexto
        string userContext = GetOrAskForContext(targetPath);

        // 2. Obtener API Key (Si no viene por argumento, miramos variables de entorno o preguntamos)
        string apiKey = settings.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("Introduce tu [green]API Key[/] de OpenAI:")
                    .PromptStyle("red")
                    .Secret()); // Oculta los caracteres mientras teclea
        }

        // 3. Obtener archivos
        var fileAnalyzer = new FileAnalyzerService();
        List<string> filesToDocument = [];

        AnsiConsole.Status()
            .Start("Mapeando árbol de archivos...", ctx => 
            {
                // ¡Aquí recuperamos el índice real de ficheros!
                filesToDocument = fileAnalyzer.GetFilesToDocument(targetPath).ToList();
            });

        if (filesToDocument.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No se encontraron archivos válidos para documentar.[/]");
            AnsiConsole.MarkupLine("[gray]Revisa que la ruta sea correcta y que los archivos no estén excluidos en tu .docsmakerignore.[/]");
            return -1;
        }

        AnsiConsole.MarkupLine($"[green]¡Análisis completado![/] Se ha generado un índice con [blue]{filesToDocument.Count}[/] archivos.");
        
        // 4. Iniciar la magia de la IA con feedback visual
        var aiService = new AiDocumentationService(apiKey, targetPath);
        
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots) // Un spinner minimalista
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Despertando al Agente de IA...", async ctx =>
            {
                // Inyectamos un callback para que el servicio actualice la UI
                aiService.OnProgress = (mensaje) => 
                {
                    ctx.Status(mensaje);
                };

                // Llamamos al servicio
                await aiService.GenerateDocumentationAsync(filesToDocument, userContext);
            });

        AnsiConsole.MarkupLine("\n[green]¡Documentación generada con éxito de forma autónoma![/]");
        AnsiConsole.MarkupLine($"[gray]Total ficheros leídos:[/] {aiService.FilesReadCount}");
        AnsiConsole.MarkupLine($"[gray]Total ficheros escritos:[/] {aiService.FilesWrittenCount}");
        AnsiConsole.MarkupLine("Ejecuta [blue]npm run dev[/] en la carpeta del proyecto para previsualizar tu Starlight.");
        
        return 0;
    }

    private string GetOrAskForContext(string targetPath)
    {
        var contextFilePath = Path.Combine(targetPath, ".docsmaker-context.md");

        if (File.Exists(contextFilePath))
        {
            AnsiConsole.MarkupLine("[gray]Archivo de contexto encontrado. Usándolo como base.[/]");
            return File.ReadAllText(contextFilePath);
        }

        // Si no existe, usamos Spectre Console para preguntar interactivamente
        AnsiConsole.MarkupLine("\n[cyan]Parece que es la primera vez que documentas este proyecto.[/]");
        var context = AnsiConsole.Ask<string>("Por favor, describe brevemente la [green]arquitectura y el objetivo[/] del proyecto:");
        
        File.WriteAllText(contextFilePath, context);
        AnsiConsole.MarkupLine("[gray]Contexto guardado en .docsmaker-context.md para futuras ejecuciones.[/]\n");

        return context;
    }
}