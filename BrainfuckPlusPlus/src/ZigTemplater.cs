using System;
using System.Diagnostics;
using System.Text;
using Tomlyn.Serialization;

namespace Brainfuck;

public static class ZigTemplater
{

    public class ZigSettings : CommonSettings
    {
        public string? zigPath { get; set; }
        public List<string> args { get; set; } = [];
        public bool buildAfterTemplate { get; set; } = true;
        public string? templateOverrides { get; set; }
        [TomlSingleOrArray]
        public List<string> ignoreError { get; set; } = [];
        public int cellSize { get; set; } = 1;
    }

    /// <summary>The zig version the templates are written for, read from minimum_zig_version in template/build.zig.zon.</summary>
    public static readonly string ZigVersion = ReadZigVersion();

    static string ReadZigVersion()
    {
        using var stream = OpenTemplateFile(null, "build.zig.zon");
        var zon = new StreamReader(stream).ReadToEnd();
        var match = System.Text.RegularExpressions.Regex.Match(zon, @"\.minimum_zig_version\s*=\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    /// <summary>Environment variable that can point at a zig executable.</summary>
    public static readonly string ZigPathEnvVar = "BFPP_ZIG";

    static readonly string ExeSuffix = OperatingSystem.IsWindows() ? ".exe" : "";
    static readonly string ZigExeName = $"zig{ExeSuffix}";

    /// <summary>
    /// Finds the zig executable. Search order:
    /// 1. zig_path in the project's [zig] config (relative to the project directory)
    /// 2. the BFPP_ZIG environment variable
    /// 3. zig/zig next to the compiler (the "with zig" release layout)
    /// 4. zig on the PATH
    /// </summary>
    static string? FindZig(BuildIO IO, ZigSettings zigSettings, string projectDir)
    {
        if (!string.IsNullOrWhiteSpace(zigSettings.zigPath))
            return ResolveExplicit(Path.Combine(projectDir, zigSettings.zigPath), "zig_path in project config");

        if (Environment.GetEnvironmentVariable(ZigPathEnvVar) is { } envPath && !string.IsNullOrWhiteSpace(envPath))
            return ResolveExplicit(envPath, $"{ZigPathEnvVar} environment variable");

        var exeDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(exeDir, "zig", ZigExeName);
        if (File.Exists(bundled))
            return bundled;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), ZigExeName);
            if (File.Exists(candidate))
                return candidate;
        }

        IO.WriteErr($"Could not find zig {ZigVersion}. Do one of the following:");
        IO.WriteErr($"  - download the release of bfpp that comes with zig");
        IO.WriteErr($"  - set the {ZigPathEnvVar} environment variable to a zig executable");
        IO.WriteErr($"  - set zig_path under [zig] in the project config");
        IO.WriteErr($"  - put zig on your PATH");
        return null;

