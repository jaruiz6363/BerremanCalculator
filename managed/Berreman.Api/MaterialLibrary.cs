// MaterialLibrary.cs — the materials a stack can take from outside itself (docs/material-model.md,
// "Libraries"):
//
//   DATABASES. ThinFilmDesigner's two files (materials/thinfilm-materials.txt and
//     literature-materials.txt, shipped byte for byte), and the user's own file. Choosing a
//     database material COPIES it into the stack, as ThinFilmDesigner does: the stack then owns it
//     and may edit it, and a later change to the database never changes a stack made from it.
//     A crystal listed as "X (o)" and "X (e)" is also offered as one uniaxial material.
//   GLASS CATALOGUES. The vendors' ZEMAX AGF files (materials/glass/*.AGF, byte for byte). A glass
//     is NAMED, never copied (the user's rule for ThinFilmDesigner, 2026-10-02): the stack says
//     {"glass": {"catalog": "SCHOTT", "name": "N-BK7"}} and its index always comes from the
//     catalogue, because a stack that altered it would no longer be using that glass.
//
// The ThinFilmDesigner database format, in micrometres:
//     MATERIAL <name>
//       CONSTANT n k | SELLMEIER B1 C1 B2 C2 ... | CAUCHY A B C | TABLE (rows: λ n k)
//       RANGE min max          (optional)
//       SOURCE <text>
//     END

using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Berreman.Api;

/// <summary>One material a library offers: where it is from, its name, and its stack-file form.</summary>
public sealed record LibraryEntry(string Collection, string Name, JsonObject Spec, string Source);

