// McpTools.cs — the MCP tools beyond evaluate_stack: finding samples and materials, describing and
// editing a stack, moving designs to and from ThinFilmDesigner, and the analyses the GUI has.
//
// STATELESS, LIKE evaluate_stack. A stack goes in as 'path' or as 'stack' (the file's JSON text),
// and a tool that changes a stack returns the new JSON, which the model passes on or saves. The
// stack file is self-describing (docs/stack-format.md), so a model can also edit the JSON directly;
// these tools do what is awkward by hand: expand a design string, look a material up, convert.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Mcp;

public sealed partial class McpServer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0) schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        return new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
    }

    private static JsonObject S(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject N(string description) => new() { ["type"] = "number", ["description"] = description };
    private static JsonObject B(string description) => new() { ["type"] = "boolean", ["description"] = description };

    private static JsonObject StackArgs(JsonObject more)
    {
        var o = new JsonObject
        {
            ["path"] = S("A stack file (.json)."),
            ["stack"] = S("The stack JSON itself, instead of a path."),
        };
        foreach (var (k, v) in more) o[k] = v!.DeepClone();
        return o;
    }

    private static JsonObject[] MoreTools() =>
    [
        Tool("list_samples", "The literature samples that ship with the program: name (for read_sample), title, source, and a line on what is in it. 'filter' keeps those whose name, title, source or notes contain every word of it.",
             new JsonObject { ["filter"] = S("Words to narrow the list, e.g. \"Passler\", \"cholesteric\", \"monoclinic\".") }),
        Tool("read_sample", "A literature sample's stack JSON, by its name from list_samples. Pass it to evaluate_stack or the other tools as 'stack', or edit it.",
             new JsonObject { ["name"] = S("The sample's name, e.g. \"schubert-2016 / ga2o3-010\".") }, "name"),
        Tool("describe_stack", "A stack in words: units, the media, each layer (material, thickness, orientation), each material at the first spectral value, and whether each material is passive and reciprocal over the sweep.",
             StackArgs(new JsonObject())),
        Tool("search_materials", "Materials from the shipped libraries: ThinFilmDesigner's two databases, five vendors' glass catalogues and the user's own file. Without 'collection', lists the collections. Returns each entry's name, source and its stack-file form, to put under the stack's \"materials\". A catalogue glass is named, never copied.",
             new JsonObject { ["collection"] = S("e.g. \"Thin-film materials\", \"Literature materials\", \"My materials\", \"Glass: SCHOTT\"."), ["query"] = S("Words that must appear in the name or source."), ["limit"] = N("At most this many entries; default 30.") }),
        Tool("build_layers", "Layers from a design string, ThinFilmDesigner's notation: \"(HL)8 H\", \"0.5L (HL)6 H 0.5L\". Each letter stands for a material and a PHYSICAL thickness in the stack's length unit, given in 'letters'; a number before a letter multiplies its thickness. Returns the stack JSON with the layers replaced (or appended).",
             StackArgs(new JsonObject
             {
                 ["design"] = S("The design string."),
                 ["letters"] = new JsonObject { ["type"] = "object", ["description"] = "For each letter, {\"material\": name, \"thickness\": physical thickness}, e.g. {\"H\": {\"material\": \"TiO2\", \"thickness\": 60}}." },
                 ["append"] = B("Add after the existing layers instead of replacing them."),
                 ["first_next_to_exit"] = B("The string's first layer is next to the exit medium (default: next to the incident medium)."),
             }), "design", "letters"),
        Tool("import_tfd", "A ThinFilmDesigner design (.tfd) read as a stack: its layers, materials (constant, Sellmeier, Cauchy, tables, catalogue glasses) and its wavelengths and angles as the sweep. The merit function is not carried over. Returns the stack JSON.",
             new JsonObject { ["path"] = S("The .tfd file.") }, "path"),
        Tool("export_tfd", "Writes the stack as a ThinFilmDesigner design (.tfd), for optimisation there. Every material must be isotropic and in a form ThinFilmDesigner holds; anything else is refused by name.",
             StackArgs(new JsonObject { ["out"] = S("Where to write the .tfd.") }), "out"),
        Tool("field_profile", "The fields against depth at one point: one spectral value, one angle of incidence (or xi, as the stack's sweep gives them) and one azimuth. Depth is from the first interface, in the stack's length unit, negative in the incident medium. Returns CSV of depth and |E|^2, |Ex|^2, |Ey|^2, |Ez|^2 for incident p (or mode 0) and s (mode 1), per unit incident amplitude, after a line giving the interfaces.",
             StackArgs(new JsonObject
             {
                 ["spectral"] = N("In the stack's spectral unit; default its first value."),
                 ["angle"] = N("Angle of incidence in degrees (or xi when the stack sweeps xi); default its first."),
                 ["azimuth"] = N("Degrees; default 0."),
                 ["depths"] = N("How many depths; default 201."),
                 ["before"] = N("How far in front of the stack, in the length unit; default 20% of its thickness."),
                 ["after"] = N("How far behind it; default the same."),
             })),
        Tool("conoscope_map", "One quantity over every direction of incidence at one spectral value: CSV of angle, azimuth (the sample turned about the normal) and value. The quantity is any list_quantities name, or \"crossed\" / \"parallel\" for transmission between polarisers fixed to the sample at 'polariser' degrees (isotropic outer media only).",
             StackArgs(new JsonObject
             {
                 ["spectral"] = N("In the stack's spectral unit; default its first value."),
                 ["quantity"] = S("e.g. \"crossed\", \"R_mean\", \"Mr34\"."),
                 ["max_angle"] = N("Degrees; default 60."),
                 ["angles"] = N("How many angles from 0 to max_angle; default 13."),
                 ["azimuths"] = N("How many azimuths round the circle; default 24."),
                 ["polariser"] = N("Degrees from the sample's x; default 0."),
             }), "quantity"),
        Tool("material_response", "One material of the stack across a spectral range: n and k along its principal axes, and whether it is passive and reciprocal there. Returns CSV and a line of checks.",
             StackArgs(new JsonObject
             {
                 ["material"] = S("The material's name in the stack."),
                 ["from"] = N("In the stack's spectral unit."),
                 ["to"] = N("In the stack's spectral unit."),
                 ["points"] = N("Default 21."),
             }), "material", "from", "to"),
    ];

    /// <summary>The stack the arguments name, as text: a path's contents or the JSON given.</summary>
    private static string StackText(JsonObject a)
    {
        var path = a["path"]?.GetValue<string>();
        var json = a["stack"]?.GetValue<string>();
        if ((path is null) == (json is null)) throw new StackFormatException("give exactly one of 'path' and 'stack'");
        return path is not null ? File.ReadAllText(path) : json!;
    }

    private static double Arg(JsonObject a, string k, double fallback) =>
        a[k] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number ? double.Parse(v.ToJsonString(), Inv) : fallback;

    private static double First(string sweepText, double fallback)
    {
        try { return StackDocument.Values(SweepText.Parse(sweepText, "x"), "x")[0]; } catch (StackFormatException) { return fallback; }
    }

    /// <summary>The new tools; null when the name is not one of them.</summary>
    private static (string Text, bool Failed)? CallMore(string name, JsonObject a)
    {
        try
        {
            switch (name)
            {
                case "list_samples":
                {
                    var all = SampleCatalog.List(SampleCatalog.DefaultRoot).Select(SampleCatalog.Describe);
                    var found = SampleCatalog.Search(all, a["filter"]?.GetValue<string>() ?? "");
                    var arr = new JsonArray(found.Select(s => (JsonNode)new JsonObject
                    {
                        ["name"] = s.Entry.Name, ["title"] = s.Title, ["source"] = s.Source, ["contents"] = s.Contents,
                    }).ToArray());
                    return (arr.ToJsonString(Wire), false);
                }
                case "read_sample":
                {
                    var want = a["name"]?.GetValue<string>() ?? "";
                    var s = SampleCatalog.List(SampleCatalog.DefaultRoot).FirstOrDefault(e => e.Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                            ?? throw new StackFormatException($"no sample is called '{want}'; list_samples names them");
                    return (File.ReadAllText(s.Path), false);
                }
                case "describe_stack":
                    return (Describe(StackModel.FromJson(StackText(a))), false);
                case "search_materials":
                {
                    if (a["collection"]?.GetValue<string>() is not { } col)
                        return ("Collections: " + string.Join("; ", MaterialLibrary.Collections()), false);
                    var words = (a["query"]?.GetValue<string>() ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    int limit = (int)Arg(a, "limit", 30);
                    var hits = MaterialLibrary.Entries(col)
                        .Where(e => words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Source.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    var arr = new JsonArray(hits.Take(limit).Select(e => (JsonNode)new JsonObject
                    {
                        ["name"] = e.Name, ["source"] = e.Source, ["spec"] = e.Spec.DeepClone(),
                    }).ToArray());
                    return ((hits.Count > limit ? $"{hits.Count} found; the first {limit}:\n" : "") + arr.ToJsonString(Wire), false);
                }
                case "build_layers":
                {
                    var model = StackModel.FromJson(StackText(a));
                    var letters = a["letters"] as JsonObject ?? throw new StackFormatException("'letters' must be an object of {material, thickness} per letter");
                    var map = new Dictionary<char, (string, double)>();
                    foreach (var (k, v) in letters)
                    {
                        if (k.Length != 1) throw new StackFormatException($"'{k}' is not one letter");
                        var mat = v?["material"]?.GetValue<string>() ?? throw new StackFormatException($"letter {k} needs a material");
                        map[char.ToUpperInvariant(k[0])] = (mat, Arg(v!.AsObject(), "thickness", double.NaN));
                    }
                    var layers = model.LayersFromString(a["design"]?.GetValue<string>() ?? "", map, a["first_next_to_exit"]?.GetValue<bool>() == true);
                    if (a["append"]?.GetValue<bool>() != true) model.Layers.Clear();
                    model.Layers.AddRange(layers);
                    return (model.ToJson(), false);
                }
                case "import_tfd":
                    return (TfdFile.Read(File.ReadAllText(a["path"]?.GetValue<string>() ?? throw new StackFormatException("give 'path'"))).ToJson(), false);
                case "export_tfd":
                {
                    var outPath = a["out"]?.GetValue<string>() ?? throw new StackFormatException("give 'out'");
                    File.WriteAllText(outPath, TfdFile.Write(StackModel.FromJson(StackText(a))), new UTF8Encoding(false));
                    return ($"written to {outPath}; ThinFilmDesigner opens it with no merit function", false);
                }
                case "field_profile":
                {
                    var model = StackModel.FromJson(StackText(a));
                    var doc = model.ToDocument();
                    double total = doc.Layers.Sum(l => l.ThicknessNm) / Units.NmPerLength(doc.LengthUnit);
                    double margin = total > 0 ? total * 0.2 : 100;
                    var f = FieldProfile.Compute(model, Arg(a, "spectral", First(model.Spectral, 550)), Arg(a, "angle", First(model.Angles, 0)),
                                                 Arg(a, "azimuth", 0), (int)Arg(a, "depths", 201), Arg(a, "before", margin), Arg(a, "after", margin));
                    var sb = new StringBuilder($"interfaces at {string.Join(", ", f.Boundaries.Select(x => x.ToString("G8", Inv)))} {f.LengthUnit}; R = {f.Point.Power[0]:G6} (p), {f.Point.Power[3]:G6} (s)\n");
                    sb.Append("depth,E2_p,Ex2_p,Ey2_p,Ez2_p,E2_s,Ex2_s,Ey2_s,Ez2_s\n");
                    var c = new List<PlotSeries>[2] { FieldProfile.Curves(f, 1, 0), FieldProfile.Curves(f, 1, 1) };
                    for (int d = 0; d < f.Depths.Length; ++d)
                    {
                        sb.Append(f.Depths[d].ToString("G8", Inv));
                        for (int w = 0; w < 2; ++w)
                        {
                            double ex = c[w][0].Y[d], ey = c[w][1].Y[d], ez = c[w][2].Y[d];
                            sb.Append(FormattableString.Invariant($",{ex + ey + ez:G8},{ex:G8},{ey:G8},{ez:G8}"));
                        }
                        sb.Append('\n');
                    }
                    return (sb.ToString(), false);
                }
                case "conoscope_map":
                {
                    var model = StackModel.FromJson(StackText(a));
                    var q = a["quantity"]?.GetValue<string>() ?? throw new StackFormatException("give 'quantity'");
                    double pol = Arg(a, "polariser", 0);
                    var all = Conoscope.Quantities(pol);
                    var quantity = q.Equals("crossed", StringComparison.OrdinalIgnoreCase) ? all[0]
                        : q.Equals("parallel", StringComparison.OrdinalIgnoreCase) ? all[1]
                        : Quantities.Find(q) is { } found ? new ConoscopeQuantity(found.Name, found.Description, found.Value)
                        : throw new StackFormatException($"'{q}' is not a quantity; give \"crossed\", \"parallel\" or a list_quantities name");
                    var sweep = Conoscope.Sweep(model, Arg(a, "spectral", First(model.Spectral, 550)), Arg(a, "max_angle", 60), (int)Arg(a, "angles", 13), (int)Arg(a, "azimuths", 24));
                    var map = Conoscope.Build(Evaluator.Evaluate(sweep.ToDocument()), quantity);
                    var sb = new StringBuilder($"{map.Quantity}: from {map.Min:G6} to {map.Max:G6}\nangle_deg,azimuth_deg,value\n");
                    for (int i = 0; i < map.AnglesDeg.Length; ++i)
                        for (int j = 0; j < map.AzimuthsDeg.Length; ++j)
                            sb.Append(FormattableString.Invariant($"{map.AnglesDeg[i]:G8},{map.AzimuthsDeg[j]:G8},{map.Values[i, j]:G8}\n"));
                    return (sb.ToString(), false);
                }
                case "material_response":
                {
                    var model = StackModel.FromJson(StackText(a));
                    var matName = a["material"]?.GetValue<string>() ?? "";
                    var mat = model.Material(matName) ?? throw new StackFormatException($"the stack has no material '{matName}'");
                    var m = MaterialParser.Parse(mat.Spec, matName);
                    var x = MaterialResponse.Grid(Arg(a, "from", double.NaN), Arg(a, "to", double.NaN), (int)Arg(a, "points", 21));
                    var curves = MaterialResponse.Curves(m, x, model.SpectralUnit, ResponseQuantity.PrincipalIndex);
                    var sb = new StringBuilder(MaterialResponse.Checks(m, x, model.SpectralUnit) + "\n");
                    sb.Append($"{model.SpectralUnit},{string.Join(",", curves.Select(cv => cv.Name.Replace(", ", "_").Replace(" ", "")))}\n");
                    for (int i = 0; i < x.Length; ++i)
                        sb.Append(x[i].ToString("G8", Inv)).Append(',').Append(string.Join(",", curves.Select(cv => cv.Y[i].ToString("G8", Inv)))).Append('\n');
                    return (sb.ToString(), false);
                }
                default:
                    return null;
            }
        }
        catch (Exception e) when (e is StackFormatException or FormatException or IOException or ArgumentException)
        {
            return (e.Message, true);
        }
    }

    /// <summary>describe_stack's text.</summary>
    private static string Describe(StackModel m)
    {
        var doc = m.ToDocument();
        var sb = new StringBuilder();
        if (m.Title.Length > 0) sb.AppendLine(m.Title);
        if (m.Source.Length > 0) sb.AppendLine("Source: " + m.Source);
        string lu = m.LengthUnit;
        sb.AppendLine($"Units: lengths in {lu}, spectral in {m.SpectralUnit}. Sweep: {m.Spectral}; {(m.AnglesAreXi ? "xi" : "angle")} {m.Angles}; azimuth {(m.Azimuths.Trim().Length > 0 ? m.Azimuths : "0")}.");
        var ws = new StackWorkspace();
        ws.Load(m.ToJson(), null, "stack");
        string Orient(double[] e) => e.All(v => v == 0) ? "" : $", Euler [{string.Join(", ", e.Select(v => v.ToString("G6", Inv)))}]°";
        sb.AppendLine($"Incident: {m.Incident.Material} ({ws.Summary(m.Incident.Material)}){Orient(m.Incident.EulerDeg)}");
        for (int k = 0; k < m.Layers.Count; ++k)
        {
            var l = m.Layers[k];
            sb.AppendLine($"Layer {k + 1}: {l.Material}, {l.Thickness.ToString("G8", Inv)} {lu}{Orient(l.EulerDeg)}{(l.Twist is { } t ? $", twisted with pitch {t.Pitch.ToString("G6", Inv)} {lu} ({t.Handedness})" : "")}");
        }
        sb.AppendLine($"Exit: {m.Exit.Material} ({ws.Summary(m.Exit.Material)}){Orient(m.Exit.EulerDeg)}");
        sb.AppendLine($"Total thickness {ws.TotalThickness.ToString("G8", Inv)} {lu}.");
        var x = doc.Spectral.Length > 1 ? MaterialResponse.Grid(doc.Spectral.Min(), doc.Spectral.Max(), 21) : doc.Spectral;
        foreach (var mat in m.Materials)
        {
            string checks;
            try { checks = MaterialResponse.Checks(doc.Materials[mat.Name], x, m.SpectralUnit); }
            catch (StackFormatException e) { checks = e.Message; }
            sb.AppendLine($"Material {mat.Name}: {ws.Summary(mat.Name)}. {checks}");
        }
        return sb.ToString();
    }
}
