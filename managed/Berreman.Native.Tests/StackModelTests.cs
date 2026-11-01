// StackModelTests.cs — the stack held for editing (StackModel) and the editor's workspace.
//
// The one property everything rests on: a stack read into the model and written back evaluates
// to exactly the same numbers. Checked on every installed sample, bit for bit.

using System.Numerics;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Native.Tests;

internal static class StackModelTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    public static void Run()
    {
        SamplesRoundTripExactly();
        SweepTextRoundTrips();
        AQuarterWaveLayerIsTheTextbookAntireflection();
        DesignStringBuildsTheStack();
        WorkspaceEditsAreAtomicAndUndoable();
        MaterialsRenameAndRemove();
        UnitChangesKeepTheStack();
        AxisDirectionIsTheRotatedZ();
        UnknownKeysSurvive();
        EveryMaterialWritesAsTerms();
    }

    private static bool Same(EvaluationResult a, EvaluationResult b)
    {
        if (a.Points.Count != b.Points.Count) return false;
        for (int i = 0; i < a.Points.Count; ++i)
        {
            var p = a.Points[i]; var q = b.Points[i];
            if (!p.Jones.SequenceEqual(q.Jones)) return false;
            if (!p.Power.Zip(q.Power).All(t => t.First.Equals(t.Second))) return false;
        }
        return true;
    }

    private static void SamplesRoundTripExactly()
    {
        var samples = SampleCatalog.List(SampleCatalog.DefaultRoot);
        Check(samples.Count >= 17, "the samples are there to round-trip");
        foreach (var s in samples)
        {
            var text = File.ReadAllText(s.Path);
            var model = StackModel.FromJson(text);
            var written = model.ToJson();
            Check(StackModel.FromJson(written).ToJson() == written, $"{s.Name}: writing is stable (written twice, the same text)");
            var original = Evaluator.Evaluate(StackDocument.Parse(text));
            var again = Evaluator.Evaluate(StackDocument.Parse(written));
            Check(Same(original, again), $"{s.Name}: the written file evaluates to exactly the same numbers");
        }
    }

    /// <summary>Every sample material, rewritten as terms (and through JSON text), evaluates to exactly the same M.</summary>
    private static void EveryMaterialWritesAsTerms()
    {
        int compared = 0;
        foreach (var s in SampleCatalog.List(SampleCatalog.DefaultRoot))
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(s.Path),
                documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
            var doc = StackDocument.Parse(File.ReadAllText(s.Path));
            var lambdas = new[] { doc.Spectral[0], doc.Spectral[doc.Spectral.Length / 2], doc.Spectral[^1] }
                .Select(v => Units.ToWavelengthNm(v, doc.SpectralUnit)).ToArray();
            foreach (var (name, node) in root["materials"]!.AsObject())
            {
                var spec = node!.AsObject();
                var terms = MaterialTerms.ToTerms(spec);
                if (terms is null)
                {
                    Check(spec["model"]?.GetValue<string>() == "resistive_sheet", $"{s.Name}/{name}: only a resistive sheet has no terms");
                    continue;
                }
                var again = MaterialParser.Parse(System.Text.Json.Nodes.JsonNode.Parse(terms.ToJsonString()), name);
                bool same = true;
                foreach (var l in lambdas)
                {
                    var a = doc.Materials[name].At(l, 100);
                    var b = again.At(l, 100);
                    for (int k = 0; k < 36; ++k) same &= a.M[k] == b.M[k];
                }
                Check(same, $"{s.Name}/{name}: written as terms, the same M");
                ++compared;
            }
        }
        Check(compared >= 30, $"materials compared as terms ({compared})");
    }

    private static void SweepTextRoundTrips()
    {
        foreach (var t in new[] { "633", "0, 30, 60", "400 to 800 step 2", "400 to 800 count 201", "0.5" })
            Check(SweepText.Format(SweepText.Parse(t, "x")) == t, $"sweep text '{t}' round-trips");
        Check(StackDocument.Values(SweepText.Parse("400 to 800 count 5", "x"), "x").SequenceEqual([400.0, 500, 600, 700, 800]), "a counted range expands");
        bool refused = false;
        try { SweepText.Parse("400 to 800 step -1", "x"); } catch (StackFormatException) { refused = true; }
        Check(refused, "a negative step is refused");
        refused = false;
        try { SweepText.Parse("four hundred", "x"); } catch (StackFormatException) { refused = true; }
        Check(refused, "words are refused, with a message");
    }

    private static void AQuarterWaveLayerIsTheTextbookAntireflection()
    {
        /* Thicknesses are physical; this checks the new stack evaluates the textbook case. At
           d = λ/(4n), a layer of index n between air and glass reflects
           R = ((n0 ns − n²)/(n0 ns + n²))². */
        var m = StackModel.NewDefault();
        Check(m.Layers[0].Thickness == 100 && m.Layers[0].Material == "low", "the new stack is 100 nm of the low-index material");
        m.Layers[0].Thickness = 550 / (4 * 1.38);
        m.Spectral = "550";
        var r = Evaluator.Evaluate(m.ToDocument()).Points[0].Power[3];
        double expect = Math.Pow((1.52 - 1.38 * 1.38) / (1.52 + 1.38 * 1.38), 2);
        Check(Math.Abs(r - expect) < 1e-12, $"a quarter-wave layer is the textbook antireflection layer (R = {r:G10}, expected {expect:G10})");
        bool refused = false;
        try { StackDocument.Parse(System.Text.RegularExpressions.Regex.Replace(m.ToJson(), "\"thickness\": [0-9.]+", "\"thickness\": -1")); }
        catch (StackFormatException) { refused = true; }
        Check(refused, "a negative thickness in the file is refused");
    }

    private static void DesignStringBuildsTheStack()
    {
        Check(DesignString.Expand("(HL)3 H").Length == 7, "(HL)3 H is seven layers");
        Check(DesignString.Expand("(1H (1L 1H)2 1L)2").Length == 12, "nested groups expand");
        bool refused = false;
        try { DesignString.Expand("(HL"); } catch (FormatException) { refused = true; }
        Check(refused, "an unclosed bracket is refused");

        var m = StackModel.NewDefault();
        m.Materials.Add(new NamedMaterial("high", new System.Text.Json.Nodes.JsonObject { ["n"] = 2.3 }));
        var names = m.Materials.Select(x => x.Name).ToList();
        var index = names.Select(m.RankingIndex).ToList();
        Check(DesignString.Guess(names, index, 'H') == "high" && DesignString.Guess(names, index, 'L') == "air",
              "H guesses the highest index and L the lowest");
        var letters = new Dictionary<char, (string, double)> { ['H'] = ("high", 60), ['L'] = ("low", 95) };
        var layers = m.LayersFromString("(HL)2 0.5H", letters, firstNextToExit: false);
        Check(layers.Count == 5 && layers[0].Material == "high" && layers[1].Material == "low", "layers are in string order");
        Check(layers[1].Thickness == 95 && layers[4].Thickness == 30, "a letter is its thickness, and 0.5H is half of it");
        var reversed = m.LayersFromString("H L L", letters, firstNextToExit: true);
        Check(reversed[0].Material == "low" && reversed[2].Material == "high", "leftmost next to the exit medium reverses the string");
        refused = false;
        try { m.LayersFromString("H", new Dictionary<char, (string, double)> { ['H'] = ("high", 0) }, false); } catch (StackFormatException) { refused = true; }
        Check(refused, "a letter with no thickness is refused");
    }

    private static void WorkspaceEditsAreAtomicAndUndoable()
    {
        var ws = new StackWorkspace();
        int changes = 0;
        ws.Changed += () => ++changes;
        var before = ws.Text;
        int at = ws.InsertLayer(0);
        Check(at == 1 && ws.Model.Layers.Count == 2, "insert copies the selected layer after it");
        Check(ws.CanUndo && changes == 1, "an edit can be undone, and says it changed");
        ws.Undo();
        Check(ws.Text == before && ws.CanRedo, "undo restores the stack exactly");
        ws.Redo();
        Check(ws.Model.Layers.Count == 2, "redo repeats it");

        var rows = ws.Rows();
        string? error = null;
        void OnError(string e) => error = e;
        LayerRow.Error += OnError;
        var good = ws.Text;
        rows[0].ThicknessText = "-3";
        Check(error is not null && ws.Text == good, "a negative thickness is refused and the stack is unchanged");
        error = null;
        rows[0].ThicknessText = "abc";
        Check(error is not null && ws.Text == good, "a thickness that is not a number is refused");
        error = null;
        rows[0].Theta = "30";
        Check(error is null && ws.Model.Layers[0].EulerDeg[1] == 30, "an Euler angle is set from the row");
        LayerRow.Error -= OnError;

        int moved = ws.MoveLayer(0, +1);
        Check(moved == 1 && ws.Model.Layers[1].EulerDeg[1] == 30, "move down takes the layer with its orientation");
        Check(ws.MoveLayer(1, +1) == 1, "the last layer does not move further down");
        ws.DeleteLayers([0, 1]);
        Check(ws.Model.Layers.Count == 0, "delete removes the selected layers");
        ws.Edit(m => m.Spectral = "500 to 600 step 10");
        bool refused = false;
        try { ws.Edit(m => m.Spectral = "500 to"); } catch (StackFormatException) { refused = true; }
        Check(refused && ws.Model.Spectral == "500 to 600 step 10", "a sweep that cannot be written is refused, and the old one kept");
    }

    private static void MaterialsRenameAndRemove()
    {
        var m = StackModel.NewDefault();
        m.RenameMaterial("low", "MgF2");
        Check(m.Layers[0].Material == "MgF2" && m.Material("low") is null, "rename follows into the layers");
        m.RenameMaterial("air", "vacuum");
        Check(m.Incident.Material == "vacuum", "and into the media");
        bool refused = false;
        try { m.RemoveMaterial("MgF2"); } catch (StackFormatException e) { refused = e.Message.Contains("layer 1"); }
        Check(refused, "a material in use is not removed, and the message says where it is used");
        refused = false;
        try { m.RenameMaterial("glass", "MgF2"); } catch (StackFormatException) { refused = true; }
        Check(refused, "a rename onto an existing name is refused");
        Check(m.UniqueName("glass") == "glass (2)", "a unique name is made from a taken one");
        var ws = new StackWorkspace();
        Check(ws.Summary("low") == "n = 1.38" && ws.Summary("nothing") == "missing", "a material is summarised for lists");
    }

    private static void UnitChangesKeepTheStack()
    {
        var path = SampleCatalog.List(SampleCatalog.DefaultRoot).First(s => s.Name == "mansuripur-1990 / quadrilayer").Path;
        var ws = new StackWorkspace();
        ws.Load(File.ReadAllText(path), null, "quadrilayer");
        var before = Evaluator.Evaluate(ws.Document());
        ws.SetLengthUnit("um");
        ws.SetSpectralUnit("eV");
        var after = Evaluator.Evaluate(ws.Document());
        bool close = before.Points.Count == after.Points.Count && before.Points.Zip(after.Points).All(t =>
            Math.Abs(t.First.WavelengthNm - t.Second.WavelengthNm) < 1e-9 * t.First.WavelengthNm &&
            t.First.Power.Zip(t.Second.Power).All(u => Math.Abs(u.First - u.Second) < 1e-9));
        Check(close, "changing the length and spectral units evaluates the same physical stack");
    }

    private static void AxisDirectionIsTheRotatedZ()
    {
        var e = MaterialMatrix.EulerForAxis(30, 45);
        var (tilt, az) = MaterialMatrix.AxisDirection(e[0], e[1], e[2]);
        Check(Math.Abs(tilt - 30) < 1e-12 && Math.Abs(az - 45) < 1e-12, "EulerForAxis and AxisDirection are inverses");

        /* The rotated uniaxial tensor has its extraordinary eigenvector along that direction. */
        var m = MaterialMatrix.FromBlocks(MaterialMatrix.Diagonal(2.25, 2.25, 3.0), MaterialMatrix.Scalar(1)).Rotated(MaterialMatrix.EulerRotation(e[0], e[1], e[2]));
        double t = 30 * Math.PI / 180, a = 45 * Math.PI / 180;
        double[] c = [Math.Sin(t) * Math.Cos(a), Math.Sin(t) * Math.Sin(a), Math.Cos(t)];
        bool eigen = true;
        for (int i = 0; i < 3; ++i)
        {
            Complex s = 0;
            for (int j = 0; j < 3; ++j) s += m[i, j] * c[j];
            eigen &= Complex.Abs(s - 3.0 * c[i]) < 1e-12;
        }
        Check(eigen, "the optic axis shown is the rotated tensor's extraordinary direction");
    }

    private static void UnknownKeysSurvive()
    {
        var text = """
            { "format": "berreman-stack", "version": 1, "x-origin": "lab notebook 4",
              "materials": { "a": { "n": 1 }, "g": { "n": 1.5 } },
              "incident": { "material": "a" }, "exit": { "material": "g" },
              "layers": [ { "material": "g", "thickness": 10, "x-batch": 7 } ],
              "sweep": { "spectral": 500, "angle_deg": 0 } }
            """;
        var written = StackModel.FromJson(text).ToJson();
        Check(written.Contains("\"x-origin\": \"lab notebook 4\"") && written.Contains("\"x-batch\": 7"), "keys this version does not know are kept");
    }
}
