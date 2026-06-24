

namespace Brainfuck.Modes;

public class HelpMode : Mode
{
    public string Keyword => "help";

    public void Execute(ReadOnlySpan<string> args)
    {
        static void ShowUsage() => Console.WriteLine($"Usage: brainfuck++ help <{string.Join("|", Mode.GetModes().Where(m => m is not HelpMode).Select(m=>m.Keyword))}>");
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }
        
        switch (args[0])
        {
            case "init":
                Console.WriteLine("""
                Usage: brainfuck++ init
                Creates "project.toml" and "program.bfpp" in the current working directory.
                """);
                break;
            case "build":
                Console.WriteLine("""
                Usage: brainfuck++ build [path] [flags]
                Builds and runs the project.

                Flags:
                -redirectLaunch     Print project executable options instead of launching.
                -idePipe=name       Send build log and exit event to named pipe. Supports exit command.

                Finds a .toml or .bfpp file in the current working directory.
                A path to search in, a .toml file, or a .bfpp file may be provided as [path].
                """);
                break;
            default:
                ShowUsage();
                break;
        }

    }
}