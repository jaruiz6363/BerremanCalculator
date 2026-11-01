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

    public static void MaterialsAreAddedEditedAndProtected(Harness h, HeadlessUnitTestSession session)
    {
        h.Section("the Materials tab adds, edits, renames, and will not remove a material in use");
        WithWindow(session, w =>
        {
            ShowTab(w, "Materials");
            Click(w, Find<Button>(w, "NewMaterialButton"));
            h.Expect(w.Workspace.Model.Material("material") is not null, "New adds a material");
            Find<TextBox>(w, "MaterialSpecBox").Text = """{ "no": 1.54, "ne": 1.55 }""";
            Click(w, Find<Button>(w, "ApplyMaterialButton"));
            h.Expect(w.Workspace.Summary("material").StartsWith("uniaxial"), $"Apply makes it uniaxial ({w.Workspace.Summary("material")})");
            Find<TextBox>(w, "MaterialNameBox").Text = "quartz";
            Click(w, Find<Button>(w, "RenameMaterialButton"));
            h.Expect(w.Workspace.Model.Material("quartz") is not null, "Rename renames it");
            Find<TextBox>(w, "MaterialSpecBox").Text = """{ "n": 1.5, "eps": 2 }""";
            Click(w, Find<Button>(w, "ApplyMaterialButton"));
            h.Expect(w.Workspace.Summary("quartz").StartsWith("uniaxial") && Find<SelectableTextBlock>(w, "StatusText").Text!.Contains("only one"),
                     "a material the parser refuses is not applied, and the status says why");
            var list = Find<ListBox>(w, "MaterialList");
            list.SelectedIndex = w.Workspace.Model.Materials.FindIndex(m => m.Name == "low");
            Pump(w);
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
