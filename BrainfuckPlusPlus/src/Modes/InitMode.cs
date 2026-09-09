

namespace Brainfuck.Modes;

public class InitMode : Mode
{
    public string Keyword => "init";

    public void Execute(ReadOnlySpan<string> args)
    {
        var assembly = typeof(ZigTemplater).Assembly;
        var projectDir = args.Length switch
        {
            0 => Directory.GetCurrentDirectory(),
            1 => Path.GetFullPath(args[0]),
            _ => throw new ArgumentException("init accepts at most one path")
        };

        Directory.CreateDirectory(projectDir);

        void CreateFile(string fileName)
        {
            var path = Path.Combine(projectDir, fileName);
            if (File.Exists(path)) return;

            using var fileStream = File.Create(path);
            using var resourceStream = assembly.GetManifestResourceStream("BrainfuckPlusPlus.example." + fileName)!;
            resourceStream.CopyTo(fileStream);
        }

        CreateFile("project.toml");
        CreateFile("main.bfpp");
        CreateFile("reference.md");
    }
}