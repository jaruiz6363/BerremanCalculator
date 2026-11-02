// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// WindowTests.cs — the editor window, driven as a person drives it, with no display.
//
// REAL MOUSE INPUT: a click is MouseDown and MouseUp at the point the control occupies after
// layout, so layout, hit-testing and the wiring are exercised together (ThinFilmDesigner's
// approach). Typing into a cell is the text set and the focus moved away, which is when the table
// takes a value.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Berreman.Api;
using System.Text.Json.Nodes;
using Berreman.Gui.Core;

namespace Berreman.Gui.Tests;

public sealed class Harness
{
    public int Checks { get; private set; }
    public int Failures { get; private set; }
    public void Section(string title) => Console.WriteLine(title);
    public void Expect(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }
}

public static partial class WindowTests
{
    public static bool Failed;

    private static void WithWindow(HeadlessUnitTestSession session, Action<MainWindow> body)
    {
        session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            try { body(window); }
            catch (Exception e) { Console.WriteLine($"  FAIL  the test threw: {e}"); Failed = true; }
            window.AskToSave = false;
            foreach (var w in window.OwnedWindows.ToList()) w.Close();
            window.Close();
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        (window is MainWindow ? window.FindControl<T>(name) : null) ?? window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named {name}");

    private static void Pump(Window window, int ms = 50)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(5); }
        while (DateTime.UtcNow < until);
        window.UpdateLayout();
    }

    /// <summary>
    /// Lets a pending automatic evaluation start and finish, so it cannot overwrite the status line
    /// between an action and the check that reads it.
    /// </summary>
    private static void Settle(MainWindow window)
    {
        Pump(window, 400);
        PumpUntil(window, () => !window.IsEvaluating);
    }

    /// <summary>Runs the dispatcher until the condition holds, or the time is up.</summary>
    private static bool PumpUntil(Window window, Func<bool> done, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            Pump(window, 20);
            if (done()) return true;
        }
        return false;
    }

    /// <summary>Edit > Undo, the one Undo of the window.</summary>
    private static void EditUndo(MainWindow window)
    {
        Find<MenuItem>(window, "UndoItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump(window);
    }

    private static void Click(Window window, Control control)
    {
        control.BringIntoView();   // as a person scrolls to it first
        Pump(window, 20);
        var root = control.GetVisualRoot() as TopLevel ?? window;
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)
                     ?? throw new InvalidOperationException($"{control.Name ?? control.GetType().Name} was never laid out");
        root.MouseMove(centre);
        root.MouseDown(centre, MouseButton.Left);
        root.MouseUp(centre, MouseButton.Left);
        Pump(window);
    }

    /// <summary>A value typed into a box and the focus moved on, as Tab or a click elsewhere does.</summary>
    private static void TypeAndLeave(MainWindow window, TextBox box, string text)
    {
        /* The cell takes its value when it loses focus, so it must have it first; Focus() can take
           a moment while the grid is still settling. */
        PumpUntil(window, () => { if (!box.IsFocused) box.Focus(); return box.IsFocused; }, 5);
        box.Text = text;
        /* Evaluate is disabled while an evaluation runs, and then cannot take the focus. */
        PumpUntil(window, () => { Find<Button>(window, "EvaluateButton").Focus(); return !box.IsFocused; }, 5);
        Pump(window);
    }

    /// <summary>A value typed into a box and Enter pressed, as a person ends a single value.</summary>
    private static void TypeAndEnter(MainWindow window, TextBox box, string text)
    {
        PumpUntil(window, () => { if (!box.IsFocused) box.Focus(); return box.IsFocused; }, 5);
        box.Text = text;
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump(window);
    }

    private static T? Dialog<T>(MainWindow window) where T : Window
    {
        PumpUntil(window, () => window.OwnedWindows.OfType<T>().Any(w => w.IsVisible), 5);
        var d = window.OwnedWindows.OfType<T>().LastOrDefault(w => w.IsVisible);
        d?.UpdateLayout();
        return d;
    }

    private static void ShowTab(MainWindow window, string header)
    {
        var tab = Find<TabControl>(window, "Tabs").Items.OfType<TabItem>().First(t => (string?)t.Header == header);
        Click(window, tab);
    }

    /// <summary>A frame of the window saved as PNG, when BC_SHOT_DIR names a folder (to look at, not to test).</summary>
    private static void Shot(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("BC_SHOT_DIR") is not { Length: > 0 } dir) return;
        Pump(window, 100);
        window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, name + ".png"));
    }

    private static void ShowMaterialTab(MainWindow window, string header)
    {
        /* Terms, Response and Text are under Advanced. */
        var advanced = Find<TabControl>(window, "AdvancedTabs").Items.OfType<TabItem>().FirstOrDefault(t => (string?)t.Header == header);
        if (advanced is not null)
        {
            Click(window, Find<TabControl>(window, "MaterialTabs").Items.OfType<TabItem>().First(t => (string?)t.Header == "Advanced"));
            Click(window, advanced);
            return;
        }
        Click(window, Find<TabControl>(window, "MaterialTabs").Items.OfType<TabItem>().First(t => (string?)t.Header == header));
    }

    private static string Sample(string name) =>
        File.ReadAllText(SampleCatalog.List(SampleCatalog.DefaultRoot).First(s => s.Name == name).Path);

    private static List<LayerRow> Rows(MainWindow w) => Find<DataGrid>(w, "LayerGrid").ItemsSource!.Cast<LayerRow>().ToList();

    // ---- the tests ---------------------------------------------------------------------------

    public static void TheWindowOpensAndEvaluatesTheNewStack(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the window opens with a new stack, and evaluates it without being asked");
        WithWindow(session, w =>
        {
            foreach (var name in new[] { "LayerGrid", "IncidentBox", "ExitBox", "SpectralStartBox", "SpectralEndBox", "SpectralStepBox", "AngleStartBox", "AngleEndBox", "AngleStepBox", "AzimuthBox",
                                         "LengthUnitBox", "SpectralUnitBox",
                                         "InsertButton", "DeleteButton", "UpButton", "DownButton", "PropertiesButton",
                                         "UndoButton", "RedoButton", "MaterialList", "MaterialSpecBox",
                                         "Editor", "Plot", "StatusText", "SummaryText", "EvaluateButton" })
                h.Expect(w.FindControl<Control>(name) is not null, $"{name} exists");
            h.Expect(Rows(w).Count == 1 && Rows(w)[0].ThicknessText == "100", "one 100 nm layer to start");
            h.Expect(PumpUntil(w, () => !w.IsEvaluating && Find<SpectrumPlot>(w, "Plot").Series is { Count: > 0 }),
                     "and the plot fills by itself");
            h.Expect((string?)Find<CheckBox>(w, "ShowP").Content == "p" && (string?)Find<CheckBox>(w, "ShowS").Content == "s",
                     "with air and glass the incident waves are labelled p and s");
        });
    }

    public static void LayersAreInsertedMovedAndUndone(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the layer buttons insert, move and delete, and Undo takes each back");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("mansuripur-1990 / quadrilayer"), "mansuripur-1990 / quadrilayer");
            Pump(w);
            var grid = Find<DataGrid>(w, "LayerGrid");
            h.Expect(Rows(w).Count == 4, "the quadrilayer has four layers");
            grid.SelectedIndex = 1;
            Click(w, Find<Button>(w, "InsertButton"));
            h.Expect(Rows(w).Count == 5 && Rows(w)[2].Material == "MO", "Insert copies the selected layer after it");
            grid.SelectedIndex = 2;
            Click(w, Find<Button>(w, "UpButton"));
            h.Expect(grid.SelectedIndex == 1, "Up moves the layer and the selection follows");
            Click(w, Find<Button>(w, "DeleteButton"));
            h.Expect(Rows(w).Count == 4, "Delete removes it");
            Click(w, Find<Button>(w, "UndoButton"));
            Click(w, Find<Button>(w, "UndoButton"));
            Click(w, Find<Button>(w, "UndoButton"));
            h.Expect(w.Workspace.Text == StackModel.FromJson(Sample("mansuripur-1990 / quadrilayer")).ToJson(), "three Undos give back the sample exactly");
            h.Expect(w.Title?.EndsWith("quadrilayer.berr") == true, $"and the title no longer says it changed ({w.Title})");
        });
    }

    /// <summary>
    /// The typed cells of a layer's row, as the grid shows it now. After an edit the grid rebuilds its
    /// rows, and an old container can still be in the visual tree for a moment with a row object
    /// that is no longer the grid's; typing into it would go nowhere. So the row is the one whose
    /// object is the grid's current one, after letting the grid settle.
    /// </summary>
    private static List<TextBox> RowBoxes(MainWindow w, int index)
    {
        List<TextBox>? boxes = null;
        PumpUntil(w, () =>
        {
            var current = Rows(w)[index];
            var row = Find<DataGrid>(w, "LayerGrid").GetVisualDescendants().OfType<DataGridRow>()
                .FirstOrDefault(r => ReferenceEquals(r.DataContext, current) && r.IsEffectivelyVisible);
            boxes = row?.GetVisualDescendants().OfType<TextBox>().ToList();
            return boxes is { Count: 4 };
        }, 5);
        return boxes ?? [];
    }

    public static void CellsAreEditedByTyping(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("thickness and orientation are typed into the table's cells");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("mansuripur-1990 / quadrilayer"), "q");
            Pump(w);
            var boxes = RowBoxes(w, 1);   // thickness, φ, θ, ψ
            h.Expect(boxes.Count == 4, $"a row has four typed cells ({boxes.Count})");
            if (boxes.Count != 4) return;
            var iso = RowBoxes(w, 0);     // SiOx, isotropic
            h.Expect(iso.Count == 4 && iso[0].IsVisible && iso.Skip(1).All(b => !b.IsVisible) && Rows(w)[0].AxisText == "",
                     "an isotropic layer shows its thickness but no φ, θ, ψ or optic axis");
            h.Expect(boxes.All(b => b.IsVisible), "the magneto-optic layer shows all four");
            TypeAndLeave(w, boxes[0], "25");
            h.Expect(w.Workspace.Model.Layers[1].Thickness == 25, "the thickness typed is the layer's");
            TypeAndLeave(w, RowBoxes(w, 1)[2], "40");
            h.Expect(w.Workspace.Model.Layers[1].EulerDeg[1] == 40, "θ typed in the row turns the layer");
            h.Expect(Rows(w)[1].AxisText.StartsWith("tilt 40°"), $"and the optic-axis column follows ({Rows(w)[1].AxisText})");
            TypeAndLeave(w, RowBoxes(w, 1)[0], "-5");
            h.Expect(w.Workspace.Model.Layers[1].Thickness == 25 && Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("zero or positive"),
                     $"a negative thickness is refused, with why ({Find<SelectableTextBlock>(w, "StatusText").Text})");
        });
    }

    public static void LayerPropertiesSetTheOpticAxisAndTwist(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("Layer properties points the optic axis and twists the layer");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("mansuripur-1990 / quadrilayer"), "q");
            Pump(w);
            Find<DataGrid>(w, "LayerGrid").SelectedIndex = 1;   // the MO layer: an isotropic one has no orientation to show
            Click(w, Find<Button>(w, "PropertiesButton"));
            var d = Dialog<LayerPropertiesDialog>(w);
            h.Expect(d is not null, "the dialog opens");
            if (d is null) return;
            Find<TextBox>(d, "TiltBox").Text = "30";
            Find<TextBox>(d, "AzimuthBox").Text = "45";
            Click(d, Find<Button>(d, "SetAxisButton"));
            h.Expect(Find<TextBox>(d, "ThetaBox").Text == "30" && Find<TextBox>(d, "PhiBox").Text == "135", "Set the angles fills φ = 135°, θ = 30°");
            Click(d, Find<CheckBox>(d, "TwistedBox"));
            Find<TextBox>(d, "PitchBox").Text = "300";
            Click(d, Find<Button>(d, "OkButton"));
            Pump(w);
            var l = w.Workspace.Model.Layers[1];
            h.Expect(l.EulerDeg.SequenceEqual([135.0, 30, 0]), "OK gives the layer those angles");
            h.Expect(l.Twist is { Pitch: 300, Handedness: "right" }, "and the twist");
            h.Expect(Rows(w)[1].AxisText == "tilt 30°, az 45°" && Rows(w)[1].TwistText == "300 nm right", $"which the table shows ({Rows(w)[1].AxisText}; {Rows(w)[1].TwistText})");
            Click(w, Find<Button>(w, "UndoButton"));
            h.Expect(w.Workspace.Model.Layers[1].Twist is null, "and one Undo takes it all back");
        });
    }

    public static void MaterialsAreEditedAsFormsTermsAndResponse(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the Materials tab edits n and k, edits terms field by field, and plots the response");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("schubert-2016 / ga2o3-010"), "ga");
            Settle(w);
            ShowTab(w, "Materials");
            var list = Find<ListBox>(w, "MaterialList");

            /* The Material page: air is n = 1. */
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "air");
            Pump(w);
            h.Expect(Find<TabControl>(w, "MaterialTabs").SelectedIndex == 0 && Find<TextBox>(w, "PlainN0").Text == "1"
                     && Find<ComboBox>(w, "PlainChoice0").SelectedIndex == 0 && Find<ComboBox>(w, "PlainAxesBox").SelectedIndex == 0,
                     $"a constant material opens on the Material page: the same in every direction, constant, n = 1 (tab {Find<TabControl>(w, "MaterialTabs").SelectedIndex}, n {Find<TextBox>(w, "PlainN0").Text})");
            h.Expect(Find<SpectrumPlot>(w, "PlainPlot").Series is { Count: 1 } one && one[0].Name == "n", "and its plot is one curve, n");
            TypeAndEnter(w, Find<TextBox>(w, "PlainN0"), "1.0003");
            h.Expect(w.Workspace.Model.Material("air")!.Spec["n"]!.GetValue<double>() == 1.0003, "Enter in n writes it, with no Apply");
            EditUndo(w);
            h.Expect(w.Workspace.Model.Material("air")!.Spec["n"]!.GetValue<double>() == 1 && Find<TextBox>(w, "PlainN0").Text == "1",
                     "Edit > Undo takes it back, and the box follows");
            TypeAndLeave(w, Find<TextBox>(w, "PlainN0"), "1.5x");
            h.Expect(w.Workspace.Model.Material("air")!.Spec["n"]!.GetValue<double>() == 1 && Find<TextBox>(w, "PlainN0").Text == "1.5x"
                     && Find<TextBox>(w, "PlainN0").Classes.Contains("invalid") && Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("1.5x"),
                     $"text that is not a number is refused when the box is left: it stays, outlined in red, and the status says why ({Find<SelectableTextBlock>(w, "StatusText").Text})");
            Shot(w, "materials-refused");
            TypeAndLeave(w, Find<TextBox>(w, "PlainN0"), "1.0003");
            h.Expect(w.Workspace.Model.Material("air")!.Spec["n"]!.GetValue<double>() == 1.0003 && !Find<TextBox>(w, "PlainN0").Classes.Contains("invalid"),
                     "corrected, it is taken when the box is left, and the red goes");
            EditUndo(w);

            /* Uniaxial, with a formula for the extraordinary index. */
            Find<ComboBox>(w, "PlainAxesBox").SelectedIndex = 1;
            Pump(w);
            h.Expect(Find<TextBox>(w, "PlainN0").Text == "1" && Find<TextBox>(w, "PlainN1").Text == "1", "uniaxial starts from the isotropic value on both axes");
            h.Expect(w.Workspace.Model.Material("air")!.Spec.ContainsKey("no") && Find<ComboBox>(w, "PlainAxesBox").SelectedIndex == 1,
                     $"and the choice takes effect at once, written as no and ne ({w.Workspace.Model.Material("air")!.Spec.ToJsonString()})");
            Find<ComboBox>(w, "PlainChoice1").SelectedIndex = Array.FindIndex(MaterialEditing.PlainChoices, c => c.Law == "cauchy");
            Pump(w);
            h.Expect(w.Workspace.Model.Material("air")!.Spec["uniaxial"]?["extraordinary"]?["cauchy"] is JsonObject, "so does a formula, with its default values");
            TypeAndLeave(w, Find<TextBox>(w, "Plain1_a"), "1.2");
            var air = w.Workspace.Model.Material("air")!.Spec;
            h.Expect(air["uniaxial"]?["ordinary"]?["n"]?.GetValue<double>() == 1 && air["uniaxial"]?["extraordinary"]?["cauchy"]?["a"]?.GetValue<double>() == 1.2,
                     $"leaving a parameter writes it: an ordinary constant and an extraordinary Cauchy law ({air.ToJsonString()})");
            h.Expect(Find<SpectrumPlot>(w, "PlainPlot").Series?.Select(s => s.Name).SequenceEqual(["n ordinary", "n extraordinary"]) == true, "the plot shows both indices");
            Shot(w, "materials-plain");
            EditUndo(w);
            EditUndo(w);
            EditUndo(w);
            h.Expect(w.Workspace.Model.Material("air")!.Spec["n"]?.GetValue<double>() == 1 && Find<ComboBox>(w, "PlainAxesBox").SelectedIndex == 0,
                     $"three Undos give back air, and the page follows ({w.Workspace.Model.Material("air")!.Spec.ToJsonString()})");

            /* Ga2O3 is a list of terms: the page says so, and Advanced edits it. */
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "Ga2O3");
            Pump(w);
            h.Expect(Find<StackPanel>(w, "NotPlainPanel").IsVisible && !Find<StackPanel>(w, "PlainPanel").IsVisible && Find<TextBlock>(w, "NotPlainText").Text!.Contains("16 terms"),
                     $"a material written as terms says what it is on the Material page ({Find<TextBlock>(w, "NotPlainText").Text})");
            h.Expect(Find<SpectrumPlot>(w, "PlainPlot").Series is { Count: > 0 }, "and still plots n and k");
            ShowMaterialTab(w, "Terms");
            var terms = Find<ListBox>(w, "TermList");
            h.Expect(terms.ItemCount == 16, $"its 16 terms are listed ({terms.ItemCount})");
            terms.SelectedIndex = 1;
            Pump(w);
            h.Expect(Find<ComboBox>(w, "TermLawBox").SelectedItem as string == "lorentz" && Find<TextBox>(w, "AxisAngleBox").Text == "47.8",
                     "Bu-1 shows its law and its direction");
            var center = Find<TextBox>(w, "Law_center");
            h.Expect(center.Text == "743.48", $"its centre is a field ({center.Text})");
            TypeAndEnter(w, center, "750");
            JsonNode Term(int i) => w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]![i]!;
            h.Expect(Term(1)["lorentz"]!["center"]!.GetValue<double>() == 750 && Term(1)["axis_deg"]!.GetValue<double>() == 47.8,
                     "Enter in the centre changes it and keeps the direction");
            w.Workspace.Undo();
            Pump(w);
            h.Expect(Term(1)["lorentz"]!["center"]!.GetValue<double>() == 743.48, "Undo takes it back");

            /* The 6×6 shape: Bu-1 as a matrix is cos² and sin² on ε. */
            terms.SelectedIndex = 1;
            Pump(w);
            Find<ComboBox>(w, "ShapeModeBox").SelectedIndex = 2;
            Pump(w);
            var c = Math.Cos(47.8 * Math.PI / 180);
            h.Expect(Math.Abs(MaterialEditing.ParseComplex(Find<TextBox>(w, "Shape00").Text ?? "").Real - c * c) < 1e-15
                     && string.IsNullOrEmpty(Find<TextBox>(w, "Shape33").Text), "the direction becomes ê êᵀ in the 6×6 cells");
            Shot(w, "materials-matrix");
            TypeAndLeave(w, Find<TextBox>(w, "Shape01"), "0.1i");
            TypeAndLeave(w, Find<TextBox>(w, "Shape10"), "-0.1i");
            h.Expect(Term(1)["eps"] is JsonArray, $"a typed matrix is written as an ε block ({Find<SelectableTextBlock>(w, "StatusText").Text}; {Term(1).ToJsonString()})");
            h.Expect(Find<TextBlock>(w, "MaterialSummary").Text!.Contains("16 terms"), "the summary counts the terms");

            /* A direction in crystal axes: [0 0 1] of the monoclinic cell, with its lattice. */
            terms.SelectedIndex = 2;
            Pump(w);
            Find<ComboBox>(w, "ShapeModeBox").SelectedIndex = 3;
            Pump(w);
            foreach (var (box, text) in new[] { ("UvwUBox", "0"), ("UvwVBox", "0"), ("UvwWBox", "1"), ("LatticeABox", "12.23"), ("LatticeBBox", "3.04"),
                                                ("LatticeCBox", "5.80"), ("LatticeAlphaBox", "90"), ("LatticeBetaBox", "103.7"), ("LatticeGammaBox", "90") })
                TypeAndLeave(w, Find<TextBox>(w, box), text);
            var ga = w.Workspace.Model.Material("Ga2O3")!.Spec;
            h.Expect(Term(2)["axis_uvw"] is JsonArray && ga["lattice"]?["beta"]?.GetValue<double>() == 103.7,
                     $"a direction in crystal axes is written with the material's lattice ({Find<SelectableTextBlock>(w, "StatusText").Text})");

            /* Add and delete. */
            int before = w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]!.AsArray().Count;
            Click(w, Find<Button>(w, "AddTermButton"));
            h.Expect(w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]!.AsArray().Count == before + 1, "Add adds a term");
            Click(w, Find<Button>(w, "DeleteTermButton"));
            h.Expect(w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]!.AsArray().Count == before, "Delete removes it");

            /* A short-form material is shown as terms, and a change there writes it as terms. */
            w.Workspace.Edit(m => m.Materials.Add(new NamedMaterial("SiO2", new JsonObject
            {
                ["sellmeier"] = JsonNode.Parse("""{ "unit": "um", "terms": [ { "b": 0.6961663, "c2": 0.0046791 } ] }"""),
            })));
            Pump(w);
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "SiO2");
            Pump(w);
            h.Expect(Find<TabControl>(w, "MaterialTabs").SelectedIndex == 0 && Find<ComboBox>(w, "PlainChoice0").SelectedItem as string == "Sellmeier formula"
                     && Find<TextBox>(w, "Plain0_terms").Text!.Contains("0.6961663"),
                     "the Material page shows a Sellmeier glass as its formula, with its numbers");
            ShowMaterialTab(w, "Terms");
            Pump(w);
            h.Expect(terms.ItemCount == 2 && Find<ComboBox>(w, "TermLawBox").SelectedItem as string == "sellmeier", "a Sellmeier material shows as its law and μ = 1");
            h.Expect(Find<TextBox>(w, "Law_terms").Text!.Contains("0.6961663"), "its terms are lines of numbers");
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "Ga2O3");
            Pump(w);
            terms.SelectedIndex = 2;
            Pump(w);
            Shot(w, "materials-terms");

            /* Response. */
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "Ga2O3");
            Pump(w);
            ShowMaterialTab(w, "Response");
            h.Expect(Find<SpectrumPlot>(w, "ResponsePlot").Series is { Count: 6 }, "the response plots n and k on three axes");
            h.Expect(Find<TextBlock>(w, "ResponseChecks").Text!.StartsWith("NOT PASSIVE") || Find<TextBlock>(w, "ResponseChecks").Text!.StartsWith("Passive"),
                     $"and says whether it is passive ({Find<TextBlock>(w, "ResponseChecks").Text})");
            Find<ComboBox>(w, "ResponseQuantityBox").SelectedIndex = 1;
            Pump(w);
            h.Expect(Find<SpectrumPlot>(w, "ResponsePlot").Series is { Count: 2 }, "one element: real and imaginary");
            h.Expect(Find<SelectableTextBlock>(w, "ResponseMatrix").Text!.Split((char)10).Length == 7, "M at a point is printed");
            Find<ComboBox>(w, "ResponseQuantityBox").SelectedIndex = 0;
            Pump(w);
            Shot(w, "materials-response-nk");
            Find<ComboBox>(w, "ResponseQuantityBox").SelectedIndex = 2;
            Pump(w);
            Shot(w, "materials-response-axes");
        });
    }

    public static void ResultsShowAnyQuantityAndTheMuellerMatrix(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the results pane plots chosen quantities against a chosen variable, and the Mueller matrix");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("schubert-2016 / ga2o3-010"), "ga");
            /* F5, rather than the automatic evaluation: the headless platform stops delivering a
               DispatcherTimer's ticks to windows opened after the first one is closed. */
            Click(w, Find<Button>(w, "EvaluateButton"));
            h.Expect(PumpUntil(w, () => Find<SpectrumPlot>(w, "Plot").Series is { Count: > 0 }), $"the sample evaluates ({Find<SelectableTextBlock>(w, "StatusText").Text}; evaluating {w.IsEvaluating}; empty text {Find<SpectrumPlot>(w, "Plot").EmptyText}; visible {Find<SpectrumPlot>(w, "Plot").IsVisible}; eval {Find<TextBlock>(w, "EvaluationText").Text})");
            h.Expect((string?)Find<CheckBox>(w, "ShowP").Content == "mode 0" && (string?)Find<CheckBox>(w, "ShowS").Content == "mode 1",
                     $"with an anisotropic exit medium the incident waves are its modes 0 and 1 ({Find<CheckBox>(w, "ShowP").Content}, {Find<CheckBox>(w, "ShowS").Content})");
            var axes = Find<ComboBox>(w, "XAxisBox");
            h.Expect(axes.ItemCount == 2, $"wavenumber and angle are offered as x ({axes.ItemCount})");
            axes.SelectedIndex = 1;
            Pump(w);
            h.Expect(Find<SpectrumPlot>(w, "Plot").XTitle.StartsWith("angle"), "x can be the angle instead");
            axes.SelectedIndex = 0;
            w.MoreQuantities = ["Psi_ps", "Mr13"];
            Pump(w);
            var names = Find<SpectrumPlot>(w, "Plot").Series!.Select(x => x.Name).ToList();
            h.Expect(names.Any(n => n.StartsWith("Psi_01")) && names.Any(n => n.StartsWith("Mr13")),
                     $"quantities chosen with More… are plotted, Psi_ps as Psi_01 since the exit medium is anisotropic ({string.Join(", ", names.Take(20))})");
            w.MoreQuantities = [];
            Pump(w);

            /* In dB: the R and T curves become 10 log10 of themselves, on a dB axis. */
            var plotDb = Find<SpectrumPlot>(w, "Plot");
            var rFraction = plotDb.Series!.First(x => x.Name.StartsWith("R_0")).Y;
            Click(w, Find<CheckBox>(w, "ShowDb"));
            Pump(w);
            var rDb = plotDb.Series!.FirstOrDefault(x => x.Name.StartsWith("R_0 (dB)"));
            h.Expect(rDb is { Axis: "dB" } && plotDb.Series!.All(x => x.Axis == "dB")
                     && rDb.Y.Zip(rFraction).All(z => Math.Abs(z.First - 10 * Math.Log10(Math.Max(z.Second, 1e-30))) < 1e-12),
                     $"in dB, R and T are drawn as 10 log10 on a dB axis ({string.Join(", ", plotDb.Series!.Take(4).Select(x => $"{x.Name} [{x.Axis}]"))})");
            Click(w, Find<CheckBox>(w, "ShowDb"));
            Pump(w);
            h.Expect(plotDb.Series!.All(x => x.Axis is null), "and as fractions again when it is unticked");

            /* More…'s names keep their underscores. A string label is shown by an AccessText, which
               hides "_" as an access key though its Text keeps it; a plain TextBlock shows it. */
            var dialog = new QuantityDialog([], ("p", "s"));
            _ = dialog.ShowDialog<List<string>?>(w);
            Pump(dialog);
            var shown = dialog.GetVisualDescendants().OfType<CheckBox>().Where(b => b.Name is "Q_R_ps" or "Q_|r_ps|")
                .Select(b => b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault() is { } t and not Avalonia.Controls.Primitives.AccessText ? t.Text : "(an access-key label)").ToList();
            h.Expect(shown.SequenceEqual(["R_ps", "|r_ps|"]), $"More… shows R_ps and |r_ps| as written ({string.Join(", ", shown)})");
            dialog.Close();

            /* Zoom: drag a rectangle over the plot, then double-click to see everything again. */
            var plot = Find<SpectrumPlot>(w, "Plot");
            Point At(double fx, double fy) => plot.TranslatePoint(new Point(plot.Bounds.Width * fx, plot.Bounds.Height * fy), w)!.Value;
            w.MouseMove(At(0.3, 0.3));
            w.MouseDown(At(0.3, 0.3), MouseButton.Left);
            w.MouseMove(At(0.5, 0.6));
            w.MouseUp(At(0.5, 0.6), MouseButton.Left);
            Pump(w);
            h.Expect(plot.XZoom is { } z && z.Min > 200 && z.Max < 1100 && z.Max > z.Min, $"dragging a rectangle zooms into it ({plot.XZoom})");
            w.MouseDown(At(0.5, 0.5), MouseButton.Left, RawInputModifiers.None);
            w.MouseUp(At(0.5, 0.5), MouseButton.Left);
            w.MouseDown(At(0.5, 0.5), MouseButton.Left, RawInputModifiers.None);
            w.MouseUp(At(0.5, 0.5), MouseButton.Left);
            Pump(w);
            h.Expect(plot.XZoom is null, "a double-click shows everything again");
            var pngCurves = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"berreman-curves-{Guid.NewGuid():N}.png");
            w.SavePlotPng(pngCurves);
            h.Expect(new System.IO.FileInfo(pngCurves).Length > 10000, "the curves export as a PNG");
            File.Delete(pngCurves);

            Find<ComboBox>(w, "ViewBox").SelectedIndex = 1;
            Pump(w, 200);
            var m13 = w.GetVisualDescendants().OfType<SpectrumPlot>().First(p => p.Name == "Mueller13");
            h.Expect(m13.IsEffectivelyVisible && m13.Series is { Count: 3 }, "the Mueller view has a panel per element, a curve per angle");
            Shot(w, "mueller-reflection");
            var png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"berreman-mueller-{Guid.NewGuid():N}.png");
            w.SavePlotPng(png);
            var head = File.ReadAllBytes(png);
            h.Expect(head.Length > 10000 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G', $"the Mueller panels export as a PNG ({head.Length} bytes)");
            File.Delete(png);
            Find<ComboBox>(w, "ViewBox").SelectedIndex = 0;
            Pump(w);
        });
    }

    public static void TheConoscopicMapShowsTheIsochromes(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("Analysis > Conoscopic map computes and draws a map, and saves it");
        WithWindow(session, w =>
        {
            w.LoadSample("""
                { "format": "berreman-stack", "version": 1, "units": { "length": "um", "spectral": "nm" },
                  "materials": { "oil": { "n": 1.55 }, "crystal": { "no": 1.5, "ne": 1.6 } },
                  "incident": { "material": "oil" }, "layers": [ { "material": "crystal", "thickness": 60 } ],
                  "exit": { "material": "oil" }, "sweep": { "spectral": 600, "angle_deg": 0 } }
                """, "plate");
            Pump(w);
            var c = w.OpenConoscope();
            Pump(w, 200);
            Find<ComboBox>(c, "ConoscopeQuantityBox").SelectedIndex = 0;
            Click(c, Find<Button>(c, "ComputeMapButton"));
            h.Expect(PumpUntil(w, () => !c.IsComputing && c.View.Map is not null, 30), $"the map is computed ({Find<TextBlock>(c, "ConoscopeStatus").Text})");
            if (c.View.Map is not { } map) return;
            h.Expect(map.AnglesDeg.Length == 31 && map.AzimuthsDeg.Length == 72, "on 31 angles and 72 azimuths");
            h.Expect(map.Values[0, 0] < 1e-6 && map.Max > 0.5, $"crossed polarisers: dark on the optic axis, bright rings off it (max {map.Max:G3})");
            if (Environment.GetEnvironmentVariable("BC_SHOT_DIR") is { Length: > 0 } dir) { Pump(w, 200); c.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, "conoscope.png")); }
            var png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"berreman-map-{Guid.NewGuid():N}.png");
            var csv = System.IO.Path.ChangeExtension(png, ".csv");
            c.SaveTo(png, png: true);
            c.SaveTo(csv, png: false);
            h.Expect(new System.IO.FileInfo(png).Length > 10000 && File.ReadAllLines(csv).Length == 1 + 31 * 72, "the map saves as PNG and as CSV");
            File.Delete(png); File.Delete(csv);
            c.Close();
        });
    }

    public static void TheFieldProfileShowsTheFieldsThroughTheStack(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("Analysis > Field profile computes the fields against depth and the index profile");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("mansuripur-1990 / quadrilayer"), "q");
            Pump(w);
            var f = w.OpenFieldProfile();
            Pump(w, 200);
            Click(f, Find<Button>(f, "ComputeProfileButton"));
            h.Expect(PumpUntil(w, () => f.Profile is not null && f.Plot.Series is { Count: > 0 }, 30), $"the profile is computed ({Find<TextBlock>(f, "ProfileStatus").Text})");
            if (f.Profile is not { } p) return;
            h.Expect(p.Depths.Length == 1001 && p.Boundaries.Length == w.Workspace.Model.Layers.Count + 1, "1001 depths, and an interface per layer boundary");
            h.Expect(f.Plot.Markers?.Count == p.Boundaries.Length, "the interfaces are drawn");
            if (Environment.GetEnvironmentVariable("BC_SHOT_DIR") is { Length: > 0 } dir) { Pump(w, 200); f.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, "field-profile.png")); }
            Find<ComboBox>(f, "ProfileQuantityBox").SelectedIndex = 4;
            Pump(w);
            h.Expect(f.Plot.Series is { Count: 6 } && f.Plot.Series[0].Name == "n_x", "the index profile shows n and k along x, y, z");
            var csv = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"berreman-profile-{Guid.NewGuid():N}.csv");
            f.SaveTo(csv, png: false);
            var lines = File.ReadAllLines(csv);
            h.Expect(lines.Length == 1002 && lines[0].Split(',').Length == 25, "the fields save as CSV: depth and 24 columns");
            File.Delete(csv);
            f.Close();
        });
    }

    public static void TheSampleBrowserFindsAndOpens(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("File > Browse samples searches the samples and opens one");
        WithWindow(session, w =>
        {
            _ = w.BrowseSamples();
            var d = Dialog<SampleBrowser>(w);
            h.Expect(d is not null, "the browser opens");
            if (d is null) return;
            Find<TextBox>(d, "SampleSearchBox").Text = "cholesteric";
            Pump(w);
            var list = Find<ListBox>(d, "SampleList");
            h.Expect(list.ItemCount == 1 && Find<SelectableTextBlock>(d, "SampleSource").Text!.Contains("Ting"), $"a search finds the cholesteric and shows its source ({list.ItemCount})");
            Click(d, Find<Button>(d, "OkButton"));
            h.Expect(PumpUntil(w, () => w.Workspace.Name.StartsWith("ting-2006"), 5), $"Open opens it as a copy ({w.Workspace.Name})");
        });
    }

    public static void AThinFilmDesignerDesignIsImported(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("File > Import a ThinFilmDesigner design reads a .tfd as a new stack");
        /* The fixture, found from the build output up to the repository. */
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "tests", "fixtures", "tfd"))) dir = dir.Parent;
        h.Expect(dir is not null, "the .tfd fixtures are found");
        if (dir is null) return;
        var text = File.ReadAllText(System.IO.Path.Combine(dir.FullName, "tests", "fixtures", "tfd", "AR-visible.tfd"));
        WithWindow(session, w =>
        {
            w.ImportTfd(text, "AR-visible");
            Pump(w);
            h.Expect(w.Workspace.Model.Layers.Count == 4 && w.Workspace.Name == "AR-visible.berr" && Rows(w).Count == 4, "its four layers are the table's");
            h.Expect(w.Workspace.Model.Notes.Contains("merit function"), "and its notes say what was not carried over");
        });
    }

    public static void MaterialsComeFromLibraries(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("From library… adds database materials and catalogue glasses; Save keeps one in my materials");
        var userFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"berreman-gui-user-{Guid.NewGuid():N}.json");
        MaterialLibrary.UserFile = userFile;   // never the real one
        try
        {
            WithWindow(session, w =>
            {
                ShowTab(w, "Materials");
                void Choose(string collection, string search, string name)
                {
                    Click(w, Find<Button>(w, "LibraryButton"));
                    var d = Dialog<LibraryDialog>(w);
                    h.Expect(d is not null, "the library dialog opens");
                    if (d is null) return;
                    Find<ComboBox>(d, "CollectionBox").SelectedItem = collection;
                    Find<TextBox>(d, "SearchBox").Text = search;
                    Pump(w);
                    var list = Find<ListBox>(d, "EntryList");
                    list.SelectedItem = list.Items.Cast<object>().FirstOrDefault(i => i.ToString()!.StartsWith(name + " ", StringComparison.Ordinal) || i.ToString() == name);
                    Pump(w);
                    h.Expect(list.SelectedItem is not null && Find<SelectableTextBlock>(d, "EntryDetail").Text!.Contains(name), $"{name} is found in {collection}");
                    if (Environment.GetEnvironmentVariable("BC_SHOT_DIR") is { Length: > 0 } dir) d.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, $"library-{collection.Replace(":", "")}.png"));
                    Click(d, Find<Button>(d, "OkButton"));
                    Pump(w, 100);
                }
                Choose("Glass: SCHOTT", "N-BK7", "N-BK7");
                h.Expect(w.Workspace.Model.Material("N-BK7")?.Spec.ToJsonString() == """{"glass":{"catalog":"SCHOTT","name":"N-BK7"}}""",
                         "a glass is added by name");
                Choose("Thin-film materials", "MgF2", "MgF2 (uniaxial)");
                h.Expect(w.Workspace.Model.Material("MgF2 (uniaxial)")?.Spec["uniaxial"] is not null, "a crystal is added as one uniaxial material");
                h.Expect(w.Workspace.Summary("MgF2 (uniaxial)").StartsWith("uniaxial"), $"and is summarised as uniaxial ({w.Workspace.Summary("MgF2 (uniaxial)")})");

                Click(w, Find<Button>(w, "SaveToLibraryButton"));
                h.Expect(MaterialLibrary.ReadUserFile(userFile).Any(e => e.Name == "MgF2 (uniaxial)"), "Save to my materials writes the selected one");
                Choose("My materials", "", "MgF2 (uniaxial)");
                h.Expect(w.Workspace.Model.Materials.Count(m => m.Spec["uniaxial"] is not null) == 2,
                         "and From library… offers it back, under a new name in this stack");
            });
        }
        finally { System.IO.File.Delete(userFile); }
    }

    public static void MaterialsAreAddedEditedAndProtected(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the Materials tab adds, edits, renames, and will not remove a material in use");
        WithWindow(session, w =>
        {
            ShowTab(w, "Materials");
            Click(w, Find<Button>(w, "NewMaterialButton"));
            h.Expect(w.Workspace.Model.Material("material") is not null, "New adds a material");
            ShowMaterialTab(w, "Text");
            TypeAndLeave(w, Find<TextBox>(w, "MaterialSpecBox"), """{ "no": 1.54, "ne": 1.55 }""");
            h.Expect(w.Workspace.Summary("material").StartsWith("uniaxial"), $"leaving the text makes it uniaxial ({w.Workspace.Summary("material")})");
            TypeAndEnter(w, Find<TextBox>(w, "MaterialNameBox"), "quartz");
            h.Expect(w.Workspace.Model.Material("quartz") is not null, "Enter in the name renames it");
            Settle(w);
            ShowMaterialTab(w, "Text");
            TypeAndLeave(w, Find<TextBox>(w, "MaterialSpecBox"), """{ "n": 1.5, "eps": 2 }""");
            h.Expect(w.Workspace.Summary("quartz").StartsWith("uniaxial") && Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("only one")
                     && Find<TextBox>(w, "MaterialSpecBox").Classes.Contains("invalid") && Find<TextBox>(w, "MaterialSpecBox").Text!.Contains("eps"),
                     "a material the parser refuses is not applied: the text stays, outlined in red, and the status says why");
            var list = Find<ListBox>(w, "MaterialList");
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "low");
            Settle(w);
            Click(w, Find<Button>(w, "RemoveMaterialButton"));
            h.Expect(w.Workspace.Model.Material("low") is not null && Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("layer 1"),
                     "a material in use stays, and the status says where it is used");
        });
    }

    public static void UnitsConvertAndTheFileFollows(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("changing units converts the stack; the File tab shows and applies the text");
        WithWindow(session, w =>
        {
            Find<ComboBox>(w, "SpectralUnitBox").SelectedItem = "um";
            Pump(w);
            string Sweep() => $"{Find<TextBox>(w, "SpectralStartBox").Text} | {Find<TextBox>(w, "SpectralEndBox").Text} | {Find<TextBox>(w, "SpectralStepBox").Text} {Find<TextBlock>(w, "SpectralUnitLabel").Text}";
            h.Expect(Sweep() == "0.4 | 0.8 | 0.002 µm", $"nm to µm converts the sweep: start, end and step, in the new unit ({Sweep()})");
            Find<ComboBox>(w, "LengthUnitBox").SelectedItem = "µm";
            Pump(w);
            h.Expect(Rows(w)[0].ThicknessText == "0.1", $"nm to µm converts the thicknesses ({Rows(w)[0].ThicknessText})");
            ShowTab(w, "File");
            var editor = Find<TextBox>(w, "Editor");
            h.Expect(editor.Text!.Contains("\"spectral\": \"um\""), "the File tab shows the stack file");
            TypeAndLeave(w, editor, editor.Text.Replace("\"title\": \"New stack\"", "\"title\": \"Edited as text\""));
            h.Expect(w.Workspace.Model.Title == "Edited as text", "and an edit made there is taken when the text is left");
        });
    }

    public static void TheSpectralSweepIsStartEndAndStep(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the spectral sweep is typed as start, end and step");
        WithWindow(session, w =>
        {
            TextBox Box(string name) => Find<TextBox>(w, name);
            h.Expect(Box("SpectralStartBox").Text == "400" && Box("SpectralEndBox").Text == "800" && Box("SpectralStepBox").Text == "2"
                     && Find<TextBlock>(w, "SpectralPoints").Text == "201 points", "a new stack shows 400 to 800, step 2: 201 points");
            TypeAndLeave(w, Box("SpectralStepBox"), "50");
            h.Expect(w.Workspace.Model.Spectral == "400 to 800 step 50" && Find<TextBlock>(w, "SpectralPoints").Text == "9 points", $"a new step is the sweep's ({w.Workspace.Model.Spectral})");
            TypeAndLeave(w, Box("SpectralEndBox"), "");
            h.Expect(w.Workspace.Model.Spectral == "400" && Box("SpectralStepBox").Text == "", $"an empty end is one wavelength ({w.Workspace.Model.Spectral})");
            TypeAndLeave(w, Box("SpectralEndBox"), "700");
            h.Expect(w.Workspace.Model.Spectral == "400 to 700 step 5" && Box("SpectralStepBox").Text == "5",
                     $"an end typed with no step gets a round one, about a hundred intervals ({w.Workspace.Model.Spectral})");
            TypeAndLeave(w, Box("SpectralStepBox"), "-5");
            h.Expect(w.Workspace.Model.Spectral == "400 to 700 step 5" && Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("positive"),
                     $"a negative step is refused, with why ({Find<SelectableTextBlock>(w, "StatusText").Text})");
            TypeAndLeave(w, Box("SpectralStepBox"), "100");
            Shot(w, "stack-spectral");
            h.Expect(w.Workspace.Model.Spectral == "400 to 700 step 100" && Find<TextBlock>(w, "SpectralPoints").Text == "4 points", $"a typed step is the sweep's ({w.Workspace.Model.Spectral})");
        });
    }

    public static void TheAnglesAreStartEndAndStep(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the angles are typed as start, end and step; a file's even list shows as a range");
        WithWindow(session, w =>
        {
            TextBox Box(string name) => Find<TextBox>(w, name);
            string Angles() => $"{Box("AngleStartBox").Text} | {Box("AngleEndBox").Text} | {Box("AngleStepBox").Text} {Find<TextBlock>(w, "AngleUnitLabel").Text} ({Find<TextBlock>(w, "AnglePoints").Text})";
            h.Expect(Angles() == "0 |  |  ° ()", $"a new stack is at normal incidence: a start alone ({Angles()})");
            TypeAndLeave(w, Box("AngleEndBox"), "80");
            h.Expect(w.Workspace.Model.Angles == "0 to 80 step 1" && Angles() == "0 | 80 | 1 ° (81 angles)", $"an end gets a round step ({Angles()})");
            TypeAndLeave(w, Box("AngleStepBox"), "10");
            h.Expect(w.Workspace.Model.Angles == "0 to 80 step 10", $"and a typed step replaces it ({w.Workspace.Model.Angles})");

            w.LoadSample(Sample("peshlov-2006 / twintex"), "peshlov-2006 / twintex");
            Pump(w);
            h.Expect(Angles() == "0 | 60 | 30 ° (3 angles)" && w.Workspace.Model.Angles == "0, 30, 60",
                     $"the file's list 0, 30, 60 shows as a range and stays a list until edited ({Angles()})");
            w.LoadSample(Sample("schubert-1996 / biaxial-film"), "schubert-1996 / biaxial-film");
            Pump(w);
            Shot(w, "stack-angles");
            h.Expect(Angles().StartsWith("0 | 80 |  ° (5 listed values"), $"an uneven list shows its ends and says it is a list ({Angles()})");
        });
    }

    public static void ACoatingIsImportedFromOpticStudioAndOptiland(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("File > Import a coating: an OpticStudio coating file, then an Optiland coating with its media");
        WithWindow(session, w =>
        {
            var e = new System.Text.UnicodeEncoding(false, true);
            byte[] dat = [.. e.GetPreamble(), .. e.GetBytes(string.Join("\r\n",
                "MATE MGF2", ".400 1.38 0", ".700 1.38 0", "", "COAT AR", "MGF2 .25", "", "COAT LOOPED", "MGF2 .25 0 3", "", "COAT AR_UM", "MGF2 .1 1", ""))];
            var d = new CoatingImportDialog(dat, "coating.dat", 600, (0.4, 0.8));
            var shown = d.ShowDialog<CoatingChoice?>(w);
            Pump(d);
            var list = Find<ListBox>(d, "CoatingList");
            h.Expect(list.ItemCount == 3 && list.SelectedIndex == 0, $"the file's three coatings are listed, the first selected ({list.ItemCount})");
            h.Expect(Find<TextBox>(d, "WavelengthBox").IsEffectivelyVisible && Find<TextBox>(d, "WavelengthBox").Text == "600",
                     "AR is in waves: the wavelength is asked, offering the sweep's first");
            list.SelectedIndex = 1;
            Pump(d);
            h.Expect(Find<TextBlock>(d, "ErrorText").Text!.Contains("loop"), $"LOOPED says why it cannot be imported ({Find<TextBlock>(d, "ErrorText").Text})");
            list.SelectedIndex = 0;
            Pump(d);
            if (Environment.GetEnvironmentVariable("BC_SHOT_DIR") is { Length: > 0 } shots) d.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(shots, "coating-import.png"));
            Find<TextBox>(d, "WavelengthBox").Text = "552";
            Click(d, Find<Button>(d, "OkButton"));
            Pump(w);
            h.Expect(shown.IsCompleted && shown.Result is { } c && Math.Abs(c.Coating.Layers[0].ThicknessNm - 0.25 * 552 / 1.38) < 1e-9,
                     "Import reads it at the wavelength typed: a quarter wave of MgF2 at 552 nm");
            if (shown.Result is not { } zemax) return;
            w.ApplyCoating(zemax);
            Pump(w);
            h.Expect(Rows(w).Count == 1 && Rows(w)[0].Material == "MGF2" && Rows(w)[0].TypeText == "isotropic · table",
                     $"the layer table holds the coating ({Rows(w).Count} rows, {Rows(w).FirstOrDefault()?.TypeText})");
            h.Expect(Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("1 layers imported"), $"and the status says so ({Find<SelectableTextBlock>(w, "StatusText").Text})");

            var optiland = """
                {"type": "ThinFilmCoating",
                 "material_pre": {"type": "IdealMaterial", "index": 1.0, "absorp": 0.0},
                 "material_post": {"type": "IdealMaterial", "index": 1.52, "absorp": 0.0},
                 "layers": [{"material": {"type": "IdealMaterial", "index": 2.3, "absorp": 0.0}, "thickness_nm": 60.0},
                            {"material": {"type": "IdealMaterial", "index": 1.46, "absorp": 0.0}, "thickness_nm": 95.0}]}
                """;
            var d2 = new CoatingImportDialog(System.Text.Encoding.UTF8.GetBytes(optiland), "ar.json", 600, (0.4, 0.8));
            var shown2 = d2.ShowDialog<CoatingChoice?>(w);
            Pump(d2);
            var mediaBox = Find<CheckBox>(d2, "MediaBox");
            var mediaText = (mediaBox.Content as TextBlock)?.Text ?? "";   /* a TextBlock, so "_" in a name shows */
            h.Expect(mediaBox.IsEffectivelyVisible && mediaBox.IsChecked == true && mediaText.Contains("n=1.52"),
                     $"an Optiland coating offers its media ({mediaText})");
            Click(d2, Find<Button>(d2, "OkButton"));
            Pump(w);
            if (shown2.Result is not { } opt) { h.Expect(false, "the Optiland dialog returned a coating"); return; }
            w.ApplyCoating(opt);
            Pump(w);
            h.Expect(Rows(w).Count == 2 && w.Workspace.Model.Exit.Material == "n=1.52" && w.Workspace.Model.Incident.Material == "air",
                     $"its layers and its media are the stack's ({w.Workspace.Model.Incident.Material} / {w.Workspace.Model.Exit.Material})");
            Click(w, Find<Button>(w, "UndoButton"));
            h.Expect(Rows(w).Count == 1 && Rows(w)[0].Material == "MGF2", "Undo takes the whole import back");
        });
    }

    public static void MediaShowOrientationOnlyWhenAnisotropic(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("an isotropic incident or exit medium shows no φ, θ, ψ: turning it changes nothing");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("passler-2017 / sic-otto"), "passler-2017 / sic-otto");
            Pump(w);
            h.Expect(!Find<StackPanel>(w, "IncidentAngles").IsVisible, "the KRS5 prism (isotropic) has no orientation boxes");
            h.Expect(Find<StackPanel>(w, "ExitAngles").IsVisible, "the SiC substrate (uniaxial) has them");
            w.Workspace.Edit(m => m.Exit.Material = "air");
            Pump(w);
            h.Expect(!Find<StackPanel>(w, "ExitAngles").IsVisible, "made air, the exit medium loses them");
            Click(w, Find<Button>(w, "UndoButton"));
            h.Expect(Find<StackPanel>(w, "ExitAngles").IsVisible, "and Undo brings them back with the SiC");
        });
    }

    public static void ClosingAChangedStackAsks(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("closing a changed stack asks whether to save it");
        WithWindow(session, w =>
        {
            w.Workspace.Edit(m => m.Title = "changed");
            Pump(w);
            h.Expect(w.Title!.EndsWith("*"), "the title marks the change");
            w.Close();
            Pump(w);
            var ask = w.OwnedWindows.LastOrDefault(x => x.IsVisible);
            h.Expect(w.IsVisible && ask is not null && ask.Title == "Unsaved changes", "the window stays, and asks");
            if (ask is not null) Click(ask, Find<Button>(ask, "Answer2"));   // Cancel
            Pump(w);
            h.Expect(w.IsVisible, "Cancel keeps it open");
        });
    }
}
