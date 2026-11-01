// CoatingImport.cs — coatings read from OpticStudio coating files and from Optiland, as layers and
// materials of a stack. The parsing is ported from ThinFilmDesigner (ZemaxCoating.cs and
// OptilandCoating.cs, MIT, the same author); what comes out is this program's stack form.
//
// OPTICSTUDIO (COATING.DAT and the like). Keyword sections: MATE name, then rows "λ(µm) n k";
// COAT name, then layers "material thickness [is_absolute] [loop] [taper]". A thickness is in
// waves of optical thickness (n d = waves × λ) unless is_absolute is 1, when it is in µm. k is
// negative for an absorber there and positive here, so it is negated. A MATE is read as its n,k
// table, which is what OpticStudio itself uses. OpticStudio writes UTF-16; a hand-made file is
// usually ASCII; the byte-order mark says which.
//
// WAVES ARE COUNTED AT A WAVELENGTH OPTICSTUDIO TAKES FROM THE LENS (its primary wavelength), which
// the coating file does not hold. ThinFilmDesigner writes it in a comment above the COAT ("designed
// at 550 nm"), and that is used when present; otherwise the caller must give one.
//
// OPTILAND. No coating file of its own: a ThinFilmCoating is a JSON object, alone in a file or on a
// surface of a lens file (surface_group.surfaces[i].interaction_model.coating). Layers are incident
// side first, each { material, thickness_nm }; the media are material_pre and material_post. Its k
// is positive for an absorber, as ours. Materials: IdealMaterial (n, k), DataMaterial (one of
// refractiveindex.info's nine formulas, or n and k tables), AGFMaterial (a catalogue glass), and a
// database Material through the refractiveindex.info YAML file it names, when that file is on this
// computer. An AbbeMaterial is refused: it is a fit to two numbers, not data. Formulas 1 and 2 are
// our Sellmeier, formula 5 with powers 0, -2, -4 our Cauchy; any other formula, or a formula with a
// measured k beside it, is tabulated finely enough (1e-6) that linear interpolation reproduces it.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Berreman.Api;

/// <summary>A coating found in a file: its name, how many layers, and why it cannot be imported, if it cannot.</summary>
public sealed record CoatingInFile(string Name, int Layers, string? Problem);

/// <summary>
/// A coating read out of a file: its layers, incident side first, each a material name and a
/// physical thickness in nm; the materials, as stack-file specifications; the media, when the file
/// gives them (Optiland does); and notes on anything the reading changed or assumed.
/// </summary>
public sealed record ImportedCoating(
    string Name,
    IReadOnlyList<(string Material, double ThicknessNm)> Layers,
    IReadOnlyList<(string Name, JsonObject Spec)> Materials,
    (string Name, JsonObject Spec)? Incident,
    (string Name, JsonObject Spec)? Exit,
    IReadOnlyList<string> Notes);

// ================================================================================================
// OpticStudio
// ================================================================================================

