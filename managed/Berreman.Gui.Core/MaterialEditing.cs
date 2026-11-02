// MaterialEditing.cs — the arithmetic behind the Materials tab, with no Avalonia in it.
//
// A material is edited in one of two ways (docs/material-model.md):
//   - a SIMPLE FORM for a constant isotropic, uniaxial or biaxial material, given as n and k per
//     principal axis, which is what most layers in a coating are;
//   - as TERMS, each a law and a 6×6 shape, which covers everything else. Any material can be
//     shown as terms (MaterialTerms.ToTerms), so this view is always available; editing in it
//     writes the material as terms.
// The law's parameters are shown as fields read from its JSON, so a new law needs no new form:
// numbers and words are fields, a list (Sellmeier terms, phonon modes, table rows) is lines of
// numbers under its column names.

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Berreman.Api;

namespace Berreman.Gui.Core;

/// <summary>One parameter of a law as text: a number or word, or rows of numbers under Columns.</summary>
public sealed class LawField(string key, string text, string[]? columns = null)
{
    public string Key { get; } = key;
    public string Text { get; set; } = text;
    /// <summary>Non-null for a list: the column names, one row per line.</summary>
    public string[]? Columns { get; } = columns;
    public bool IsRows => Columns is not null;
    public string Title => IsRows ? $"{Name(Key)} ({string.Join(", ", Columns!)}), one row per line" : Name(Key);

    /// <summary>A parameter's name for people; the file's key when it is already plain.</summary>
    private static string Name(string key) => key switch
    {
        
        "catalog" => "catalogue",
        "name" => "glass",
        "eps_inf" => "ε∞",
        "e0" => "E0 (peak)",
        "eg" => "Eg (band gap)",
        
        "range" => "valid from, to",
        
        
        "data" => "rows",
        
        "gamma" => "γ (damping)",
        "center" => "centre",
        "plasma" => "plasma frequency",
        "sigma_s_per_m" => "σ (S/m)",
        _ => key,
    };
}

