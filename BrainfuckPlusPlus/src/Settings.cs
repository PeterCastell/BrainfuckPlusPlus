namespace Brainfuck;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Tomlyn.Serialization;

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
    public static readonly string[] Features = ["extern_functions", "threads", "mutexes", "raw_input"];

    public bool? IncludeComments { get; set; }
    public bool? IncludeFormatting { get; set; }
    /// <summary>Number of cells on the tape. Null keeps the output's default.</summary>
    public long? TapeSize { get; set; }
    [TomlSingleOrArray]
    public List<string>? DisabledFeatures { get; set; }

    [JsonIgnore]
    public bool includeComments => IncludeComments ?? true;
    [JsonIgnore]
    public bool includeFormatting => IncludeFormatting ?? true;
    [JsonIgnore]
    public List<string> disabledFeatures => DisabledFeatures ?? [];
}


public class LaunchSettings
{
    public List<string> args { get; set; } = [];
}