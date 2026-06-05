namespace Brainfuck;

using System.Collections;
using System.Diagnostics.CodeAnalysis;

public struct StringSlice(string source, int start, int length) : IEnumerable<char>
{
    public string Source = source;
    public int Start = start;
    public int Length = length;

    public StringSlice(string source) : this(source, 0, source.Length) { }
    
    public readonly ReadOnlySpan<char> Span => Source.AsSpan()[Start..(Start + Length)];

    public readonly override string ToString() => Source.Substring(Start, Length);
    
    public readonly char this[int index] => Source[Start+index];
    public readonly char this[Index index] => Source[Start + (index.IsFromEnd?Length-index.Value : index.Value)];
    public readonly StringSlice this[Range range]
    {
        get
        {
            var start = range.Start.IsFromEnd ? Length - range.Start.Value : range.Start.Value;
            var end = range.End.IsFromEnd ? Length - range.End.Value : range.End.Value;
            return new(
                Source,
                Start + start,
                end - start
            );
        }
    }

    public static implicit operator ReadOnlySpan<char>(StringSlice slice) => slice.Span;
    public static explicit operator StringSlice(string source) => new(source);

    public static bool operator ==(StringSlice left, StringSlice right) => left.Span.SequenceEqual(right.Span);
    public static bool operator !=(StringSlice left, StringSlice right) => !(left == right);
    
    
    public static bool operator ==(string left, StringSlice right) => left.AsSpan().SequenceEqual(right.Span);
    public static bool operator ==(StringSlice left, string right) => left.Span.SequenceEqual(right.AsSpan());
    public static bool operator !=(string left, StringSlice right) => !(left == right);
    public static bool operator !=(StringSlice left, string right) => !(left == right);

    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is StringSlice slice && slice == this;
    public override readonly int GetHashCode() => string.GetHashCode(Span);

    public readonly IEnumerator<char> GetEnumerator()
    {
        return new Enumerator(this);
    }

    public struct Enumerator(StringSlice source) : IEnumerator<char>
    {
        int index = -1;
        public readonly char Current => source[index];
        readonly object IEnumerator.Current => Current;

        public readonly void Dispose() { }

        public bool MoveNext()
        {
            index++;
            return index < source.Length;
        }

        public void Reset() => index = -1;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}