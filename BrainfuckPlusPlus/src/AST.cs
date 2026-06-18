
namespace Brainfuck;

public sealed record AST(AST.Body body, List<AST.GlobalToken> globals, bool ReturnIncluded)
{
    public interface Token
    {
        public virtual void Print(string indent) => Console.WriteLine(indent + this);
    }

    public record struct TokenContext(TokenPosition Start, TokenPosition End, string Source, string File, string FilePath, Token Token)
    {
        public override readonly string ToString() => $"{Token} {{ Line = {Start.Row}, Column = {Start.Column}, File = {File} }}";
    }

    public class Body()
    {
        public readonly List<TokenContext> Tokens = [];
        public override string ToString() => $"Body {{ Tokens = [{Tokens.Count}] }}";
        public void Print(string indent = "")
        {
            foreach (var token in Tokens)
                token.Token.Print(indent + "  ");
        }
    }

    // All token types are will reach a Templater are here, other types exist within the parser.
    public record Modify(int Amount) : Token;
    public record ModifyString(bool SignPositive, string Amounts) : Token;
    public record Move(long Dist) : Token;
    public record WhileLoop(Body Body) : Token
    {
        public void Print(string indent)
        {
            Console.WriteLine(indent + this);
            Body.Print(indent);
        }
    }
    public record ForLoop(Body Body, long Count) : Token
    {
        public void Print(string indent)
        {
            Console.WriteLine(indent + this);
            Body.Print(indent);
        }
    }
    public record InvokeBody(StringSlice Name, Body Body, int? AssertId) : Token
    {
        public void Print(string indent)
        {
            Console.WriteLine(indent + $"InvokeBody {{ Name = {Name}, AssertId = {AssertId} }}");
            Body.Print(indent);
        }
    }
    public record MutexBody(Body Body) : Token
    {
        public void Print(string indent)
        {
            Console.WriteLine(indent + $"MutexBody {{ }}");
            Body.Print(indent);
        }
    }
    public record Print() : Token;
    public record Read() : Token;
    public record WaitMS(long Delay) : Token;
    public record DebugPrint(long Width, StringSlice? Message) : Token;
    public record DebugAssert(long Index, StringSlice? Message) : Token;
    public record DebugAssertRelative(int EntryId, long Offset, StringSlice? Message) : Token;
    public record DebugQuit(StringSlice? Message) : Token;
    public record DebugPrintLitteral(StringSlice Message) : Token;
    public record TakeReference() : Token;
    public record Dereference() : Token;
    public record FindExternFunction() : Token;
    public record PrepareExternCaller() : Token;
    public record CallExternFunction() : Token;
    public record SpawnThread(int FuncId) : Token;
    public record JoinThread() : Token;
    public record CreateMutex() : Token;
    public record GetFunction(int Id) : Token;
    public record GetFunctionParam(int Index) : Token;
    public record GetMainParam(int Index) : Token;
    public record Return(Type Type) : Token;
    public record GetEmbedReference(int EmbedId) : Token;
    public record SetRawInput() : Token;
    public record Comment(StringSlice Content) : Token;

    public interface GlobalToken;
    public record FileEmbed(int Id, string FilePath) : GlobalToken;
    public record Function(StringSlice Name, int Id, Type RetType, List<Type> ArgTypes, Body Body, bool ReturnIncluded, int? AssertId) : GlobalToken
    {
        public const int MaxArgCount = 64;
    }
    public enum Type
    {
        Void,
        U8,
        I8,
        U16,
        I16,
        U32,
        I32,
        U64,
        I64,
        F32,
        F64,
        Pointer,
        CUInt,
        CInt,
        CULong,
        CLong,
        CLongDouble,
        ISize,
        USize,
        Cell
    }

    public static Type? GetType(int index) => index >= 0 && index < Enum.GetValues<Type>().Length ? (Type)index : null;
    public static int GetTypeIndex(Type type) => (int)type + 1;
    public static int TypeCount => Enum.GetValues<Type>().Length;
}