namespace Brainfuck;

public record struct TokenPosition(int Column, int Row, int RowStart, int Position)
{
    public static bool operator >(TokenPosition left, TokenPosition right) => left.Row > right.Row || left.Column > right.Column;
    public static bool operator <(TokenPosition left, TokenPosition right) => left.Row < right.Row || left.Column < right.Column;
    public static bool operator >=(TokenPosition left, TokenPosition right) => left.Row >= right.Row || left.Column >= right.Column;
    public static bool operator <=(TokenPosition left, TokenPosition right) => left.Row <= right.Row || left.Column <= right.Column;
    
    public static TokenPosition operator +(TokenPosition left, int right) => left with { Column = left.Column + right, Position = left.Position + right };
    public static TokenPosition operator +(int left, TokenPosition right) => right + left;
}