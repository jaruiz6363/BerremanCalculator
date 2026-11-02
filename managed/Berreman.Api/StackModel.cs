// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// StackModel.cs — the stack file as something to edit.
//
// StackDocument is the file READ FOR EVALUATION: every material parsed into a model, every range
// expanded. StackModel is the same file HELD FOR EDITING: the layers, media, units and sweep as
// fields a table or a tool can change, and each material kept as the JSON object it is written as,
// so a model the editors do not know yet survives a round trip untouched. ToJson writes the file;
// StackDocument.Parse(ToJson()) is what gets evaluated, so the GUI, the CLI and the MCP server
// still evaluate one thing.
//
// THICKNESSES ARE PHYSICAL, in the stack's length unit, as the file has them. There is no QWOT or
// FWOT: in a stack of anisotropic layers "the index" of a layer is not one number, so an optical
// thickness would be a convention to explain rather than a convenience (the user, 2026-10-31).
//
// Keys a version of this program does not know are kept, per object, and written back after the
// known ones. Comments in a hand-written file are not: the "notes" field is the place for those.

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Berreman.Api;

public sealed class NamedMaterial(string name, JsonObject spec)
{
    public string Name { get; set; } = name;
    /// <summary>The material as the file writes it (docs/stack-format.md, "Materials").</summary>
    public JsonObject Spec { get; set; } = spec;
}

public sealed class OuterModel
{
    public string Material { get; set; } = "";
    public double[] EulerDeg { get; set; } = [0, 0, 0];
    public JsonObject Extra { get; set; } = [];
    public OuterModel Clone() => new() { Material = Material, EulerDeg = (double[])EulerDeg.Clone(), Extra = (JsonObject)Extra.DeepClone() };
}

public sealed class TwistModel
{
    /// <summary>One full turn, in the stack's length unit.</summary>
    public double Pitch { get; set; }
    /// <summary>"right" or "left".</summary>
    public string Handedness { get; set; } = "right";
    public double StartDeg { get; set; }
    /// <summary>A fixed number of sublayers, or null to use <see cref="SublayersPerPitch"/>.</summary>
    public int? Sublayers { get; set; }
    /// <summary>Sublayers per pitch when <see cref="Sublayers"/> is null; null means the default, 64.</summary>
    public double? SublayersPerPitch { get; set; }
    public TwistModel Clone() => (TwistModel)MemberwiseClone();
}

public sealed class LayerModel
{
    public string Material { get; set; } = "";
    /// <summary>Physical thickness in the stack's length unit.</summary>
    public double Thickness { get; set; }
    public double[] EulerDeg { get; set; } = [0, 0, 0];
    public TwistModel? Twist { get; set; }
    public JsonObject Extra { get; set; } = [];

    public LayerModel Clone() => new()
    {
        Material = Material, Thickness = Thickness, EulerDeg = (double[])EulerDeg.Clone(),
        Twist = Twist?.Clone(), Extra = (JsonObject)Extra.DeepClone(),
    };
}

public sealed class StackModel
{
    public const string Format = "berreman-stack";
    public static readonly string[] LengthUnits = ["nm", "um", "mm", "cm", "m"];
    public static readonly string[] SpectralUnits = ["nm", "um", "mm", "cm", "m", "GHz", "THz", "cm-1", "eV"];

    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
    public string LengthUnit { get; set; } = "nm";
    public string SpectralUnit { get; set; } = "nm";

    public List<NamedMaterial> Materials { get; set; } = [];
    public OuterModel Incident { get; set; } = new();
    public OuterModel Exit { get; set; } = new();
    public List<LayerModel> Layers { get; set; } = [];

    /// <summary>The sweep, each axis in the text form of <see cref="SweepText"/>.</summary>
    public string Spectral { get; set; } = "400 to 800 count 201";
    /// <summary>True when <see cref="Angles"/> holds ξ = kx/k0 rather than angles of incidence.</summary>
    public bool AnglesAreXi { get; set; }
    public string Angles { get; set; } = "0";
    /// <summary>Empty for the default, 0°.</summary>
    public string Azimuths { get; set; } = "";

