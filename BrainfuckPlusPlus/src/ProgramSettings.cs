namespace Brainfuck;

using System.Runtime.Intrinsics.Arm;
using CommandLine;

public static class ProgramSettings
{
    public class Arguments
    {
        // [Option("lsp", HelpText = "Run as an lsp server")]
        // public bool IsLSP { get; set; }
        // [Option("stdio", HelpText = "Used by lsp clients (does nothing)")]
        // public bool LspStdio { get; set; }
        
        [Option('f', "file", HelpText = "Entrypoint file. Optional if project file is present overrides \"main\" in project file", MetaValue = "SRING")]
        public string? File { get; set; }
        
        [Option('p', "project", HelpText = "Project toml file")]
        public string? Project { get; set; }
        
        [Option('m', "mode", HelpText = "Release mode (Debug, ReleaseSafe, ReleaseFast)")]
        public string? Mode { get; set; }
        
        [Option("emitbf")]
        public bool EmitBf { get; set; }
    }
    
    public static Arguments? GetArguments(string[] args)
    {
        var res = CommandLine.Parser.Default.ParseArguments<Arguments>(args);
        if (res.Errors.Any())
        {
            Console.WriteLine("Error reading command line arguments");
            foreach (var error in res.Errors)
                Console.WriteLine(error.StopsProcessing);
            return null;
        }
        var arguments = res.Value;
        
        return arguments;
    }
}