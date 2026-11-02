// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// TfdFile.cs — ThinFilmDesigner designs (.tfd), read into a stack and written from one.
//
// THE TWO PROGRAMS DIVIDE THE WORK: ThinFilmDesigner designs and optimises isotropic coatings;
// BerremanCalculator evaluates anything, anisotropic included, and never optimises. So a design
// comes in from a .tfd (to be made anisotropic, oriented, analysed here) and an isotropic stack can
// go back out (to be optimised there).
//
// READING. The design's materials are its material models: CONSTANT (n, k), SELLMEIER (B, C pairs,
// λ in µm), CAUCHY (A, B, C, λ in µm), TABLE (λ µm, n, k) and GLASS (a catalogue glass, NAMED here
// too). A material with no model (a file from before models) becomes a table of its n and k on the
// design's grid. Thicknesses in a .tfd are physical, in nm, whatever unit the design shows them in.
// The sweep is the design's wavelengths and angles. NOT CARRIED, and said in the notes: the merit
// function and the optimisation settings (this program has none), and a back surface (an
// incoherent substrate, not built here).
//
// WRITING. Only what ThinFilmDesigner can hold: every material isotropic and in one of its five
// forms, no twisted layer. Anything else is refused with the material or layer named. The design
// gets no merit function; its wavelengths and angles are this stack's sweep.

using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Berreman.Api;

