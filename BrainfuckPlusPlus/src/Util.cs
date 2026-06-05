using System.Diagnostics.CodeAnalysis;

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
}