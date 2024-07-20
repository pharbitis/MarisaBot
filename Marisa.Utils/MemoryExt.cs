﻿using System.Text;

namespace Marisa.Utils;

public static class MemoryExt
{

    public class ReadOnlyMemoryCharComparer(StringComparison comparer = StringComparison.Ordinal) : IEqualityComparer<ReadOnlyMemory<char>>
    {
        public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
        {
            return x.Span.Equals(y.Span, comparer);
        }

        public int GetHashCode(ReadOnlyMemory<char> obj)
        {
            return obj.GetHashCode();
        }
    }

    #region utils

    public static bool IsWhiteSpace(this ReadOnlyMemory<char> input)
    {
        return input.Span.IsWhiteSpace();
    }

    public static ReadOnlyMemory<char> UnEscapeTsvCell(this ReadOnlyMemory<char> s)
    {
        if (s.Length < 2) return s;

        var sSpan = s.Span;

        if (sSpan[0] == '"' && sSpan[^1] == '"') return s[1..^1].Replace("\"\"".AsMemory(), "\"".AsMemory());
        return s;
    }

    #endregion

    #region comp

    public static bool Contains(this ReadOnlyMemory<char> input, ReadOnlyMemory<char> val, StringComparison comparison = StringComparison.Ordinal)
    {
        return input.Span.IndexOf(val.Span, comparison) != -1;
    }

    public static bool Contains(this ReadOnlyMemory<char> input, string val, StringComparison comparison = StringComparison.Ordinal)
    {
        return input.Span.IndexOf(val, comparison) != -1;
    }

    public static bool Contains(this string input, ReadOnlyMemory<char> val, StringComparison comparison = StringComparison.Ordinal)
    {
        return input.AsSpan().IndexOf(val.Span, comparison) != -1;
    }

    public static bool IsPartOf(this ReadOnlyMemory<char> input, ReadOnlyMemory<char> val, StringComparison comparison = StringComparison.Ordinal)
    {
        return val.Contains(input, comparison);
    }

    public static bool Equals(this ReadOnlyMemory<char> input, string another, StringComparison comp = StringComparison.Ordinal)
    {
        return input.Span.Equals(another.AsSpan(), comp);
    }

    public static bool Equals(this string a, ReadOnlyMemory<char> b, StringComparison comp = StringComparison.Ordinal)
    {
        return b.Equals(a, comp);
    }

    public static bool StartsWith(this ReadOnlyMemory<char> input, string val, StringComparison comparison = StringComparison.Ordinal)
    {
        return input.Span.StartsWith(val, comparison);
    }

    #endregion

    #region linq

    public static bool Any(this ReadOnlyMemory<char> input, Func<char, bool> predicate)
    {
        foreach (var c in input.Span)
        {
            if (predicate(c)) return true;
        }
        return false;
    }

    public static bool Contains(this IEnumerable<string> input, ReadOnlyMemory<char> val, StringComparison comparison = StringComparison.Ordinal)
    {
        return input.Any(x => x.AsSpan().Equals(val.Span, comparison));
    }

    public static bool Contains(this IEnumerable<ReadOnlyMemory<char>> input, string val, StringComparison comparison = StringComparison.Ordinal)
    {
        return input.Any(x => x.Span.Equals(val.AsSpan(), comparison));
    }

    public static IEnumerable<ReadOnlyMemory<char>> Distinct(this IEnumerable<ReadOnlyMemory<char>> inp, StringComparison comp = StringComparison.Ordinal)
    {
        return inp.Distinct(new ReadOnlyMemoryCharComparer(comp));
    }

    #endregion

    #region split

    public static IEnumerable<ReadOnlyMemory<char>> Split(this ReadOnlyMemory<char> input, ReadOnlyMemory<char> val)
    {
        var last = 0;

        while (true)
        {
            var at = input.Span[last..].IndexOf(val.Span);
            if (at < 0)
            {
                yield return input[last..];
                yield break;
            }
            yield return input.Slice(last, at);
            last += at + val.Span.Length;
        }
    }

    public static IEnumerable<ReadOnlyMemory<char>> Split(this ReadOnlyMemory<char> input, params char[] val)
    {
        return input.Split(val.AsMemory());
    }

    public static IEnumerable<ReadOnlyMemory<char>> Split(this ReadOnlyMemory<char> input, string val)
    {
        return input.Split(val.AsMemory());
    }

    #endregion

    #region replace

    public static ReadOnlyMemory<char> Replace(this ReadOnlyMemory<char> input, ReadOnlyMemory<char> from, ReadOnlyMemory<char> to)
    {
        var sb = new StringBuilder();

        foreach (var s in input.Split(from))
        {
            sb.Append(s);
            sb.Append(to);
        }

        var mem = sb.ToString().AsMemory();

        return mem[..^to.Length];
    }

    public static ReadOnlyMemory<char> Replace(this ReadOnlyMemory<char> input, string from, string to)
    {
        return input.Replace(from.AsMemory(), to.AsMemory());
    }

    #endregion
}