    public JsonObject Extra { get; set; } = [];
    public JsonObject UnitsExtra { get; set; } = [];
    public JsonObject SweepExtra { get; set; } = [];

    // ---- a new stack ---------------------------------------------------------------------------

    /// <summary>Air on glass with one 100 nm layer of index 1.38 (about a quarter wave at 550 nm):
    /// something that evaluates at once and shows where everything goes.</summary>
    public static StackModel NewDefault()
    {
        var m = new StackModel
        {
            Title = "New stack",
            Materials =
            [
                new("air", new JsonObject { ["n"] = 1 }),
                new("low", new JsonObject { ["n"] = 1.38 }),
                new("glass", new JsonObject { ["n"] = 1.52 }),
            ],
            Incident = new OuterModel { Material = "air" },
            Exit = new OuterModel { Material = "glass" },
        };
        m.Layers.Add(new LayerModel { Material = "low", Thickness = 100 });
        return m;
    }

    // ---- reading -------------------------------------------------------------------------------

    public static StackModel FromJson(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new StackFormatException($"not valid JSON: {e.Message}"); }
        if (root is not JsonObject o) throw new StackFormatException("the file must be a JSON object");
        if (StackDocument.Str(o, "format", "") != Format) throw new StackFormatException($"\"format\" must be \"{Format}\"");
        if ((int)StackDocument.Num(o["version"], "version") != 1) throw new StackFormatException("this program reads version 1");

        var m = new StackModel
        {
            Title = StackDocument.Str(o, "title", ""),
            Source = StackDocument.Str(o, "source", ""),
            Notes = StackDocument.Str(o, "notes", ""),
            Extra = Rest(o, "format", "version", "title", "source", "notes", "units", "materials", "incident", "layers", "exit", "sweep"),
        };
        if (o["units"] is JsonObject u)
        {
            m.LengthUnit = StackDocument.Str(u, "length", "nm");
            m.SpectralUnit = StackDocument.Str(u, "spectral", "nm");
            m.UnitsExtra = Rest(u, "length", "spectral");
        }
        if (o["materials"] is JsonObject mats)
            foreach (var (name, node) in mats)
                m.Materials.Add(new NamedMaterial(name, node as JsonObject ?? throw new StackFormatException($"materials.{name} must be an object")));

        OuterModel Outer(string key)
        {
            var n = o[key] as JsonObject ?? throw new StackFormatException($"\"{key}\" is required");
            return new OuterModel { Material = StackDocument.Str(n, "material", null, key), EulerDeg = Euler(n), Extra = Rest(n, "material", "euler_deg") };
        }
        m.Incident = Outer("incident");
        m.Exit = Outer("exit");

        if (o["layers"] is JsonArray la)
            for (int i = 0; i < la.Count; ++i)
            {
                var where = $"layers[{i + 1}]";
                var n = la[i] as JsonObject ?? throw new StackFormatException($"{where} must be an object");
                var layer = new LayerModel
                {
                    Material = StackDocument.Str(n, "material", null, where),
                    Thickness = StackDocument.Num(n["thickness"], $"{where}.thickness"),
                    EulerDeg = Euler(n),
                    Extra = Rest(n, "material", "thickness", "euler_deg", "twist"),
                };
                if (n["twist"] is JsonObject tw)
                {
                    var w = $"{where}.twist";
                    layer.Twist = new TwistModel
                    {
                        Pitch = StackDocument.Num(tw["pitch"], $"{w}.pitch"),
                        Handedness = StackDocument.Str(tw, "handedness", null, w),
                        StartDeg = tw["start_deg"] is JsonNode s ? StackDocument.Num(s, $"{w}.start_deg") : 0,
                        Sublayers = tw["sublayers"] is JsonNode c ? (int)StackDocument.Num(c, $"{w}.sublayers") : null,
                        SublayersPerPitch = tw["sublayers_per_pitch"] is JsonNode p ? StackDocument.Num(p, $"{w}.sublayers_per_pitch") : null,
                    };
                }
                m.Layers.Add(layer);
            }