public static class ZemaxCoatingReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Keywords = ["MATE", "TAPR", "COAT", "TABLE", "IDEAL", "IDEAL2", "ENCRYPTED"];

    private sealed record Section(string Keyword, string Name, int Start, int End);

    private sealed class File(string[] lines)
    {
        public readonly string[] Lines = lines;
        public readonly List<Section> Sections = Index(lines);

        public Section? Find(string keyword, string name) =>
            Sections.FirstOrDefault(s => s.Keyword == keyword && s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The file's text, in whatever encoding its byte-order mark names.</summary>
    private static File Open(byte[] bytes)
    {
        Encoding encoding = new UTF8Encoding(false);
        int skip = 0;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encoding = new UnicodeEncoding(false, true); skip = 2; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encoding = new UnicodeEncoding(true, true); skip = 2; }
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) skip = 3;
        var text = encoding.GetString(bytes, skip, bytes.Length - skip);
        return new File(text.Replace("\r\n", "\n").Split('\n'));
    }

    private static string[] Words(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static bool IsComment(string line) => line.TrimStart().StartsWith('!');

    private static string? KeywordOf(string line)
    {
        var w = Words(line);
        return w.Length == 0 ? null : Keywords.FirstOrDefault(k => k.Equals(w[0], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every section: from its keyword line to the next keyword or comment line.</summary>
    private static List<Section> Index(string[] lines)
    {
        var sections = new List<Section>();
        for (int i = 0; i < lines.Length; ++i)
        {
            if (KeywordOf(lines[i]) is not { } keyword) continue;
            var w = Words(lines[i]);
            int end = i + 1;
            while (end < lines.Length && KeywordOf(lines[end]) is null && !IsComment(lines[end])) ++end;
            sections.Add(new Section(keyword, w.Length > 1 ? w[1] : "", i, end));
        }
        return sections;
    }

    private sealed record Layer(string Material, double Thickness, bool Microns, int Loop, string? Taper);

    private static Layer[] Layers(File f, Section coat)
    {
        var layers = new List<Layer>();
        for (int i = coat.Start + 1; i < coat.End; ++i)
        {
            var w = Words(f.Lines[i]);
            if (w.Length == 0) continue;
            if (w.Length < 2 || !Number(w[1], out double t))
                throw new StackFormatException($"line {i + 1}: a COAT layer is 'material thickness [is_absolute] [loop] [taper]', not '{f.Lines[i].Trim()}'");
            int absolute = 0, loop = 0;
            if (w.Length > 2 && (!int.TryParse(w[2], NumberStyles.Integer, Inv, out absolute) || absolute is not (0 or 1)))
                throw new StackFormatException($"line {i + 1}: is_absolute is 0 (waves) or 1 (µm), not '{w[2]}'");
            if (w.Length > 3 && !int.TryParse(w[3], NumberStyles.Integer, Inv, out loop))
                throw new StackFormatException($"line {i + 1}: the loop index is a whole number, not '{w[3]}'");
            layers.Add(new Layer(w[0], t, absolute == 1, loop, w.Length > 4 ? w[4] : null));
        }
        return layers.ToArray();
    }

    /// <summary>A MATE's rows, [λ µm, n, k] with k as OpticStudio writes it (negative absorbs).</summary>
    private static double[][] Rows(File f, Section mate)
    {
        var rows = new List<double[]>();
        for (int i = mate.Start + 1; i < mate.End; ++i)
        {
            var w = Words(f.Lines[i]);
            if (w.Length == 0) continue;
            if (w.Length < 3 || !Number(w[0], out double l) || !Number(w[1], out double n) || !Number(w[2], out double k))
                throw new StackFormatException($"line {i + 1}: a MATE row is 'wavelength real imaginary', not '{f.Lines[i].Trim()}'");
            rows.Add([l, n, k]);
        }
        return rows.ToArray();
    }

    private static bool Number(string s, out double v) => double.TryParse(s, NumberStyles.Float, Inv, out v);

    /// <summary>The reference wavelength a comment above the COAT gives ("designed at 550 nm",
    /// "reference wavelength 0.55 um"), in nm; null when none does.</summary>
    private static double? ReferenceNm(File f, Section coat)
    {
        int i = coat.Start;
        while (i > 0 && IsComment(f.Lines[i - 1])) --i;
        for (; i < coat.Start; ++i)
        {
            var m = System.Text.RegularExpressions.Regex.Match(f.Lines[i],
                @"(?i)(?:designed\s+at|reference\s+wavelength)\D*?([0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*(nm|um|µm|microns?)?");
            if (!m.Success) continue;
            double v = double.Parse(m.Groups[1].Value, Inv);
            string unit = m.Groups[2].Value.ToLowerInvariant();
            double r = unit == "nm" || (unit.Length == 0 && v > 50) ? v : v * 1000;
            if (r > 0) return r;
        }
        return null;
    }

    /// <summary>The layer-stack coatings in the file, in file order, each with why it cannot be
    /// imported if it cannot. `COAT I.95` and its kind are an ideal transmission, not a stack, and
    /// TABLE, IDEAL and ENCRYPTED coatings are defined by their effect; none is listed.</summary>
    public static IReadOnlyList<CoatingInFile> List(byte[] bytes)
    {
        var f = Open(bytes);
        var list = new List<CoatingInFile>();
        foreach (var coat in f.Sections.Where(s => s.Keyword == "COAT" && !s.Name.StartsWith("I.", StringComparison.OrdinalIgnoreCase)))
        {
            string? why = null;
            int count = 0;
            try
            {
                var layers = Layers(f, coat);
                count = layers.Length;
                if (count == 0) continue;
                if (layers.Any(l => l.Loop != 0)) why = "uses a loop index, which import does not expand";
                else if (layers.Select(l => l.Material).FirstOrDefault(m => f.Find("MATE", m) is null) is { } missing)
                    why = $"uses {missing}, which has no MATE in this file";
            }
            catch (StackFormatException e) { why = e.Message; }
            list.Add(new CoatingInFile(coat.Name, count, why));
        }
        return list;
    }

    /// <summary>Whether a coating gives any thickness in waves, and the reference wavelength (nm)
    /// the file states for it, if it states one. A coating in waves needs a wavelength.</summary>
    public static (bool Waves, double? ReferenceNm) Waves(byte[] bytes, string coating)
    {
        var f = Open(bytes);
        var coat = f.Find("COAT", coating) ?? throw new StackFormatException($"the file has no coating called {coating}");
        return (Layers(f, coat).Any(l => !l.Microns), ReferenceNm(f, coat));
    }

    /// <summary>
    /// A coating as layers and materials. A thickness in waves is converted at the reference
    /// wavelength the file states, or else at <paramref name="referenceNm"/>; with neither, it is
    /// refused. Each MATE becomes an n,k table in µm (a single row, a constant).
    /// </summary>
    public static ImportedCoating Read(byte[] bytes, string coating, double? referenceNm = null)
    {
        var f = Open(bytes);
        var coat = f.Find("COAT", coating)
            ?? throw new StackFormatException(f.Sections.FirstOrDefault(s => s.Name.Equals(coating, StringComparison.OrdinalIgnoreCase)) is { } other
                ? $"{other.Name} is a {other.Keyword} coating, defined by its effect rather than by layers, so it cannot be imported"
                : $"the file has no coating called {coating}");
        var layers = Layers(f, coat);
        if (layers.Length == 0) throw new StackFormatException($"{coat.Name} has no layers");
        if (layers.Any(l => l.Loop != 0)) throw new StackFormatException($"{coat.Name} uses a loop index, which import does not expand; write its layers out in full");

        var notes = new List<string>();
        if (layers.Any(l => l.Taper is not null)) notes.Add($"{coat.Name} is tapered across the surface; its layers are taken at their nominal thickness");

        var tables = new Dictionary<string, double[][]>(StringComparer.OrdinalIgnoreCase);
        var materials = new List<(string, JsonObject)>();
        foreach (var name in layers.Select(l => l.Material).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var mate = f.Find("MATE", name) ?? throw new StackFormatException($"{coat.Name} uses {name}, which has no MATE in the file");
            var rows = Rows(f, mate).OrderBy(r => r[0]).ToArray();
            if (rows.Length == 0) throw new StackFormatException($"{mate.Name} has no rows");
            tables[name] = rows;
            JsonObject spec = rows.Length == 1
                ? new JsonObject { ["n"] = Complex(rows[0][1], -rows[0][2]) }
                : new JsonObject
                {
                    ["table"] = new JsonObject
                    {
                        ["unit"] = "um",
                        ["columns"] = new JsonArray("x", "n", "k"),
                        ["data"] = new JsonArray(rows.Select(r => (JsonNode)new JsonArray(r[0], r[1], r[2] == 0 ? 0.0 : -r[2])).ToArray()),
                    },
                };
            materials.Add((mate.Name, spec));
        }

        double? reference = null;
        if (layers.Any(l => !l.Microns))
        {
            reference = ReferenceNm(f, coat) ?? referenceNm
                ?? throw new StackFormatException($"{coat.Name} gives thicknesses in waves, and the file does not say at which wavelength; give it");
            notes.Add($"thicknesses in waves are counted at {reference.Value.ToString("0.###", Inv)} nm{(ReferenceNm(f, coat) is null ? "" : ", which the file says it was designed at")}");
        }
        var stack = layers.Select(l =>
        {
            string m = materials.First(x => x.Item1.Equals(l.Material, StringComparison.OrdinalIgnoreCase)).Item1;
            if (l.Microns) return (m, l.Thickness * 1000.0);
            double n = Interpolate(tables[l.Material], reference!.Value / 1000.0);
            return (m, l.Thickness * reference!.Value / n);
        }).ToList();
        return new ImportedCoating(coat.Name, stack, materials, null, null, notes);
    }

    private static JsonNode Complex(double n, double k) => k == 0 ? JsonValue.Create(n) : new JsonArray(n, k);

    private static double Interpolate(double[][] rows, double um)
    {
        if (um <= rows[0][0]) return rows[0][1];
        if (um >= rows[^1][0]) return rows[^1][1];
        int i = 1;
        while (rows[i][0] < um) ++i;
        double t = (um - rows[i - 1][0]) / (rows[i][0] - rows[i - 1][0]);
        return rows[i - 1][1] + t * (rows[i][1] - rows[i - 1][1]);
    }
}

// ================================================================================================
// Optiland
// ================================================================================================

public static class OptilandCoatingReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The text as JSON, with Optiland's bare Infinity, -Infinity and NaN read as null.</summary>
    private static JsonNode Parse(string text)
    {
        var s = new StringBuilder(text.Length);
        bool inString = false;
        for (int i = 0; i < text.Length; ++i)
        {
            char c = text[i];
            if (inString)
            {
                s.Append(c);
                if (c == '\\' && i + 1 < text.Length) s.Append(text[++i]);
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; s.Append(c); continue; }
            if (Word(text, i, "-Infinity")) { s.Append("null"); i += 8; continue; }
            if (Word(text, i, "Infinity")) { s.Append("null"); i += 7; continue; }
            if (Word(text, i, "NaN")) { s.Append("null"); i += 2; continue; }
            s.Append(c);
        }
        try { return JsonNode.Parse(s.ToString()) ?? throw new StackFormatException("the file is empty"); }
        catch (System.Text.Json.JsonException e) { throw new StackFormatException($"the file is not JSON: {e.Message}"); }

        static bool Word(string t, int at, string w) =>
            string.CompareOrdinal(t, at, w, 0, w.Length) == 0
            && (at + w.Length == t.Length || !char.IsLetterOrDigit(t[at + w.Length]));
    }

    /// <summary>Every ThinFilmCoating in a file: the file itself when it is one, otherwise each
    /// coated surface of a lens file, named by its index and comment.</summary>
    private static List<(string Name, JsonObject Coating)> Coatings(JsonNode root)
    {
        var found = new List<(string, JsonObject)>();
        if (root is JsonObject o && Str(o, "type") == "ThinFilmCoating") { found.Add(("coating", o)); return found; }
        if (root["surface_group"]?["surfaces"] is JsonArray surfaces)
            for (int i = 0; i < surfaces.Count; ++i)
            {
                if (surfaces[i] is not JsonObject surface) continue;
                var coating = surface["interaction_model"]?["coating"] as JsonObject ?? surface["coating"] as JsonObject;
                if (coating is null || Str(coating, "type") != "ThinFilmCoating") continue;
                string comment = Str(surface, "comment") ?? "";
                found.Add((comment.Length > 0 ? $"surface {i} ({comment})" : $"surface {i}", coating));
            }
        return found;
    }

    /// <summary>The coatings in the file, in order, each with why it cannot be imported if it cannot.
    /// <paramref name="gridUm"/> is where a formula with no range of its own would be tabulated.</summary>
    public static IReadOnlyList<CoatingInFile> List(string text, (double Lo, double Hi) gridUm)
    {
        var list = new List<CoatingInFile>();
        foreach (var (name, coating) in Coatings(Parse(text)))
        {
            var layers = coating["layers"] as JsonArray ?? [];
            string? why = layers.Count == 0 ? "has no layers" : null;
            foreach (var l in layers)
            {
                if (why is not null) break;
                if (l?["material"] is not JsonObject m) { why = "has a layer with no material"; break; }
                why = Material(m, gridUm).Problem;
            }
            list.Add(new CoatingInFile(name, layers.Count, why));
        }
        return list;
    }

    /// <summary>A coating as layers, materials and media; <paramref name="index"/> counts the
    /// coatings <see cref="List"/> gives.</summary>
    public static ImportedCoating Read(string text, int index, (double Lo, double Hi) gridUm)
    {
        var found = Coatings(Parse(text));
        if (index < 0 || index >= found.Count)
            throw new StackFormatException($"the file has {found.Count} thin-film coating{(found.Count == 1 ? "" : "s")}; there is no number {index + 1}");
        var (name, coating) = found[index];
        var layers = coating["layers"] as JsonArray ?? [];
        if (layers.Count == 0) throw new StackFormatException($"{name} has no layers");

        var notes = new List<string>();
        var materials = new List<(string Name, JsonObject Spec)>();
        var byContent = new Dictionary<string, string>();
        string Add(JsonObject m)
        {
            string key = m.ToJsonString();
            if (byContent.TryGetValue(key, out var known)) return known;
            var r = Material(m, gridUm);
            if (r.Problem is not null) throw new StackFormatException(r.Problem);
            if (r.Note is not null) notes.Add(r.Note);
            string chosen = r.Name;
            for (int k = 2; materials.Any(x => x.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase)); ++k) chosen = $"{r.Name}_{k}";
            materials.Add((chosen, r.Spec!));
            return byContent[key] = chosen;
        }

        var stack = new List<(string, double)>();
        foreach (var layer in layers)
        {
            if (layer?["material"] is not JsonObject m || layer["thickness_nm"] is not JsonValue tv || !tv.TryGetValue(out double nm))
                throw new StackFormatException($"{name}: a layer without a material or a thickness_nm");
            stack.Add((Add(m), nm));
        }

        (string, JsonObject)? Medium(JsonNode? node, string which)
        {
            if (node is not JsonObject o) return null;
            var r = Material(o, gridUm);
            if (r.Problem is not null) { notes.Add($"the coating's {which} cannot be read ({r.Problem}); the stack keeps its own"); return null; }
            return (r.Name, r.Spec!);
        }
        return new ImportedCoating(name, stack, materials, Medium(coating["material_pre"], "incident medium"), Medium(coating["material_post"], "substrate"), notes);
    }

    // ---- an Optiland material as a stack-file material ----------------------------------------

    private sealed record MaterialRead(string Name, JsonObject? Spec, string? Note, string? Problem);

    private static MaterialRead Material(JsonObject m, (double Lo, double Hi) gridUm)
    {
        string type = Str(m, "type") ?? "";
        try
        {
            switch (type)
            {
                case "IdealMaterial":
                {
                    double n = Num(m, "index") ?? 1.0, k = Num(m, "absorp") ?? 0.0;
                    string name = n == 1.0 && k == 0.0 ? "air" : $"n={n.ToString("0.#####", Inv)}" + (k > 0 ? $" k={k.ToString("G3", Inv)}" : "");
                    return new MaterialRead(name, Constant(n, k), null, null);
                }
                case "DataMaterial":
                {
                    string name = Str(m, "name") is { Length: > 0 } nm ? nm : "DataMaterial";
                    var definition = m["definition"] as JsonObject ?? throw new StackFormatException("a DataMaterial with no definition");
                    return FromDefinition(name, definition, gridUm);
                }
                case "AGFMaterial":
                {
                    string name = Str(m, "name") ?? "glass", catalog = Str(m, "catalog") ?? "";
                    if (HasGlass(catalog, name))
                        return new MaterialRead(name, new JsonObject { ["glass"] = new JsonObject { ["catalog"] = catalog.ToUpperInvariant(), ["name"] = name } }, null, null);
                    int agf = (int)(Num(m, "formula") ?? 0);
                    var coefficients = Nums(m["coefficients"]) ?? [];
                    if (FormulaOfGlass(agf, coefficients) is not { } f)
                        return new MaterialRead(name, null, null, $"{name} is a catalogue glass with formula {agf}, which is not read here, and {catalog} is not among this program's glass catalogues");
                    return FromFormula(name, f.Formula, f.Coefficients, Nums(m["wavelength_range"]), null, gridUm,
                                       $"{name} is read from its {catalog} formula; {catalog} is not among this program's glass catalogues");
                }
                case "Material":
                case "MaterialFile":
                {
                    string file = Str(m, "filename") ?? "";
                    string name = Str(m, "name") ?? Path.GetFileNameWithoutExtension(file);
                    string reference = Str(m, "reference") is { Length: > 0 } r ? $" ({r})" : "";
                    if (file.Length == 0 || !System.IO.File.Exists(file))
                        return new MaterialRead(name, null, null,
                            $"{name}{reference} is a material from Optiland's database, read from {(file.Length == 0 ? "a file it does not name" : file)}, which is not on this computer");
                    return FromYaml(name, ReadYaml(System.IO.File.ReadAllText(file)), gridUm, Path.GetFileName(file));
                }
                case "AbbeMaterial":
                case "AbbeMaterialE":
                    return new MaterialRead("Abbe", null, null,
                        $"an {type} (n = {Num(m, "index")}, V = {Num(m, "abbe")}) is a fit to two numbers rather than data; give the layer a real material in Optiland");
                default:
                    return new MaterialRead(type, null, null, $"'{type}' is not an Optiland material this program reads");
            }
        }
        catch (Exception e) when (e is StackFormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            return new MaterialRead(Str(m, "name") ?? type, null, null, $"{Str(m, "name") ?? type}: {e.Message}");
        }
    }

    private static bool HasGlass(string catalog, string glass)
    {
        try { GlassCatalogs.Find(catalog, glass); return true; }
        catch (StackFormatException) { return false; }
    }

    private static MaterialRead FromDefinition(string name, JsonObject definition, (double Lo, double Hi) gridUm)
    {
        var dispersion = definition["dispersion"] as JsonObject ?? throw new StackFormatException("no dispersion");
        double[][]? k = null;
        if (definition["extinction"] is JsonObject e)
        {
            var kw = Nums(e["wavelengths_um"]) ?? []; var kv = Nums(e["values"]) ?? [];
            if (kv.Any(v => v > 0.0)) k = kw.Zip(kv, (a, b) => new[] { a, b }).ToArray();
        }
        string kind = Str(dispersion, "kind") ?? "";
        if (kind == "tabulated")
        {
            var w = Nums(dispersion["wavelengths_um"]) ?? []; var n = Nums(dispersion["indices"]) ?? [];
            return FromTables(name, w.Zip(n, (a, b) => new[] { a, b }).ToArray(), k);
        }
        if (kind == "formula")
            return FromFormula(name, Str(dispersion, "formula") ?? "", Nums(dispersion["coefficients"]) ?? [],
                               Nums(dispersion["wavelength_range_um"]), k, gridUm, null);
        throw new StackFormatException($"a dispersion of kind '{kind}'");
    }

    private static MaterialRead FromYaml(string name, List<YamlEntry> entries, (double Lo, double Hi) gridUm, string file)
    {
        double[][]? k = entries.FirstOrDefault(x => x.Type == "tabulated k") is { } kt && kt.Rows.Any(r => r[1] > 0)
            ? kt.Rows.Select(r => new[] { r[0], r[1] }).ToArray() : null;
        if (entries.FirstOrDefault(x => x.Type == "tabulated nk") is { } nk)
            return FromTables(name, nk.Rows.Select(r => new[] { r[0], r[1] }).ToArray(),
                              nk.Rows.Any(r => r.Length > 2 && r[2] > 0) ? nk.Rows.Select(r => new[] { r[0], r.Length > 2 ? r[2] : 0.0 }).ToArray() : k);
        if (entries.FirstOrDefault(x => x.Type == "tabulated n") is { } tn)
            return FromTables(name, tn.Rows.Select(r => new[] { r[0], r[1] }).ToArray(), k);
        if (entries.FirstOrDefault(x => x.Type.StartsWith("formula ")) is { } f)
            return FromFormula(name, f.Type, f.Coefficients, f.Range, k, gridUm, $"{name} is read from Optiland's database file {file}");
        throw new StackFormatException($"{file} has no data this program reads");
    }

    /// <summary>An n table and a k table on their own grids, as one table on both.</summary>
    private static MaterialRead FromTables(string name, double[][] n, double[][]? k)
    {
        if (n.Length == 0) throw new StackFormatException("an empty table");
        var w = n.Select(r => r[0]).Concat(k?.Select(r => r[0]) ?? []).Distinct().OrderBy(x => x).ToArray();
        var rows = w.Select(l => new[] { l, Linear(n, l), k is null ? 0.0 : Linear(k, l) }).ToArray();
        return new MaterialRead(name, rows.Length == 1 ? Constant(rows[0][1], rows[0][2]) : Table(rows), null, null);
    }

    private static MaterialRead FromFormula(string name, string formula, double[] c, double[]? range, double[][]? k,
                                    (double Lo, double Hi) gridUm, string? from)
    {
        Formula(formula, c, gridUm.Lo);   // refuses an unknown formula
        if (k is null && SpecOfFormula(formula, c, range) is { } spec) return new MaterialRead(name, spec, from, null);

        /* No law of ours: a table, fine enough that linear interpolation is within 1e-6. */
        double lo = range?[0] ?? gridUm.Lo, hi = range?[1] ?? gridUm.Hi;
        if (k is not null) { lo = Math.Min(lo, k[0][0]); hi = Math.Max(hi, k[^1][0]); }
        if (range is not null) { lo = Math.Max(lo, range[0]); hi = Math.Min(hi, range[1]); }
        var rows = Sample(l => (Formula(formula, c, l), k is null ? 0.0 : Linear(k, l)), lo, hi);
        string why = k is not null ? "it has a measured k beside its formula" : $"this program has no law for {formula}";
        string note = $"{name} is read as a table of {rows.Length} rows from {lo.ToString("0.###", Inv)} to {hi.ToString("0.###", Inv)} µm, because {why}";
        return new MaterialRead(name, rows.Length == 1 ? Constant(rows[0][1], rows[0][2]) : Table(rows), from is null ? note : $"{from}; {note}", null);
    }

    /// <summary>Formulas 1 and 2 as a Sellmeier (a term with no resonance adds to a), formula 5
    /// with powers 0, −2, −4 as a Cauchy (a constant when it has only powers 0); null otherwise.</summary>
    private static JsonObject? SpecOfFormula(string formula, double[] c, double[]? range)
    {
        JsonObject Ranged(JsonObject law) { if (range is { Length: 2 }) law["range"] = new JsonArray(range[0], range[1]); return law; }
        if (formula is "formula 1" or "formula 2")
        {
            double a = c.Length > 0 ? c[0] : 0;
            var terms = new JsonArray();
            for (int i = 1; i + 1 < c.Length; i += 2)
            {
                double c2 = formula == "formula 1" ? c[i + 1] * c[i + 1] : c[i + 1];
                if (c2 == 0) a += c[i];
                else if (c[i] != 0) terms.Add(new JsonObject { ["b"] = c[i], ["c2"] = c2 });
            }
            if (terms.Count == 0) return new JsonObject { ["n"] = Math.Sqrt(1 + a) };
            var law = new JsonObject { ["unit"] = "um" };
            if (a != 0) law["a"] = a;
            law["terms"] = terms;
            return new JsonObject { ["sellmeier"] = Ranged(law) };
        }
        if (formula == "formula 5")
        {
            double a = c.Length > 0 ? c[0] : 0, b = 0, cc = 0;
            for (int i = 1; i + 1 < c.Length; i += 2)
            {
                if (c[i + 1] == 0) a += c[i];
                else if (c[i + 1] == -2) b += c[i];
                else if (c[i + 1] == -4) cc += c[i];
                else return null;
            }
            if (b == 0 && cc == 0) return new JsonObject { ["n"] = a };
            return new JsonObject { ["cauchy"] = Ranged(new JsonObject { ["unit"] = "um", ["a"] = a, ["b"] = b, ["c"] = cc }) };
        }
        return null;
    }

    /// <summary>A catalogue glass's formula as refractiveindex.info's: Schott (AGF 1) is formula 3,
    /// Sellmeier 1 (AGF 2) formula 2, Herzberger (AGF 3) formula 7; null for any other.</summary>
    private static (string Formula, double[] Coefficients)? FormulaOfGlass(int agf, double[] k)
    {
        double C(int i) => i < k.Length ? k[i] : 0.0;
        return agf switch
        {
            1 => ("formula 3", [C(0), C(1), 2, C(2), -2, C(3), -4, C(4), -6, C(5), -8]),
            2 => ("formula 2", [0, C(0), C(1), C(2), C(3), C(4), C(5)]),
            3 => ("formula 7", [C(0), C(1), C(2), C(3), C(4), C(5)]),
            _ => null,
        };
    }

    /// <summary>One of refractiveindex.info's nine dispersion formulas at λ µm, as Optiland
    /// evaluates them (optiland/materials/dispersion.py).</summary>
    private static double Formula(string formula, double[] c, double w)
    {
        double w2 = w * w;
        switch (formula)
        {
            case "formula 1":
            case "formula 2":
            {
                double v = 1 + c[0];
                for (int i = 1; i + 1 < c.Length; i += 2)
                    v += c[i] * w2 / (w2 - (formula == "formula 1" ? c[i + 1] * c[i + 1] : c[i + 1]));
                return Math.Sqrt(v);
            }
            case "formula 3":
            case "formula 5":
            {
                double v = c[0];
                for (int i = 1; i + 1 < c.Length; i += 2) v += c[i] * Math.Pow(w, c[i + 1]);
                return formula == "formula 3" ? Math.Sqrt(v) : v;
            }
            case "formula 4":
            {
                double v = c[0] + c[1] * Math.Pow(w, c[2]) / (w2 - Math.Pow(c[3], c[4]))
                           + c[5] * Math.Pow(w, c[6]) / (w2 - Math.Pow(c[7], c[8]));
                for (int i = 9; i + 1 < c.Length; i += 2) v += c[i] * Math.Pow(w, c[i + 1]);
                return Math.Sqrt(v);
            }
            case "formula 6":
            {
                double v = 1 + c[0];
                for (int i = 1; i + 1 < c.Length; i += 2) v += c[i] / (c[i + 1] - 1.0 / w2);
                return v;
            }
            case "formula 7":
            {
                double r = 1.0 / (w2 - 0.028);
                double v = c[0] + c[1] * r + c[2] * r * r;
                for (int i = 3; i < c.Length; ++i) v += c[i] * Math.Pow(w, 2 * (i - 2));
                return v;
            }
            case "formula 8":
            {
                double v = c[0] + c[1] * w2 / (w2 - c[2]) + c[3] * w2;
                return Math.Sqrt((1 + 2 * v) / (1 - v));
            }
            case "formula 9":
                return Math.Sqrt(c[0] + c[1] / (w2 - c[2]) + c[3] * (w - c[4]) / ((w - c[4]) * (w - c[4]) + c[5]));
            default:
                throw new StackFormatException($"'{formula}' is not one of refractiveindex.info's formulas 1 to 9");
        }
    }

    // ---- refractiveindex.info YAML, for Optiland's database materials ---------------------------

    private sealed record YamlEntry(string Type, double[] Coefficients, double[]? Range, List<double[]> Rows);

    private static List<YamlEntry> ReadYaml(string text)
    {
        var entries = new List<YamlEntry>();
        string? type = null;
        double[] coefficients = [];
        double[]? range = null;
        var rows = new List<double[]>();
        bool inData = false, inRows = false;
        void Finish()
        {
            if (type is not null) entries.Add(new YamlEntry(type, coefficients, range, rows));
            (type, coefficients, range, rows, inRows) = (null, [], null, new List<double[]>(), false);
        }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd(), t = line.Trim();
            if (!inData) { if (t == "DATA:") inData = true; continue; }
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !t.StartsWith('-')) { Finish(); break; }   // the next top-level key
            if (t.StartsWith("- type:")) { Finish(); type = t["- type:".Length..].Trim(); continue; }
            if (t.StartsWith("type:")) { Finish(); type = t["type:".Length..].Trim(); continue; }
            if (t.StartsWith("coefficients:")) { coefficients = Numbers(t["coefficients:".Length..]); inRows = false; continue; }
            if (t.StartsWith("wavelength_range:")) { range = Numbers(t["wavelength_range:".Length..]); inRows = false; continue; }
            if (t.StartsWith("data:")) { inRows = true; continue; }
            if (inRows && t.Length > 0 && (char.IsDigit(t[0]) || t[0] is '.' or '-' or '+')) rows.Add(Numbers(t));
        }
        Finish();
        return entries;

        static double[] Numbers(string s) =>
            s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, NumberStyles.Float, Inv)).ToArray();
    }

    // ---- small pieces -----------------------------------------------------------------------------

    private static JsonObject Constant(double n, double k) => new() { ["n"] = k == 0 ? JsonValue.Create(n) : new JsonArray(n, k) };

    private static JsonObject Table(double[][] rows) => new()
    {
        ["table"] = new JsonObject
        {
            ["unit"] = "um",
            ["columns"] = new JsonArray("x", "n", "k"),
            ["data"] = new JsonArray(rows.Select(r => (JsonNode)new JsonArray(r[0], r[1], r[2])).ToArray()),
        },
    };

    /// <summary>Rows where the function bends, until linear interpolation is within 1e-6.</summary>
    private static double[][] Sample(Func<double, (double N, double K)> f, double lo, double hi)
    {
        double[] Row(double l) { var (n, k) = f(l); return [l, n, k]; }
        var rows = new List<double[]> { Row(lo), Row(hi) };
        if (!(hi > lo)) return [rows[0]];
        for (int guard = 0; guard < 4000; ++guard)
        {
            int worst = -1;
            double miss = 1e-6;
            double[] at = [];
            for (int i = 0; i + 1 < rows.Count; ++i)
            {
                var mid = Row(0.5 * (rows[i][0] + rows[i + 1][0]));
                double e = Math.Max(Math.Abs(mid[1] - 0.5 * (rows[i][1] + rows[i + 1][1])), Math.Abs(mid[2] - 0.5 * (rows[i][2] + rows[i + 1][2])));
                if (e > miss) (worst, miss, at) = (i, e, mid);
            }
            if (worst < 0) break;
            rows.Insert(worst + 1, at);
        }
        return rows.ToArray();
    }

    private static double Linear(double[][] t, double l)
    {
        if (l <= t[0][0]) return t[0][1];
        if (l >= t[^1][0]) return t[^1][1];
        int i = 1;
        while (t[i][0] < l) ++i;
        double f = (l - t[i - 1][0]) / (t[i][0] - t[i - 1][0]);
        return t[i - 1][1] + f * (t[i][1] - t[i - 1][1]);
    }

    private static string? Str(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static double[]? Nums(JsonNode? node)
    {
        if (node is not JsonArray a) return null;
        var r = new double[a.Count];
        for (int i = 0; i < a.Count; ++i)
            if (a[i] is not JsonValue v || !v.TryGetValue(out r[i])) return null;
        return r;
    }
}