public static class MaterialLibrary
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The materials folder beside the program.</summary>
    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "materials");

    /// <summary>The user's own materials, kept between sessions; a test points it elsewhere.</summary>
    public static string UserFile { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BerremanCalculator", "user-materials.json");

    /// <summary>The collections a person chooses from, by name: the two databases, the user's file, each glass catalogue.</summary>
    public static IReadOnlyList<string> Collections(string? folder = null, string? userFile = null)
    {
        folder ??= ShippedFolder;
        var list = new List<string>();
        if (File.Exists(Path.Combine(folder, "thinfilm-materials.txt"))) list.Add("Thin-film materials");
        if (File.Exists(Path.Combine(folder, "literature-materials.txt"))) list.Add("Literature materials");
        list.Add("My materials");
        list.AddRange(GlassCatalogs.All.Select(c => $"Glass: {c.Name}"));
        return list;
    }

    /// <summary>The entries of one collection, as <see cref="Collections"/> names it.</summary>
    public static IReadOnlyList<LibraryEntry> Entries(string collection, string? folder = null, string? userFile = null)
    {
        folder ??= ShippedFolder;
        if (collection == "Thin-film materials") return ReadDatabase(Path.Combine(folder, "thinfilm-materials.txt"), collection);
        if (collection == "Literature materials") return ReadDatabase(Path.Combine(folder, "literature-materials.txt"), collection);
        if (collection == "My materials") return ReadUserFile(userFile ?? UserFile);
        if (collection.StartsWith("Glass: ", StringComparison.Ordinal))
        {
            var cat = GlassCatalogs.All.FirstOrDefault(c => c.Name == collection["Glass: ".Length..]);
            if (cat is null) return [];
            return cat.Glasses.Select(g => new LibraryEntry(collection, g.Name,
                new JsonObject { ["glass"] = new JsonObject { ["catalog"] = g.Catalog, ["name"] = g.Name } },
                $"{g.Catalog} catalogue: nd = {g.Nd.ToString("0.#####", Inv)}, vd = {g.Vd.ToString("0.##", Inv)}, "
                + $"{g.MinMicrons.ToString("0.###", Inv)}–{g.MaxMicrons.ToString("0.###", Inv)} µm"
                + (g.Caution is { } c ? $". {c}" : ""))).ToList();
        }
        throw new ArgumentException($"'{collection}' is not a material collection");
    }

    // ---- ThinFilmDesigner's database files --------------------------------------------------------

    public static IReadOnlyList<LibraryEntry> ReadDatabase(string path, string collection) =>
        File.Exists(path) ? ParseDatabase(File.ReadAllText(path), collection) : [];

    public static IReadOnlyList<LibraryEntry> ParseDatabase(string text, string collection)
    {
        var entries = new List<LibraryEntry>();
        string? name = null, kind = null, source = "";
        double[] coefficients = [];
        var rows = new List<double[]>();
        double[]? range = null;
        int line = 0, start = 0;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            ++line;
            var t = raw.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var w = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (w[0] == "MATERIAL")
            {
                name = t["MATERIAL".Length..].Trim();
                (kind, source, coefficients, range, start) = (null, "", [], null, line);
                rows.Clear();
            }
            else if (name is null) throw Bad($"'{w[0]}' outside a MATERIAL block");
            else if (w[0] is "CONSTANT" or "SELLMEIER" or "CAUCHY") { kind = w[0]; coefficients = w.Skip(1).Select(Number).ToArray(); }
            else if (w[0] == "TABLE") kind = "TABLE";
            else if (w[0] == "RANGE" && w.Length >= 3) range = [Number(w[1]), Number(w[2])];
            else if (w[0] == "SOURCE") source = t["SOURCE".Length..].Trim();
            else if (w[0] == "END")
            {
                entries.Add(new LibraryEntry(collection, name, Spec(name, kind, coefficients, rows, range, source, start), source));
                name = null;
            }
            else if (kind == "TABLE") rows.Add(w.Select(Number).ToArray());
            else throw Bad($"'{w[0]}' is not a database keyword");
        }
        if (name is not null) throw Bad($"material '{name}' has no END");

        /* A crystal given as "X (o)" and "X (e)" is one uniaxial material too. */
        foreach (var o in entries.Where(e => e.Name.EndsWith(" (o)", StringComparison.Ordinal)).ToList())
        {
            var baseName = o.Name[..^4];
            if (entries.FirstOrDefault(e => e.Name == baseName + " (e)") is not { } e) continue;
            var spec = new JsonObject
            {
                ["uniaxial"] = new JsonObject { ["ordinary"] = Law(o.Spec), ["extraordinary"] = Law(e.Spec) },
                ["source"] = $"ordinary: {o.Source}; extraordinary: {e.Source}",
            };
            entries.Add(new LibraryEntry(collection, baseName + " (uniaxial)", spec, (string)spec["source"]!));
        }
        return entries;

        double Number(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : throw Bad($"'{s}' is not a number");
        FormatException Bad(string why) => new($"{collection}, line {line}: {why}");

        /* A database entry's law, as one principal value of a uniaxial material takes it. */
        static JsonObject Law(JsonObject spec)
        {
            if (spec["n"] is { } n) return new JsonObject { ["n"] = n.DeepClone() };
            var key = MaterialParser.DispersionKeys.First(spec.ContainsKey);
            return new JsonObject { [key] = spec[key]!.DeepClone() };
        }
    }

    private static JsonObject Spec(string name, string? kind, double[] c, List<double[]> rows, double[]? range, string source, int line)
    {
        JsonArray Range() => new(range![0], range[1]);
        var spec = kind switch
        {
            "CONSTANT" when c.Length >= 1 => new JsonObject
            {
                ["n"] = c.Length >= 2 && c[1] != 0 ? new JsonArray(c[0], c[1]) : JsonValue.Create(c[0]),
            },
            "SELLMEIER" when c.Length >= 2 && c.Length % 2 == 0 => new JsonObject
            {
                ["sellmeier"] = new JsonObject
                {
                    ["unit"] = "um",
                    ["terms"] = new JsonArray(Enumerable.Range(0, c.Length / 2)
                        .Select(i => (JsonNode)new JsonObject { ["b"] = c[2 * i], ["c2"] = c[2 * i + 1] }).ToArray()),
                },
            },
            "CAUCHY" when c.Length >= 1 => new JsonObject
            {
                ["cauchy"] = new JsonObject { ["unit"] = "um", ["a"] = c[0], ["b"] = c.ElementAtOrDefault(1), ["c"] = c.ElementAtOrDefault(2) },
            },
            "TABLE" when rows.Count >= 2 && rows.All(r => r.Length == 3) => new JsonObject
            {
                ["table"] = new JsonObject
                {
                    ["unit"] = "um",
                    ["columns"] = new JsonArray("x", "n", "k"),
                    ["data"] = new JsonArray(rows.Select(r => (JsonNode)new JsonArray(r[0], r[1], r[2])).ToArray()),
                },
            },
            _ => throw new FormatException($"material '{name}' (line {line}): no usable CONSTANT, SELLMEIER, CAUCHY or TABLE"),
        };
        if (range is not null && kind is "SELLMEIER" or "CAUCHY") spec[kind == "SELLMEIER" ? "sellmeier" : "cauchy"]!["range"] = Range();
        if (source.Length > 0) spec["source"] = source;
        return spec;
    }

    // ---- the user's own materials -----------------------------------------------------------------

    /// <summary>The user's file: {"format": "berreman-materials", "version": 1, "materials": {name: spec}}.</summary>
    public static IReadOnlyList<LibraryEntry> ReadUserFile(string path)
    {
        if (!File.Exists(path)) return [];
        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })
                   as JsonObject ?? throw new FormatException($"{path} is not a materials file");
        if (root["materials"] is not JsonObject mats) return [];
        return mats.Select(p => new LibraryEntry("My materials", p.Key, (JsonObject)p.Value!.DeepClone(), p.Value?["source"]?.GetValue<string>() ?? "")).ToList();
    }

    /// <summary>Adds or replaces a material in the user's file (made if absent); refuses one that would not evaluate.</summary>
    public static void SaveToUserFile(string name, JsonObject spec, string? path = null)
    {
        path ??= UserFile;
        if (string.IsNullOrWhiteSpace(name)) throw new FormatException("a material needs a name");
        MaterialParser.Parse(spec, name);
        JsonObject root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
              ?? throw new FormatException($"{path} is not a materials file")
            : new JsonObject { ["format"] = "berreman-materials", ["version"] = 1 };
        if (root["materials"] is not JsonObject mats) root["materials"] = mats = new JsonObject();
        mats[name] = spec.DeepClone();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonText.Write(root));
    }
}