        var sweep = o["sweep"] as JsonObject ?? throw new StackFormatException("\"sweep\" is required");
        m.Spectral = SweepText.Format(sweep["spectral"]);
        m.AnglesAreXi = sweep["xi"] is not null && sweep["angle_deg"] is null;
        m.Angles = SweepText.Format(m.AnglesAreXi ? sweep["xi"] : sweep["angle_deg"]);
        m.Azimuths = sweep["azimuth_deg"] is null ? "" : SweepText.Format(sweep["azimuth_deg"]);
        m.SweepExtra = Rest(sweep, "spectral", "angle_deg", "xi", "azimuth_deg");
        return m;
    }

    private static JsonObject Rest(JsonObject o, params string[] known)
    {
        var rest = new JsonObject();
        foreach (var (k, v) in o) if (!known.Contains(k)) rest[k] = v?.DeepClone();
        return rest;
    }

    private static double[] Euler(JsonObject n) =>
        n["euler_deg"] is JsonArray a && a.Count == 3
            ? [StackDocument.Num(a[0], "euler_deg[1]"), StackDocument.Num(a[1], "euler_deg[2]"), StackDocument.Num(a[2], "euler_deg[3]")]
            : [0, 0, 0];

    // ---- writing -------------------------------------------------------------------------------

    public JsonObject ToNode()
    {
        var o = new JsonObject { ["format"] = Format, ["version"] = 1 };
        if (Title != "") o["title"] = Title;
        if (Source != "") o["source"] = Source;
        if (Notes != "") o["notes"] = Notes;
        var units = new JsonObject { ["length"] = LengthUnit, ["spectral"] = SpectralUnit };
        Merge(units, UnitsExtra);
        o["units"] = units;
        var mats = new JsonObject();
        foreach (var mat in Materials) mats[mat.Name] = mat.Spec.DeepClone();
        o["materials"] = mats;
        o["incident"] = Outer(Incident);
        var layers = new JsonArray();
        foreach (var l in Layers)
        {
            var n = new JsonObject { ["material"] = l.Material, ["thickness"] = l.Thickness };
            if (l.EulerDeg.Any(a => a != 0)) n["euler_deg"] = new JsonArray(l.EulerDeg.Select(a => (JsonNode)a).ToArray());
            if (l.Twist is { } t)
            {
                var tw = new JsonObject { ["pitch"] = t.Pitch, ["handedness"] = t.Handedness };
                if (t.StartDeg != 0) tw["start_deg"] = t.StartDeg;
                if (t.Sublayers is { } s) tw["sublayers"] = s;
                else if (t.SublayersPerPitch is { } p) tw["sublayers_per_pitch"] = p;
                n["twist"] = tw;
            }
            Merge(n, l.Extra);
            layers.Add(n);
        }
        o["layers"] = layers;
        o["exit"] = Outer(Exit);
        var sweep = new JsonObject { ["spectral"] = SweepText.Parse(Spectral, "sweep.spectral") };
        sweep[AnglesAreXi ? "xi" : "angle_deg"] = SweepText.Parse(Angles, AnglesAreXi ? "sweep.xi" : "sweep.angle_deg");
        if (Azimuths.Trim() != "") sweep["azimuth_deg"] = SweepText.Parse(Azimuths, "sweep.azimuth_deg");
        Merge(sweep, SweepExtra);
        o["sweep"] = sweep;
        Merge(o, Extra);
        return o;

        static JsonObject Outer(OuterModel m)
        {
            var n = new JsonObject { ["material"] = m.Material };
            if (m.EulerDeg.Any(a => a != 0)) n["euler_deg"] = new JsonArray(m.EulerDeg.Select(a => (JsonNode)a).ToArray());
            Merge(n, m.Extra);
            return n;
        }
    }

    private static void Merge(JsonObject into, JsonObject extra)
    {
        foreach (var (k, v) in extra) into[k] = v?.DeepClone();
    }

    public string ToJson() => JsonText.Write(ToNode());

    /// <summary>The stack as the evaluator reads it, or a StackFormatException naming what is wrong.</summary>
    public StackDocument ToDocument() => StackDocument.Parse(ToJson());

    public StackModel Clone() => FromJson(ToJson());

    // ---- materials -----------------------------------------------------------------------------

    public NamedMaterial? Material(string name) => Materials.FirstOrDefault(m => m.Name == name);

    /// <summary>Where a material is used: "incident", "exit" and "layer 3", in that order.</summary>
    public List<string> Uses(string name)
    {
        var uses = new List<string>();
        if (Incident.Material == name) uses.Add("incident medium");
        if (Exit.Material == name) uses.Add("exit medium");
        for (int i = 0; i < Layers.Count; ++i) if (Layers[i].Material == name) uses.Add($"layer {i + 1}");
        return uses;
    }

    /// <summary>A name not yet used, from a stem: "SiO2", then "SiO2 (2)".</summary>
    public string UniqueName(string stem)
    {
        if (Material(stem) is null) return stem;
        for (int k = 2; ; ++k) if (Material($"{stem} ({k})") is null) return $"{stem} ({k})";
    }

    public void RenameMaterial(string from, string to)
    {
        to = to.Trim();
        if (to == "") throw new StackFormatException("a material needs a name");
        if (from == to) return;
        if (Material(to) is not null) throw new StackFormatException($"there is already a material named '{to}'");
        var m = Material(from) ?? throw new StackFormatException($"no material named '{from}'");
        m.Name = to;
        if (Incident.Material == from) Incident.Material = to;
        if (Exit.Material == from) Exit.Material = to;
        foreach (var l in Layers) if (l.Material == from) l.Material = to;
    }

    public void RemoveMaterial(string name)
    {
        var uses = Uses(name);
        if (uses.Count > 0) throw new StackFormatException($"'{name}' is used by the {string.Join(", ", uses.Take(4))}{(uses.Count > 4 ? ", …" : "")}");
        Materials.RemoveAll(m => m.Name == name);
    }

    /// <summary>
    /// An index to RANK materials by, for guessing which one a design-string letter means (H the
    /// highest, L the lowest): Re √(ε_xx μ_yy) of the material in its own frame, at the first
    /// spectral value of the sweep. Only a guess, shown beside each letter and changed by a click;
    /// no thickness is ever derived from it. NaN when the material cannot be evaluated there.
    /// </summary>
    public double RankingIndex(string material)
    {
        if (Material(material) is not { } m) return double.NaN;
        try
        {
            var first = StackDocument.Values(SweepText.Parse(Spectral, "sweep.spectral"), "sweep.spectral")[0];
            var M = MaterialParser.Parse(m.Spec, $"materials.{m.Name}").At(Units.ToWavelengthNm(first, SpectralUnit), 0);
            return Complex.Sqrt(M[0, 0] * M[4, 4]).Real;
        }
        catch (StackFormatException) { return double.NaN; }
    }

    // ---- layers --------------------------------------------------------------------------------

    public void ReverseLayers(bool withMedia)
    {
        Layers.Reverse();
        if (withMedia) (Incident, Exit) = (Exit, Incident);
    }

    public void MoveLayer(int from, int to)
    {
        var l = Layers[from];
        Layers.RemoveAt(from);
        Layers.Insert(to, l);
    }

    /// <summary>
    /// The layers a design string spells, in table order: each letter stands for a material and a
    /// physical thickness (in the length unit), and a number before it multiplies that thickness,
    /// so with H = 60 the string "0.5H" is a 30 layer of H's material.
    /// </summary>
    public List<LayerModel> LayersFromString(string text, IReadOnlyDictionary<char, (string Material, double Thickness)> letters, bool firstNextToExit)
    {
        var expanded = DesignString.Expand(text);
        if (firstNextToExit) Array.Reverse(expanded);
        var layers = new List<LayerModel>();
        foreach (var s in expanded)
        {
            if (!letters.TryGetValue(s.Letter, out var l) || Material(l.Material) is null)
                throw new StackFormatException($"no material is chosen for {s.Letter}");
            if (!(l.Thickness > 0) || double.IsInfinity(l.Thickness))
                throw new StackFormatException($"{s.Letter} needs a positive thickness");
            layers.Add(new LayerModel { Material = l.Material, Thickness = s.Count * l.Thickness });
        }
        return layers;
    }
}