public static class MaterialEditing
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly string[] LawChoices =
        ["none (constant)", "sellmeier", "cauchy", "lorentz", "drude", "tauc_lorentz", "phonon", "phonon_additive", "table", "glass", "conductivity", "polder"];

    /// <summary>What a law means, for the line under its choice.</summary>
    public static string LawFormula(string? law) => law switch
    {
        null => "f = 1: the shape is added as it is.",
        "sellmeier" => "n² = 1 + a + Σ b λ²/(λ² − c2), λ in the unit (c2 = c² for a resonance wavelength c)",
        "cauchy" => "n = a + b/λ² + c/λ⁴, k = 0; f = n². λ in the unit (a wavelength).",
        "lorentz" => "f = S/(ω₀² − ω² − iγω), S = amplitude²; centre, γ and amplitude in the unit (a frequency)",
        "drude" => "f = −ω_p²/(ω² + iγω); plasma and γ in the unit (a frequency)",
        "tauc_lorentz" => "f = ε∞ + χ(E): Im χ = A E0 C (E − Eg)²/(E((E² − E0²)² + C²E²)) above Eg, 0 below; Re χ its Kramers–Kronig transform (Jellison and Modine 1996). E, A, E0, C, Eg in the unit (eV usually).",
        "phonon" => "f = ε∞ Π (ω_L² − ω² − iγ_L ω)/(ω_T² − ω² − iγ_T ω)",
        "phonon_additive" => "f = ε∞ [1 + Σ (ω_T² − ω_L²)/(ω² − ω_T² + iγω)]",
        "table" => "f = (n + ik)² or ε′ + iε″, interpolated linearly in x; refused outside the rows",
        "glass" => "f = n², n from the vendor's catalogue (materials/glass), k = 0; refused outside the catalogue's range",
        "conductivity" => "f = iσ/(ωε₀), σ in S/m",
        "polder" => "the ferrite's Polder μ (part mu) or κ (part kappa); bias along z",
        _ => "",
    };

    /// <summary>The parameters a new term with this law starts from.</summary>
    public static JsonObject DefaultLaw(string law) => (JsonObject)JsonNode.Parse(law switch
    {
        "sellmeier" => """{ "unit": "um", "a": 0, "terms": [ { "b": 1.0, "c2": 0.01 } ] }""",
        "cauchy" => """{ "unit": "um", "a": 1.5, "b": 0.004, "c": 0 }""",
        "lorentz" => """{ "unit": "cm-1", "amplitude": 500, "center": 500, "gamma": 5 }""",
        "drude" => """{ "unit": "eV", "plasma": 1, "gamma": 0.1 }""",
        "tauc_lorentz" => """{ "unit": "eV", "eps_inf": 1, "a": 100, "e0": 4.5, "c": 1, "eg": 3.2 }""",
        "phonon" => """{ "unit": "cm-1", "eps_inf": 1, "modes": [ { "to": 500, "lo": 600, "gamma_to": 5, "gamma_lo": 5 } ] }""",
        "phonon_additive" => """{ "unit": "cm-1", "eps_inf": 1, "modes": [ { "to": 500, "lo": 600, "gamma": 5 } ] }""",
        "table" => """{ "unit": "um", "columns": ["x", "n", "k"], "data": [ [0.4, 1.5, 0], [0.8, 1.5, 0] ] }""",
        "glass" => """{ "catalog": "SCHOTT", "name": "N-BK7" }""",
        "conductivity" => """{ "sigma_s_per_m": 1000000 }""",
        "polder" => """{ "four_pi_ms_gauss": 1000, "h0_oe": 500, "alpha": 0, "gamma_mhz_per_oe": 2.8, "part": "mu" }""",
        _ => throw new ArgumentException($"'{law}' is not a law"),
    })!;

    // ---- a law's parameters as fields ------------------------------------------------------------

    public static List<LawField> Fields(JsonObject law)
    {
        var fields = new List<LawField>();
        foreach (var (key, value) in law)
        {
            switch (value)
            {
                case JsonArray a when a.Count > 0 && a.All(x => x is JsonObject):
                {
                    var cols = a.SelectMany(x => ((JsonObject)x!).Select(p => p.Key)).Distinct().ToArray();
                    var lines = a.Select(x => string.Join("  ", cols.Select(c => ((JsonObject)x!)[c] is { } v ? Num(v) : "")));
                    fields.Add(new LawField(key, string.Join("\n", lines), cols));
                    break;
                }
                case JsonArray a when a.Count > 0 && a.All(x => x is JsonArray):
                {
                    int width = a.Max(x => ((JsonArray)x!).Count);
                    var cols = key == "data" && law["columns"] is JsonArray names && names.Count == width
                        ? names.Select(n => n!.GetValue<string>()).ToArray()
                        : Enumerable.Range(1, width).Select(i => $"c{i}").ToArray();
                    fields.Add(new LawField(key, string.Join("\n", a.Select(x => string.Join("  ", ((JsonArray)x!).Select(Num)))), cols));
                    break;
                }
                case JsonArray a:   /* a short list of words or numbers: range, columns */
                    fields.Add(new LawField(key, string.Join(" ", a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : Num(x!)))));
                    break;
                default:
                    fields.Add(new LawField(key, value is JsonValue jv && jv.TryGetValue<string>(out var str) ? str : Num(value!)));
                    break;
            }
        }
        return fields;
    }

    /// <summary>The law's parameters from its fields, or a FormatException naming the field.</summary>
    public static JsonObject FromFields(IEnumerable<LawField> fields)
    {
        var o = new JsonObject();
        foreach (var f in fields)
        {
            if (f.IsRows)
            {
                var rows = new JsonArray();
                int line = 0;
                foreach (var raw in f.Text.Split('\n'))
                {
                    ++line;
                    var parts = raw.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    if (parts.Length != f.Columns!.Length)
                        throw new FormatException($"{f.Key}, line {line}: {parts.Length} numbers, but the columns are {string.Join(", ", f.Columns)}");
                    var values = parts.Select(p => Number(p, $"{f.Key}, line {line}")).ToArray();
                    if (f.Key == "data") rows.Add(new JsonArray(values.Select(v => (JsonNode)v).ToArray()));
                    else
                    {
                        var row = new JsonObject();
                        for (int c = 0; c < values.Length; ++c) row[f.Columns[c]] = values[c];
                        rows.Add(row);
                    }
                }
                o[f.Key] = rows;
                continue;
            }
            var text = f.Text.Trim();
            var words = text.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            if (f.Key == "columns") o[f.Key] = new JsonArray(words.Select(w => (JsonNode)w).ToArray());
            else if (f.Key == "range") o[f.Key] = new JsonArray(words.Select(w => (JsonNode)Number(w, f.Key)).ToArray());
            else if (double.TryParse(text, NumberStyles.Float, Inv, out var v)) o[f.Key] = v;
            else if (text.Length == 0) throw new FormatException($"{f.Key} is empty");
            else o[f.Key] = text;   /* a word: the unit, a Polder part */
        }
        return o;
    }

    private static double Number(string s, string where) =>
        double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : throw new FormatException($"{where}: '{s}' is not a number");

    private static string Num(JsonNode? n) => n is null ? "" : AsNumber(n) is { } d ? d.ToString("R", Inv) : n.ToJsonString().Trim('"');

    /// <summary>
    /// A JSON number, however it is held: TryGetValue&lt;double&gt; refuses a value built from an int
    /// (as { ["n"] = 1 } makes), so the number is read from its text.
    /// </summary>
    public static double? AsNumber(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number ? double.Parse(v.ToJsonString(), Inv) : null;

    // ---- complex numbers as typed ------------------------------------------------------------------

    /// <summary>A complex number as a person types it: 2, -0.08, 2+0.1i, 0.1i, -i, 1e-3-2e-4i.</summary>
    public static Complex ParseComplex(string text)
    {
        var s = text.Replace(" ", "").Replace("j", "i").Replace("−", "-");
        if (s.Length == 0) return Complex.Zero;
        if (!s.EndsWith('i'))
            return double.TryParse(s, NumberStyles.Float, Inv, out var re) ? re : throw new FormatException($"'{text}' is not a number");
        var body = s[..^1];
        /* The split between the real and imaginary parts: the last sign that is not an exponent's. */
        int split = -1;
        for (int k = body.Length - 1; k > 0; --k)
            if ((body[k] == '+' || body[k] == '-') && body[k - 1] != 'e' && body[k - 1] != 'E') { split = k; break; }
        string rePart = split < 0 ? "0" : body[..split], imPart = split < 0 ? body : body[split..];
        if (imPart is "" or "+") imPart = "1";
        else if (imPart == "-") imPart = "-1";
        if (double.TryParse(rePart, NumberStyles.Float, Inv, out var r) && double.TryParse(imPart, NumberStyles.Float, Inv, out var im))
            return new Complex(r, im);
        throw new FormatException($"'{text}' is not a complex number (write it as 2, 0.1i or 2+0.1i)");
    }

    /// <summary>The inverse of <see cref="ParseComplex"/>, exact: shortest round-trip digits.</summary>
    public static string FormatComplex(Complex z)
    {
        double re = z.Real == 0 ? 0 : z.Real, im = z.Imaginary == 0 ? 0 : z.Imaginary;
        if (im == 0) return re.ToString("R", Inv);
        string i = Math.Abs(im) == 1 ? "i" : Math.Abs(im).ToString("R", Inv) + "i";
        if (re == 0) return (im < 0 ? "-" : "") + i;
        return re.ToString("R", Inv) + (im < 0 ? "-" : "+") + i;
    }

    // ---- a term's shape --------------------------------------------------------------------------

    /// <summary>A term's shape as the 36 cells of its 6×6 matrix, whatever keys gave it.</summary>
    public static string[] ShapeCells(JsonObject term, JsonNode? lattice = null)
    {
        var probe = new JsonObject { ["terms"] = new JsonArray(StripLaw(term)) };
        if (lattice is not null) probe["lattice"] = lattice.DeepClone();
        /* Parse a law-free copy: the constant term's matrix is exactly the shape. A μ = 1 term is
           added when the term gives no μ; that one is left out by evaluating only the first term. */
        var m = (TermMaterial)MaterialParser.Parse(EnsureEps(probe), "term");
        return m.Terms[0].Shape.M.Select(FormatComplex).ToArray();
    }

    private static JsonObject StripLaw(JsonObject term)
    {
        var o = new JsonObject();
        foreach (var (k, v) in term)
            if (MaterialParser.ShapeKeys.Contains(k)) o[k] = v!.DeepClone();
        return o;
    }

    // A μ-only shape gives no ε, which a material refuses; give the probe an empty second term that does.
    private static JsonObject EnsureEps(JsonObject probe)
    {
        probe["terms"]!.AsArray().Add(new JsonObject { ["eps"] = 0 });
        return probe;
    }

    /// <summary>
    /// The shape keys for 36 typed cells: each 3×3 block written as a scalar when it is a multiple
    /// of the identity, as rows otherwise, and left out when it is zero.
    /// </summary>
    public static JsonObject ShapeFromCells(IReadOnlyList<string> cells)
    {
        if (cells.Count != 36) throw new ArgumentException("a shape has 36 cells");
        var z = new Complex[36];
        for (int k = 0; k < 36; ++k)
        {
            try { z[k] = ParseComplex(cells[k]); }
            catch (FormatException e) { throw new FormatException($"row {k / 6 + 1}, column {k % 6 + 1}: {e.Message}"); }
        }
        var o = new JsonObject();
        foreach (var (key, r, c) in new[] { ("eps", 0, 0), ("rho", 0, 3), ("rho_prime", 3, 0), ("mu", 3, 3) })
        {
            var b = new Complex[3, 3];
            bool zero = true;
            for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) { b[i, j] = z[(r + i) * 6 + c + j]; zero &= b[i, j] == Complex.Zero; }
            if (zero) continue;
            bool scalar = b[0, 0] == b[1, 1] && b[1, 1] == b[2, 2];
            for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) if (i != j && b[i, j] != Complex.Zero) scalar = false;
            if (scalar) o[key] = MaterialTerms.C(b[0, 0]);
            else
            {
                var rows = new JsonArray();
                for (int i = 0; i < 3; ++i) rows.Add(new JsonArray(MaterialTerms.C(b[i, 0]), MaterialTerms.C(b[i, 1]), MaterialTerms.C(b[i, 2])));
                o[key] = rows;
            }
        }
        if (o.Count == 0) throw new FormatException("the shape is all zeros, so the term adds nothing");
        return o;
    }

    /// <summary>A term in a few words, for the list: "Bu-1: lorentz at 743.48 cm-1, along 47.8°".</summary>
    public static string Describe(JsonObject term)
    {
        var sb = new StringBuilder();
        if (term["label"] is JsonValue l) sb.Append(l.GetValue<string>()).Append(": ");
        var law = MaterialParser.DispersionKeys.FirstOrDefault(term.ContainsKey);
        if (law is null) sb.Append("constant");
        else
        {
            sb.Append(law);
            var p = term[law] as JsonObject;
            string unit = p?["unit"] is JsonValue u ? " " + u.GetValue<string>() : "";
            foreach (var k in new[] { "center", "plasma" })
                if (p?[k] is JsonValue v) { sb.Append($" at {Num(v)}{unit}"); break; }
        }
        if (term["axis_uvw"] is JsonArray uvw) sb.Append($", along [{string.Join(" ", uvw.Select(Num))}]");
        else if (term["axis_deg"] is { } a) sb.Append($", along {(a is JsonArray arr ? string.Join("°, ", arr.Select(x => Num(x!))) : Num(a))}°");
        else if (term["axis"] is JsonArray v3) sb.Append($", along ({string.Join(", ", v3.Select(x => Num(x!)))})");
        else
        {
            var blocks = new[] { "eps", "mu", "rho", "rho_prime", "matrix" }.Where(term.ContainsKey).ToList();
            sb.Append(", ").Append(string.Join(" + ", blocks.Select(b => term[b] is JsonArray ? b == "matrix" ? "6×6" : $"{Greek(b)} 3×3" : $"{Greek(b)} = {Num(term[b]!)}")));
        }
        return sb.ToString();
    }

    private static string Greek(string block) => block switch { "eps" => "ε", "mu" => "μ", "rho" => "ρ", "rho_prime" => "ρ′", _ => block };

    // ---- the simple form -------------------------------------------------------------------------

    public enum SimpleKind { Isotropic, Uniaxial, Biaxial }

    /// <summary>n and k along x, y and z, and which of the three simple kinds the material is.</summary>
    public sealed record SimpleForm(SimpleKind Kind, double[] N, double[] K);

    /// <summary>The material as a simple form, or null when it is anything else.</summary>
    public static SimpleForm? AsSimple(JsonObject spec)
    {
        var keys = spec.Select(p => p.Key).Where(k => k is not ("source" or "notes")).ToHashSet();
        (double N, double K)? Index(string key)
        {
            var node = spec[key];
            if (AsNumber(node) is { } n) return (n, 0);
            if (node is JsonArray a && a.Count == 2 && AsNumber(a[0]) is { } re && AsNumber(a[1]) is { } im) return (re, im);
            return null;
        }
        SimpleForm? Of(SimpleKind kind, params string[] k)
        {
            if (!keys.SetEquals(k)) return null;
            var v = k.Select(Index).ToArray();
            if (v.Any(x => x is null)) return null;
            var full = kind switch
            {
                SimpleKind.Isotropic => new[] { v[0]!.Value, v[0]!.Value, v[0]!.Value },
                SimpleKind.Uniaxial => new[] { v[0]!.Value, v[0]!.Value, v[1]!.Value },
                _ => v.Select(x => x!.Value).ToArray(),
            };
            return new SimpleForm(kind, full.Select(x => x.N).ToArray(), full.Select(x => x.K).ToArray());
        }
        return Of(SimpleKind.Isotropic, "n") ?? Of(SimpleKind.Uniaxial, "no", "ne") ?? Of(SimpleKind.Biaxial, "nx", "ny", "nz");
    }

    /// <summary>The spec of a simple form (keeping the old spec's source and notes).</summary>
    public static JsonObject FromSimple(SimpleForm f, JsonObject? old = null)
    {
        JsonNode Ix(int i) => f.K[i] == 0 ? JsonValue.Create(f.N[i]) : new JsonArray(f.N[i], f.K[i]);
        var o = new JsonObject();
        switch (f.Kind)
        {
            case SimpleKind.Isotropic: o["n"] = Ix(0); break;
            case SimpleKind.Uniaxial: o["no"] = Ix(0); o["ne"] = Ix(2); break;
            default: o["nx"] = Ix(0); o["ny"] = Ix(1); o["nz"] = Ix(2); break;
        }
        foreach (var k in new[] { "source", "notes" })
            if (old?[k] is { } v) o[k] = v.DeepClone();
        return o;
    }

    // ---- the plain form --------------------------------------------------------------------------

    /// <summary>How many principal values the plain form gives: one for every direction, ordinary and extraordinary, or x, y and z.</summary>
    public enum PlainAxes { Same, Uniaxial, Biaxial }

    /// <summary>One principal value: a constant n + ik (Law null), or a law with its parameters.</summary>
    public sealed record PlainValue(string? Law, JsonObject? Parameters, double N = 1.5, double K = 0);

    /// <summary>
    /// The Material page's form: a constant or one dispersion law per principal axis. It is the
    /// short forms of the file (n; no, ne; nx, ny, nz; a law; uniaxial; biaxial) and nothing else,
    /// so what it writes is what the file would say anyway.
    /// </summary>
    public sealed record PlainForm(PlainAxes Axes, PlainValue[] Values);

    /// <summary>
    /// The laws the plain form offers, with their names for people. Each gives a whole ε on its own;
    /// a Lorentz or Drude law, or a conductivity, is only a contribution (it has no background ε),
    /// so a material made of one is shown on Advanced as terms.
    /// </summary>
    public static readonly (string? Law, string Title)[] PlainChoices =
    [
        (null, "Constant n, k"),
        ("glass", "Catalogue glass"),
        ("sellmeier", "Sellmeier formula"),
        ("cauchy", "Cauchy formula"),
        ("table", "Table of measured values"),
        ("tauc_lorentz", "Tauc–Lorentz (amorphous semiconductor)"),
        ("phonon", "Phonons, TO–LO product form"),
        ("phonon_additive", "Phonons, sum of oscillators"),
    ];

    /// <summary>What a plain choice means, for the line under it.</summary>
    public static string PlainFormula(string? law) => law switch
    {
        null => "n + ik, the same at every wavelength. k > 0 absorbs.",
        "glass" => "n from the vendor's catalogue, k = 0. Refused outside the catalogue's wavelength range.",
        "sellmeier" => "n² = 1 + a + Σ b λ²/(λ² − c2), λ in the unit. One line per term: b and c2.",
        "cauchy" => "n = a + b/λ² + c/λ⁴, λ in the unit; k = 0.",
        "table" => "Rows of wavelength (in the unit), n and k, interpolated linearly. Refused outside the rows.",
        "tauc_lorentz" => "ε = ε∞ + χ(E), absorbing only above the band gap Eg (Jellison and Modine 1996). Energies in the unit, eV usually.",
        "phonon" => "ε = ε∞ Π (ω_L² − ω² − iγ_L ω)/(ω_T² − ω² − iγ_T ω). One line per TO–LO pair, in the unit.",
        "phonon_additive" => "ε = ε∞ [1 + Σ (ω_T² − ω_L²)/(ω² − ω_T² + iγω)]. One line per mode, in the unit.",
        _ => LawFormula(law),
    };

    /// <summary>The principal values' names for each choice of axes.</summary>
    public static string[] PlainAxisNames(PlainAxes axes) => axes switch
    {
        PlainAxes.Same => [""],
        PlainAxes.Uniaxial => ["Ordinary (x and y)", "Extraordinary (z, the optic axis)"],
        _ => ["Along x", "Along y", "Along z"],
    };

    /// <summary>The material as a plain form, or null when it is anything else (terms, a model, a tensor, μ).</summary>
    public static PlainForm? AsPlain(JsonObject spec)
    {
        if (AsSimple(spec) is { } s)
        {
            PlainValue V(int i) => new(null, null, s.N[i], s.K[i]);
            return s.Kind switch
            {
                SimpleKind.Isotropic => new PlainForm(PlainAxes.Same, [V(0)]),
                SimpleKind.Uniaxial => new PlainForm(PlainAxes.Uniaxial, [V(0), V(2)]),
                _ => new PlainForm(PlainAxes.Biaxial, [V(0), V(1), V(2)]),
            };
        }
        var keys = spec.Select(p => p.Key).Where(k => k is not ("source" or "notes")).ToList();
        if (keys.Count != 1) return null;
        if (keys[0] is "uniaxial" or "biaxial")
        {
            var names = keys[0] == "uniaxial" ? new[] { "ordinary", "extraordinary" } : ["x", "y", "z"];
            if (spec[keys[0]] is not JsonObject o || o.Count != names.Length || !names.All(o.ContainsKey)) return null;
            var values = names.Select(n => o[n] is JsonObject v ? Value(v) : null).ToArray();
            return values.Any(v => v is null) ? null : new PlainForm(keys[0] == "uniaxial" ? PlainAxes.Uniaxial : PlainAxes.Biaxial, values!);
        }
        return Value(spec) is { } one && one.Law is not null ? new PlainForm(PlainAxes.Same, [one]) : null;

        static PlainValue? Value(JsonObject o)
        {
            var k = o.Select(p => p.Key).Where(k => k is not ("source" or "notes")).ToList();
            if (k.Count != 1) return null;
            if (k[0] == "n")
            {
                if (AsNumber(o["n"]) is { } n) return new PlainValue(null, null, n, 0);
                if (o["n"] is JsonArray a && a.Count == 2 && AsNumber(a[0]) is { } re && AsNumber(a[1]) is { } im) return new PlainValue(null, null, re, im);
                return null;
            }
            return PlainChoices.Any(c => c.Law == k[0]) && o[k[0]] is JsonObject p ? new PlainValue(k[0], p) : null;
        }
    }

    /// <summary>The spec of a plain form, in the file's shortest way of saying it (keeping the old spec's source and notes).</summary>
    public static JsonObject FromPlain(PlainForm f, JsonObject? old = null)
    {
        int count = f.Axes switch { PlainAxes.Same => 1, PlainAxes.Uniaxial => 2, _ => 3 };
        if (f.Values.Length != count) throw new ArgumentException($"{f.Axes} takes {count} values");
        if (f.Values.All(v => v.Law is null))
        {
            var v = f.Values;
            return f.Axes switch
            {
                PlainAxes.Same => FromSimple(new SimpleForm(SimpleKind.Isotropic, [v[0].N, v[0].N, v[0].N], [v[0].K, v[0].K, v[0].K]), old),
                PlainAxes.Uniaxial => FromSimple(new SimpleForm(SimpleKind.Uniaxial, [v[0].N, v[0].N, v[1].N], [v[0].K, v[0].K, v[1].K]), old),
                _ => FromSimple(new SimpleForm(SimpleKind.Biaxial, [v[0].N, v[1].N, v[2].N], [v[0].K, v[1].K, v[2].K]), old),
            };
        }
        static JsonObject Value(PlainValue v) => v.Law is null
            ? new JsonObject { ["n"] = v.K == 0 ? JsonValue.Create(v.N) : new JsonArray(v.N, v.K) }
            : new JsonObject { [v.Law] = (v.Parameters ?? DefaultLaw(v.Law)).DeepClone() };
        var o = f.Axes switch
        {
            PlainAxes.Same => Value(f.Values[0]),
            PlainAxes.Uniaxial => new JsonObject { ["uniaxial"] = new JsonObject { ["ordinary"] = Value(f.Values[0]), ["extraordinary"] = Value(f.Values[1]) } },
            _ => new JsonObject { ["biaxial"] = new JsonObject { ["x"] = Value(f.Values[0]), ["y"] = Value(f.Values[1]), ["z"] = Value(f.Values[2]) } },
        };
        foreach (var k in new[] { "source", "notes" })
            if (old?[k] is { } kept) o[k] = kept.DeepClone();
        return o;
    }

    /// <summary>
    /// Why a material is not a plain form, in a sentence for the Material page: what it is
    /// written as, so the user knows what the Advanced tab will show.
    /// </summary>
    public static string WhyNotPlain(JsonObject spec)
    {
        if (spec["terms"] is JsonArray t) return $"It is written as a list of {t.Count} terms, each a law times a 6×6 shape, which is how a crystal whose axes turn with wavelength, or a magneto-optic, chiral or magnetic material, is written.";
        if (spec["model"] is { } m) return $"It is the {m.GetValue<string>().Replace('_', ' ')} model, computed at each frequency.";
        if (spec.ContainsKey("mu") || spec.ContainsKey("mu_r")) return "It has a magnetic permeability μ as well as ε.";
        if (spec.ContainsKey("rho") || spec.ContainsKey("rho_prime")) return "It couples the electric and magnetic fields (ρ, ρ′).";
        if (spec["eps"] is JsonArray) return "It is a 3×3 ε tensor given element by element.";
        if (spec.ContainsKey("eps") || spec.ContainsKey("eps_r")) return "It is given as ε rather than n.";
        if (spec.ContainsKey("convention")) return "Its values are in the engineering exp(+jωt) convention.";
        if (MaterialParser.DispersionKeys.FirstOrDefault(spec.ContainsKey) is { } law)
            return $"It is a single {law} law, which is a contribution to ε with no background, so it is shown as a term.";
        return "It is written in a form the Material page does not show.";
    }

    /// <summary>The spec written as terms, keeping its source and notes.</summary>
    public static JsonObject AsTerms(JsonObject spec)
    {
        var t = MaterialTerms.ToTerms(spec) ?? throw new FormatException("a resistive sheet depends on its layer's thickness and is not written as terms");
        foreach (var k in new[] { "source", "notes" })
            if (spec[k] is { } v && !t.ContainsKey(k)) t[k] = v.DeepClone();
        return t;
    }
}

