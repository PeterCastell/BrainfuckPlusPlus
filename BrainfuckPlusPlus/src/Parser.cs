namespace Brainfuck;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

public class Parser(Stream? errorStream = null)
{
    readonly StreamWriter errorWriter = new(errorStream ?? Console.OpenStandardOutput());

    readonly ConcurrentDictionary<string, AST.FileEmbed> fileEmbeds = [];
    int nextFileEmbedId = 0;
    int EmbedFile(string fileName)
    {
        return fileEmbeds.GetOrAdd(fileName, key => new(nextFileEmbedId++, key)).Id;
    }
    
    class ParseException(string message, string afterMessage, TokenPosition start, TokenPosition end, string source, string filename) : Exception
    {
        public string message = message;
        public string afterMessage = afterMessage;
        public TokenPosition start = start;
        public TokenPosition end = end;
        public string source = source;
        public string filename = filename;
    }
    
    public static string GetLinePreview(TokenPosition start, TokenPosition end, string source)
    {
        StringBuilder builder = new();
        if (start.Row == end.Row)
        {
            for (int i = start.RowStart; i < source.Length && source[i] != '\n'; i++)
                builder.Append(source[i]);
            builder.Append('\n');
            for (int i = 0; i <= end.Column - 2; i++)
                if (i < start.Column - 1)
                    builder.Append(' ');
                else if (i == start.Column - 1)
                    builder.Append('^');
                else
                    builder.Append('~');
        }
        else
        {
            int rowLength = 0;
            int rowStart;
            bool Condition(int i) 
            {
                rowStart = i + 1;
                return source[i] != '\n';
            }
            
            for (int i = start.RowStart; Condition(i); i++)
            {
                if (!char.IsWhiteSpace(source[i])) rowLength = Math.Max(rowLength, i - start.RowStart + 1);
                builder.Append(source[i]);
            }
            builder.Append('\n');
            for (int i = 0; i < rowLength; i++)
                if (i < start.Column - 1)
                    builder.Append(' ');
                else if (i == start.Column - 1)
                    builder.Append('^');
                else
                    builder.Append('~');
            builder.Append('\n');
            
            int rowPad;
            for (int row = start.Row + 1; row < end.Row; row++)
            {
                int currentRowStart = rowStart;
                (rowPad, rowLength) = (-1, 0);
                for (int i = rowStart; Condition(i); i++)
                {
                    if (rowPad == -1 && !char.IsWhiteSpace(source[i])) rowPad = i - currentRowStart;
                    if (!char.IsWhiteSpace(source[i])) rowLength = Math.Max(rowLength, i - currentRowStart + 1);
                    builder.Append(source[i]);
                }
                builder.Append('\n');
                for (int i = 0; i < rowLength; i++)
                    builder.Append(i < rowPad ? ' ' : '~');
                builder.Append('\n');
            }
            
            (rowPad, rowLength) = (-1, 0);
            for (int i = end.RowStart; i < source.Length && source[i] != '\n'; i++)
            {
                if (rowPad == -1 && !char.IsWhiteSpace(source[i])) rowPad = i - end.RowStart;
                if (!char.IsWhiteSpace(source[i])) rowLength = Math.Max(rowLength, i + 1);
                builder.Append(source[i]);
            }
            builder.Append('\n');
            for (int i = 0; i < end.Column-1; i++)
                builder.Append(i < rowPad ? ' ' : '~');
        }
        return builder.ToString();
    }
    
    LexicalScope? InternalParse(string path, Func<LexicalScope, LexicalScope>? extraStep)
    {
        string uri = new Uri(path).AbsoluteUri;
        string source;
        try
        {
            source = LSP.FileCache.cache.TryGetValue(uri, out var cached)
                ? cached
                : File.ReadAllText(path);
        }
        catch (Exception e)
        {
            errorWriter.WriteLine("File Error");
            errorWriter.WriteLine(e.Message);
            errorWriter.Flush();
            return null;
        }
        
        string directory = Path.GetDirectoryName(path)!;
        string filename = Path.GetFileName(path);
        
        try
        {
            var tokens = Tokenize(source, filename);

            var rootContext = ParseTree(tokens, filename, uri);
            
            rootContext = CompleteMacros(rootContext, directory);

            if (extraStep is not null)
                rootContext = extraStep(rootContext);

            return rootContext;
        }
        catch (ParseException ex)
        {
            errorWriter.WriteLine($"Error (line {ex.start.Row}, col {ex.start.Column} in {ex.filename}): {ex.message}");
            errorWriter.WriteLine(GetLinePreview(ex.start, ex.end, ex.source));
            errorWriter.WriteLine(ex.afterMessage);
            errorWriter.Flush();
            
            return null;
        }
    }

