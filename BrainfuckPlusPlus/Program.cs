

namespace Brainfuck;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var option = ProgramSettings.GetArguments(args);
        if (option is null) return;
        
        // if (option.IsLSP)
        // {
        //     await RunLSPAsync();
        //     return;
        // }

        const string path = "../../../../project/program.bfpp";

        if (!File.Exists(path))
        {
            Console.WriteLine("Provided file does not exist");
            return;
        }

        var fullPath = Path.GetFullPath(path);

        var settings = new TemplateSettings()
        {
            projectDir = Path.GetDirectoryName(fullPath)!,
            projectName = Path.GetFileNameWithoutExtension(fullPath)!,
            launchAfterBuild = true,
            releaseMode = TemplateSettings.ReleaseMode.Debug,
            extraArgs = new() { { "compact-bf", "" } },
            includeComments = false

            // releaseMode = TemplateSettings.ReleaseMode.ReleaseFast,
            // includeComments = false
        };

        if (new Parser().Parse(fullPath) is not AST ast)
        {
            Console.WriteLine("Build Failed");
            return;
        }

        Action? runCommand = null;

        if (!ZigTemplater.Template(ast, settings, ref runCommand))
        {
            Console.WriteLine("Zig Build Failed");
        }

        // if (!BfTemplater.Template(ast, settings, ref runCommand))
        // {
        //     Console.WriteLine("Bf Build Failed");
        // }

        runCommand?.Invoke();
    }
    
    // static async Task RunLSPAsync()
    // {
    //     var server = await LanguageServer.From(options =>
    //         options
    //             .WithInput(Console.OpenStandardInput())
    //             .WithOutput(Console.OpenStandardOutput())
    //             .WithHandler<LSP.TextDocumentSyncHandler>()
    //             .WithHandler<LSP.DefinitionHandler>()
    //     );

    //     await server.WaitForExit;
    // }
}