/// <summary>What the Materials tab plots against the spectral variable.</summary>
public enum ResponseQuantity { PrincipalIndex, Element, AxisAngles }

/// <summary>A material evaluated across a range: curves for the plot and the checks over the range.</summary>
public static class MaterialResponse
{
    private static readonly uint[] Palette = [0xFF1F77B4, 0xFFD62728, 0xFF2CA02C, 0xFFFF7F0E, 0xFF9467BD, 0xFF8C564B];

    private static readonly int[][] Permutations = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];

    /// <summary>Evenly spaced spectral values from a to b, in the stack's spectral unit.</summary>
    public static double[] Grid(double from, double to, int count)
    {
        if (count < 2) return [from];
        return Enumerable.Range(0, count).Select(i => from + (to - from) * i / (count - 1)).ToArray();
    }

    /// <summary>
    /// The curves: principal-axis n and k (n + ik = √(vᵀ ε v) along each axis v of the real
    /// symmetric part of ε); one element of M, real and imaginary; or each principal axis's
    /// azimuth from x and tilt from z. Points the material refuses (outside a table) are NaN.
    ///
    /// FOLLOWING AN AXIS. The eigen-solver returns axes in order of their values, and two values
    /// cross where a resonance passes; so each axis is matched to the nearest axis of the point
    /// before (the pairing with the largest total |cosine|), not to its rank. An axis is a line, so
    /// its azimuth is taken modulo 180° and unwrapped to stay continuous, and is NaN for an axis
    /// along z, where it has none.
    /// </summary>
    public static List<PlotSeries> Curves(Material m, double[] x, string unit, ResponseQuantity q, int row = 0, int col = 0)
    {
        int n = x.Length;
        var ys = Enumerable.Range(0, 6).Select(_ => Enumerable.Repeat(double.NaN, n).ToArray()).ToArray();
        var lastAxes = new double[3][];
        var lastAz = new[] { double.NaN, double.NaN, double.NaN };
        for (int p = 0; p < n; ++p)
        {
            MaterialMatrix M;
            try { M = m.At(Units.ToWavelengthNm(x[p], unit), 100); }
            catch (StackFormatException) { continue; }
            switch (q)
            {
                case ResponseQuantity.Element:
                    ys[0][p] = M[row, col].Real; ys[1][p] = M[row, col].Imaginary;
                    break;
                case ResponseQuantity.PrincipalIndex:
                case ResponseQuantity.AxisAngles:
                {
                    var (_, axes) = MaterialChecks.PrincipalAxes(M);
                    var cols = Enumerable.Range(0, 3).Select(c => new[] { axes[0, c], axes[1, c], axes[2, c] }).ToArray();
                    if (lastAxes[0] is not null)
                    {
                        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
                        int[] best = [0, 1, 2];
                        double bestScore = -1;
                        foreach (var perm in Permutations)
                        {
                            double score = Enumerable.Range(0, 3).Sum(c => Math.Abs(Dot(cols[perm[c]], lastAxes[c])));
                            if (score > bestScore) { bestScore = score; best = perm; }
                        }
                        cols = best.Select(k => cols[k]).ToArray();
                    }
                    for (int c = 0; c < 3; ++c)
                    {
                        var v = cols[c];
                        /* Keep each axis's sense continuous from point to point. */
                        if (lastAxes[c] is { } prev && v[0] * prev[0] + v[1] * prev[1] + v[2] * prev[2] < 0) v = v.Select(t => -t).ToArray();
                        lastAxes[c] = v;
                        if (q == ResponseQuantity.PrincipalIndex)
                        {
                            Complex e = 0;
                            for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) e += v[i] * M[i, j] * v[j];
                            var ix = Complex.Sqrt(e);
                            if (ix.Imaginary < 0) ix = -ix;
                            ys[c][p] = ix.Real; ys[3 + c][p] = ix.Imaginary;
                        }
                        else
                        {
                            double az = double.NaN;
                            if (Math.Sqrt(v[0] * v[0] + v[1] * v[1]) > 1e-6)
                            {
                                az = Math.Atan2(v[1], v[0]) * 180 / Math.PI;
                                az -= 180 * Math.Round(az / 180);              // a line: (−90°, 90°]
                                if (double.IsFinite(lastAz[c])) az += 180 * Math.Round((lastAz[c] - az) / 180);
                                lastAz[c] = az;
                            }
                            ys[c][p] = az;
                            ys[3 + c][p] = Math.Acos(Math.Clamp(Math.Abs(v[2]), 0, 1)) * 180 / Math.PI;
                        }
                    }
                    break;
                }
            }
        }
        string[] names = ["axis 1", "axis 2", "axis 3"];
        return q switch
        {
            ResponseQuantity.Element =>
            [
                new($"Re M[{row + 1},{col + 1}]", x, ys[0], Palette[0]),
                new($"Im M[{row + 1},{col + 1}]", x, ys[1], Palette[1], Dashed: true),
            ],
            ResponseQuantity.PrincipalIndex => Enumerable.Range(0, 3).SelectMany(c => new[]
            {
                new PlotSeries($"n, {names[c]}", x, ys[c], Palette[c]),
                new PlotSeries($"k, {names[c]}", x, ys[3 + c], Palette[c], Dashed: true, Axis: "k"),
            }).ToList(),
            _ => Enumerable.Range(0, 3).SelectMany(c => new[]
            {
                new PlotSeries($"azimuth from x, {names[c]}", x, ys[c], Palette[c]),
                new PlotSeries($"tilt from z, {names[c]}", x, ys[3 + c], Palette[c], Dashed: true),
            }).ToList(),
        };
    }

    /// <summary>
    /// n and k of a plain form's principal values (the diagonal of ε, which is diagonal in the
    /// material's own axes): one pair for an isotropic material, ordinary and extraordinary, or
    /// x, y and z. A k that is zero everywhere is left out, so a clear glass is one curve.
    /// </summary>
    public static List<PlotSeries> IndexCurves(Material m, double[] x, string unit, MaterialEditing.PlainAxes axes)
    {
        var (names, diag) = axes switch
        {
            MaterialEditing.PlainAxes.Same => (new[] { "" }, new[] { 0 }),
            MaterialEditing.PlainAxes.Uniaxial => (new[] { " ordinary", " extraordinary" }, new[] { 0, 2 }),
            _ => (new[] { " x", " y", " z" }, new[] { 0, 1, 2 }),
        };
        var n = names.Select(_ => Enumerable.Repeat(double.NaN, x.Length).ToArray()).ToArray();
        var k = names.Select(_ => Enumerable.Repeat(double.NaN, x.Length).ToArray()).ToArray();
        for (int p = 0; p < x.Length; ++p)
        {
            MaterialMatrix M;
            try { M = m.At(Units.ToWavelengthNm(x[p], unit), 100); }
            catch (StackFormatException) { continue; }
            for (int c = 0; c < names.Length; ++c)
            {
                var ix = Complex.Sqrt(M[diag[c], diag[c]]);
                if (ix.Imaginary < 0) ix = -ix;
                n[c][p] = ix.Real; k[c][p] = ix.Imaginary;
            }
        }
        var series = new List<PlotSeries>();
        for (int c = 0; c < names.Length; ++c) series.Add(new($"n{names[c]}", x, n[c], Palette[c]));
        for (int c = 0; c < names.Length; ++c)
            if (k[c].Any(v => v > 1e-12)) series.Add(new($"k{names[c]}", x, k[c], Palette[c], Dashed: true, Axis: "k"));
        return series;
    }

    /// <summary>Passivity and reciprocity over the range, in a sentence each (docs/material-model.md, "Checks").</summary>
    public static string Checks(Material m, double[] x, string unit)
    {
        double least = double.PositiveInfinity, leastAt = double.NaN, greatest = 0, worstRecip = 0;
        int gain = 0, refused = 0;
        foreach (var v in x)
        {
            MaterialMatrix M;
            try { M = m.At(Units.ToWavelengthNm(v, unit), 100); }
            catch (StackFormatException) { ++refused; continue; }
            var (l, g) = MaterialChecks.LossRange(M);
            if (l < least) { least = l; leastAt = v; }
            greatest = Math.Max(greatest, g);
            if (l < -1e-12) ++gain;
            worstRecip = Math.Max(worstRecip, MaterialChecks.NonReciprocity(M));
        }
        var sb = new StringBuilder();
        if (refused == x.Length) return "The material cannot be evaluated anywhere in this range.";
        sb.Append(gain == 0
            ? greatest <= 1e-12 ? "Passive and lossless here (no absorption at any point). " : "Passive: it absorbs, with no gain at any point. "
            : $"NOT PASSIVE at {gain} of {x.Length - refused} points: gain for some field (least loss {least:G3} at {leastAt:G6} {unit}). ");
        sb.Append(worstRecip <= 1e-12 ? "Reciprocal." : $"Not reciprocal (largest departure {worstRecip:G3} of the largest element), as a magneto-optic or Faraday medium is.");
        if (refused > 0) sb.Append($" {refused} points are outside the material's range.");
        return sb.ToString();
    }

    /// <summary>The 6×6 M at one spectral value, as aligned text.</summary>
    public static string MatrixText(Material m, double x, string unit)
    {
        var M = m.At(Units.ToWavelengthNm(x, unit), 100);
        var cells = Enumerable.Range(0, 36).Select(k => Short(M.M[k])).ToArray();
        int w = cells.Max(c => c.Length) + 2;
        var sb = new StringBuilder();
        for (int i = 0; i < 6; ++i)
        {
            if (i == 3) sb.AppendLine();
            for (int j = 0; j < 6; ++j) sb.Append(j == 3 ? "  " : "").Append(cells[i * 6 + j].PadLeft(w));
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string Short(Complex z)
    {
        string R(double v) => v.ToString("G5", CultureInfo.InvariantCulture);
        if (z.Imaginary == 0) return R(z.Real);
        if (z.Real == 0) return R(z.Imaginary) + "i";
        return R(z.Real) + (z.Imaginary < 0 ? "-" : "+") + R(Math.Abs(z.Imaginary)) + "i";
    }
}
