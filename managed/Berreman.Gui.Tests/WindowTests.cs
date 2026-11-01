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

public static class WindowTests
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
        box.Focus();
        Pump(window, 10);
        box.Text = text;
        Find<Button>(window, "EvaluateButton").Focus();
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
        var tab = Find<TabControl>(window, "MaterialTabs").Items.OfType<TabItem>().First(t => (string?)t.Header == header);
        Click(window, tab);
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
            foreach (var name in new[] { "LayerGrid", "IncidentBox", "ExitBox", "SpectralBox", "AnglesBox", "AzimuthBox",
                                         "LengthUnitBox", "SpectralUnitBox",
                                         "InsertButton", "DeleteButton", "UpButton", "DownButton", "PropertiesButton",
                                         "DesignStringButton", "UndoButton", "RedoButton", "MaterialList", "MaterialSpecBox",
                                         "Editor", "Plot", "StatusText", "SummaryText", "EvaluateButton" })
                h.Expect(w.FindControl<Control>(name) is not null, $"{name} exists");
            h.Expect(Rows(w).Count == 1 && Rows(w)[0].ThicknessText == "100", "one 100 nm layer to start");
            h.Expect(PumpUntil(w, () => !w.IsEvaluating && Find<SpectrumPlot>(w, "Plot").Series is { Count: > 0 }),
                     "and the plot fills by itself");
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
            h.Expect(w.Title?.EndsWith("quadrilayer.json") == true, $"and the title no longer says it changed ({w.Title})");
        });
    }

    public static void CellsAreEditedByTyping(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("thickness and orientation are typed into the table's cells");
        WithWindow(session, w =>
        {
            w.LoadSample(Sample("mansuripur-1990 / quadrilayer"), "q");
            Pump(w);
            var row = Find<DataGrid>(w, "LayerGrid").GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext is LayerRow { Index: 1 });
            var boxes = row.GetVisualDescendants().OfType<TextBox>().ToList();   // thickness, φ, θ, ψ
            h.Expect(boxes.Count == 4, $"a row has four typed cells ({boxes.Count})");
            if (boxes.Count != 4) return;
            TypeAndLeave(w, boxes[0], "25");
            h.Expect(w.Workspace.Model.Layers[1].Thickness == 25, "the thickness typed is the layer's");
            row = Find<DataGrid>(w, "LayerGrid").GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext is LayerRow { Index: 1 });
            boxes = row.GetVisualDescendants().OfType<TextBox>().ToList();
            TypeAndLeave(w, boxes[2], "40");
            h.Expect(w.Workspace.Model.Layers[1].EulerDeg[1] == 40, "θ typed in the row turns the layer");
            h.Expect(Rows(w)[1].AxisText.StartsWith("tilt 40°"), $"and the optic-axis column follows ({Rows(w)[1].AxisText})");
            TypeAndLeave(w, Find<DataGrid>(w, "LayerGrid").GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext is LayerRow { Index: 1 })
                               .GetVisualDescendants().OfType<TextBox>().First(), "-5");
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
            Find<DataGrid>(w, "LayerGrid").SelectedIndex = 0;
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
            var l = w.Workspace.Model.Layers[0];
            h.Expect(l.EulerDeg.SequenceEqual([135.0, 30, 0]), "OK gives the layer those angles");
            h.Expect(l.Twist is { Pitch: 300, Handedness: "right" }, "and the twist");
            h.Expect(Rows(w)[0].AxisText == "tilt 30°, az 45°" && Rows(w)[0].TwistText == "300 nm right", $"which the table shows ({Rows(w)[0].AxisText}; {Rows(w)[0].TwistText})");
            Click(w, Find<Button>(w, "UndoButton"));
            h.Expect(w.Workspace.Model.Layers[0].Twist is null, "and one Undo takes it all back");
        });
    }

    public static void ADesignStringBuildsLayers(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("Design string… builds the layers from (HL)n");
        WithWindow(session, w =>
        {
            w.Workspace.Edit(m => m.Materials.Add(new NamedMaterial("TiO2", new System.Text.Json.Nodes.JsonObject { ["n"] = 2.35 })));
            Pump(w);
            Click(w, Find<Button>(w, "DesignStringButton"));
            var d = Dialog<DesignStringDialog>(w);
            h.Expect(d is not null, "the dialog opens");
            if (d is null) return;
            Find<TextBox>(d, "StringBox").Text = "(HL)5 H";
            Pump(d);
            Find<TextBox>(d, "ThicknessH").Text = "58.5";
            Find<TextBox>(d, "ThicknessL").Text = "120";
            Find<ComboBox>(d, "ReplaceBox").SelectedIndex = 0;
            Click(d, Find<Button>(d, "OkButton"));
            Pump(w);
            var layers = w.Workspace.Model.Layers;
            h.Expect(layers.Count == 11 && layers[0].Material == "TiO2" && layers[1].Material == "air",
                     $"eleven layers, H guessed as TiO2 and L as the lowest index ({string.Join(" ", layers.Select(l => l.Material))})");
            h.Expect(layers.Where(l => l.Material == "TiO2").All(l => l.Thickness == 58.5) && layers.Where(l => l.Material == "air").All(l => l.Thickness == 120),
                     "each with the thickness given for its letter");
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

            /* Simple: air is n = 1. */
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "air");
            Pump(w);
            h.Expect(Find<TabControl>(w, "MaterialTabs").SelectedIndex == 0 && Find<TextBox>(w, "SimpleN0").Text == "1", $"a constant material opens on Simple, n = 1 (tab {Find<TabControl>(w, "MaterialTabs").SelectedIndex}, n {Find<TextBox>(w, "SimpleN0").Text}, selected {list.SelectedIndex})");
            Find<TextBox>(w, "SimpleN0").Text = "1.0003";
            Click(w, Find<Button>(w, "ApplySimpleButton"));
            h.Expect(w.Workspace.Model.Material("air")!.Spec["n"]!.GetValue<double>() == 1.0003, "Apply writes n");

            /* Terms: Ga2O3 opens on its 16 terms. */
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "Ga2O3");
            Pump(w);
            h.Expect(Find<TabControl>(w, "MaterialTabs").SelectedIndex == 1, "a material written as terms opens on Terms");
            var terms = Find<ListBox>(w, "TermList");
            h.Expect(terms.ItemCount == 16, $"its 16 terms are listed ({terms.ItemCount})");
            terms.SelectedIndex = 1;
            Pump(w);
            h.Expect(Find<ComboBox>(w, "TermLawBox").SelectedItem as string == "lorentz" && Find<TextBox>(w, "AxisAngleBox").Text == "47.8",
                     "Bu-1 shows its law and its direction");
            var center = Find<TextBox>(w, "Law_center");
            h.Expect(center.Text == "743.48", $"its centre is a field ({center.Text})");
            center.Text = "750";
            Click(w, Find<Button>(w, "ApplyTermButton"));
            JsonNode Term(int i) => w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]![i]!;
            h.Expect(Term(1)["lorentz"]!["center"]!.GetValue<double>() == 750 && Term(1)["axis_deg"]!.GetValue<double>() == 47.8,
                     "Apply term changes the centre and keeps the direction");
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
            Find<TextBox>(w, "Shape01").Text = "0.1i";
            Find<TextBox>(w, "Shape10").Text = "-0.1i";
            Click(w, Find<Button>(w, "ApplyTermButton"));
            h.Expect(Term(1)["eps"] is JsonArray, $"a typed matrix is written as an ε block ({Find<SelectableTextBlock>(w, "StatusText").Text}; {Term(1).ToJsonString()})");
            h.Expect(Find<TextBlock>(w, "MaterialSummary").Text!.Contains("16 terms"), "the summary counts the terms");

            /* Add and delete. */
            int before = w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]!.AsArray().Count;
            Click(w, Find<Button>(w, "AddTermButton"));
            h.Expect(w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]!.AsArray().Count == before + 1, "Add adds a term");
            Click(w, Find<Button>(w, "DeleteTermButton"));
            h.Expect(w.Workspace.Model.Material("Ga2O3")!.Spec["terms"]!.AsArray().Count == before, "Delete removes it");

            /* A short-form material is shown as terms and written as terms on Apply. */
            w.Workspace.Edit(m => m.Materials.Add(new NamedMaterial("SiO2", new JsonObject
            {
                ["sellmeier"] = JsonNode.Parse("""{ "unit": "um", "terms": [ { "b": 0.6961663, "c2": 0.0046791 } ] }"""),
            })));
            Pump(w);
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "SiO2");
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
            Find<TextBox>(w, "MaterialSpecBox").Text = """{ "no": 1.54, "ne": 1.55 }""";
            Click(w, Find<Button>(w, "ApplyMaterialButton"));
            h.Expect(w.Workspace.Summary("material").StartsWith("uniaxial"), $"Apply makes it uniaxial ({w.Workspace.Summary("material")})");
            Find<TextBox>(w, "MaterialNameBox").Text = "quartz";
            Click(w, Find<Button>(w, "RenameMaterialButton"));
            h.Expect(w.Workspace.Model.Material("quartz") is not null, "Rename renames it");
            Settle(w);
            ShowMaterialTab(w, "Text");
            Find<TextBox>(w, "MaterialSpecBox").Text = """{ "n": 1.5, "eps": 2 }""";
            Click(w, Find<Button>(w, "ApplyMaterialButton"));
            h.Expect(w.Workspace.Summary("quartz").StartsWith("uniaxial") && Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("only one"),
                     "a material the parser refuses is not applied, and the status says why");
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
            h.Expect(Find<TextBox>(w, "SpectralBox").Text == "0.4 to 0.8 count 201",
                     $"nm to µm converts the sweep ({Find<TextBox>(w, "SpectralBox").Text})");
            Find<ComboBox>(w, "LengthUnitBox").SelectedItem = "µm";
            Pump(w);
            h.Expect(Rows(w)[0].ThicknessText == "0.1", $"nm to µm converts the thicknesses ({Rows(w)[0].ThicknessText})");
            ShowTab(w, "File");
            var editor = Find<TextBox>(w, "Editor");
            h.Expect(editor.Text!.Contains("\"spectral\": \"um\""), "the File tab shows the stack file");
            editor.Text = editor.Text.Replace("\"title\": \"New stack\"", "\"title\": \"Edited as text\"");
            Click(w, Find<Button>(w, "ApplyTextButton"));
            h.Expect(w.Workspace.Model.Title == "Edited as text", "and Apply takes an edit made there");
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