// ---- glass catalogues (ZEMAX AGF), ported from ThinFilmDesigner ---------------------------------

/// <summary>
/// One catalogue glass. The shipped catalogues use three formulas, λ in µm:
///   1 Schott       n² = a0 + a1λ² + a2λ⁻² + a3λ⁻⁴ + a4λ⁻⁶ + a5λ⁻⁸
///   2 Sellmeier 1  n² − 1 = Σ Kᵢλ²/(λ² − Lᵢ), three terms
///   3 Herzberger   n = A + B·L + C·L² + Dλ² + Eλ⁴ + Fλ⁶,  L = 1/(λ² − 0.028)
/// Any other formula is refused with the glass named, never guessed. k = 0: the catalogues'
/// internal transmittance (IT lines) is not read yet.
/// </summary>
public sealed record Glass(string Catalog, string Name, int Formula, double[] Coefficients,
                           double MinMicrons, double MaxMicrons, double Nd, double Vd)
{
    public string FullName => $"{Catalog} {Name}";

    /// <summary>A sentence when the catalogue's formula and its own nd disagree by more than 2e-4 at the d line.</summary>
    public string? Caution
    {
        get
        {
            const double d = 0.5875618;
            if (Nd <= 1.0 || d < MinMicrons || d > MaxMicrons) return null;
            double n;
            try { n = N(d); } catch (StackFormatException) { return null; }
            return Math.Abs(n - Nd) > 2e-4
                ? $"The formula gives n = {n:F5} at the d line, but the catalogue's own nd is {Nd:F5}; the formula is used. Check the vendor's data sheet."
                : null;
        }
    }

    /// <summary>n at λ µm; refused outside the catalogue's range, as every law here is.</summary>
    public double N(double um)
    {
        if (um < MinMicrons - 1e-12 || um > MaxMicrons + 1e-12)
            throw new StackFormatException($"{um:G6} um is outside glass {FullName}'s range [{MinMicrons}, {MaxMicrons}] um");
        double l2 = um * um;
        double C(int i) => i < Coefficients.Length ? Coefficients[i] : 0.0;
        double n = Formula switch
        {
            1 => Math.Sqrt(C(0) + C(1) * l2 + C(2) / l2 + C(3) / (l2 * l2) + C(4) / (l2 * l2 * l2) + C(5) / (l2 * l2 * l2 * l2)),
            2 => Math.Sqrt(1.0 + C(0) * l2 / (l2 - C(1)) + C(2) * l2 / (l2 - C(3)) + C(4) * l2 / (l2 - C(5))),
            3 => Herzberger(),
            _ => throw new StackFormatException($"glass {FullName} uses dispersion formula {Formula}, which is not evaluated (1 Schott, 2 Sellmeier 1 and 3 Herzberger are)"),
        };
        if (!(n > 0) || !double.IsFinite(n)) throw new StackFormatException($"glass {FullName} has no physical index at {um:G6} um");
        return n;

        double Herzberger()
        {
            double L = 1.0 / (l2 - 0.028);
            return C(0) + C(1) * L + C(2) * L * L + C(3) * l2 + C(4) * l2 * l2 + C(5) * l2 * l2 * l2;
        }
    }
}

