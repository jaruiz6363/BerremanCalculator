// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// GuideShots.cs — the user guide's screen captures, taken from the real window.
//
//   dotnet run --project Berreman.Gui.Tests -c Release -- --guide ..\docs\images
//
// Each scene drives the window as a person would (the same helpers as the tests) and saves a PNG
// under the name docs/user-guide.md uses. So the pictures can be taken again whenever the window
// changes, and a scene that no longer works fails loudly here rather than leaving a stale picture.
// The window is the size it opens at.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Berreman.Gui.Core;

namespace Berreman.Gui.Tests;

public static partial class WindowTests
{
    private static string _guideDir = "";
    private static readonly List<string> GuideFailures = [];

    public static int Guide(HeadlessUnitTestSession session, string dir)
    {
        _guideDir = Path.GetFullPath(dir);
        Directory.CreateDirectory(_guideDir);
        Console.WriteLine($"=== the user guide's screen captures, into {_guideDir} ===");
        GuideFirstEvaluation(session);
        GuideOrientation(session);
        GuideMaterials(session);
        foreach (var f in GuideFailures) Console.WriteLine($"  FAIL  {f}");
        Console.WriteLine($"{Directory.GetFiles(_guideDir, "*.png").Length} pictures, {GuideFailures.Count} failures");
        return GuideFailures.Count == 0 && !Failed ? 0 : 1;
    }

    private static void Need(bool ok, string what)
    {
        if (!ok) GuideFailures.Add(what);
    }

    private static void Save(Window window, string name)
    {
        Pump(window, 150);
        var frame = window.CaptureRenderedFrame();
        Need(frame is not null, $"{name}: the window rendered");
        frame?.Save(Path.Combine(_guideDir, name + ".png"));
        Console.WriteLine($"  {name}.png");
    }

    /// <summary>F5, and the plot redrawn from it. (IsEvaluating also counts the automatic
    /// evaluation's timer, which the headless platform stops ticking in later windows.)</summary>
    private static void Evaluate(MainWindow w, string scene)
    {
        var plot = Find<SpectrumPlot>(w, "Plot");
        var before = plot.Series;
        PumpUntil(w, () => Find<Button>(w, "EvaluateButton").IsEnabled, 10);
        Click(w, Find<Button>(w, "EvaluateButton"));
        Need(PumpUntil(w, () => plot.Series is { Count: > 0 } s && !ReferenceEquals(s, before)), $"{scene}: the stack evaluates");
        Pump(w, 100);
    }

    private static void XAxis(MainWindow w, string startsWith)
    {
        var axes = Find<ComboBox>(w, "XAxisBox");
        for (int i = 0; i < axes.ItemCount; ++i)
        {
            axes.SelectedIndex = i;
            Pump(w);
            if (Find<SpectrumPlot>(w, "Plot").XTitle.StartsWith(startsWith)) return;
        }
        Need(false, $"x can be {startsWith}");
    }

    private static void Tick(MainWindow w, string name, bool on)
    {
        var box = Find<CheckBox>(w, name);
        if ((box.IsChecked == true) != on) Click(w, box);
    }

    /* 1. The window as it opens; a sample from the browser; the quadrilayer against angle. */
    private static void GuideFirstEvaluation(HeadlessUnitTestSession session)
    {
        WithWindow(session, w =>
        {
            Need(PumpUntil(w, () => !w.IsEvaluating && Find<SpectrumPlot>(w, "Plot").Series is { Count: > 0 }), "the new stack evaluates by itself");
            Save(w, "window-new");

            _ = w.BrowseSamples();
            var d = Dialog<SampleBrowser>(w);
            Need(d is not null, "the sample browser opens");
            if (d is null) return;
            Find<TextBox>(d, "SampleSearchBox").Text = "quadrilayer";
            Pump(w);
            var list = Find<ListBox>(d, "SampleList");
            Need(list.ItemCount == 1, $"a search finds the quadrilayer ({list.ItemCount})");
            list.SelectedIndex = 0;
            Pump(w);
            Save(d, "sample-browser");
            Click(d, Find<Button>(d, "OkButton"));
            Need(PumpUntil(w, () => w.Workspace.Name.StartsWith("mansuripur-1990"), 5), "Open opens the quadrilayer");

            Evaluate(w, "quadrilayer");
            XAxis(w, "angle");
            Save(w, "quadrilayer-angle");
        });
    }

    /* 2. The magneto-optic layer turned: R hardly moves, the conversion r_ps does (cos θ at normal
       incidence). The numbers are in the guide. */
    private static void GuideOrientation(HeadlessUnitTestSession session)
    {
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("mansuripur-1990 / quadrilayer"), "mansuripur-1990 / quadrilayer");
            Pump(w);
            Evaluate(w, "orientation");
            XAxis(w, "angle");

            Click(w, Find<Button>(w, "MoreQuantitiesButton"));
            var q = Dialog<QuantityDialog>(w);
            Need(q is not null, "More… opens the quantity dialog");
            if (q is null) return;
            foreach (var name in new[] { "Q_|r_ps|", "Q_|r_sp|" })
            {
                var box = q.GetVisualDescendantsOf<CheckBox>().FirstOrDefault(b => b.Name == name);
                Need(box is not null, $"the dialog offers {name[2..]}");
                if (box is not null) box.IsChecked = true;
            }
            Pump(w);
            Save(q, "more-quantities");
            Click(q, Find<Button>(q, "OkButton"));
            Pump(w);
            Tick(w, "ShowR", false);
            Tick(w, "ShowT", false);
            Pump(w);
            Need(Find<SpectrumPlot>(w, "Plot").Series!.Any(s => s.Name.StartsWith("|r_ps|")), "|r_ps| is plotted");
            Save(w, "rps-untilted");