    public AST? Parse(string path)
    {
        var scope = InternalParse(path, null);

        if (scope is null)
            return null;

        var ast = CreateASTBodyFromLexicalScope(scope);
        
        ast = CompressOperations(ast);

        ast = Cleanup(ast);

        var globals = new List<AST.GlobalToken>();
        globals.AddRange(fileEmbeds.Values);
        return new(ast, globals);
    }
    LexicalScope? IncompleteParse(string path)
    {
        return InternalParse(path, null);
    }
    
    public record struct Token
    {
        [AttributeUsage(AttributeTargets.Field)]
        class TypeRegex(string regexStr) : Attribute
        {
            public readonly Regex regex = new(@"\G" + regexStr);
            public static readonly Regex[] AllRegexes = typeof(Type).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.GetCustomAttribute<TypeRegex>()!.regex).ToArray();
        }
        public static Regex GetRegex(Type type) => TypeRegex.AllRegexes[(int)type];
        public enum Type
        {
            [TypeRegex(@"\+")] Add,
            [TypeRegex(@"\-")] Subtract,
            [TypeRegex(@"\<")] MoveLeft,
            [TypeRegex(@"\>")] MoveRight,

            [TypeRegex(@"\@\$")] AssertRelative,
            [TypeRegex(@"\@\.")] DebugPrint,
            [TypeRegex(@"\@\!")] DebugQuit,
            [TypeRegex(@"\@")] Assert,

            [TypeRegex(@"\%\*")] ExternGet,
            [TypeRegex(@"\%\$")] ExternInvoke,
            [TypeRegex(@"\%\&")] ExternDefine,

            [TypeRegex(@"\.")] Print,
            [TypeRegex(@"\,")] Read,
            [TypeRegex(@"\[")] OpenWhileLoop,
            [TypeRegex(@"\]")] CloseWhileLoop,
            [TypeRegex(@"\{")] OpenForLoop,
            [TypeRegex(@"\}")] CloseForLoop,
            [TypeRegex(@"\&\$")] ExportMacro,
            [TypeRegex(@"\&")] DefineMacro,
            [TypeRegex(@"\$")] InvokeMacro,
            [TypeRegex(@"\(")] OpenMacro,
            [TypeRegex(@"\)")] CloseMacro,

            [TypeRegex(@"\!")] Wait,
            [TypeRegex(@"\*")] TakeReference,
            [TypeRegex(@"\~")] Dereference,

