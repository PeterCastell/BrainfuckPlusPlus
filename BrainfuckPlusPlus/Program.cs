

using System.Reflection;
using Brainfuck.Modes;

namespace Brainfuck;

public static class Program
{

    public static async Task Main(string[] args)
    {
        string? modeKeyword = args.Length > 0 ? args[0] : null;

        Mode? mode = Mode.GetModes().FirstOrDefault(mode => mode.Keyword == modeKeyword);

        if (mode is null)
        {
            Console.WriteLine("""
            Usage: brainfuck++ <command> [options]

            Commands:
            init    Initialize a project directory
            build   Build a project

            Run 'brainfuck++ help <command>' for more information on any command.
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