public static class TfdFile
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- reading ----------------------------------------------------------------------------------

    /// <summary>The stack a .tfd describes, with what was not carried over in its notes.</summary>
    public static StackModel Read(string text)
    {
        var root = JsonNode.Parse(text) as JsonObject ?? throw new StackFormatException("a .tfd file is a JSON object");
        double Num(JsonNode? n, string where) => n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? double.Parse(v.ToJsonString(), Inv) : throw new StackFormatException($"{where} is not a number");
        var grid = (root["Wavelength"] as JsonArray ?? throw new StackFormatException("the design has no Wavelength grid"))
                   .Select((x, i) => Num(x, $"Wavelength[{i}]")).ToArray();
        if (grid.Length == 0) throw new StackFormatException("the design has no wavelengths");

        var model = StackModel.NewDefault();
        model.Materials.Clear();
        model.Layers.Clear();
        model.Title = root["Name"]?.GetValue<string>() ?? "imported design";
        model.Source = root["Source"]?.GetValue<string>() ?? "";
        model.LengthUnit = "nm";
        model.SpectralUnit = "nm";

        /* Materials: one per distinct definition, under the design's names. */
        string Add(JsonObject doc, string where)
        {
            var name = doc["Name"]?.GetValue<string>() ?? "material";
            var spec = Spec(doc, grid, where);
            if (model.Material(name) is { } existing)
            {
                if (existing.Spec.ToJsonString() == spec.ToJsonString()) return name;
                name = model.UniqueName(name);
            }
            model.Materials.Add(new NamedMaterial(name, spec));
            return name;
        }
        var mats = (root["Materials"] as JsonArray ?? []).Select((m, i) => Add(m!.AsObject(), $"Materials[{i}]")).ToList();
        model.Incident.Material = Add(root["Incident"]?.AsObject() ?? throw new StackFormatException("the design has no Incident medium"), "Incident");
        model.Exit.Material = Add(root["Exit"]?.AsObject() ?? throw new StackFormatException("the design has no Exit medium"), "Exit");
        model.Incident.EulerDeg = [0, 0, 0];
        model.Exit.EulerDeg = [0, 0, 0];

        foreach (var (l, i) in (root["Layers"] as JsonArray ?? []).Select((l, i) => (l!.AsObject(), i)))
        {
            int m = (int)Num(l["Material"], $"Layers[{i}].Material");
            if (m < 0 || m >= mats.Count) throw new StackFormatException($"layer {i + 1} names material {m}, which the design does not have");
            model.Layers.Add(new LayerModel { Material = mats[m], Thickness = Num(l["Thickness"], $"Layers[{i}].Thickness"), EulerDeg = [0, 0, 0] });
        }

        /* The sweep: the grid, and the angles of the design's points. */
        model.Spectral = SweepText.Format(GridNode(grid));
        var angles = (root["Points"] as JsonArray ?? []).Select(p => Num(p!["AngleDegrees"], "Points.AngleDegrees")).Distinct().OrderBy(a => a).ToList();
        if (angles.Count == 0) angles.Add(0);
        bool inAir = root["AngleInAir"]?.GetValue<bool>() == true;
        double nInc = Math.Sqrt(MaterialParser.Parse(model.Material(model.Incident.Material)!.Spec, "Incident").At(grid[0], 0)[0, 0].Real);
        if (inAir && Math.Abs(nInc - 1) > 1e-12)
        {
            /* Angles given in air: ξ = sin θ_air, the same in every medium. */
            model.AnglesAreXi = true;
            model.Angles = string.Join(", ", angles.Select(a => Math.Sin(a * Math.PI / 180).ToString("R", Inv)));
        }
        else
        {
            model.AnglesAreXi = false;
            model.Angles = string.Join(", ", angles.Select(a => a.ToString("R", Inv)));
        }
        model.Azimuths = "";

        var notes = new List<string>();
        if (root["Notes"]?.GetValue<string>() is { Length: > 0 } n) notes.Add(n);
        notes.Add("Read from a ThinFilmDesigner design. Not carried over: the merit function and the optimisation settings"
                  + (root["BackSurface"]?.GetValue<bool>() == true ? ", and the substrate's back surface (an incoherent substrate is not built in this program)" : "")
                  + (inAir && Math.Abs(nInc - 1) > 1e-12 ? ". The design's angles were given in air, so they are written as ξ = sin θ_air" : "")
                  + ".");
        model.Notes = string.Join("\n\n", notes);
        model.ToJson();   // refuse here what would not evaluate
        return model;
    }

    /// <summary>A material document's model as a stack-file material.</summary>
    private static JsonObject Spec(JsonObject doc, double[] gridNm, string where)
    {
        var source = doc["Model"]?["Source"]?.GetValue<string>();
        JsonObject spec;
        if (doc["Model"] is JsonObject m)
        {
            var kind = (m["Kind"]?.GetValue<string>() ?? "").ToUpperInvariant();
            var c = (m["Coefficients"] as JsonArray ?? []).Select(x => x!.GetValue<double>()).ToArray();
            double[]? range = m["MinMicrons"] is JsonValue lo && m["MaxMicrons"] is JsonValue hi ? [lo.GetValue<double>(), hi.GetValue<double>()] : null;
            switch (kind)
            {
                case "CONSTANT":
                    spec = new JsonObject { ["n"] = c.Length > 1 && c[1] != 0 ? new JsonArray(c[0], c[1]) : JsonValue.Create(c[0]) };
                    break;
                case "SELLMEIER":
                {
                    var s = new JsonObject
                    {
                        ["unit"] = "um",
                        ["terms"] = new JsonArray(Enumerable.Range(0, c.Length / 2).Select(i => (JsonNode)new JsonObject { ["b"] = c[2 * i], ["c2"] = c[2 * i + 1] }).ToArray()),
                    };
                    if (range is not null) s["range"] = new JsonArray(range[0], range[1]);
                    spec = new JsonObject { ["sellmeier"] = s };
                    break;
                }
                case "CAUCHY":
                {
                    var s = new JsonObject { ["unit"] = "um", ["a"] = c.ElementAtOrDefault(0), ["b"] = c.ElementAtOrDefault(1), ["c"] = c.ElementAtOrDefault(2) };
                    if (range is not null) s["range"] = new JsonArray(range[0], range[1]);
                    spec = new JsonObject { ["cauchy"] = s };
                    break;
                }
                case "TABLE":
                {
                    var rows = m["Table"] as JsonArray ?? throw new StackFormatException($"{where}: a TABLE model with no table");
                    spec = new JsonObject
                    {
                        ["table"] = new JsonObject
                        {
                            ["unit"] = "um", ["columns"] = new JsonArray("x", "n", "k"),
                            ["data"] = new JsonArray(rows.Select(r => (JsonNode)new JsonArray(r!.AsArray().Select(v => (JsonNode)v!.GetValue<double>()).ToArray())).ToArray()),
                        },
                    };
                    break;
                }
                case "GLASS":
                    spec = new JsonObject { ["glass"] = new JsonObject { ["catalog"] = m["Catalog"]?.GetValue<string>(), ["name"] = m["Glass"]?.GetValue<string>() } };
                    break;
                default:
                    throw new StackFormatException($"{where}: the material model \"{kind}\" is not one this program reads");
            }
        }
        else
        {
            /* No model: the design's own n and k on its grid. */
            var n = (doc["N"] as JsonArray ?? []).Select(x => x!.GetValue<double>()).ToArray();
            var k = (doc["K"] as JsonArray ?? []).Select(x => x!.GetValue<double>()).ToArray();
            if (n.Length != gridNm.Length || k.Length != gridNm.Length) throw new StackFormatException($"{where}: n and k do not match the design's wavelengths");
            spec = n.Distinct().Count() == 1 && k.Distinct().Count() == 1
                ? new JsonObject { ["n"] = k[0] != 0 ? new JsonArray(n[0], k[0]) : JsonValue.Create(n[0]) }
                : new JsonObject
                {
                    ["table"] = new JsonObject
                    {
                        ["unit"] = "nm", ["columns"] = new JsonArray("x", "n", "k"),
                        ["data"] = new JsonArray(gridNm.Select((x, i) => (JsonNode)new JsonArray(x, n[i], k[i])).ToArray()),
                    },
                };
        }
        if (source is { Length: > 0 }) spec["source"] = source;
        return spec;
    }

    /// <summary>A grid as the sweep writes it: a counted range when it is evenly spaced, a list otherwise.</summary>
    private static JsonNode GridNode(double[] g)
    {
        if (g.Length == 1) return JsonValue.Create(g[0]);
        double step = (g[^1] - g[0]) / (g.Length - 1);
        bool even = g.Select((x, i) => Math.Abs(x - (g[0] + step * i))).Max() <= 1e-9 * Math.Max(1, Math.Abs(g[^1]));
        return even
            ? new JsonObject { ["from"] = g[0], ["to"] = g[^1], ["count"] = g.Length }
            : new JsonArray(g.Select(x => (JsonNode)x).ToArray());
    }

    // ---- writing ----------------------------------------------------------------------------------

    /// <summary>The stack as a ThinFilmDesigner design, or a StackFormatException naming what it cannot hold.</summary>
    public static string Write(StackModel model)
    {
        var doc = model.ToDocument();
        if (doc.Layers.FirstOrDefault(l => l.Twist is not null) is { } tw)
            throw new StackFormatException($"layer {doc.Layers.IndexOf(tw) + 1} is twisted; ThinFilmDesigner holds no twisted layers");
        var grid = doc.Spectral.Select(v => Units.ToWavelengthNm(v, doc.SpectralUnit)).ToArray();
        if (grid.Length < 1) throw new StackFormatException("the stack has no spectral values");

        var used = doc.Layers.Select(l => l.Material).Distinct().ToList();
        JsonObject Material(string name)
        {
            var spec = model.Material(name)!.Spec;
            var mm = new JsonObject { ["Kind"] = "" };
            var ns = new JsonArray(); var ks = new JsonArray();
            var mat = doc.Materials[name];
            foreach (var l in grid)
            {
                var M = mat.At(l, 100);
                if (!M.IsIsotropic(out var eps, out var mu) || mu != Complex.One)
                    throw new StackFormatException($"\"{name}\" is not an isotropic, non-magnetic material at {l:G6} nm; ThinFilmDesigner holds only those");
                var n = Complex.Sqrt(eps);
                if (n.Imaginary < 0) n = -n;
                ns.Add(n.Real); ks.Add(n.Imaginary);
            }
            return new JsonObject { ["Name"] = name, ["N"] = ns, ["K"] = ks, ["Model"] = Model(name, spec, doc.SpectralUnit) };
        }

        var layers = new JsonArray(doc.Layers.Select(l => (JsonNode)new JsonObject
        {
            ["Material"] = used.IndexOf(l.Material), ["Thickness"] = l.ThicknessNm, ["Variable"] = true,
        }).ToArray());
        double nInc = Math.Sqrt(doc.Materials[doc.Incident.Material].At(grid[0], 0)[0, 0].Real);
        var angles = doc.AnglesDeg ?? doc.Xi!.Select(x => Math.Asin(Math.Clamp(x / nInc, -1, 1)) * 180 / Math.PI).ToArray();
        var points = new JsonArray();
        for (int w = 0; w < grid.Length; ++w)
            foreach (var a in angles) points.Add(new JsonObject { ["Wavelength"] = w, ["AngleDegrees"] = a });

        var root = new JsonObject
        {
            ["Version"] = 2,
            ["Name"] = model.Title.Length > 0 ? model.Title : "from BerremanCalculator",
            ["Source"] = model.Source.Length > 0 ? model.Source : null,
            ["Notes"] = (model.Notes.Length > 0 ? model.Notes + "\n\n" : "") + "Written by BerremanCalculator: no merit function; the wavelengths and angles are the stack's sweep.",
            ["Wavelength"] = new JsonArray(grid.Select(x => (JsonNode)x).ToArray()),
            ["Materials"] = new JsonArray(used.Select(n => (JsonNode)Material(n)).ToArray()),
            ["Layers"] = layers,
            ["Points"] = points,
            ["Incident"] = Material(doc.Incident.Material),
            ["Exit"] = Material(doc.Exit.Material),
            ["Operands"] = new JsonArray(),
            ["Norm"] = "L2",
            ["WavelengthUnit"] = "nm",
            ["ThicknessUnit"] = "nm",
            ["ReferenceWavelength"] = grid[grid.Length / 2],
            ["BackSurface"] = false,
            ["AngleInAir"] = false,
        };
        return JsonText.Write(root);
    }

    /// <summary>A stack-file material as a ThinFilmDesigner model, or a refusal naming it.</summary>
    private static JsonObject Model(string name, JsonObject spec, string spectralUnit)
    {
        string? source = spec["source"]?.GetValue<string>();
        JsonObject M(string kind, double[] c) => new() { ["Kind"] = kind, ["Coefficients"] = new JsonArray(c.Select(x => (JsonNode)x).ToArray()), ["Source"] = source };
        double UmPer(string unit, string what) => unit is "nm" or "um" or "µm" or "mm" or "cm" or "m"
            ? Units.NmPerLength(unit) / 1000.0 : throw new StackFormatException($"\"{name}\": a {what} in {unit} cannot be written for ThinFilmDesigner, which uses wavelengths in µm");
        var keys = spec.Select(p => p.Key).Where(k => k is not ("source" or "notes")).ToList();
        if (keys.SequenceEqual(["n"]))
        {
            var n = MaterialParser.Cplx(spec["n"], name);
            return M("CONSTANT", [n.Real, n.Imaginary]);
        }
        if (keys.SequenceEqual(["sellmeier"]) && spec["sellmeier"] is JsonObject s)
        {
            double f = UmPer(s["unit"]!.GetValue<string>(), "Sellmeier formula");
            var c = new List<double>();
            if (s["a"] is { } a && a.GetValue<double>() != 0) { c.Add(a.GetValue<double>()); c.Add(0); }   /* a = B with C = 0 */
            foreach (var t in s["terms"]!.AsArray())
            {
                double c2 = t!["c2"] is { } x ? x.GetValue<double>() : Math.Pow(t["c"]!.GetValue<double>(), 2);
                c.Add(t["b"]!.GetValue<double>()); c.Add(c2 * f * f);
            }
            var m = M("SELLMEIER", [.. c]);
            if (s["range"] is JsonArray r) { m["MinMicrons"] = r[0]!.GetValue<double>() * f; m["MaxMicrons"] = r[1]!.GetValue<double>() * f; }
            return m;
        }
        if (keys.SequenceEqual(["cauchy"]) && spec["cauchy"] is JsonObject cy)
        {
            double f = UmPer(cy["unit"]!.GetValue<string>(), "Cauchy formula");
            double G(string k) => cy[k]?.GetValue<double>() ?? 0;
            var m = M("CAUCHY", [G("a"), G("b") * f * f, G("c") * f * f * f * f]);
            if (cy["range"] is JsonArray r) { m["MinMicrons"] = r[0]!.GetValue<double>() * f; m["MaxMicrons"] = r[1]!.GetValue<double>() * f; }
            return m;
        }
        if (keys.SequenceEqual(["table"]) && spec["table"] is JsonObject tb)
        {
            string unit = tb["unit"]!.GetValue<string>();
            bool index = tb["columns"]!.AsArray().Select(c => c!.GetValue<string>()).SequenceEqual(["x", "n", "k"]);
            var data = tb["data"]!.AsArray().Select(r => r!.AsArray().Select(v => v!.GetValue<double>()).ToArray()).ToArray();
            (double Um, Complex Nk) Row(double x, Complex value)
            {
                var nk = index ? value : Complex.Sqrt(value);
                if (nk.Imaginary < 0) nk = -nk;
                return (Units.ToWavelengthNm(x, unit) / 1000.0, nk);
            }
            IEnumerable<(double Um, Complex Nk)> rows;
            if (index) rows = data.Select(r => Row(r[0], new Complex(r[1], r[2])));
            else
            {
                /* ThinFilmDesigner interpolates n and k linearly, this program ε (as the table is
                 * written). So an ε table is written finely enough, 100 rows between each pair of
                 * the original's, that the two interpolations agree. */
                var fine = new List<(double, Complex)>();
                for (int i = 0; i + 1 < data.Length; ++i)
                    for (int j = 0; j < 100; ++j)
                    {
                        double t = j / 100.0;
                        fine.Add(Row(data[i][0] + t * (data[i + 1][0] - data[i][0]),
                                     new Complex(data[i][1] + t * (data[i + 1][1] - data[i][1]), data[i][2] + t * (data[i + 1][2] - data[i][2]))));
                    }
                fine.Add(Row(data[^1][0], new Complex(data[^1][1], data[^1][2])));
                rows = fine;
            }
            var table = rows.OrderBy(r => r.Um).Select(r => (JsonNode)new JsonArray(r.Um, r.Nk.Real, r.Nk.Imaginary)).ToArray();
            return new JsonObject { ["Kind"] = "TABLE", ["Table"] = new JsonArray(table), ["Source"] = source };
        }
        if (keys.SequenceEqual(["glass"]) && spec["glass"] is JsonObject g)
            return new JsonObject { ["Kind"] = "GLASS", ["Catalog"] = g["catalog"]!.GetValue<string>(), ["Glass"] = g["name"]!.GetValue<string>(), ["Source"] = source ?? $"{g["catalog"]} glass catalogue" };
        throw new StackFormatException($"\"{name}\" is written as {string.Join(", ", keys)}; ThinFilmDesigner holds only a constant n, k, a Sellmeier or Cauchy formula, an n, k table or a catalogue glass");
    }
}
