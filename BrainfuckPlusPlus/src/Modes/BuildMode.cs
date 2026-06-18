

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualBasic;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Brainfuck.Modes;


public class BuildMode : Mode
{
    public class CommandSettings
    {
        public bool redirectLaunch = false;
    }
    public string Keyword => "build";
    public void Execute(ReadOnlySpan<string> args)
    {
        ProjectSettings? projSettings;
        CommandSettings cmdSettings = new();
        string? path = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith('-'))
            {
                switch (arg)
                {
                    case "-redirectLaunch":
                        cmdSettings.redirectLaunch = true;
                        break;
                    default:
                        Console.Error.WriteLine(@$"Unknown flag ""{arg}""");
                        break;
                }
            }
            else if (path != null)
            {
                Console.Error.WriteLine(@$"Args cannot contain multiple paths");
                Console.Error.WriteLine(@$"Found ""{path}"" and ""{arg}""");
            }
            else
                path = arg;
        }

        if (path is null)
        {
            var projFile = FindProjectFile("");
            if (projFile is null) return;
            projSettings = LoadProject(projFile);
        }
        else if (Directory.Exists(path))
        {
            var projFile = FindProjectFile(args[0]);
            if (projFile is null) return;
            projSettings = LoadProject(projFile);
        }
        else
        {
            projSettings = LoadProject(args[0]);
        }

        if (projSettings is null)
            return;


        var mainPath = projSettings.programFile;

        if (!File.Exists(mainPath))
        {
            Console.WriteLine(@$"Main file does not exist ""{mainPath}""");
            return;
        }

        if (new Parser().Parse(mainPath) is not AST ast)
        {
            Console.Error.WriteLine("Build Failed");
            return;
        }

        Console.WriteLine("Templating...");

        Func<string>? outputExecutable = null;

        if (projSettings.emitTypes.Contains(ProjectSettings.EmitType.Zig))
        {
            if (!ZigTemplater.Template(ast, projSettings, ref outputExecutable))
            {
                Console.Error.WriteLine("Zig Build Failed");
            }
        }

        if (projSettings.emitTypes.Contains(ProjectSettings.EmitType.Bf))
        {
            if (!BfTemplater.Template(ast, projSettings))
            {
                Console.Error.WriteLine("Bf Build Failed");
            }
        }

        if (outputExecutable is not null)
        {
            // Compiler subsystem returns process after starting the compiled result.
            var executablePath = outputExecutable.Invoke();

            if (cmdSettings.redirectLaunch)
            {
                string launchCommand = JsonSerializer.Serialize(new LaunchCommand()
                {
                    exe = executablePath,
                    cwd = projSettings.projectDir,
                    args = projSettings.launchSettings.args
                });
                Console.WriteLine("-Launch " + launchCommand);
                return;
            }
            var startInfo = new ProcessStartInfo(executablePath) { WorkingDirectory = projSettings.projectDir };
            foreach (var arg in projSettings.launchSettings.args)
                startInfo.ArgumentList.Add(arg);
            var proc = Process.Start(startInfo);
            if (proc is null)
            {
                Console.Error.WriteLine("Failed to run build output");
                return;
            }

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            };

            proc.WaitForExit();
        }
    }
    static string? FindProjectFile(string folder)
    {
        var files = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), folder));
        var tomlFiles = files.Where(file => Path.GetExtension(file) == ".toml").ToArray();
        var bfppFiles = files.Where(file => Path.GetExtension(file) == ".bfpp").ToArray();

        if (tomlFiles.Length > 1)
        {
            var project = tomlFiles.FirstOrDefault(file => Path.GetFileName(file) == "project.toml");
            if (project is null)
            {
                Console.WriteLine("""
                Failed to disambuguate .toml files.
                Multiple found and none called "project.toml".
                """);
                return null;
            }
            return project;
        }
        else if (tomlFiles.Length == 1)
        {
            return tomlFiles[0];
        }
        else if (bfppFiles.Length > 1)
        {
            var project = tomlFiles.FirstOrDefault(file => Path.GetFileName(file) == "main.bfpp");
            if (project is null)
            {
                Console.WriteLine("""
                Failed to disambuguate .bfpp files.
                Multiple found and none called "program.bfpp".
                """);
                return null;
            }
            return project;
        }
        else if (bfppFiles.Length == 1)
        {
            return bfppFiles[0];
        }
        else
        {
            Console.WriteLine("Failed to find any .toml or .bfpp files in the directory.");
            return null;
        }
    }

    class ConfigSeralized
    {
#pragma warning disable CS8618
#pragma warning disable CS0649
        [TomlRequired]
        public string main { get; set; }

        [TomlRequired]
        [TomlSingleOrArray]
        public List<string> outputs { get; set; }

        public CommonSettings? common { get; set; }
        public ZigTemplater.ZigSettings? zig { get; set; }
        public BfTemplater.BfSettings? bf { get; set; }

        public LaunchSettings? launch { get; set; }
#pragma warning restore CS8618
#pragma warning restore CS0649
    }

    static readonly TomlSerializerOptions TomlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    static ProjectSettings? LoadProject(string projectFile)
    {
        if (!File.Exists(projectFile))
        {
            Console.WriteLine(@$"Provided file ""{projectFile}"" does not exist.");
            return null;
        }

        static T ApplyDefaults<T>(T? config, CommonSettings common) where T : CommonSettings, new()
        {
            config ??= new T();
            foreach (var field in typeof(CommonSettings).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
                if (field.GetValue(config) is null)
                {
                    var commonVal = field.GetValue(common);
                    if (commonVal is not null)
                        field.SetValue(config, commonVal);
                }
            }
            return config;
        }

        if (Path.GetExtension(projectFile) is ".toml")
        {
            var file = File.ReadAllText(projectFile);
            ConfigSeralized config;
            try
            {
                if (TomlSerializer.Deserialize<ConfigSeralized>(file, options: TomlOptions) is not ConfigSeralized _config)
                    throw new Exception();
                config = _config;
            }
            catch (Exception e)
            {
                Console.WriteLine(@$"Failed to parse toml config file ""{projectFile}"".");
                Console.WriteLine(e.Message);
                return null;
            }

            var outputs = new List<ProjectSettings.EmitType>();
            foreach (var output in config.outputs)
            {
                foreach (var emitType in Enum.GetValues<ProjectSettings.EmitType>())
                {
                    if (Enum.GetName(emitType)!.Equals(output, StringComparison.CurrentCultureIgnoreCase))
                    {
                        outputs.Add(emitType);
                        goto nextOutput;
                    }
                }
                Console.WriteLine(@$"Unknown output type ""{output}""");
                Console.WriteLine(@$"Only [{string.Join(", ", Enum.GetNames<ProjectSettings.EmitType>().Select(name => name.ToLower()))}] are allowed");

            nextOutput:;
            }

            var common = config.common ?? new();

            var dir = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;

            return new ProjectSettings()
            {
                projectDir = dir,
                projectName = Path.GetFileNameWithoutExtension(projectFile)!,
                programFile = Path.GetFullPath(Path.Combine(dir, config.main)),
                emitTypes = outputs.AsReadOnly(),
                zigSettings = ApplyDefaults(config.zig, common),
                bfSettings = ApplyDefaults(config.bf, common),
                launchSettings = config.launch ?? new()
            };
        }
        else
        {
            var fullPath = Path.GetFullPath(projectFile);
            var common = new CommonSettings();
            return new ProjectSettings
            {
                projectDir = Path.GetDirectoryName(fullPath)!,
                projectName = Path.GetFileNameWithoutExtension(fullPath)!,
                programFile = fullPath,
                emitTypes = [ProjectSettings.EmitType.Zig],
                zigSettings = ApplyDefaults<ZigTemplater.ZigSettings>(null, common),
                bfSettings = ApplyDefaults<BfTemplater.BfSettings>(null, common),
                launchSettings = new()
            };
        }
    }
    
    class LaunchCommand
    {
        public required string exe { get; set; }
        public required string cwd { get; set; }
        public required List<string> args { get; set; }
    }
}