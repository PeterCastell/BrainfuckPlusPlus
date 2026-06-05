using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Brainfuck;


public static class BfTemplater
{
    
    
    public static readonly Templater Template = (ast, settings, ref runCommand) =>
    {
        var indentBytes = Encoding.UTF8.GetBytes("    ");
        var commentBytes = Encoding.UTF8.GetBytes("// ");

        bool compact = settings.extraArgs.ContainsKey("compact-bf");
        
        void EmitBody(AST.Body body, int indent, Stream stream)
        {
            void Emit(string line, long count = 1, bool debug = false)
            {
                if (debug && settings.releaseMode == TemplateSettings.ReleaseMode.ReleaseFast && settings.includeComments == false)
                    return;
                if (settings.includeWhitespace)
                    for (int i = 0; i < indent; i++)
                        stream.Write(indentBytes);

                if (debug && settings.releaseMode == TemplateSettings.ReleaseMode.ReleaseFast)
                    stream.Write(commentBytes);
                
                var bytes = Encoding.UTF8.GetBytes(line);
                for (int i = 0; i < count; i++)
                    stream.Write(bytes);
                
                if (settings.includeWhitespace)
                    stream.WriteByte((byte)'\n');
            }
            string BlankOne(long num) => num != 1 ? num.ToString() : "";
            foreach (var ctx in body.Tokens)
            {
                switch (ctx.Token)
                {
                    case AST.Modify modify:
                        if (compact)
                            Emit((modify.Amount < 0 ? $"-" : $"+") + BlankOne(Math.Abs(modify.Amount)));
                        else
                            Emit(modify.Amount < 0 ? $"-" : $"+", Math.Abs(modify.Amount));
                        break;
                    case AST.ModifyString modify:
                        Emit(modify.SignPositive ?
                            $@"+""{modify.Amounts}""" :
                            $@"-""{modify.Amounts}"""
                        );
                        break;
                    case AST.Move move:
                        if (compact)
                            Emit((move.Dist < 0 ? $"<" : $">") + BlankOne(Math.Abs(move.Dist)));
                        else
                            Emit(move.Dist < 0 ? $"<" : $">", Math.Abs(move.Dist));
                        break;
                    case AST.WhileLoop wl:
                        Emit($"[");
                        EmitBody(wl.Body, indent + 1, stream);
                        Emit($"]");
                        break;
                    case AST.ForLoop fl:
                        if (fl.Count == 0) break;
                        if (compact)
                        {
                            Emit($"{{{BlankOne(fl.Count)}");
                            EmitBody(fl.Body, indent + 1, stream);
                            Emit($"}}");
                        }
                        else
                            for (int i = 0; i < fl.Count; i++)
                                EmitBody(fl.Body, indent + 1, stream);
                        break;
                    case AST.InvokeBody ib:
                        if (settings.includeComments)
                            Emit($"# begin: {ib.Name}");
                        EmitBody(ib.Body, indent + (settings.includeComments?1:0), stream);
                        if (settings.includeComments)
                            Emit($"# end: {ib.Name}");
                        break;
                    case AST.Print:
                        Emit($".");
                        break;
                    case AST.Read:
                        Emit($",");
                        break;
                    case AST.WaitMS waitMs:
                        Emit($"!{BlankOne(waitMs.Delay)}");
                        break;
                    case AST.DebugPrint dbPrint:
                        if (dbPrint.Message is not null)
                            Emit($@"@.{BlankOne(dbPrint.Width)}""{dbPrint.Message.Value}""", debug: true);
                        else
                            Emit($@"@.{BlankOne(dbPrint.Width)}", debug: true);
                        break;
                    case AST.DebugAssert assert:
                        if (assert.Message is not null)
                            Emit($@"@{assert.Index}""{assert.Message.Value}""", debug: true);
                        else
                            Emit($@"@{assert.Index}", debug: true);
                        break;
                    case AST.DebugPrintLitteral dbLitteral:
                        Emit($@"@""{dbLitteral.Message}""", debug: true);
                        break;
                    case AST.DebugQuit quit:
                        Emit($@"@!", debug: true);
                        break;
                    case AST.TakeReference:
                        Emit($"*");
                        break;
                    case AST.Dereference:
                        Emit($"~");
                        break;
                    case AST.FindExternFunction:
                        Emit($"%*");
                        break;
                    case AST.PrepareExternFunction:
                        Emit($"%&");
                        break;
                    case AST.CallExternFunction:
                        Emit($"%$");
                        break;
                    case AST.GetEmbedReference embed:
                        var filePath = ast.globals
                            .WhereType<AST.GlobalToken, AST.FileEmbed>()
                            .First(fe => fe.Id == embed.EmbedId)
                            .FilePath;
                        Emit($@"$embed_file(""{filePath}"")");
                        break;
                    case AST.Comment comment:
                        if (settings.includeComments)
                            Emit($"#{comment.Content}");
                        break;
                    default: break;
                }
            }
        }
        
        Console.WriteLine("Templating...");
        
        Directory.CreateDirectory(Path.Join(settings.projectDir, "build-bf"));
        var fileStream = File.Create(Path.Join(settings.projectDir, "build-bf", settings.projectName + ".bfpp"));

        EmitBody(ast.body, 1, fileStream);

        fileStream.Close();

        Console.WriteLine("Bf Build Completed");
        
        return true;
    };
}