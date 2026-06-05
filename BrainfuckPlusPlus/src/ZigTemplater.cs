using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Brainfuck;


public static class ZigTemplater
{
    
    static void CreateLocalZigFiles()
    {
        var assembly = typeof(ZigTemplater).Assembly;
        var localPath = Path.GetDirectoryName(assembly.Location)!;
        
        var localZigPath = Path.Combine(localPath, "zig");
        
        if (!Directory.Exists(localZigPath))
        {
            Console.WriteLine("Unpacking zig...");
            Directory.CreateDirectory(localZigPath);
            using var zip = new ZipArchive(assembly.GetManifestResourceStream("BrainfuckPlusPlus.template.zig-x86_64-windows-0.17.0-dev.702+18b3c78a9.zip")!);

            zip.ExtractToDirectory(localZigPath);

            void CreateFile(string fileName)
            {
                using var fileStream = File.Create(Path.Combine(localZigPath, fileName));
                using var resourceStream = assembly.GetManifestResourceStream("BrainfuckPlusPlus.template." + fileName)!;
                resourceStream.CopyTo(fileStream);
            }
            CreateFile("build.zig");
            CreateFile("build.zig.zon");
            CreateFile("template.zig");
        }
    }

    public static readonly Templater Template = (ast, settings, ref runCommand) =>
    {
        var indentBytes = Encoding.UTF8.GetBytes("    ");

        void EmitLine(Stream stream, int indent, string line)
        {
            if (settings.includeWhitespace)
                for (int i = 0; i < indent; i++)
                    stream.Write(indentBytes);

            stream.Write(Encoding.UTF8.GetBytes(line));

            if (settings.includeWhitespace)
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
                        Emit($@"const embed{embed.Id}: [*]const u8 = @embedFile(""{embed.FilePath.Replace(@"\", @"\\")}"");");
                        break;
                }
            }
        }
        
        void EmitBody(AST.Body body, int indent, Stream stream)
        {
            void Emit(string line) => EmitLine(stream, indent, line);
            foreach (var ctx in body.Tokens)
            {
                switch (ctx.Token)
                {
                    case AST.Modify modify:
                        Emit(modify.Amount < 0 ?
                            $"decrease({-modify.Amount});" :
                            $"increase({modify.Amount});"
                        );
                        break;
                    case AST.ModifyString modify:
                        Emit(modify.SignPositive ?
                            $@"increaseString(""{modify.Amounts}"");" :
                            $@"decreaseString(""{modify.Amounts}"");"
                        );
                        break;
                    case AST.Move move:
                        Emit(move.Dist < 0 ?
                            $"moveLeft({-move.Dist});" :
                            $"moveRight({move.Dist});"
                        );
                        break;
                    case AST.WhileLoop wl:
                        Emit($"while(whileCondition()) {{");
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
                        if (settings.includeComments)
                            Emit($"// begin: {ib.Name}");
                        if (ib.EntryId is int entryId)
                            Emit($"{{ const entry{entryId} = tapeCursor;");
                        EmitBody(ib.Body, indent + (settings.includeComments?1:0), stream);
                        if (ib.EntryId is not null)
                            Emit("}");
                        if (settings.includeComments)
                            Emit($"// end: {ib.Name}");
                        break;
                    case AST.Print:
                        Emit($"try print();");
                        break;
                    case AST.Read:
                        Emit($"try read();");
                        break;
                    case AST.WaitMS waitMs:
                        Emit($"waitMs({waitMs.Delay});");
                        break;
                    case AST.DebugPrint dbPrint:
                        if (dbPrint.Width == 1)
                            Emit($@"printCell({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {dbPrint.Message?.ToString() ?? "null"});");
                        else
                            Emit($@"printDump({dbPrint.Width}, {ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {dbPrint.Message?.ToString() ?? "null"});");
                        break;
                    case AST.DebugAssert assert:
                        Emit($@"assert({assert.Index}, {ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {assert.Message?.ToString() ?? "null"});");
                        break;
                    case AST.DebugAssertRelative assertRelative:
                        Emit($@"assertRelative({assertRelative.Offset}, entry{assertRelative.EntryId}, {ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", {assertRelative.Message?.ToString() ?? "null"});");
                        break;
                    case AST.DebugPrintLitteral dbLitteral:
                        Emit($@"printDebugMessage({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"", ""{dbLitteral.Message}"");");
                        break;
                    case AST.DebugQuit quit:
                        Emit($@"debugQuit({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"");");
                        break;
                    case AST.TakeReference:
                        Emit($"takeReference();");
                        break;
                    case AST.Dereference:
                        Emit($"dereference();");
                        break;
                    case AST.GetEmbedReference embedRef:
                        Emit($"getEmbedReference(embed{embedRef.EmbedId});");
                        break;
                    case AST.FindExternFunction:
                        Emit($@"try findExternFunction({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"");");
                        break;
                    case AST.PrepareExternFunction:
                        Emit($@"try createExternCaller({ctx.Start.Row}, {ctx.Start.Column}, ""{ctx.File}"");");
                        break;
                    case AST.CallExternFunction:
                        Emit($"try callExternFunction();");
                        break;
                    case AST.Comment comment:
                        if (settings.includeComments)
                            Emit($"//{comment.Content}");
                        break;
                    default: break;
                }
            }
        }

        CreateLocalZigFiles();
        
        Console.WriteLine("Templating...");
        
        var assembly = typeof(ZigTemplater).Assembly;
        var localZigPath = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "zig");
        
        using var templateStream = File.OpenRead(Path.Join(localZigPath, "template.zig"));

        Directory.CreateDirectory(Path.Join(settings.projectDir, "build-zig"));
        var fileStream = File.Create(Path.Join(settings.projectDir, "build-zig/template.zig"));

        var insertFlaged = false;
        while (true)
        {
            var @byte = templateStream.ReadByte();

            if (@byte == -1)
                break;

            fileStream.WriteByte((byte)@byte);

            if (@byte == '#')
                insertFlaged = true;

            else if (insertFlaged)
            {
                insertFlaged = false;
                switch (@byte)
                {
                    case 'g':
                        fileStream.WriteByte((byte)'\n');
                        EmitGlobals(fileStream);
                        break;
                    
                    case 'm':
                        fileStream.WriteByte((byte)'\n');
                        EmitBody(ast.body, 1, fileStream);
                        break;
                }
            }
        }

        fileStream.Close();
        
        File.Copy(Path.Join(localZigPath, "build.zig"), Path.Join(settings.projectDir, "build-zig/build.zig"), true);
        File.Copy(Path.Join(localZigPath, "build.zig.zon"), Path.Join(settings.projectDir, "build-zig/build.zig.zon"), true);
        Console.WriteLine("Zig Template Completed");

        if (settings.buildAfterTemplate)
        {
            var zigExecutable = Path.Join(Path.GetDirectoryName(assembly.Location)!, "zig/zig-x86_64-windows-0.17.0-dev.702+18b3c78a9/zig.exe");

            var optimizeFlag = settings.releaseMode switch
            {
                TemplateSettings.ReleaseMode.Debug       => "-Doptimize=Debug",
                TemplateSettings.ReleaseMode.ReleaseSafe => "-Doptimize=ReleaseSafe",
                TemplateSettings.ReleaseMode.ReleaseFast => "-Doptimize=ReleaseFast",
                _ => throw new ArgumentOutOfRangeException()
            };

            var proc = Process.Start(new ProcessStartInfo(zigExecutable)
            {
                ArgumentList = { "build", optimizeFlag, "-Dtarget=x86_64-windows", "-Dcpu=baseline" },
                WorkingDirectory = Path.Join(Path.Join(settings.projectDir, "build-zig"))
            })!;

            proc.WaitForExit();
            if (proc.ExitCode != 0)
                return false;
            
            Console.WriteLine("Zig Build Completed");
            
            if (settings.launchAfterBuild)
            {
                runCommand = () =>
                {
                    Console.WriteLine("Running Zig Build...");
                    var executable = Path.Join(settings.projectDir, "build-zig/zig-out/bin/brainfuck.exe");
                    var proc2 = Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = settings.projectDir })!;
                    proc2.WaitForExit();
                };
            }
        }
        
        return true;
    };
}
