namespace Brainfuck;

using System.Collections.Concurrent;
using System.CommandLine.Parsing;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

public class Parser(BuildIO IO)
{
    readonly Dictionary<string, (LexicalScope scope, bool hasReturn)> parsedFiles = [];

    readonly ConcurrentDictionary<string, AST.FileEmbed> fileEmbeds = [];
    int nextFileEmbedId = 0;
    int EmbedFile(string filePath) => fileEmbeds.GetOrAdd(filePath, key => new(nextFileEmbedId++, key)).Id;

    readonly Dictionary<int, (ASTFunctionDefine, AST.TokenContext)> functions = [];
    
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
    
    (LexicalScope scope, bool hasReturn)? InternalParse(string path, Func<LexicalScope, LexicalScope>? extraStep)
    {
        string fullPath = Path.GetFullPath(path);

        // if (fileStack.Contains(fullPath))
        // {
        //     errorWriter.WriteLine("Imports cannot be recursive");
        //     errorWriter.Flush();
        //     return null;
        // }
        // fileStack.Add(fullPath);

        if (parsedFiles.TryGetValue(fullPath, out var cachedScope))
            return cachedScope;

        string source;
        try
        {
            string uri = new Uri(path).AbsoluteUri;
            source = LSP.FileCache.cache.TryGetValue(uri, out var cached)
                ? cached
                : File.ReadAllText(path);
        }
        catch (Exception e)
        {
            IO.WriteErr("File Error");
            IO.WriteErr(e.Message);
            return null;
        }
        
        string filename = Path.GetFileName(path);
        
        try
        {
            var tokens = Tokenize(source, filename);

            var rootContext = ParseTree(tokens, filename, fullPath);

            rootContext = CompleteMacros(rootContext);

            var hasReturn = CheckForUnusedCode(rootContext);

            if (extraStep is not null)
                rootContext = extraStep(rootContext);

            // fileStack.Remove(fullPath);
            parsedFiles.TryAdd(fullPath, (rootContext, hasReturn));
            return (rootContext, hasReturn);
        }
        catch (ParseException ex)
        {
            IO.WriteErr($"Error (line {ex.start.Row}, col {ex.start.Column} in {ex.filename}): {ex.message}");
            IO.WriteErr(GetLinePreview(ex.start, ex.end, ex.source));
            IO.WriteErr(ex.afterMessage);
            
            return null;
        }
    }

    public AST? Parse(string path)
    {
        var globals = new List<AST.GlobalToken>();

        var parsed = InternalParse(path, scope =>
        {
            foreach (var (function, _) in functions.Values)
            {
                var funcScope = CompleteMacros(function.Scope);
                var returns = CheckForUnusedCode(funcScope);

                var funcBody = CreateASTBodyFromLexicalScope(funcScope);
                funcBody = CompressOperations(funcBody);
                funcBody = Cleanup(funcBody);

                globals.Add(new AST.Function(function.Name, function.Id, function.RetType, function.ArgTypes, funcBody, returns, function.AssertId));
            }

            return scope;
        });

        if (parsed is null)
            return null;
        
        var (scope, hasReturn) = parsed.Value;

        var body = CreateASTBodyFromLexicalScope(scope);
        body = CompressOperations(body);
        body = Cleanup(body);

        globals.AddRange(fileEmbeds.Values);
        
        return new(body, globals, hasReturn);
    }
    
    LexicalScope? IncompleteParse(string path)
    {
        return InternalParse(path, null)?.scope;
    }

    public record struct Token
    {
        [AttributeUsage(AttributeTargets.Field)]
        class TypeRegex(string regexStr) : Attribute
        {
            public readonly Regex regex = new(@"\G" + regexStr, RegexOptions.Multiline);
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

            [TypeRegex(@"\^\$+")] SpawnThread,
            [TypeRegex(@"\^\!")] JoinThread,
            [TypeRegex(@"\^\&")] CreateMutex,