/// <summary>
/// One sweep axis as typed: "633"; a list "0, 30, 60"; or a range "400 to 800 step 2" or
/// "400 to 800 count 201". Each is one of the file's forms (docs/stack-format.md, "Sweep").
/// </summary>
public static class SweepText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v => Num(D(v)),
        JsonArray a => string.Join(", ", a.Select(x => Num(D(x)))),
        JsonObject r when r["count"] is JsonNode c => $"{Num(D(r["from"]))} to {Num(D(r["to"]))} count {Num(D(c))}",
        JsonObject r => $"{Num(D(r["from"]))} to {Num(D(r["to"]))} step {Num(D(r["step"]))}",
        _ => "",
    };

    public static JsonNode Parse(string text, string where)
    {
        var t = text.Trim();
        if (t == "") throw new StackFormatException($"{where} is empty");
        var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 5 && words[1] == "to" && words[3] is "step" or "count")
        {
            double from = N(words[0]), to = N(words[2]), x = N(words[4]);
            if (words[3] == "step" && !(x > 0)) throw new StackFormatException($"{where}: the step must be positive");
            if (words[3] == "count" && (x < 1 || x != Math.Floor(x))) throw new StackFormatException($"{where}: the count must be a whole number, at least 1");
            return new JsonObject { ["from"] = from, ["to"] = to, [words[3]] = words[3] == "count" ? (JsonNode)(int)x : x };
        }
        var items = t.Split([',', ' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries).Select(N).ToArray();
        return items.Length == 1 ? JsonValue.Create(items[0]) : new JsonArray(items.Select(v => (JsonNode)v).ToArray());

        double N(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v
            : throw new StackFormatException($"{where}: '{s}' is not a number; write a value, a list \"0, 30, 60\" or a range \"400 to 800 step 2\"");
    }

    private static string Num(double v) => v.ToString("R", Inv);

    // A node read from a file holds a JsonElement and one built here holds an int or a double;
    // the text is the same either way.
    private static double D(JsonNode? n) => double.Parse(n?.ToJsonString() ?? throw new StackFormatException("a sweep value is missing"), Inv);
}

/// <summary>
/// JSON as a person would write it: objects one key per line, and arrays of numbers (a Euler
/// triple, a table row, a list of values) on one line.
/// </summary>
public static class JsonText
{
    private static readonly JsonSerializerOptions Scalar = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(JsonNode node)
    {
        var sb = new StringBuilder();
        Write(sb, node, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, JsonNode? node, int depth)
    {
        string pad = new(' ', 2 * (depth + 1)), end = new(' ', 2 * depth);
        switch (node)
        {
            case JsonObject o when o.Count == 0: sb.Append("{}"); break;
            case JsonObject o:
            {
                sb.Append("{\n");
                int i = 0;
                foreach (var (k, v) in o)
                {
                    sb.Append(pad).Append(JsonSerializer.Serialize(k, Scalar)).Append(": ");
                    Write(sb, v, depth + 1);
                    sb.Append(++i < o.Count ? ",\n" : "\n");
                }
                sb.Append(end).Append('}');
                break;
            }
            case JsonArray a when a.Count == 0: sb.Append("[]"); break;
            case JsonArray a when Flat(a): sb.Append(Inline(a)); break;
            case JsonArray a:
            {
                sb.Append("[\n");
                for (int i = 0; i < a.Count; ++i)
                {
                    sb.Append(pad);
                    Write(sb, a[i], depth + 1);
                    sb.Append(i + 1 < a.Count ? ",\n" : "\n");
                }
                sb.Append(end).Append(']');
                break;
            }
            case null: sb.Append("null"); break;
            default: sb.Append(node.ToJsonString(Scalar)); break;
        }
    }

    // Numbers and short arrays of numbers (a 3×3 tensor, a complex [re, im]) go on one line.
    private static bool Flat(JsonArray a) =>
        a.All(x => x is JsonValue || x is JsonArray inner && inner.All(y => y is JsonValue || y is JsonArray z && z.All(w => w is JsonValue)))
        && Inline(a).Length <= 160;

    private static string Inline(JsonNode? n) => n switch
    {
        JsonArray a => "[" + string.Join(", ", a.Select(Inline)) + "]",
        null => "null",
        _ => n.ToJsonString(Scalar),
    };
}
