// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// DesignString.cs — a layer stack written as text: "(1H 1L)12", "( 1H (1L 1H)6 1L)3 (0.5H 0.3L)2".
//
// THE NOTATION is ThinFilmDesigner's, so a string typed in one program means the same in the other:
//   - A layer is an optional number and a letter: "1H", "0.8L", "H" (the same as 1H). Each letter
//     stands for a material and a physical thickness, both chosen beside the string, and the number
//     multiplies that thickness. (ThinFilmDesigner counts QWOT instead; here every thickness is
//     physical, because an anisotropic layer has no one index to count a quarter wave with.)
//   - A group in brackets may be followed by a whole number of repeats: "(1H 1L)12" is 24 layers.
//     "^" is allowed before it, "(1H 1L)^12". A digit straight after ")" is always the repeat, so
//     "(1H 1L)2 0.5H" needs its space.
//   - Spaces are optional between layers: "(HL)3" is "(1H 1L)3".
//
// WHICH END IS LAYER 1 is chosen beside the string too: the leftmost layer next to the incident
// medium (the table's order) or next to the exit medium (the string read backwards into the table).
// There are no pickups: BerremanCalculator evaluates stacks and never varies them.

using System.Globalization;
using System.Text;

namespace Berreman.Api;

/// <summary>One layer of an expanded design string: a material letter and its number of QWOT or FWOT.</summary>
public sealed record StringLayer(char Letter, double Count);

public static class DesignString
{
    /// <summary>More layers than this is taken for a typing slip, "(HL)1000", not a stack.</summary>
    public const int MaxLayers = 5000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The layers the string spells, leftmost first, or a FormatException saying where it
    /// stops making sense.</summary>
    public static StringLayer[] Expand(string? text)
    {
        var p = new Parser(text ?? "");
        var layers = p.Sequence(depth: 0);
        if (layers.Count == 0) throw new FormatException("the string has no layers; write one as a number and a letter, e.g. 1H");
        return layers.ToArray();
    }

    /// <summary>The letters the string uses, in the order they first appear, or none when it does not parse.</summary>
    public static char[] Letters(string? text)
    {
        try { return Expand(text).Select(l => l.Letter).Distinct().ToArray(); }
        catch (FormatException) { return []; }
    }

    /// <summary>
    /// The material a letter most likely stands for, or null: H the highest-index material at the
    /// reference, L the lowest, M the one nearest the middle (of three or more), and any other
    /// letter the first material whose name starts with it.
    /// </summary>
    /// <param name="index">Each material's index to rank by (<see cref="StackModel.RankingIndex"/>);
    /// NaN for one that cannot be ranked.</param>
    public static string? Guess(IReadOnlyList<string> materials, IReadOnlyList<double> index, char letter)
    {
        var ranked = Enumerable.Range(0, materials.Count).Where(i => double.IsFinite(index[i]))
                               .OrderBy(i => index[i]).ToArray();
        if (ranked.Length > 0)
        {
            if (letter == 'H') return materials[ranked[^1]];
            if (letter == 'L') return materials[ranked[0]];
            if (letter == 'M' && ranked.Length >= 3) return materials[ranked[ranked.Length / 2]];
        }
        return materials.FirstOrDefault(m => m.StartsWith(letter.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The stack written back as a short string, for a status line: "1H 0.8L 1H … (24 layers)".</summary>
    public static string Describe(StringLayer[] layers)
    {
        var sb = new StringBuilder();
        foreach (var l in layers.Take(6)) sb.Append(Number(l.Count)).Append(l.Letter).Append(' ');
        if (layers.Length > 6) sb.Append("… ");
        return $"{sb.ToString().TrimEnd()} ({layers.Length} layer{(layers.Length == 1 ? "" : "s")})";
    }

    private static string Number(double v) => v.ToString("0.####", Inv);

    private sealed class Parser(string text)
    {
        private int _at;

        private void SkipSpace() { while (_at < text.Length && char.IsWhiteSpace(text[_at])) ++_at; }

        private FormatException Error(string what) =>
            new($"{what} at character {_at + 1} of the string{(_at < text.Length ? $" ('{text[_at]}')" : "")}");

        public List<StringLayer> Sequence(int depth)
        {
            var layers = new List<StringLayer>();
            while (true)
            {
                SkipSpace();
                if (_at >= text.Length)
                {
                    if (depth > 0) throw Error("a '(' is not closed");
                    return layers;
                }
                char c = text[_at];
                if (c == ')')
                {
                    if (depth == 0) throw Error("a ')' has no '(' to close");
                    ++_at;
                    return layers;
                }
                if (c == '(')
                {
                    ++_at;
                    var group = Sequence(depth + 1);
                    if (group.Count == 0) throw Error("a group has no layers in it");
                    int repeats = Repeats();
                    if ((long)layers.Count + (long)group.Count * repeats > MaxLayers)
                        throw new FormatException($"the string makes more than {MaxLayers} layers");
                    for (int r = 0; r < repeats; ++r) layers.AddRange(group);
                    continue;
                }
                double count = 1.0;
                if (char.IsDigit(c) || c == '.')
                {
                    int start = _at;
                    while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.')) ++_at;
                    string typed = text[start.._at];
                    if (!double.TryParse(typed, NumberStyles.AllowDecimalPoint, Inv, out count))
                    { _at = start; throw Error($"'{typed}' is not a number"); }
                    SkipSpace();
                    if (_at >= text.Length || !char.IsAsciiLetter(text[_at]))
                        throw Error($"the number {Number(count)} needs a material letter after it");
                    if (count <= 0.0) { _at = start; throw Error("a layer must be thicker than zero"); }
                }
                else if (!char.IsAsciiLetter(c))
                    throw Error($"'{c}' is not a layer; write a layer as a number and a letter, e.g. 0.5H");
                layers.Add(new StringLayer(char.ToUpperInvariant(text[_at]), count));
                ++_at;
                if (layers.Count > MaxLayers) throw new FormatException($"the string makes more than {MaxLayers} layers");
            }
        }

        // The repeat count straight after ')': none means once.
        private int Repeats()
        {
            int start = _at;
            if (_at < text.Length && text[_at] == '^') ++_at;
            int digits = _at;
            while (_at < text.Length && char.IsDigit(text[_at])) ++_at;
            if (_at == digits)
            {
                if (_at > start) throw Error("'^' needs a number of repeats after it");
                return 1;
            }
            if (_at < text.Length && text[_at] == '.') throw Error("a number of repeats must be a whole number");
            if (!int.TryParse(text[digits.._at], NumberStyles.None, Inv, out int n) || n < 1)
            { _at = digits; throw Error("a group must be repeated at least once"); }
            return n;
        }
    }
}
