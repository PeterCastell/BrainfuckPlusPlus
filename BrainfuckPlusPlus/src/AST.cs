
namespace Brainfuck;

public sealed record AST(AST.Body body, List<AST.GlobalToken> globals)
{
    public interface Token
    {
        public virtual void Print(string indent) => Console.WriteLine(indent + this);
    }

    public record struct TokenContext(TokenPosition Start, TokenPosition End, string Source, StringSlice File, StringSlice FileUri, Token Token)
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
    public record ModifyString(bool SignPositive, StringSlice Amounts) : Token;
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
    public record InvokeBody(StringSlice Name, Body Body, int? EntryId) : Token
    {
        public void Print(string indent)
        {
            Console.WriteLine(indent + $"InvokeBody {{ Name = {Name}, EntryId = {EntryId} }}");
            Body.Print(indent);
        }
    }
    public record Print() : Token;
    public record Read() : Token;
    public record WaitMS(long Delay) : Token;
    public record DebugPrint(long Width, StringSlice? Message) : Token;
    public record DebugAssert(long Index, StringSlice? Message) : Token;
    public record DebugAssertRelative(int EntryId, long Offset, StringSlice? Message) : Token;
    public record DebugQuit() : Token;
    public record DebugPrintLitteral(StringSlice Message) : Token;
    public record TakeReference() : Token;
    public record Dereference() : Token;
    public record FindExternFunction() : Token;
    public record PrepareExternFunction() : Token;
    public record CallExternFunction() : Token;
    public record GetEmbedReference(int EmbedId) : Token;
    public record Comment(StringSlice Content) : Token;

    public interface GlobalToken;
    public record FileEmbed(int Id, string FilePath) : GlobalToken;
}