            [TypeRegex(@"\.")] Print,
            [TypeRegex(@"\,")] Read,
            [TypeRegex(@"\[")] OpenWhileLoop,
            [TypeRegex(@"\]")] CloseWhileLoop,
            [TypeRegex(@"\^\{")] OpenMutex,
            [TypeRegex(@"\{")] OpenForLoop,
            [TypeRegex(@"\}")] CloseForLoopOrMutex,
            [TypeRegex(@"\&\&?\*")] DefineFunction,
            [TypeRegex(@"\$+\*")] GetFunction,
            [TypeRegex(@"\$!")] Return,
            [TypeRegex(@"\&\$+")] ExportMacro,
            [TypeRegex(@"\&\&?")] DefineMacro,
            [TypeRegex(@"\$+")] InvokeMacro,

            [TypeRegex(@"\(")] OpenMacro,
            [TypeRegex(@"\)")] CloseMacro,

            [TypeRegex(@"\!\,")] SetRawInput,
            [TypeRegex(@"\!")] Wait,
            [TypeRegex(@"\*")] TakeReference,
            [TypeRegex(@"\~")] Dereference,

            [TypeRegex(@"#(.*?)(?:\\\#|$)")] Comment,
            [TypeRegex(@"\d+")] Number,
            [TypeRegex(@"\'(\\?.)\'")] Character,
            [TypeRegex(@"\w+")] Name,
            [TypeRegex(@"\""(.*?)(?<!\\)\""")] String
        }
        public required Type type;
        public required StringSlice content;
        public required TokenPosition position;
        public readonly int Length => content.Length;
        public readonly TokenPosition EndPosition => position with { Column = position.Column + Length, Position = position.Position + Length };

        public override readonly string ToString() => $"Token.{type} {{ Row = {position.Row}, Column = {position.Column}, Content = \"{content}\" }}";
    }

    static char? GetEscapedCharacter(char c) => c switch
    {
        '\"' => '\"',
        '\\' => '\\',
        'n' => '\n',
        'b' => '\b',
        't' => '\t',
        '0' => '\0',
        _ => null
    };
    
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
                var capture = match.Groups.Count > 1 ? match.Groups[1] : match;
                tokens.Add(new()
                {
                    type = type,
                    content = new(source, capture.Index, capture.Length),
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
        MacroArg,
        Function,
        Mutex
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


    interface NamedEntity
    {
        StringSlice Name { get; }
        bool IsExporting { get; }
    }
    interface RelativeAssertable
    {
        static int nextAssertId = 0;
        public int? AssertId { get; set; }
        public int EnsureAssertId => AssertId ?? (AssertId = nextAssertId++).Value;
    }
    record ASTMacroDefine(StringSlice Name, LexicalScope Scope, bool IsExporting) : AST.Token, NamedEntity, RelativeAssertable
    {
        public int? AssertId { get; set; }

        public override string ToString() => $"ASTMacroDefine {{ Name = {Name}, Scope = {Scope}, IsExporting = {IsExporting} }}";
    }
    record ASTMacroParameter(int Index, int Depth, List<LexicalScope> Args) : AST.Token;
    record ASTMacroInvoke(StringSlice Name, int Depth, List<LexicalScope> Args) : AST.Token;
    record ASTExport(StringSlice Name, int Depth) : AST.Token;
    record ASTLink(NamedEntity Target, bool IsExporting) : AST.Token;
    record ASTFunctionDefine(StringSlice Name, LexicalScope Scope, AST.Type RetType, List<AST.Type> ArgTypes, bool IsExporting) : AST.Token, NamedEntity, RelativeAssertable
    {
        static int nextId;
        int? id;
        public int Id => id ?? (id = nextId++).Value;
        public int? AssertId { get; set; }
    }
    record ASTFunctionGet(StringSlice Name, int Depth) : AST.Token;
    record ASTSpawnThread(StringSlice Name, int Depth) : AST.Token;

    // loose scopes allow exporting out of them
    interface LooseScope
    {
        LexicalScope Scope { get; }
    }

    record LexWhileLoop(LexicalScope Scope) : AST.Token, LooseScope;
    record LexForLoop(LexicalScope Scope, long Count) : AST.Token, LooseScope;
    record LexInvokeBody(StringSlice Name, LexicalScope Scope) : AST.Token, LooseScope;
    record LexMutexBody(LexicalScope Scope) : AST.Token, LooseScope;

    record LexDebugAssertRelative(long Offset, int Depth, StringSlice? Message) : AST.Token;

    static LexicalScope ParseTree(List<Token> tokens, string filename, string filePath)
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

        ASTFunctionDefine? GetFunction() => stack.Select(scope => scope.Owner?.Token as ASTFunctionDefine).FirstOrDefault(owner => owner is not null);
        string Peek() => position + 1 < tokens.Count ? tokens[position + 1].ToString() : "EOF";
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
            {
                char c = t2.content.Length == 2 ?
                    GetEscapedCharacter(t2.content[1]) ?? throw Except(@$"Unknown escape character ""{t2.content[1]}""") :
                    t2.content[0];
                return c < 256 ? c : throw Except("Character must have a char code less than 256");
            }
            return 1;
        }
        string? GetNextString(ref TokenPosition end)
        {
            if (GetNext(Token.Type.String, ref end) is not Token tok) return null;
            var str = tok.content;
            var outStr = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] is '\\')
                {
                    var c = GetEscapedCharacter(str[i + 1]) ?? throw Except(@$"Unknown escape character ""{str[i + 1]}""");
                    outStr.Append(c);
                    i++;
                }
                else
                    outStr.Append(str[i]);
            }
            return outStr.ToString();
        }
        AST.TokenContext Push(Token srcToken, AST.Token token, TokenPosition end)
        {
            var ctx = new AST.TokenContext(srcToken.position, end, srcToken.content.Source, filename, filePath, token);
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
                        if (GetNextString(ref end) is string str)
                        {
                            foreach (var c in str)
                                if (c > 255) throw Except("Characters must have a char code less than 256");
                            Push(token, new AST.ModifyString(true, str), end);
                            break;
                        }
                        Push(token, new AST.Modify(GetNextByteOrCharOr1(ref end)), end);
                    }
                    break;
                case Token.Type.Subtract:
                    {
                        if (GetNextString(ref end) is string str)
                        {
                            foreach (var c in str)
                                if (c > 255) throw Except("Characters must have a char code less than 256");
                            Push(token, new AST.ModifyString(false, str), end);
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
                    Push(token, new AST.WaitMS(GetNextNumber64(ref end) ?? 0), end);
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
                    Push(token, new AST.PrepareExternCaller(), end);
                    break;
                case Token.Type.ExternInvoke:
                    Push(token, new AST.CallExternFunction(), end);
                    break;
                case Token.Type.Assert:
                    {
                        if (GetNextNumberSigned64(ref end) is long n)
                            Push(token, new AST.DebugAssert(n, GetNextString(ref end)?.Slice()), end);
                        else
                            Push(token, new AST.DebugPrintLitteral(GetNext(Token.Type.String, ref end)?.content ??
                                throw Except("Debug Assert must be followed by a number or a string or both", "When followed by a number, the program is stopped if the tape cursor does not match said number, also printing the string if provided\nWhen followed by just a string, that string will always be printed.")
                            ), end);
                    }
                    break;
                case Token.Type.AssertRelative:
                    {
                        Push(token, new LexDebugAssertRelative(
                            GetNextNumberSigned64(ref end) ?? 0,
                            0,
                            GetNext(Token.Type.String, ref end)?.content),
                        end);
                    }
                    break;
                case Token.Type.DebugPrint:
                    {
                        var n = GetNextNumber64Or1(ref end);
                        if (n == 0)
                            throw Except("Debug Assert Print can't have a width of zero");
                        Push(token, new AST.DebugPrint(n, GetNextString(ref end)?.Slice()), end);
                    }
                    break;

                case Token.Type.DebugQuit:
                    Push(token, new AST.DebugQuit(GetNextString(ref end)?.Slice()), end);
                    break;
                case Token.Type.OpenWhileLoop:
                    PushContext(BodyType.While, lc => Push(token, new LexWhileLoop(lc), end));
                    break;
                case Token.Type.OpenForLoop:
                    PushContext(BodyType.For,
                        ctx => Push(token, new LexForLoop(ctx, GetNextByteOrCharOr1(ref end)), end)
                    );
                    break;
                case Token.Type.OpenMutex:
                    {
                        PushContext(BodyType.Mutex,
                            ctx => Push(token, new LexMutexBody(ctx), end)
                        );
                    }
                    break;
                case Token.Type.CreateMutex:
                    Push(token, new AST.CreateMutex(), end);
                    break;
                case Token.Type.DefineMacro:
                    {
                        var isExporting = token.Length > 1;
                        var name = GetNext(Token.Type.Name, ref end) is Token n ? n.content : throw Except("Macro Definition must be followed by a name");
                        foreach (var builtin in Enum.GetValues<BuiltinMacros.Macro>())
                            if (BuiltinMacros.GetMacroName(builtin) == name)
                                throw Except("Cannot define macro with same name as builtin macro");

                        foreach (var t in stack.Last().Body.Tokens)
                            if (t.Token is NamedEntity other && other.Name == name)
                                throw Except("Cannot define macro with duplicate name within the same scope");

                        if (GetNext(Token.Type.OpenMacro, ref end) is null) throw Except("Macro Name must be followed by Open Bracket", "Brackets after a macro definition contain the code that will be pasted in at the invocation site");

                        PushContext(BodyType.Macro, ctx => Push(token, new ASTMacroDefine(name, ctx, isExporting), end));
                    }
                    break;
                case Token.Type.DefineFunction:
                    {
                        if (GetFunction() is not null) throw Except("Cannot define function within another function");

                        string example = "This looks like `&*foo~1~3,2()` or `&&*bar~12()`";
                        var isExporting = token.Length > 1;
                        var name = GetNext(Token.Type.Name, ref end) is Token n ? n.content : throw Except("Function Definition must be followed by a name", example);

                        foreach (var builtin in Enum.GetValues<BuiltinMacros.Macro>())
                            if (BuiltinMacros.GetMacroName(builtin) == name)
                                throw Except("Cannot define function with same name as builtin macro");
                        
                        foreach (var t in stack.Last().Body.Tokens)
                            if (t.Token is NamedEntity other && other.Name == name)
                                throw Except("Cannot define function with duplicate name within the same scope");
                        
                        _ = GetNext(Token.Type.Dereference, ref end) ?? throw Except("Function Definition name must be followed by a tilde", example);

                        var retTypeIndex = GetNextNumber(ref end) ?? throw Except(@$"Function definition must have a argument count after the first tilde separator. Found ""{Peek()}""", example);
                        var retType = AST.GetType(retTypeIndex - 1) ?? throw Except(@$"Return type index ""{retTypeIndex}"" out of range", "Index must be from 1 to " + AST.TypeCount);
                        var args = new List<AST.Type>();
                        if (GetNext(Token.Type.Dereference, ref end) is not null)
                        {
                            while (true)
                            {
                                var argTypeIndex = GetNextNumber(ref end) ?? throw Except(@$"Expected Arg type index ilde to mark end of args. Found ""{Peek()}""", example);
                                var argType = AST.GetType(argTypeIndex - 1) ?? throw Except(@$"Arg type index ""{argTypeIndex}"" out of range", "Index must be from 1 to " + AST.TypeCount);
                                args.Add(argType);

                                if (GetNext(Token.Type.Read, ref end) is not null)
                                    continue;
                                if (GetNext(Token.Type.OpenMacro, ref end) is not null)
                                    break;
                                throw Except(@$"Expected comma to mark next arg or open bracket to mark start of body. Found ""{Peek()}""", example);
                            }
                        }
                        else
                            _ = GetNext(Token.Type.OpenMacro, ref end) ?? throw Except("Function Definition must be followed by open bracket", "Brackets after a function definition contain the code that will be executed when the function is called");

                        if (args.Count > AST.Function.MaxArgCount)
                            throw Except(@$"Max argument count exceeded. The limit is 64.");
                        
                        PushContext(BodyType.Function, ctx => Push(token, new ASTFunctionDefine(name, ctx, retType, args, isExporting), end));
                    }
                    break;

                case Token.Type.GetFunction:
                    {
                        int depth = token.Length - 2;

                        if (GetNextNumber(ref end) is int paramIndex)
                        {
                            if (depth > 0) throw Except("Search Depth is not allowed when getting function parameters", "use a single dollar sign");
                            var func = GetFunction();
                            if (func is null && paramIndex >= 2) throw Except("Parameter out of range for root parameters (argc, argv)");
                            if (func is not null && paramIndex >= func.ArgTypes.Count) throw Except(@$"Parameter out of range for function ""{func.Name}""");
                            Push(token, func is null ?
                                new AST.GetMainParam(paramIndex) :
                                new AST.GetFunctionParam(paramIndex),
                            end);
                        }
                        else if (GetNext(Token.Type.Name, ref end) is Token nt)
                        {
                            var name = nt.content;

                            foreach (var bm in Enum.GetValues<BuiltinMacros.Macro>())
                                if (BuiltinMacros.GetMacroName(bm) == name)
                                    throw Except(@$"Cannot get address non-function ""{name}""");
                            
                            Push(token, new ASTFunctionGet(name, depth), end);
                        }
                        else
                            throw Except("Macro Invocation must be followed by a name or parameter index", "A name will invoke another macro here, and a number will retrieve a parameter by index");
                    }
                    break;

                case Token.Type.Return:
                    Push(token, new AST.Return(GetFunction()?.RetType ?? AST.Type.U8), end);
                    break;

                case Token.Type.ExportMacro:
                    {
                        var name = GetNext(Token.Type.Name, ref end) is Token n ? n.content : throw Except("Macro Export must be followed by a name", "The name is how Macro which will be relayed to the surrounding scope");
                        Push(token, new ASTExport(name, token.Length - 2), end);
                    }
                    break;
                case Token.Type.InvokeMacro:
                    {
                        int depth = token.Length - 1;

                        var args = new List<LexicalScope>();
                        AST.TokenContext tokenCtx;
                        if (GetNextNumber(ref end) is int paramIndex)
                        {
                            tokenCtx = Push(token, new ASTMacroParameter(paramIndex, depth, args), end);
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
                                Push(token, new BuiltinMacros.ASTInvocation(builtin.Value, str.content), end);
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
                case Token.Type.CloseForLoopOrMutex:
                    {
                        if (PopContext().type is not (BodyType.For or BodyType.Mutex)) throw Except("Unexpected End of For Loop or Mutex when not in for loop body or mutex");
                    }
                    break;
                case Token.Type.CloseMacro:
                    {
                        var (type, owner) = PopContext();
                        if (type is BodyType.Macro or BodyType.Function)
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
                                        ASTMacroParameter param => param.Args,
                                        _ => throw new Exception("How could this be anything else??")
                                    }).Add(ctx);
                                    return owner!.Value;
                                });
                            }
                        }
                        else throw Except("Unexpected end of macro body or argument when not in macro body or argument");
                    }
                    break;

                case Token.Type.SpawnThread:
                    {
                        int depth = token.Length - 2;
                        var name = GetNext(Token.Type.Name, ref end)?.content ?? throw Except("Spawn Thread must be followed by the name of the function");
                        Push(token, new ASTSpawnThread(name, depth), end);
                    }
                    break;
                case Token.Type.JoinThread:
                    Push(token, new AST.JoinThread(), end);
                    break;
                case Token.Type.SetRawInput:
                    Push(token, new AST.SetRawInput(), end);
                    break;
                case Token.Type.Comment:
                    Push(token, new AST.Comment(token.content), end);
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
    LexicalScope CompleteMacros(LexicalScope rootScope)
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
            return (scope, []);
        }

        NamedEntity? FindNamedEntity(AST.TokenContext callSite, LexicalScope scope, StringSlice name, int depth)
        {
            static NamedEntity? Recurse(LexicalScope scope, StringSlice name)
            {
                foreach (var ctx in scope.Body.Tokens.Concat(scope.NewScope!.Body.Tokens))
                {
                    // defined in this scope
                    if (ctx.Token is NamedEntity entity && entity.Name == name)
                        return entity;
                    // exported from child scope
                    if (ctx.Token is LooseScope looseScope)
                    {
                        foreach (var childToken in looseScope.Scope.Body.Tokens)
                        {
                            if (childToken.Token is NamedEntity { IsExporting: true } exEntity && exEntity.Name == name)
                                return exEntity;

                            if (childToken.Token is ASTLink { IsExporting: true } exLink)
                                return exLink.Target;
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
                if (tokenCtx.Token is ASTLink { IsExporting: true } link)
                    exports.Add((link.Target.Name, tokenCtx));
            }

            return exports;
        }

        // call error if there is a problem
        static void ValidateExportedNames(LexicalScope currentScope, LexicalScope importedScope, Action<(StringSlice name, AST.TokenContext ctx)> error)
        {
            var exports = GetExports(importedScope);

            foreach (var tokenCtx in currentScope.Body.Tokens.Concat(currentScope.NewScope!.Body.Tokens))
            {
                if (tokenCtx.Token is NamedEntity entity)
                {
                    if (exports.TryFind(ex => ex.name == entity.Name, out var export))
                        error(export);
                }
                if (tokenCtx.Token is LooseScope looseScope)
                {
                    var currentNames = GetExports(looseScope.Scope);
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

                    case LexMutexBody mb:
                        body.Tokens.Add(ctx with { Token = new LexMutexBody(TraverseScope(mb.Scope)) });
                        break;

                    case ASTMacroParameter param:
                        {
                            var (searchScope, localArgs) = FindSearchScope(ctx, scope, param.Depth);

                            if (param.Index >= localArgs.Count)
                                throw Except($"Parameter index {param.Index} out of range, {localArgs.Count} value{(localArgs.Count == 0 ? "" : "s")} were provided", ctx);

                            var paramScope = localArgs[param.Index];

                            callStack.Add(new(ctx, paramScope, param.Args));
                            var paramBody = TraverseScope(paramScope);
                            callStack.RemoveAt(callStack.Count - 1);

                            body.Tokens.Add(ctx with { Token = new LexInvokeBody(new(param.Index + ""), paramBody) });
                        }
                        break;

                    case ASTMacroInvoke invoke:
                        {
                            var entity = FindNamedEntity(ctx, scope, invoke.Name, invoke.Depth) ?? throw Except($"Failed to find name \"{invoke.Name}\"", ctx);

                            if (entity is not ASTMacroDefine macro)
                                throw Except(@$"Cannot invoke non-macro ""{entity.Name}"" like a macro", ctx);

                            var invokeScope = macro.Scope;

                            callStack.Add(new(ctx, invokeScope, invoke.Args));
                            var macroBody = TraverseScope(invokeScope);
                            callStack.RemoveAt(callStack.Count - 1);

                            ValidateExportedNames(scope, macroBody, ex => throw Except($"Invocation of macro \"{invoke.Name}\" exports duplicate name \"{ex.name}\"", ex.ctx));

                            body.Tokens.Add(ctx with { Token = new LexInvokeBody(invoke.Name, macroBody) });
                        }
                        break;

                    case ASTExport export:
                        {
                            var entity = FindNamedEntity(ctx, scope, export.Name, export.Depth) ?? throw Except($"Failed to find name \"{export.Name}\"", ctx);
                            body.Tokens.Add(ctx with { Token = new ASTLink(entity, true) });
                        }
                        break;

                    case ASTFunctionGet funcGet:
                        {
                            var entity = FindNamedEntity(ctx, scope, funcGet.Name, funcGet.Depth) ?? throw Except($"Failed to find name \"{funcGet.Name}\"", ctx);

                            if (entity is not ASTFunctionDefine function)
                                throw Except(@$"Cannot get address non-function ""{entity.Name}""", ctx);

                            functions.TryAdd(function.Id, (function, ctx));
                            body.Tokens.Add(ctx with { Token = new AST.GetFunction(function.Id) });
                        }
                        break;
                    case ASTSpawnThread spawnThread:
                        {
                            var entity = FindNamedEntity(ctx, scope, spawnThread.Name, spawnThread.Depth) ?? throw Except($"Failed to find name \"{spawnThread.Name}\"", ctx);

                            if (entity is not ASTFunctionDefine function)
                                throw Except(@$"Cannot spawn thread with non-function ""{entity.Name}""", ctx);

                            if (function.ArgTypes.Count != 1 || function.ArgTypes[0] != AST.Type.Cell)
                                throw Except("Thread function must have exactly one argument, and of type Cell (18)", ctx);

                            if (function.RetType != AST.Type.Void)
                                throw Except("Thread function must return void (1)", ctx);

                            functions.TryAdd(function.Id, (function, ctx));
                            body.Tokens.Add(ctx with { Token = new AST.SpawnThread(function.Id) });
                        }
                        break;

                    case ASTFunctionDefine funcDefine:
                        {
                            body.Tokens.Add(ctx with { Token = new ASTLink(funcDefine, funcDefine.IsExporting) });
                        }
                        break;

                    case BuiltinMacros.ASTInvocation builtin:
                        switch (builtin.Macro)
                        {
                            case BuiltinMacros.Macro.Import:
                                {
                                    var projectDirectory = Path.GetDirectoryName(ctx.FilePath);
                                    var filePath = Path.Join(projectDirectory, builtin.Argument);

                                    if (!File.Exists(filePath) && (Path.GetExtension(filePath).Length > 0 || !File.Exists(filePath += ".bfpp")))
                                        throw Except($@"Import failed because file doesn't exist: ""{filePath}""", ctx);

                                    if (IncompleteParse(filePath) is not LexicalScope imported)
                                        throw Except("Import failed", ctx);

                                    ValidateExportedNames(scope, imported, ex => throw Except($"Invocation of builtin macro \"import\" exports macro with duplicate name \"{ex.name}\"", ex.ctx));

                                    body.Tokens.Add(ctx with { Token = new LexInvokeBody(builtin.Argument, imported) });
                                }
                                break;
                            
                            case BuiltinMacros.Macro.EmbedFile:
                                {
                                    var projectDirectory = Path.GetDirectoryName(ctx.FilePath);
                                    var filePath = Path.Join(projectDirectory, builtin.Argument);

                                    if (!File.Exists(filePath) && (Path.GetExtension(filePath).Length > 0 || !File.Exists(filePath += ".bfpp")))
                                        throw Except($@"Embed failed because file doesn't exist: ""{filePath}""", ctx);

                                    int embedId = EmbedFile(filePath);

                                    body.Tokens.Add(ctx with { Token = new AST.GetEmbedReference(embedId) });
                                }
                                break;
                        }
                        break;

                    case LexDebugAssertRelative assertRelative:
                        {
                            var (searchScope, _) = FindSearchScope(ctx, scope, assertRelative.Depth);
                            while (searchScope.Parent is not null && searchScope.Owner?.Token is not RelativeAssertable)
                                searchScope = searchScope.Parent;

                            if (searchScope.Owner?.Token is RelativeAssertable ra)
                                body.Tokens.Add(ctx with { Token = new AST.DebugAssertRelative(ra.EnsureAssertId, assertRelative.Offset, assertRelative.Message) });
                            else
                                throw Except("The targeted scope does not support relative offsets for the assert operator", ctx);
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
    
    static bool CheckForUnusedCode(LexicalScope scope)
    {
        return TraverseScope(scope);

        static bool TraverseScope(LexicalScope scope)
        {
            bool returned = false;

            foreach (var ctx in scope.Body.Tokens)
            {
                if (ctx.Token is AST.Comment) continue;
                
                if (returned)
                    throw new ParseException(
                        "Unused Code",
                        "Zig is annoying so unused code must be removed",
                        ctx.Start,
                        ctx.End,
                        ctx.Source,
                        ctx.File
                    );

                switch (ctx.Token)
                {
                    case LexWhileLoop wl:
                        TraverseScope(wl.Scope);
                        break;

                    case LexForLoop fl:
                        returned = TraverseScope(fl.Scope);
                        break;

                    case LexInvokeBody ib:
                        returned = TraverseScope(ib.Scope);
                        break;

                    case AST.Return:
                        returned = true;
                        break;
                }
            }
            return returned;
        }
    }
    
    AST.Body CreateASTBodyFromLexicalScope(LexicalScope scope)
    {
        return TraverseScope(scope);

        AST.Body TraverseScope(LexicalScope scope)
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
                        body.Tokens.Add(ctx with { Token = new AST.InvokeBody(ib.Name, TraverseScope(ib.Scope), (ib.Scope.Owner?.Token as ASTMacroDefine)?.AssertId) });
                        break;
                    case LexMutexBody mb:
                        body.Tokens.Add(ctx with { Token = new AST.MutexBody(TraverseScope(mb.Scope)) });
                        break;
                    default:
                        body.Tokens.Add(ctx);
                        break;
                }
            }
            return body;
        }
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
                            RePush(new AST.Modify((modify.Amount + lastModify.Amount) % 256));
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
                    case ASTExport: break;
                    case ASTFunctionDefine: break;
                    
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