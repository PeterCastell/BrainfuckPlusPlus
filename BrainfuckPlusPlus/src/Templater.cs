

namespace Brainfuck;

public struct TemplateSettings()
{
    public enum ReleaseMode
    {
        Debug,
        ReleaseSafe,
        ReleaseFast,
    }
    public required string projectName;
    public required string projectDir;
    public ReleaseMode releaseMode = ReleaseMode.Debug;
    public bool includeComments = true;
    public bool includeWhitespace = true;
    public bool buildAfterTemplate = true;
    public bool launchAfterBuild = false;
    public required Dictionary<string, string> extraArgs;
}
public delegate bool Templater(AST ast, TemplateSettings settings, ref Action? runCommand);