public sealed class GlassCatalog
{
    public string Name { get; }
    public IReadOnlyList<Glass> Glasses { get; }
    private readonly Dictionary<string, Glass> _byName;

    private GlassCatalog(string name, List<Glass> glasses)
    {
        Name = name;
        Glasses = glasses;
        _byName = new Dictionary<string, Glass>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in glasses) _byName.TryAdd(g.Name, g);
    }

    public Glass? Find(string glass) => _byName.GetValueOrDefault(glass);

    /// <summary>A catalogue file, named by its file name (SCHOTT.AGF is "SCHOTT"); CDGM issues UTF-16, which the BOM gives.</summary>
    public static GlassCatalog Load(string path) =>
        Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path).ToUpperInvariant());

    /// <summary>Reads NM (name, formula, nd, vd), CD (coefficients) and LD (range); the rest is skipped.</summary>
    public static GlassCatalog Parse(string text, string catalog)
    {
        var inv = CultureInfo.InvariantCulture;
        var glasses = new List<Glass>();
        string? name = null;
        int formula = 0, line = 0;
        double nd = 0, vd = 0, lo = 0, hi = double.PositiveInfinity;
        double[] cd = [];
        void Finish()
        {
            if (name is not null) glasses.Add(new Glass(catalog, name, formula, cd, lo, hi, nd, vd));
            (name, cd, lo, hi) = (null, [], 0, double.PositiveInfinity);
        }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            ++line;
            var w = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 0) continue;
            switch (w[0])
            {
                case "NM":
                    Finish();
                    if (w.Length < 6) throw Bad("an NM line needs a name, a formula, a code, nd and vd");
                    name = w[1];
                    formula = (int)Number(w[2]);
                    nd = Number(w[4]);
                    vd = Number(w[5]);
                    break;
                case "CD" when name is not null:
                    cd = w.Skip(1).Select(Number).ToArray();
                    break;
                case "LD" when name is not null && w.Length >= 3:
                    lo = Number(w[1]);
                    hi = Number(w[2]);
                    break;
            }
        }
        Finish();
        return new GlassCatalog(catalog, glasses);

        double Number(string s) => double.TryParse(s, NumberStyles.Float, inv, out double v) ? v : throw Bad($"'{s}' is not a number");
        FormatException Bad(string why) => new($"glass catalogue {catalog}, line {line}: {why}");
    }
}

/// <summary>Every catalogue in the glass folder, read once; the one place a named glass is looked up.</summary>
public static class GlassCatalogs
{
    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "materials", "glass");

    private static string _folder = ShippedFolder;
    private static IReadOnlyList<GlassCatalog>? _catalogs;
    private static readonly object Gate = new();

    public static string Folder
    {
        get => _folder;
        set { lock (Gate) { _folder = value; _catalogs = null; } }
    }

    /// <summary>The catalogues, read once; one that does not parse is skipped and named in <see cref="Problems"/>.</summary>
    public static IReadOnlyList<GlassCatalog> All
    {
        get
        {
            lock (Gate)
            {
                if (_catalogs is not null) return _catalogs;
                var list = new List<GlassCatalog>();
                var problems = new List<string>();
                if (Directory.Exists(_folder))
                    foreach (var f in Directory.GetFiles(_folder).Where(f => f.EndsWith(".agf", StringComparison.OrdinalIgnoreCase)).Order())
                    {
                        try { list.Add(GlassCatalog.Load(f)); }
                        catch (Exception e) when (e is FormatException or IOException) { problems.Add(e.Message); }
                    }
                Problems = problems;
                return _catalogs = list;
            }
        }
    }

    public static IReadOnlyList<string> Problems { get; private set; } = [];

    public static Glass Find(string catalog, string glass) =>
        All.FirstOrDefault(c => string.Equals(c.Name, catalog, StringComparison.OrdinalIgnoreCase))?.Find(glass)
        ?? throw new StackFormatException($"glass {catalog} {glass} is not in the catalogues in {_folder}");
}

/// <summary>A catalogue glass as a law: f = n², k = 0.</summary>
public sealed class GlassDispersion(Glass glass) : Dispersion
{
    public Glass Glass { get; } = glass;
    public override Complex Eps(double wavelengthNm)
    {
        double n = Glass.N(wavelengthNm / 1000.0);
        return n * n;
    }
}