        // An explicitly configured path may be the executable itself or the folder containing it.
        string? ResolveExplicit(string path, string source)
        {
            if (Directory.Exists(path))
                path = Path.Combine(path, ZigExeName);
            if (File.Exists(path))
                return Path.GetFullPath(path);
            IO.WriteErr(@$"Zig executable from {source} does not exist: ""{path}""");
            return null;
        }
    }

    /// <summary>Warns when the zig found doesn't match the version the templates were written for.</summary>
    static void CheckZigVersion(BuildIO IO, string zigExecutable)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(zigExecutable)
            {
                ArgumentList = { "version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var version = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (version != ZigVersion)
                IO.WriteLog(@$"Warning: zig at ""{zigExecutable}"" is version {version}, but bfpp expects {ZigVersion}. The build may fail.");
        }
        catch (Exception e)
        {
            IO.WriteLog($"Warning: could not check zig version: {e.Message}");
        }
    }

    static string GetTypeString(AST.Type type) => type switch
    {
        AST.Type.Void => "void",
        AST.Type.U8 => "u8",
        AST.Type.I8 => "i8",
        AST.Type.U16 => "u16",
        AST.Type.I16 => "i16",
        AST.Type.U32 => "u32",
        AST.Type.I32 => "i32",
        AST.Type.U64 => "u64",
        AST.Type.I64 => "i64",
        AST.Type.F32 => "f32",
        AST.Type.F64 => "f64",
        AST.Type.Pointer => "?*anyopaque",
        AST.Type.CUInt => "c_uint",
        AST.Type.CInt => "c_int",
        AST.Type.CULong => "c_ulong",
        AST.Type.CLong => "c_long",
        AST.Type.CLongDouble => "c_longdouble",
        AST.Type.ISize => "isize",
        AST.Type.USize => "usize",
        AST.Type.Cell => "cell",
        _ => throw new Exception("Invalid Type")
    };

    static string? GetSizeTypeString(int size) => size switch
    {
        1 => "u8",
        2 => "u16",
        4 => "u32",
        8 => "u64",
        _ => null
    };

    static string EscapeString(ReadOnlySpan<char> text) => Util.EscapeString(text, @"\u{0}");
    /// <summary>Opens a template file, preferring the project's override directory over the embedded copy.</summary>
    static Stream OpenTemplateFile(string? overrideDir, string fileName)
    {
        if (overrideDir != null && File.Exists(Path.Join(overrideDir, fileName)))
            return File.OpenRead(Path.Join(overrideDir, fileName));
        return typeof(ZigTemplater).Assembly.GetManifestResourceStream("BrainfuckPlusPlus.template." + fileName)
            ?? throw new Exception($"Missing embedded template file {fileName}");
    }
    static string StringLitteral(StringSlice? slice)
    {
        return slice.HasValue ? @$"""{EscapeString(slice.Value)}""" : "null";
    }

    static readonly byte[] IndentBytes = Encoding.UTF8.GetBytes("    ");

    public static bool Template(BuildIO IO, AST ast, ProjectSettings projSettings, ref Func<string>? outputExecutable)
    {
        var zigSettings = projSettings.zigSettings;

        if (!zigSettings.includeFormatting && zigSettings.includeComments)
        {
            IO.WriteErr("Zig config error: Cannot including comments while not including formatting");
            return false;
        }

        if (GetSizeTypeString(zigSettings.cellSize) is null)
        {
            IO.WriteErr("Zig config error: Cell size must be 1, 2, 4, or 8");
            return false;
        }


        void EmitLine(Stream stream, int indent, string line)
        {
            if (zigSettings.includeFormatting)
                for (int i = 0; i < indent; i++)
                    stream.Write(IndentBytes);

            stream.Write(Encoding.UTF8.GetBytes(line));

            if (zigSettings.includeFormatting)
                stream.WriteByte((byte)'\n');
        }

        void EmitGlobals(Stream stream)
        {
            void Emit(string line) => EmitLine(stream, 0, line);
            foreach (var global in ast.globals)
            {
                switch (global)
                {
                    case AST.FileEmbed embed:
                        if (zigSettings.includeComments)
                            Emit(@$"// {EscapeString(embed.FilePath)}");
                        Emit(@$"const embed{embed.Id}: [*]const u8 = @embedFile(""embeded\\embed{embed.Id}"");");
                        break;
                    
                    case AST.Function function:
                        void EmitIn(string line) => EmitLine(stream, 1, line);
                        if (zigSettings.includeComments)
                            Emit(@$"// {function.Name}");

                        bool returnsVoid = function.RetType == AST.Type.Void;
                        var args = function.ArgTypes.Select((type, i) => $"arg{i}: {GetTypeString(type)}");
                        var argNames = function.ArgTypes.Select((type, i) => $"arg{i}");

                        Emit(@$"fn func{function.Id}inner({string.Join(", ", args)}) !{GetTypeString(function.RetType)} {{");
                        for (int i = 0; i < function.ArgTypes.Count; i++)
                            EmitIn($"discard(arg{i});");
                        
                        EmitContext(stream, function.Body, 1, function.AssertId);
                        if (!function.ReturnIncluded && function.RetType != AST.Type.Void)
                            EmitIn(function.RetType switch
                            {
                                AST.Type.Pointer => "return null;",
                                _ => "return 0;"
                            });
                        Emit("}");

                        Emit(@$"fn func{function.Id}({string.Join(", ", args)}) callconv(.c) {GetTypeString(function.RetType)} {{");
                        if (!returnsVoid)
                            EmitIn("return");
                        EmitIn($"func{function.Id}inner({string.Join(", ", argNames)}) catch |err| functionFailed(err);");
                        Emit("}");
                        break;
                }
            }
        }
        
        void EmitContext(Stream stream, AST.Body rootBody, int indent = 0, int? assertId = null)
        {
            EmitLine(stream, indent, "var ctx = try Context.init();");
            EmitLine(stream, indent, "_ = &ctx;");
            
            if (assertId is int entryId)
                EmitLine(stream, indent, $"const entry{entryId} = ctx.tapeCursor;");

            var mutexDepth = 0;

            EmitBody(rootBody, indent, stream);

            void EmitBody(AST.Body body, int indent, Stream stream)
            {
                void Emit(string line) => EmitLine(stream, indent, line);
                foreach (var ctx in body.Tokens)
                {
                    switch (ctx.Token)
                    {
                        case AST.Modify modify:
                            Emit(modify.Amount < 0 ?
                                $"ctx.decrease({-modify.Amount});" :
                                $"ctx.increase({modify.Amount});"
                            );
                            break;
                        case AST.ModifyString modify:
                            Emit(modify.SignPositive ?
                                $@"ctx.increaseString(""{EscapeString(modify.Amounts)}"");" :
                                $@"ctx.decreaseString(""{EscapeString(modify.Amounts)}"");"
                            );
                            break;
                        case AST.Move move:
                            Emit(move.Dist < 0 ?
                                $"ctx.moveLeft({-move.Dist});" :
                                $"ctx.moveRight({move.Dist});"
                            );
                            break;
                        case AST.WhileLoop wl:
                            Emit($"while(ctx.whileCondition()) {{");
                            EmitBody(wl.Body, indent + 1, stream);
                            Emit($"}}");
                            break;
                        case AST.ForLoop fl:
                            if (fl.Count == 0) break;
                            Emit($"for(0..{fl.Count}) |_| {{");
                            EmitBody(fl.Body, indent + 1, stream);
                            Emit($"}}");
                            break;
                        case AST.InvokeBody ib:
                            if (zigSettings.includeComments)
                                Emit($"{{ // macro: {ib.Name}");
                            else
                                Emit($"{{");
                            if (ib.AssertId is int entryId)
                                Emit($"const entry{entryId} = ctx.tapeCursor;");
                            EmitBody(ib.Body, indent + (zigSettings.includeComments ? 1 : 0), stream);
                            Emit($"}}");
                            break;
                        case AST.CreateMutex:
                            Emit($"try ctx.createMutex();");
                            break;
                        case AST.MutexBody mb:
                            Emit($"{{");
                            Emit($"const mutex{mutexDepth} = try ctx.getMutex();");
                            Emit($"try mutex{mutexDepth}.lock(io);");
                            mutexDepth++;
                            EmitBody(mb.Body, indent + (zigSettings.includeComments ? 1 : 0), stream);
                            mutexDepth--;
                            Emit($"mutex{mutexDepth}.unlock(io);");
                            Emit($"}}");
                            break;
                        case AST.Print:
                            Emit($"try ctx.print();");
                            break;
                        case AST.Read:
                            Emit($"try ctx.read();");
                            break;
                        case AST.WaitMS waitMs:
                            if (waitMs.Delay == 0)
                                Emit("ctx.resetLastTime();");
                            else
                                Emit($"ctx.waitMs({waitMs.Delay});");
                            break;
                        case AST.DebugPrint dbPrint:
                            if (dbPrint.Width == 1)
                                Emit($@"ctx.printCell({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {StringLitteral(dbPrint.Message)});");
                            else
                                Emit($@"ctx.printDump({dbPrint.Width}, {ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {StringLitteral(dbPrint.Message)});");
                            break;
                        case AST.DebugAssert assert:
                            Emit($@"ctx.assert({assert.Index}, {ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {StringLitteral(assert.Message)});");
                            break;
                        case AST.DebugAssertRelative assertRelative:
                            Emit($@"ctx.assertRelative({assertRelative.Offset}, entry{assertRelative.EntryId}, {ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {StringLitteral(assertRelative.Message)});");
                            break;
                        case AST.DebugPrintLitteral dbLitteral:
                            Emit($@"ctx.printDebugMessage({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", ""{EscapeString(dbLitteral.Message)}"");");
                            break;
                        case AST.DebugQuit quit:
                            Emit($@"ctx.debugQuit({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {StringLitteral(quit.Message)});");
                            break;
                        case AST.TakeReference:
                            Emit($"ctx.takeReference();");
                            break;
                        case AST.Dereference:
                            Emit($"ctx.dereference();");
                            break;
                        case AST.WriteToReference:
                            Emit($"ctx.writeToReference();");
                            break;
                        case AST.GetEmbedReference embedRef:
                            Emit($"ctx.writeReference(embed{embedRef.EmbedId});");
                            break;
                        case AST.GetFunction getFunc:
                            Emit($"ctx.writeReference(&func{getFunc.Id});");
                            break;
                        case AST.GetFunctionParam getFuncParam:
                            Emit($"ctx.writeReference(&arg{getFuncParam.Index});");
                            break;
                        case AST.GetMainParam getMainParam:
                            Emit($"ctx.writeReference(@ptrCast(&{getMainParam.Index switch
                            {
                                0 => "mainArgc",
                                1 => "mainArgv",
                                _ => null
                            }}));");
                            break;
                        case AST.Return @return:
                            if (@return.Type is AST.Type.Void)
                                Emit("return;");
                            else
                                Emit($"return Context.readTapeValue({GetTypeString(@return.Type)}, ctx.tapeCursor);");
                            break;
                        case AST.FindExternFunction:
                            Emit($@"try ctx.findExternFunction({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"");");
                            break;
                        case AST.PrepareExternCaller:
                            Emit($@"try ctx.createExternCaller({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"");");
                            break;
                        case AST.CallExternFunction:
                            Emit($"try ctx.callExternFunction();");
                            break;
                        case AST.SpawnThread spawn:
                            Emit($"try ctx.spawnThread(func{spawn.FuncId});");
                            break;
                        case AST.JoinThread:
                            Emit($"try ctx.joinThread();");
                            break;
                        case AST.SetRawInput:
                            Emit($"try ctx.setRawInput();");
                            break;
                        case AST.Comment comment:
                            if (zigSettings.includeComments)
                                Emit($"//{comment.Content}");
                            break;
                        default: break;
                    }
                }
            }
        }
        
        var overrideDir = zigSettings.templateOverrides != null ? Path.Combine(projSettings.projectDir, zigSettings.templateOverrides) : null;

        // Resolve zig before templating so a missing zig fails fast.
        string? zigExecutable = null;
        if (zigSettings.buildAfterTemplate)
        {
            zigExecutable = FindZig(IO, zigSettings, projSettings.projectDir);
            if (zigExecutable is null)
                return false;
            CheckZigVersion(IO, zigExecutable);
        }

        using var templateStream = new BufferedStream(OpenTemplateFile(overrideDir, "template.zig"));

        Directory.CreateDirectory(Path.Join(projSettings.projectDir, "build-zig"));
        var fileStream = File.Create(Path.Join(projSettings.projectDir, "build-zig/template.zig"));

        var embedFolder = Path.Join(projSettings.projectDir, "build-zig", "embeded");
        if (Directory.Exists(embedFolder))
            Directory.Delete(embedFolder, recursive: true);
        Directory.CreateDirectory(embedFolder);

        foreach (var fileEmbed in ast.globals.WhereType<AST.GlobalToken, AST.FileEmbed>())
        {
            File.Copy(fileEmbed.FilePath, Path.Join(embedFolder, "embed" + fileEmbed.Id));
        }

        var insertFlaged = false;
        var skipNext = false;
        while (true)
        {
            var @byte = templateStream.ReadByte();

            if (@byte == -1)
                break;

            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            fileStream.WriteByte((byte)@byte);

            if (@byte == '#')
                insertFlaged = true;

            else if (insertFlaged)
            {
                insertFlaged = false;
                fileStream.WriteByte((byte)'\n');
                switch (@byte)
                {
                    case 'g':
                        EmitGlobals(fileStream);
                        break;

                    case 'm':
                        EmitContext(fileStream, ast.body, 1);
                        if (!ast.ReturnIncluded)
                            EmitLine(fileStream, 1,"return 0;");
                        fileStream.Write(Encoding.UTF8.GetBytes($" //"));
                        skipNext = true;
                        break;

                    case 'f':
                        fileStream.Write(Encoding.UTF8.GetBytes($"{(zigSettings.ignoreError.Contains("findExternFunction") ? "true" : "false")}; //"));
                        skipNext = true;
                        break;
                    
                    case 'c':
                        fileStream.Write(Encoding.UTF8.GetBytes($"{(zigSettings.ignoreError.Contains("createExternCaller") ? "true" : "false")}; //"));
                        skipNext = true;
                        break;
                    
                    case 't':
                        fileStream.Write(Encoding.UTF8.GetBytes($"{GetSizeTypeString(zigSettings.cellSize)}; //"));
                        skipNext = true;
                        break;
                }
            }
        }

        fileStream.Close();


        void CopyFile(string name) {
            using var source = OpenTemplateFile(overrideDir, name);
            using var dest = File.Create(Path.Join(projSettings.projectDir, "build-zig", name));
            source.CopyTo(dest);
        }
        CopyFile("build.zig");
        CopyFile("build.zig.zon");
        CopyFile("DynLib.zig");
        IO.WriteLog("Zig Template Completed");

        if (zigExecutable is not null)
        {
            try
            {
                var procInfo = new ProcessStartInfo(zigExecutable)
                {
                    ArgumentList = { "build" },
                    WorkingDirectory = Path.Join(Path.Join(projSettings.projectDir, "build-zig"))
                };
                zigSettings.args.ForEach(procInfo.ArgumentList.Add);

                var proc = Process.Start(procInfo)!;

                proc.WaitForExit();
                if (proc.ExitCode != 0)
                    return false;
            }
            catch (Exception e)
            {
                IO.WriteErr(e.Message);
                return false;
            }

            IO.WriteLog("Zig Build Completed");

            outputExecutable = () => Path.Join(projSettings.projectDir, $"build-zig/zig-out/bin/brainfuck{ExeSuffix}");
        }

        return true;
    }
}
