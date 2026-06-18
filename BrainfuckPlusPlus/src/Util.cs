using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.VisualBasic;
using Tomlyn.Model;

namespace Brainfuck;

public static class Util
{
    public static T? RemoveLast<T>(this List<T> list) where T : notnull
    {
        if (list.Count == 0)
            return default;
        var val = list[^1];
        list.RemoveAt(list.Count - 1);
        return val;
    }

    public static bool TryFind<T>(this List<T> list, Predicate<T> predicate, [MaybeNullWhen(false)] out T value)
    {
        foreach (var elem in list)
        {
            if (predicate(elem))
            {
                value = elem;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public static IEnumerable<TOut> WhereType<TIn, TOut>(this IEnumerable<TIn> list) where TOut : TIn
    {
        foreach (var elem in list)
            if (elem is TOut outElem)
                yield return outElem;
    }

    public static bool TryGetValueAsOrPrint<T>(this TomlTable table, string key, [NotNullWhen(true)] out T? value) where T : class
    {
        value = null;
        if (!table.TryGetValue("main", out var objValue))
        {
            Console.WriteLine(@$"Config Error: Key ""{key}"" must be present");
            return false;
        }
        if (objValue is not T tValue)
        {
            Console.WriteLine(@$"Config Error: Key ""{key}"" must be a {typeof(T).Name}");
            return false;
        }
        value = tValue;
        return true;
    }

    public static StringSlice Slice(this string @string) => new(@string);
    public static StringSlice Slice(this string @string, Range range) => new StringSlice(@string)[range];

    public static string EscapeString(ReadOnlySpan<char> str)
    {
        var strOut = new StringBuilder(str.Length);
        for (int i = 0; i < str.Length; i++)
        {
            strOut.Append(str[i] switch
            {
                '\"' => @"\""",
                '\\' => @"\\",
                '\n' => @"\n",
                '\b' => @"\b",
                '\t' => @"\t",
                '\0' => @"\0",
                _ => str[i]
            });
        }
        return strOut.ToString();
    }
}