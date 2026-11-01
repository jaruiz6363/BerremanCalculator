// MaterialEditingTests.cs — the Materials tab's arithmetic (Gui.Core/MaterialEditing.cs).
//
// The property that matters: editing a material through the tab's fields, changing nothing, gives
// back exactly the same material. Checked on every term of every sample material, through the law
// fields and through the 36 cells of the shape.

using System.Numerics;
using System.Text.Json.Nodes;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Native.Tests;

internal static class MaterialEditingTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    public static void Run()
    {
        ComplexTextRoundTrips();
        SampleMaterialsSurviveTheEditor();
        DefaultLawsAreValid();
        SimpleForms();
        ResponseAndChecks();
    }

    private static void ComplexTextRoundTrips()
    {
        foreach (var (text, z) in new[]
        {
            ("2", new Complex(2, 0)), ("-0.08", new Complex(-0.08, 0)), ("2+0.1i", new Complex(2, 0.1)),
            ("0.1i", new Complex(0, 0.1)), ("-i", new Complex(0, -1)), ("1e-3-2e-4i", new Complex(1e-3, -2e-4)),
            ("i", new Complex(0, 1)), ("3.5-i", new Complex(3.5, -1)), ("1E+2+1E-2i", new Complex(100, 0.01)),
        })
        {
            Check(MaterialEditing.ParseComplex(text) == z, $"'{text}' reads as {z}");
            Check(MaterialEditing.ParseComplex(MaterialEditing.FormatComplex(z)) == z, $"{z} written and read back");
        }
        Check(MaterialEditing.ParseComplex("") == Complex.Zero, "an empty cell is 0");
        bool refused = false;
        try { MaterialEditing.ParseComplex("two"); } catch (FormatException) { refused = true; }
        Check(refused, "words are refused");
    }

    private static bool SameMaterial(JsonObject a, JsonObject b, double[] lambdas)
    {
        var ma = MaterialParser.Parse(a, "a");
        var mb = MaterialParser.Parse(JsonNode.Parse(b.ToJsonString()), "b");
        foreach (var l in lambdas)
        {
            var x = ma.At(l, 100); var y = mb.At(l, 100);
            for (int k = 0; k < 36; ++k) if (x.M[k] != y.M[k]) return false;
        }
        return true;
    }

    private static void SampleMaterialsSurviveTheEditor()
    {
        int terms = 0;
        foreach (var s in SampleCatalog.List(SampleCatalog.DefaultRoot))
        {
            var root = JsonNode.Parse(File.ReadAllText(s.Path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
            var doc = StackDocument.Parse(File.ReadAllText(s.Path));
            var lambdas = new[] { doc.Spectral[0], doc.Spectral[^1] }.Select(v => Units.ToWavelengthNm(v, doc.SpectralUnit)).ToArray();
            foreach (var (name, node) in root["materials"]!.AsObject())
            {
                var spec = node!.AsObject();
                if (spec["model"]?.GetValue<string>() == "resistive_sheet") continue;
                var asTerms = MaterialEditing.AsTerms(spec);
                /* Through the editor: each law through its fields, each shape through its cells. */
                var edited = new JsonArray();
                foreach (var t in asTerms["terms"]!.AsArray())
                {
                    var term = t!.AsObject();
                    var e = new JsonObject();
                    if (term["label"] is { } label) e["label"] = label.DeepClone();
                    var law = MaterialParser.DispersionKeys.FirstOrDefault(term.ContainsKey);
                    if (law is not null) e[law] = MaterialEditing.FromFields(MaterialEditing.Fields(term[law]!.AsObject()));
                    foreach (var (k, v) in MaterialEditing.ShapeFromCells(MaterialEditing.ShapeCells(term))) e[k] = v!.DeepClone();
                    edited.Add(e);
                    ++terms;
                }
                Check(SameMaterial(spec, new JsonObject { ["terms"] = edited }, lambdas),
                      $"{s.Name}/{name}: through the editor's fields and cells, exactly the same M");
            }
        }
        Check(terms >= 40, $"terms taken through the editor ({terms})");
    }

    private static void DefaultLawsAreValid()
    {
        foreach (var law in MaterialEditing.LawChoices.Skip(1))
        {
            var spec = new JsonObject { ["terms"] = new JsonArray(new JsonObject { [law] = MaterialEditing.DefaultLaw(law), ["eps"] = 1 }) };
            string? error = null;
            try
            {
                var m = MaterialParser.Parse(spec, law);
                var lambda = law is "table" or "sellmeier" or "cauchy" or "glass" ? 600 : law is "conductivity" or "polder" ? 3e7 : 2e4;
                m.At(lambda, 100);
            }
            catch (StackFormatException e) { error = e.Message; }
            Check(error is null, $"a new {law} term evaluates ({error})");
        }
    }

    private static void SimpleForms()
    {
        var iso = MaterialEditing.AsSimple(JsonNode.Parse("""{ "n": [2.1, 0.01], "source": "x" }""")!.AsObject());
        Check(iso is { Kind: MaterialEditing.SimpleKind.Isotropic } && iso.N[2] == 2.1 && iso.K[0] == 0.01, "n, k is the isotropic form");
        var fromInt = MaterialEditing.AsSimple(new JsonObject { ["n"] = 1 });
        Check(fromInt is { Kind: MaterialEditing.SimpleKind.Isotropic } && fromInt.N[0] == 1, "n held as an int is still the simple form");
        var uni = MaterialEditing.AsSimple(JsonNode.Parse("""{ "no": 1.54, "ne": 1.55 }""")!.AsObject());
        Check(uni is { Kind: MaterialEditing.SimpleKind.Uniaxial } && uni.N[1] == 1.54 && uni.N[2] == 1.55, "no, ne is the uniaxial form");
        Check(MaterialEditing.AsSimple(JsonNode.Parse("""{ "eps": 2 }""")!.AsObject()) is null, "eps is not a simple form");
        Check(MaterialEditing.AsSimple(JsonNode.Parse("""{ "sellmeier": { "unit": "um", "terms": [] } }""")!.AsObject()) is null, "a law is not a simple form");
        var back = MaterialEditing.FromSimple(iso!, JsonNode.Parse("""{ "n": 1, "source": "x" }""")!.AsObject());
        Check(back.ToJsonString() == """{"n":[2.1,0.01],"source":"x"}""", $"the simple form writes n and keeps the source ({back.ToJsonString()})");
        var bi = MaterialEditing.FromSimple(new MaterialEditing.SimpleForm(MaterialEditing.SimpleKind.Biaxial, [1.5, 1.6, 1.7], [0, 0, 0]));
        Check(bi.ToJsonString() == """{"nx":1.5,"ny":1.6,"nz":1.7}""", "biaxial writes nx, ny, nz");
    }

    private static void ResponseAndChecks()
    {
        var ga = ApiTests.Sample("schubert-2016/ga2o3-010.json").Materials["Ga2O3"];
        var x = MaterialResponse.Grid(150, 1200, 106);
        var angles = MaterialResponse.Curves(ga, x, "cm-1", ResponseQuantity.AxisAngles);
        Check(angles.Count == 6 && angles.All(c => c.Y.Length == x.Length), "six angle curves");
        /* The axis that stays along b (z) has tilt 0 at every point and no azimuth; followed by
           continuity, it stays one curve. The in-plane ones turn. */
        Check(angles.Where(c => c.Name.StartsWith("tilt")).Count(c => c.Y.All(v => v < 1e-6)) == 1
              && angles.Where(c => c.Name.StartsWith("tilt")).Count(c => c.Y.All(v => Math.Abs(v - 90) < 1e-6)) == 2,
              "one axis stays along b and two stay in the a–c plane, at every point");
        var turning = angles.Where(c => c.Name.StartsWith("azimuth")).Select(c => c.Y.Where(double.IsFinite).DefaultIfEmpty(0))
                            .Select(y => y.Max() - y.Min()).Max();
        Check(turning > 10, $"an in-plane principal axis of Ga₂O₃ turns with wavenumber ({turning:F1}°)");
        var nk = MaterialResponse.Curves(ga, x, "cm-1", ResponseQuantity.PrincipalIndex);
        Check(nk.Count(c => c.Axis == "k") == 3 && nk.Where(c => c.Axis == "k").All(c => c.Y.All(v => v >= 0)), "k is never negative");
        Check(MaterialResponse.Checks(ga, x, "cm-1").StartsWith("Passive"), "Ga₂O₃ is reported passive");
        var el = MaterialResponse.Curves(ga, x, "cm-1", ResponseQuantity.Element, 0, 1);
        Check(el.Count == 2 && el[0].Name == "Re M[1,2]", "one element, real and imaginary");

        var fer = ApiTests.Sample("thompson-1956/transverse-ferrite.json");
        var name = fer.Materials.First(p => p.Value is FerriteMaterial).Key;
        var ghz = MaterialResponse.Grid(8, 12, 5);
        Check(MaterialResponse.Checks(fer.Materials[name], ghz, "GHz").Contains("Not reciprocal"), "a ferrite is reported non-reciprocal");
        Check(MaterialResponse.MatrixText(ga, 600, "cm-1").Split('\n').Length == 7, "the 6×6 at one point prints as six rows and a gap");
    }
}
