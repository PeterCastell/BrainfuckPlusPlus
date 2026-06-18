using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Brainfuck;


public static class BfTemplater
{
    public class BfSettings : CommonSettings
    {
        public bool includeDebugOperators { get; set; } = true;
        public bool compactOperators { get; set; } = true;
    }
    
    public static bool Template(AST ast, ProjectSettings projSettings)
    {
        var bfSettings = projSettings.bfSettings;

        var indentBytes = Encoding.UTF8.GetBytes("    ");
        var commentBytes = Encoding.UTF8.GetBytes("//");

        void EmitGlobals(Stream stream)
        {
            void Emit(string line)
            {
                stream.Write(Encoding.UTF8.GetBytes(line));

                if (bfSettings.includeFormatting)
                    stream.WriteByte((byte)'\n');
            }

            void EmitComment(string contents)
            {
                if (!bfSettings.includeComments) return;
                if (bfSettings.includeFormatting)
                    Emit("#" + contents);
                else
                    Emit("#" + contents + "\\#");
            }

            foreach (var global in ast.globals)
            {
                switch (global)
                {
                    case AST.Function function:
                        EmitComment(function.Name.ToString());

                        if (function.ArgTypes.Count == 0)
                            Emit($"&*func{function.Id}~{AST.GetTypeIndex(function.RetType)}(");
                        else
                            Emit($"&*func{function.Id}~{AST.GetTypeIndex(function.RetType)}~{string.Join(",", function.ArgTypes.Select(t=>AST.GetTypeIndex(t)))}(");
                        EmitBody(function.Body, 1, stream);
                        Emit(")");
                        break;
                }
            }
        }
        
        void EmitBody(AST.Body body, int indent, Stream stream)
        {
            void Emit(string line, long count = 1, bool debug = false)
            {
                if (debug && !bfSettings.includeDebugOperators)
                    return;

                if (bfSettings.includeFormatting)
                    for (int i = 0; i < indent; i++)
                        stream.Write(indentBytes);

                var bytes = Encoding.UTF8.GetBytes(line);
                for (int i = 0; i < count; i++)
                    stream.Write(bytes);

                if (bfSettings.includeFormatting)
                    stream.WriteByte((byte)'\n');
            }
            void EmitComment(string contents)
            {
                if (!bfSettings.includeComments) return;
                if (bfSettings.includeFormatting)
                    Emit("#" + contents);
                else
                    Emit("#" + contents + "\\#");
            }
            string BlankZero(long num) => num != 0 ? num.ToString() : "";
            string BlankOne(long num) => num != 1 ? num.ToString() : "";
            foreach (var ctx in body.Tokens)
            {
                switch (ctx.Token)
                {
                    case AST.Modify modify:
                        if (bfSettings.compactOperators)
                            Emit((modify.Amount < 0 ? $"-" : $"+") + BlankOne(Math.Abs(modify.Amount)));
                        else
                            Emit(modify.Amount < 0 ? $"-" : $"+", Math.Abs(modify.Amount));
                        break;
                    case AST.ModifyString modify:
                        Emit(modify.SignPositive ?
                            $@"+""{Util.EscapeString(modify.Amounts)}""" :
                            $@"-""{Util.EscapeString(modify.Amounts)}"""
                        );
                        break;
                    case AST.Move move:
                        if (bfSettings.compactOperators)
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
                        if (bfSettings.compactOperators)
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
                        EmitComment($" macro: {ib.Name}");
                        EmitBody(ib.Body, indent + (bfSettings.includeComments?1:0), stream);
                        break;
                    case AST.CreateMutex:
                        Emit($"^&");
                        break;
                    case AST.MutexBody ib:
                        Emit($"^{{");
                        EmitBody(ib.Body, indent + (bfSettings.includeComments?1:0), stream);
                        Emit("}");
                        break;
                    case AST.Print:
                        Emit($".");
                        break;
                    case AST.Read:
                        Emit($",");
                        break;
                    case AST.WaitMS waitMs:
                        Emit($"!{BlankZero(waitMs.Delay)}");
                        break;
                    case AST.DebugPrint dbPrint:
                        if (dbPrint.Message is not null)
                            Emit($@"@.{BlankOne(dbPrint.Width)}""{Util.EscapeString(dbPrint.Message.Value)}""", debug: true);
                        else
                            Emit($@"@.{BlankOne(dbPrint.Width)}", debug: true);
                        break;
                    case AST.DebugAssert assert:
                        if (assert.Message is not null)
                            Emit($@"@{assert.Index}""{Util.EscapeString(assert.Message.Value)}""", debug: true);
                        else
                            Emit($@"@{assert.Index}", debug: true);
                        break;
                    case AST.DebugQuit quit:
                        if (quit.Message is not null)
                            Emit($@"@!""{Util.EscapeString(quit.Message.Value)}""", debug: true);
                        else
                            Emit($@"@!", debug: true);
                        break;
                    case AST.DebugPrintLitteral dbLitteral:
                        Emit($@"@""{Util.EscapeString(dbLitteral.Message)}""", debug: true);
                        break;
                    case AST.TakeReference:
                        Emit($"*");
                        break;
                    case AST.Dereference:
                        Emit($"~");
                        break;
                    case AST.GetEmbedReference embed:
                        var filePath = ast.globals
                            .WhereType<AST.GlobalToken, AST.FileEmbed>()
                            .First(fe => fe.Id == embed.EmbedId)
                            .FilePath;
                        Emit($@"$embed_file(""{filePath}"")");
                        break;
                    case AST.GetFunction getFunc:
                        Emit($"$*func{getFunc.Id}");
                        break;
                    case AST.GetFunctionParam getFuncParam:
                        Emit($"$*{getFuncParam.Index}");
                        break;
                    case AST.GetMainParam getMainParam:
                        Emit($"$*{getMainParam.Index}");
                        break;
                    case AST.Return @return:
                        Emit("$!");
                        break;
                    case AST.FindExternFunction:
                        Emit("%*");
                        break;
                    case AST.PrepareExternCaller:
                        Emit("%&");
                        break;
                    case AST.CallExternFunction:
                        Emit("%$");
                        break;
                    case AST.SpawnThread spawn:
                        Emit($"^$func{spawn.FuncId}");
                        break;
                    case AST.JoinThread:
                        Emit("^!");
                        break;
                    case AST.SetRawInput:
                        Emit("!.");
                        break;
                    case AST.Comment comment:
                        EmitComment(comment.Content.ToString());
                        break;
                    default: break;
                }
            }
        }
        
        Directory.CreateDirectory(Path.Join(projSettings.projectDir, "build-bf"));
        using var fileStream = File.Create(Path.Join(projSettings.projectDir, "build-bf", projSettings.projectName + ".bfpp"));

        EmitGlobals(fileStream);
        EmitBody(ast.body, 0, fileStream);

        Console.WriteLine("Bf Build Completed");
        
        return true;
    }
}