            var boxes = RowBoxes(w, 1);   // the MO layer: thickness, φ, θ, ψ
            Need(boxes.Count == 4, "the magneto-optic layer has φ, θ, ψ cells");
            if (boxes.Count != 4) return;
            TypeAndLeave(w, RowBoxes(w, 1)[1], "20");
            TypeAndLeave(w, RowBoxes(w, 1)[2], "39");
            TypeAndLeave(w, RowBoxes(w, 1)[3], "30");
            Need(w.Workspace.Model.Layers[1].EulerDeg.SequenceEqual([20.0, 39, 30]), "the angles typed are the layer's");
            Evaluate(w, "tilted");
            Save(w, "rps-tilted");

            Find<DataGrid>(w, "LayerGrid").SelectedIndex = 1;
            Click(w, Find<Button>(w, "PropertiesButton"));
            var p = Dialog<LayerPropertiesDialog>(w);
            Need(p is not null, "Properties… opens");
            if (p is null) return;
            Save(p, "layer-properties");
            p.Close();
        });
    }

    /* 3. Materials: a constant one; the library (a uniaxial crystal); the crystal in a layer, tilted;
       a monoclinic crystal as terms, with its principal axes turning with wavelength. */
    private static void GuideMaterials(HeadlessUnitTestSession session)
    {
        var userFile = Path.Combine(Path.GetTempPath(), $"berreman-guide-user-{Guid.NewGuid():N}.json");
        Berreman.Api.MaterialLibrary.UserFile = userFile;   // never the real one
        try
        {
            WithWindow(session, w =>
            {
                PumpUntil(w, () => !w.IsEvaluating && Find<SpectrumPlot>(w, "Plot").Series is { Count: > 0 });
                ShowTab(w, "Materials");
                var list = Find<ListBox>(w, "MaterialList");
                list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "low");
                Pump(w);
                Need(Find<TextBox>(w, "PlainN0").Text == "1.38", "the new stack's layer material is n = 1.38");
                Save(w, "materials-constant");

                Click(w, Find<Button>(w, "LibraryButton"));
                var d = Dialog<LibraryDialog>(w);
                Need(d is not null, "From library… opens");
                if (d is null) return;
                Find<ComboBox>(d, "CollectionBox").SelectedItem = "Thin-film materials";
                Find<TextBox>(d, "SearchBox").Text = "MgF2";
                Pump(w);
                var entries = Find<ListBox>(d, "EntryList");
                entries.SelectedItem = entries.Items.Cast<object>().FirstOrDefault(i => i.ToString() == "MgF2 (uniaxial)" || i.ToString()!.StartsWith("MgF2 (uniaxial) "));
                Pump(w);
                Need(entries.SelectedItem is not null, "the library offers MgF2 as a uniaxial crystal");
                Save(d, "library-dialog");
                Click(d, Find<Button>(d, "OkButton"));
                Pump(w, 150);
                list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "MgF2 (uniaxial)");
                Pump(w);
                Need(Find<ComboBox>(w, "PlainAxesBox").SelectedIndex == 1, "MgF2 opens as uniaxial");
                Save(w, "materials-uniaxial");

                /* Into the layer, and tilted. */
                ShowTab(w, "Stack");
                var row = Find<DataGrid>(w, "LayerGrid").GetVisualDescendantsOf<DataGridRow>().First(r => r.IsEffectivelyVisible);
                var material = row.GetVisualDescendants().OfType<ComboBox>().First();
                material.SelectedItem = "MgF2 (uniaxial)";
                Pump(w);
                Need(w.Workspace.Model.Layers[0].Material == "MgF2 (uniaxial)", "the layer takes MgF2");
                TypeAndLeave(w, Find<TextBox>(w, "AngleEndBox"), "70");
                TypeAndLeave(w, Find<TextBox>(w, "AngleStepBox"), "35");
                TypeAndLeave(w, RowBoxes(w, 0)[0], "1000");
                TypeAndLeave(w, RowBoxes(w, 0)[2], "60");
                Need(w.Workspace.Model.Layers[0].EulerDeg[1] == 60, "the optic axis is tilted 60°");
                Evaluate(w, "uniaxial layer");
                Tick(w, "ShowT", false);
                Save(w, "stack-uniaxial-layer");
            });

            WithWindow(session, w =>
            {
                w.LoadSample(Sample("schubert-2016 / ga2o3-010"), "schubert-2016 / ga2o3-010");
                Pump(w);
                ShowTab(w, "Materials");
                var list = Find<ListBox>(w, "MaterialList");
                list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "Ga2O3");
                Pump(w);
                ShowMaterialTab(w, "Terms");
                var terms = Find<ListBox>(w, "TermList");
                terms.SelectedIndex = 2;
                Pump(w);
                Save(w, "materials-terms");
                ShowMaterialTab(w, "Response");
                Find<ComboBox>(w, "ResponseQuantityBox").SelectedIndex = 2;
                Pump(w, 200);
                Need(Find<SpectrumPlot>(w, "ResponsePlot").Series is { Count: > 0 }, "the principal-axis directions are plotted");
                Save(w, "materials-axes");
            });
        }
        finally { File.Delete(userFile); }
    }

    private static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual window) where T : Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<T>();
}