            [TypeRegex(@"\#.*")] Comment,
            [TypeRegex(@"\d+")] Number,
            [TypeRegex(@"\'.\'")] Character,
            [TypeRegex(@"(?=\D)\w+")] Name,
            [TypeRegex(@"\"".*\""")] String
        }
        public required Type type;
        public required StringSlice content;
        public required TokenPosition position;
        public readonly int Length => content.Length;
        public readonly TokenPosition EndPosition => position with { Column = position.Column + Length, Position = position.Position + Length };

        public override readonly string ToString() => $"Token.{type} {{ Row = {position.Row}, Column = {position.Column}, Content = \"{content}\" }}";
    }
    
    static List<Token> Tokenize(string source, string filename)
    {
        List<Token> tokens = [];
        
        int position = 0;
        int column = 1;
        int row = 1;
        int rowStart = 0;
        
        void SkipChar()
        {
            if (source[position] == '\n')
            {
                row++;
                column = 1;
                rowStart = position+1;
            }
            else
                column++;
            position++;
        }
        
        while (position < source.Length)
        {
            if (char.IsWhiteSpace(source[position]))
            {
                SkipChar();
                continue;
            }
            var tokenPosition = new TokenPosition(column, row, rowStart, position);
            foreach (var type in Enum.GetValues<Token.Type>())
            {
                var match = Token.GetRegex(type).Match(source, position);
                if (!match.Success) continue;
                tokens.Add(new()
                {
                    type = type,
                    content = new(source, match.Index, match.Length),
                    position = tokenPosition,
                });

                position += match.Length;
                column += match.Length;
                
                goto MatchFound;
            }
            throw new ParseException("Unexpected Symbol", "", tokenPosition, tokenPosition, source, filename);

        MatchFound:;
        }

        return tokens;
        
    }
    
    enum BodyType
    {
        Root,
        While,
        For,
        Macro,
        MacroArg
    }

    record LexicalScope(BodyType Type, AST.Body Body, LexicalScope? Parent)
    {
        public AST.TokenContext? Owner;
        public LexicalScope? NewScope;
        
        public override string ToString() => $"LexicalScope {{ Type = {Type}, Body = {Body}, Parent = {(Parent is null ? "null" : "...")} }}";
    }

    record CallContex(AST.TokenContext? Site, LexicalScope Scope, List<LexicalScope> Args)
    {
        public override string ToString() => $"CallContext {{ Site = {Site}, Scope = {Scope}, Args = [{Args.Count}] }}";
    }
    

    record ASTMacroDefine(StringSlice Name, LexicalScope Scope, bool IsExporting) : AST.Token
    {
        static int nextAssertRefId = 0;
        public int? assertRefId;
        public int PutAssertRefId => assertRefId ?? (assertRefId = nextAssertRefId++).Value;
        
        public override string ToString() => $"ASTMacroDefine {{ Name = {Name}, Scope = {Scope}, IsExporting = {IsExporting} }}";
    }
    record ASTParameter(int Index, int Depth, List<LexicalScope> Args) : AST.Token;
    record ASTMacroInvoke(StringSlice Name, int Depth, List<LexicalScope> Args) : AST.Token;
    record ASTMacroExport(StringSlice Name) : AST.Token;
    record ASTMacroExportLinked(StringSlice Name, ASTMacroDefine Target) : AST.Token;

    record LexWhileLoop(LexicalScope Scope) : AST.Token;
    record LexForLoop(LexicalScope Scope, long Count) : AST.Token;
    record LexInvokeBody(StringSlice Name, LexicalScope Scope) : AST.Token;

    record LexDebugAssertRelative(long Offset, int Depth, StringSlice? Message) : AST.Token;

    static LexicalScope ParseTree(List<Token> tokens, string filename, string fileUri)
    {
        List<LexicalScope> stack = [new(BodyType.Root, new(), null)];
        int position = 0;

        ParseException Except(string message, string? afterMessage = null) => new(
            message,
            afterMessage ?? "",
            tokens[position].position,
            tokens[position].EndPosition,
            tokens[position].content.Source,
            filename
        );

        Token? GetNext(Token.Type type, ref TokenPosition end)
        {
            if (position + 1 < tokens.Count && tokens[position + 1].type == type)
            {
                position++;
                end = tokens[position].EndPosition;
                return tokens[position];
            }
            return null;
        }
        long? GetNextNumber64(ref TokenPosition end) => GetNext(Token.Type.Number, ref end) is Token t ? long.TryParse(t.content, out var n) ? n : throw Except("Number is too large") : null;
        long GetNextNumber64Or1(ref TokenPosition end) => GetNextNumber64(ref end) ?? 1;
        long? GetNextNumberSigned64(ref TokenPosition end) => GetNext(Token.Type.Number, ref end) is Token t ?
            long.TryParse(t.content, out var n) ? n : throw Except("Number is too large") :
            (GetNext(Token.Type.Subtract, ref end) is not null && GetNext(Token.Type.Number, ref end) is Token t2) ?
                long.TryParse(t2.content, out var n2) ? -n2 : throw Except("Number is too large") :
                null;
        int? GetNextNumber(ref TokenPosition end) => GetNext(Token.Type.Number, ref end) is Token t ? int.TryParse(t.content, out var n) ? n : throw Except("Number is too large") : null;
        int GetNextByteOrCharOr1(ref TokenPosition end)
        {
            if (GetNext(Token.Type.Number, ref end) is Token t1)
                return byte.TryParse(t1.content, out var n) ? n : throw Except("Number is too large");
            if (GetNext(Token.Type.Character, ref end) is Token t2)
                return t2.content[1] < 256 ? t2.content[1] : throw Except("Character must have a char code less than 256");
            return 1;
        }
        AST.TokenContext Push(Token srcToken, AST.Token token, TokenPosition end)
        {
            var ctx = new AST.TokenContext(srcToken.position, end, srcToken.content.Source, new(filename), new(fileUri), token);
            stack.Last().Body.Tokens.Add(ctx);
            return ctx;
        }
        void PushContext(BodyType type, Func<LexicalScope, AST.TokenContext> factory)
        {
            var ctx = new LexicalScope(type, new(), stack.Last());
            ctx.Owner = factory(ctx);
            stack.Add(ctx);
        }
        (BodyType type, AST.TokenContext? owner) PopContext() { var top = stack.Last(); stack.RemoveAt(stack.Count - 1); return (top.Type, top.Owner); }

        while (position < tokens.Count)
        {
            var token = tokens[position];
            var end = token.EndPosition;

            switch (token.type)
            {
                case Token.Type.Add:
                    {
                        if (GetNext(Token.Type.String, ref end) is Token str)
                        {
                            foreach (var c in str.content)
                                if (c > 255) throw Except("Characters must have a char code less than 256");
                            Push(token, new AST.ModifyString(true, str.content[1..^1]), end);
                            break;
                        }
                        Push(token, new AST.Modify(GetNextByteOrCharOr1(ref end)), end);
                    }
                    break;
                case Token.Type.Subtract:
                    {
                        if (GetNext(Token.Type.String, ref end) is Token str)
                        {
                            foreach (var c in str.content)
                                if (c > 255) throw Except("Characters must have a char code less than 256");
                            Push(token, new AST.ModifyString(false, str.content[1..^1]), end);
                            break;
                        }
                        Push(token, new AST.Modify(-GetNextByteOrCharOr1(ref end)), end);
                    }
                    break;
                case Token.Type.MoveLeft:
                    Push(token, new AST.Move(-GetNextNumber64Or1(ref end)), end);
                    break;
                case Token.Type.MoveRight:
                    Push(token, new AST.Move(GetNextNumber64Or1(ref end)), end);
                    break;
                case Token.Type.Print:
                    Push(token, new AST.Print(), end);
                    break;
                case Token.Type.Read:
                    Push(token, new AST.Read(), end);
                    break;
                case Token.Type.Wait:
                    Push(token, new AST.WaitMS(GetNextNumber64Or1(ref end)), end);
                    break;
                case Token.Type.TakeReference:
                    Push(token, new AST.TakeReference(), end);
                    break;
                case Token.Type.Dereference:
                    Push(token, new AST.Dereference(), end);
                    break;
                case Token.Type.ExternGet:
                    Push(token, new AST.FindExternFunction(), end);
                    break;
                case Token.Type.ExternDefine:
                    Push(token, new AST.PrepareExternFunction(), end);
                    break;
                case Token.Type.ExternInvoke:
                    Push(token, new AST.CallExternFunction(), end);
                    break;
                case Token.Type.Assert:
                    {
                        if (GetNextNumberSigned64(ref end) is long n)
                            Push(token, new AST.DebugAssert(n, GetNext(Token.Type.String, ref end)?.content[1..^1]), end);
                        else
                            Push(token, new AST.DebugPrintLitteral(GetNext(Token.Type.String, ref end)?.content[1..^1] ??
                                throw Except("Debug Assert must be followed by a number or a string or both", "When followed by a number, the program is stopped if the tape cursor does not match said number, also printing the string if provided\nWhen followed by just a string, that string will always be printed.")
                            ), end);
                    }
                    break;
                case Token.Type.AssertRelative:
                    {
                        // var depth = 0;
                        // while (GetNext(Token.Type.InvokeMacro, ref end) is not null) depth++;
                        var offset = GetNextNumberSigned64(ref end) ?? 0;
                        var message = GetNext(Token.Type.String, ref end)?.content[1..^1];

                        Push(token, new LexDebugAssertRelative(offset, 0, GetNext(Token.Type.String, ref end)?.content[1..^1]), end);
                    }
                    break;
                case Token.Type.DebugPrint:
                    {
                        var n = GetNextNumber64Or1(ref end);
                        if (n == 0)
                            throw Except("Debug Assert Print can't have a width of zero");
                        Push(token, new AST.DebugPrint(n, GetNext(Token.Type.String, ref end)?.content[1..^1]), end); 
                    }
                    break;

                case Token.Type.DebugQuit:
                    {
                        Push(token, new AST.DebugQuit(), end);
                    }
                    break;
                case Token.Type.OpenWhileLoop:
                    {
                        PushContext(BodyType.While, lc => Push(token, new LexWhileLoop(lc), end));
                    }
                    break;
                case Token.Type.OpenForLoop:
                    {
                        PushContext(BodyType.For,
                            ctx => Push(token, new LexForLoop(ctx, GetNextNumber64Or1(ref end)), end)
                        );
                    }
                    break;
                case Token.Type.DefineMacro:
                    {
                        var isExporting = GetNext(Token.Type.DefineMacro, ref end) is not null;
                        var name = GetNext(Token.Type.Name, ref end) is Token n ? n.content : throw Except("Macro Definition must be followed by a name", "The name is how Macro Invokations will refer to this macro");
                        foreach (var builtin in Enum.GetValues<BuiltinMacros.Macro>())
                            if (BuiltinMacros.GetMacroName(builtin) == name)
                                throw Except("Cannot define macro with same name as builtin macro");

                        foreach (var t in stack.Last().Body.Tokens)
                            if (t.Token is ASTMacroDefine other && other.Name == name)
                                throw Except("Cannot define macro with duplicate name within the same scope");

                        if (GetNext(Token.Type.OpenMacro, ref end) is null) throw Except("Macro Name must be followed by Open Bracket", "Brackets after a macro definition contain the code that will be pasted in at the invokation site");

                        PushContext(BodyType.Macro, ctx => Push(token, new ASTMacroDefine(name, ctx, isExporting), end));
                    }
                    break;
                case Token.Type.ExportMacro:
                    {
                        var name = GetNext(Token.Type.Name, ref end) is Token n ? n.content : throw Except("Macro Export must be followed by a name", "The name is how Macro which will be relayed to the surrounding scope");
                        Push(token, new ASTMacroExport(name), end);
                    }
                    break;
                case Token.Type.InvokeMacro:
                    {
                        int depth = 0;
                        while (GetNext(Token.Type.InvokeMacro, ref end) is not null)
                            depth++;

                        var args = new List<LexicalScope>();
                        AST.TokenContext tokenCtx;
                        if (GetNextNumber(ref end) is int paramIndex)
                        {
                            tokenCtx = Push(token, new ASTParameter(paramIndex, depth, args), end);
                        }
                        else if (GetNext(Token.Type.Name, ref end) is Token nt)
                        {
                            var name = nt.content;

                            BuiltinMacros.Macro? builtin = null;
                            foreach (var bm in Enum.GetValues<BuiltinMacros.Macro>())
                                if (BuiltinMacros.GetMacroName(bm) == name)
                                    builtin = bm;

                            if (builtin != null)
                            {
                                _ = GetNext(Token.Type.OpenMacro, ref end) ?? throw Except("Builtin Macro Invocation must be followed by an argument");
                                var str = GetNext(Token.Type.String, ref end) ?? throw Except("Builtin Macro Invocation must have a string as an argument");
                                _ = GetNext(Token.Type.CloseMacro, ref end) ?? throw Except("Macro argument body must be closed");
                                Push(token, new BuiltinMacros.ASTInvocation(builtin.Value, str.content[1..^1]), end);
                                break;
                            }
                            tokenCtx = Push(token, new ASTMacroInvoke(name, depth, args), end);
                        }
                        else
                            throw Except("Macro Invocation must be followed by a name or parameter index", "A name will invoke another macro here, and a number will retrieve a parameter by index");

                        if (GetNext(Token.Type.OpenMacro, ref end) is not null)
                        {
                            PushContext(BodyType.MacroArg, ctx =>
                            {
                                args.Add(ctx);
                                return tokenCtx;
                            });
                        }
                    }
                    break;
                case Token.Type.CloseWhileLoop:
                    {
                        if (PopContext().type != BodyType.While) throw Except("Unexpected End of While Loop when not in while loop body");
                    }
                    break;
                case Token.Type.CloseForLoop:
                    {
                        if (PopContext().type != BodyType.For) throw Except("Unexpected End of For Loop when not in for loop body");
                    }
                    break;
                case Token.Type.CloseMacro:
                    {
                        var (type, owner) = PopContext();
                        if (type == BodyType.Macro)
                        { }
                        else if (type == BodyType.MacroArg)
                        {
                            if (GetNext(Token.Type.OpenMacro, ref end) is not null)
                            {
                                PushContext(BodyType.MacroArg, ctx =>
                                {
                                    (owner!.Value.Token switch
                                    {
                                        ASTMacroInvoke invoke => invoke.Args,
                                        ASTParameter param => param.Args,
                                        _ => throw new Exception("How could this be anything else??")
                                    }).Add(ctx);
                                    return owner!.Value;
                                });
                            }
                        }
                        else throw Except("Unexpected end of macro body or argument when not in macro body or argument");
                    }
                    break;

                case Token.Type.Comment:
                    {
                        Push(token, new AST.Comment(token.content[1..]), end);
                    }
                    break;

                default: throw Except($"Unexpected Token: {token}");
            }
            ;

            position++;
        }
        if (stack.Count > 1)
        {
            var ctx = stack.Last().Owner!.Value;
            throw new ParseException($"Unclosed loop body of {stack.Last().Type}", "", ctx.Start, ctx.End, ctx.Source, ctx.File.ToString());
        }

        return stack.First();
    }
    
    static LexicalScope DuplicateLexScope(LexicalScope scope, LexicalScope? newParent = null)
    {
        var newScope = scope with { Body = new(), Parent = newParent ?? scope.Parent };
        
        foreach (var ctx in scope.Body.Tokens)
        {
            switch (ctx.Token)
            {
                case LexWhileLoop wl:
                    newScope.Body.Tokens.Add(ctx with { Token = new LexWhileLoop(DuplicateLexScope(wl.Scope, newScope)) });
                    break;
                case LexForLoop fl:
                    newScope.Body.Tokens.Add(ctx with { Token = new LexForLoop(DuplicateLexScope(fl.Scope, newScope), fl.Count) });
                    break;
                case LexInvokeBody ib:
                    throw new Exception("DuplicateLexScope - should not be run on a scope containing LexInvokeBody");
                default:
                    newScope.Body.Tokens.Add(ctx);
                    break;
            }
        }
        return newScope;
    }
    LexicalScope CompleteMacros(LexicalScope rootScope, string projectDirectory)
    {
        List<CallContex> callStack = [new(null, rootScope, [])];

        ParseException Except(string message, AST.TokenContext ctx, string? afterMessage = null)
        {
            var builder = new StringBuilder();
            if (afterMessage != null)
                builder.Append(afterMessage + "\n");
            builder.Append('\n');
            for (int i = callStack.Count - 1; i >= 0; i--)
            {
                var call = callStack[i];
                if (call.Site == null) break;
                var site = call.Site.Value;
                builder.Append($"Invoked from line {site.Start.Row}, col {site.Start.Column} in {site.File}\n");
                builder.Append(GetLinePreview(site.Start, site.End, site.Source));
                builder.Append('\n');
            }
            return new(message, builder.ToString(), ctx.Start, ctx.End, ctx.Source, ctx.File.ToString());
        }

        (LexicalScope scope, List<LexicalScope> args) FindSearchScope(AST.TokenContext callSite, LexicalScope scope, int depth)
        {

            for (; depth > 0; depth--)
            {
                if (scope.Parent == null)
                    throw Except("Depth of macro invocation is too high.", callSite);
                scope = scope.Parent;
            }

            for (var searchScope = scope; searchScope != null; searchScope = searchScope.Parent)
            {
                for (int i = callStack.Count - 1; i >= 0; i--)
                {
                    if (callStack[i].Scope == searchScope)
                    {
                        return (scope, callStack[i].Args);
                    }
                }
            }
            Console.WriteLine("fell through :(");
            return (scope, []);
        }

        (AST.TokenContext site, ASTMacroDefine macro, LexicalScope scope)? FindMacro(AST.TokenContext callSite, LexicalScope scope, StringSlice name, int depth)
        {
            static (AST.TokenContext site, ASTMacroDefine macro, LexicalScope scope)? Recurse(LexicalScope scope, StringSlice name)
            {
                // define in this scope
                foreach (var ctx in scope.Body.Tokens)
                    if (ctx.Token is ASTMacroDefine define && define.Name == name)
                        return (ctx, define, scope);

                // exported define or export in an invocation scope
                foreach (var ctx in scope.NewScope!.Body.Tokens)
                {
                    if (ctx.Token is LexInvokeBody invocation)
                    {
                        foreach (var invToken in invocation.Scope.Body.Tokens)
                        {
                            if (invToken.Token is ASTMacroDefine { IsExporting: true } define && define.Name == name)
                                return (ctx, define, define.Scope);

                            if (invToken.Token is ASTMacroExportLinked export)
                                return (ctx, export.Target, export.Target.Scope);
                        }
                    }
                }
                return scope.Parent != null ? Recurse(scope.Parent, name) : null;
            }

            var (searchScope, args) = FindSearchScope(callSite, scope, depth);

            return Recurse(searchScope, name);
        }

        static List<(StringSlice name, AST.TokenContext ctx)> GetExports(LexicalScope importedScope)
        {
            var exports = new List<(StringSlice, AST.TokenContext)>();

            foreach (var tokenCtx in importedScope.Body.Tokens)
            {
                if (tokenCtx.Token is ASTMacroDefine { IsExporting: true } define)
                    exports.Add((define.Name, tokenCtx));
                if (tokenCtx.Token is ASTMacroExportLinked exportLinked)
                    exports.Add((exportLinked.Name, tokenCtx));
            }

            return exports;
        }

        // call error if there is a problem
        static void ValidateExportedNames(LexicalScope currentScope, LexicalScope importedScope, Action<(StringSlice name, AST.TokenContext ctx)> error)
        {
            var exports = GetExports(importedScope);

            foreach (var tokenCtx in currentScope.Body.Tokens.Concat(currentScope.NewScope!.Body.Tokens))
            {
                if (tokenCtx.Token is ASTMacroDefine define)
                {
                    if (exports.TryFind(ex => ex.name == define.Name, out var export))
                        error(export);
                }
                if (tokenCtx.Token is LexInvokeBody invoke)
                {
                    var currentNames = GetExports(invoke.Scope);
                    foreach (var (name, _) in currentNames)
                        if (exports.TryFind(ex => ex.name == name, out var export))
                            error(export);
                }
            }
        }


        LexicalScope TraverseScope(LexicalScope scope)
        {
            scope.NewScope = scope with { Body = new(), Parent = scope.Parent?.NewScope };
            var body = scope.NewScope.Body;
            foreach (var ctx in scope.Body.Tokens)
            {
                switch (ctx.Token)
                {
                    case LexWhileLoop wl:
                        body.Tokens.Add(ctx with { Token = new LexWhileLoop(TraverseScope(wl.Scope)) });
                        break;

                    case LexForLoop fl:
                        body.Tokens.Add(ctx with { Token = new LexForLoop(TraverseScope(fl.Scope), fl.Count) });
                        break;

                    case ASTParameter param:
                        {
                            var (searchScope, localArgs) = FindSearchScope(ctx, scope, param.Depth);

                            if (param.Index >= localArgs.Count)
                                throw Except($"Parameter index {param.Index} out of range, {localArgs.Count} value{(localArgs.Count == 0 ? "" : "s")} were provided", ctx);

                            var paramScope = /*DuplicateLexScope*/(localArgs[param.Index]);

                            callStack.Add(new(ctx, paramScope, param.Args));
                            var paramBody = TraverseScope(paramScope);
                            callStack.RemoveAt(callStack.Count - 1);

                            body.Tokens.Add(ctx with { Token = new LexInvokeBody(new(param.Index + ""), paramBody) });
                        }
                        break;

                    case ASTMacroInvoke invoke:
                        var (_, astMacro, macroScope) = FindMacro(ctx, scope, invoke.Name, invoke.Depth) ?? throw Except($"Failed to find macro \"{invoke.Name}\"", ctx);

                        var invokeScope = /*DuplicateLexScope*/(astMacro.Scope);

                        callStack.Add(new(ctx, invokeScope, invoke.Args));
                        var macroBody = TraverseScope(invokeScope);
                        callStack.RemoveAt(callStack.Count - 1);

                        ValidateExportedNames(scope, macroBody, ex => throw Except($"Invocation of macro \"{invoke.Name}\" exports macro with duplicate name \"{ex.name}\"", ex.ctx));

                        body.Tokens.Add(ctx with { Token = new LexInvokeBody(invoke.Name, macroBody) });
                        break;

                    case LexDebugAssertRelative assertRelative:
                        {
                            var (searchScope, _) = FindSearchScope(ctx, scope, assertRelative.Depth);
                            while (searchScope.Parent is not null && searchScope.Owner?.Token is not ASTMacroDefine)
                                searchScope = searchScope.Parent;
                            
                            if (searchScope.Owner?.Token is ASTMacroDefine define)
                                body.Tokens.Add(ctx with { Token = new AST.DebugAssertRelative(define.PutAssertRefId, assertRelative.Offset, assertRelative.Message) });
                            else
                                throw Except("The targeted scope does not support relative offsets for the assert operator", ctx);
                        }
                        break;

                    case ASTMacroExport export:
                        var (_, exportedDefine, _) = FindMacro(ctx, scope, export.Name, 0) ?? throw Except($"Failed to find macro \"{export.Name}\"", ctx);

                        body.Tokens.Add(ctx with { Token = new ASTMacroExportLinked(export.Name, exportedDefine) });
                        break;

                    case BuiltinMacros.ASTInvocation builtin:
                        switch (builtin.Macro)
                        {
                            case BuiltinMacros.Macro.Import:
                                {
                                    var filePath = Path.Join(projectDirectory, builtin.Argument);

                                    if (!File.Exists(filePath) && (Path.GetExtension(filePath).Length > 0 || !File.Exists(filePath += ".bfpp")))
                                        throw Except($@"Import failed because file doesn't exist: ""{filePath}""", ctx);

                                    if (IncompleteParse(filePath) is not LexicalScope imported)
                                        throw Except("Import failed", ctx);

                                    ValidateExportedNames(scope, imported, ex => throw Except($"Invocation of builtin macro \"import\" exports macro with duplicate name \"{ex.name}\"", ex.ctx));

                                    body.Tokens.Add(ctx with { Token = new LexInvokeBody(builtin.Argument, imported) });
                                }
                                break;

