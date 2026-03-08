using DocsMakerCli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

AnsiConsole.Write(
    new FigletText("DocsMaker AI")
        .LeftJustified()
        .Color(Color.Aqua));

var app = new CommandApp<GenerateCommand>();

app.Configure(config =>
{
    config.SetApplicationName("docsmaker");
    
    config.AddCommand<GenerateCommand>("generate")
        .WithDescription("Analiza el código y genera la documentación en Astro Starlight.");
});

try
{
    return app.Run(args);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error fatal:[/] {ex.Message}");
    return 1;
}