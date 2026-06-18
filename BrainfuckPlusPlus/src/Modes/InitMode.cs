

namespace Brainfuck.Modes;

public class InitMode : Mode
{
    public string Keyword => "init";

    public void Execute(ReadOnlySpan<string> args)
    {
        var assembly = typeof(ZigTemplater).Assembly;

        void CreateFile(string fileName)
        {
            if (File.Exists(fileName)) return;

            using var fileStream = File.Create(fileName);
            using var resourceStream = assembly.GetManifestResourceStream("BrainfuckPlusPlus.example." + fileName)!;
            resourceStream.CopyTo(fileStream);
        }

        CreateFile("project.toml");
        CreateFile("main.bfpp");
        CreateFile("reference.md");
    }
}