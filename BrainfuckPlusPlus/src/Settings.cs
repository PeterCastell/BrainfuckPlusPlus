namespace Brainfuck;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

public class ProjectSettings
{
    public enum EmitType
    {
        Zig,
        Bf
    }
    public required string projectName;
    public required string projectDir;
    public required string programFile;
    public required ReadOnlyCollection<EmitType> emitTypes;
    public required ZigTemplater.ZigSettings zigSettings;
    public required BfTemplater.BfSettings bfSettings;
    public required LaunchSettings launchSettings;
}

public class CommonSettings
{
    bool? IncludeComments { get; set; }
    bool? IncludeFormatting { get; set; }

    [JsonIgnore]
    public bool includeComments => IncludeComments ?? true;
    [JsonIgnore]
    public bool includeFormatting => IncludeFormatting ?? true;
}


public class LaunchSettings
{
    public List<string> args { get; set; } = [];
}