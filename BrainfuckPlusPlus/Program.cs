

using System.Reflection;
using Brainfuck.Modes;

namespace Brainfuck;

public static class Program
{
    static string Version
    {
        get
        {
            string? infoVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
            var parts = infoVersion?.Split('+');
            return parts is { Length: 2 } && parts[1].Length > 7
                ? $"{parts[0]}+{parts[1][..7]}"
                : infoVersion ?? "";
        }
    }
    public static async Task Main(string[] args)
    {
        string? modeKeyword = args.Length > 0 ? args[0] : null;

        Mode? mode = Mode.GetModes().FirstOrDefault(mode => mode.Keyword == modeKeyword);

        if (mode is null)
        {
            
            Console.WriteLine($"""
            bfpp version {Version}
            Usage: bfpp <command> [options]
            Commands:
            init    Initialize a project directory
            build   Build a project
            run     Build and run a project

            Run 'bfpp help <command>' for more information on any command.
            """);
        }
        else
        {
            mode.Execute(args.AsSpan()[1..]);
        }

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