                            case BuiltinMacros.Macro.AddString:
                                for (int i = 0; i < builtin.Argument.Length; i++)
                                {
                                    var c = builtin.Argument[i];
                                    if (c > 255)
                                        throw Except("Characters in added string can't have a code greater than 255", ctx);

                                    body.Tokens.Add(ctx with { Token = new AST.Modify(c) });
                                    if (i < builtin.Argument.Length - 1)
                                        body.Tokens.Add(ctx with { Token = new AST.Move(1) });
                                }
                                break;
                            case BuiltinMacros.Macro.EmbedFile:
                                {
                                    var filePath = Path.Join(projectDirectory, builtin.Argument);

                                    if (!File.Exists(filePath) && (Path.GetExtension(filePath).Length > 0 || !File.Exists(filePath += ".bfpp")))
                                        throw Except($@"Embed failed because file doesn't exist: ""{filePath}""", ctx);

                                    int embedId = EmbedFile(filePath);

                                    body.Tokens.Add(ctx with { Token = new AST.GetEmbedReference(embedId) });
                                }
                                break;
                        }

                        break;

                    default:
                        body.Tokens.Add(ctx);
                        break;
                }
            }
            return scope.NewScope;
        }
        return TraverseScope(rootScope);
    }
    
    static AST.Body CreateASTBodyFromLexicalScope(LexicalScope scope)
    {
        static AST.Body TraverseScope(LexicalScope scope)
        {
            var body = new AST.Body();
            foreach (var ctx in scope.Body.Tokens)
            {
                switch (ctx.Token)
                {
                    case LexWhileLoop wl:
                        body.Tokens.Add(ctx with { Token = new AST.WhileLoop(TraverseScope(wl.Scope)) });
                        break;
                    case LexForLoop fl:
                        body.Tokens.Add(ctx with { Token = new AST.ForLoop(TraverseScope(fl.Scope), fl.Count) });
                        break;
                    case LexInvokeBody ib:
                        body.Tokens.Add(ctx with { Token = new AST.InvokeBody(ib.Name, TraverseScope(ib.Scope), ib.Scope.Owner?.Token is ASTMacroDefine d ? d.assertRefId : null) });
                        break;
                    default:
                        body.Tokens.Add(ctx);
                        break;
                }
            }
            return body;
        }
        return TraverseScope(scope);
    }
    
    static AST.Body CompressOperations(AST.Body ast)
    {
        static AST.Body TraverseScope(AST.Body body)
        {
            var newBody = new AST.Body();
            foreach(var ctx in body.Tokens)
            {
                T? GetLast<T>() where T : AST.Token => newBody.Tokens.Count > 0 && newBody.Tokens.Last().Token is T t ? t : default;
                
                void RePush(AST.Token token) 
                {
                    var ctx = newBody.Tokens.RemoveLast();
                    newBody.Tokens.Add(ctx with { Token = token });
                }
                
                switch (ctx.Token)
                {
                    case AST.WhileLoop wl:
                        newBody.Tokens.Add(ctx with { Token = wl with { Body = TraverseScope(wl.Body) } });
                        break;

                    case AST.ForLoop fl:
                        newBody.Tokens.Add(ctx with { Token = fl with { Body = TraverseScope(fl.Body) } });
                        break;

                    case AST.InvokeBody ib:
                        newBody.Tokens.Add(ctx with { Token = ib with { Body = TraverseScope(ib.Body) } });
                        break;

                    case AST.Modify modify:
                        if (GetLast<AST.Modify>() is AST.Modify lastModify)
                            RePush(new AST.Modify(modify.Amount + lastModify.Amount));
                        else
                            newBody.Tokens.Add(ctx);
                        break;
                    
                    case AST.Move move:
                        if (GetLast<AST.Move>() is AST.Move lastMove)
                            RePush(new AST.Move(move.Dist + lastMove.Dist));
                        else
                            newBody.Tokens.Add(ctx);
                        break;

                    default:
                        newBody.Tokens.Add(ctx);
                        break;
                }
            }
            return newBody;
        }
        return TraverseScope(ast);
    }
    
    // removes macro definitions and removes modifies/moves with amount/dist 0
    static AST.Body Cleanup(AST.Body ast)
    {
        static AST.Body TraverseScope(AST.Body body)
        {
            var newBody = new AST.Body();
            foreach (var ctx in body.Tokens)
            {
                switch (ctx.Token)
                {
                    case AST.WhileLoop wl:
                        newBody.Tokens.Add(ctx with { Token = wl with { Body = TraverseScope(wl.Body) } });
                        break;

                    case AST.ForLoop fl:
                        newBody.Tokens.Add(ctx with { Token = fl with { Body = TraverseScope(fl.Body) } });
                        break;
                    
                    case AST.InvokeBody ib:
                        newBody.Tokens.Add(ctx with { Token = ib with { Body = TraverseScope(ib.Body) } });
                        break;

                    case ASTMacroDefine: break;
                    case ASTMacroExport: break;
                    
                    case AST.Modify modify:
                        if (modify.Amount != 0)
                            newBody.Tokens.Add(ctx);
                        break;
                    
                    case AST.Move move:
                        if (move.Dist != 0)
                            newBody.Tokens.Add(ctx);
                        break;

                    default:
                        newBody.Tokens.Add(ctx);
                        break;
                }
            }
            return newBody;
        }
        return TraverseScope(ast);
    }
}