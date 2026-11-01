// MainWindow.axaml.cs — the stack being edited on the left (its layers, media, materials and the
// file itself), its results on the right.
//
// The window holds no stack of its own: StackWorkspace (Gui.Core, tested) does, and every control
// here either shows the workspace or calls one of its edits. After any change the workspace raises
// Changed and Refresh() redraws everything from it, so the tables, the file text and the title can
// never disagree. A refused edit puts the stack back and says why in the status line.
//
// Every number shown comes from the API; the window parses, evaluates in the background, and draws.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Berreman.Api;
using Berreman.Gui.Core;
using Berreman.Native;

namespace Berreman.Gui;

public partial class MainWindow : Window
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly FilePickerFileType StackFiles = new("Stack files") { Patterns = ["*.json"] };

    private readonly StackWorkspace _ws = new();
    private EvaluationResult? _result;
    private bool _busy, _again, _refreshing, _closeConfirmed;
    private readonly DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string? _selectedMaterial;

    /// <summary>The stack being edited; the headless tests read it.</summary>
    public StackWorkspace Workspace => _ws;
    /// <summary>False to close without asking about unsaved changes (the tests).</summary>
    public bool AskToSave { get; set; } = true;
    /// <summary>True while an evaluation is running or about to start.</summary>
    public bool IsEvaluating => _busy || _again || _autoTimer.IsEnabled;

    public MainWindow()
    {
        InitializeComponent();

        // ---- menus and buttons -----------------------------------------------------------------
        NewItem.Click += async (_, _) => { if (await ConfirmDiscard()) { _ws.New(); Status("New stack. Edit the layers, materials and sweep; it evaluates as you go."); } };
        OpenItem.Click += async (_, _) => await OpenDialog();
        SaveItem.Click += async (_, _) => await Save(false);
        SaveAsItem.Click += async (_, _) => await Save(true);
        ExportItem.Click += async (_, _) => await Export();
        ExportPlotItem.Click += async (_, _) => await ExportPlot();
        ImportTfdItem.Click += async (_, _) => await ImportTfd();
        ExportReportItem.Click += async (_, _) => await ExportReport();
        ExportTfdItem.Click += async (_, _) => await ExportTfd();
        ExitItem.Click += (_, _) => Close();
        UndoItem.Click += (_, _) => _ws.Undo();
        RedoItem.Click += (_, _) => _ws.Redo();
        UndoButton.Click += (_, _) => _ws.Undo();
        RedoButton.Click += (_, _) => _ws.Redo();
        EvaluateItem.Click += async (_, _) => await Evaluate();
        EvaluateButton.Click += async (_, _) => await Evaluate();
        AutoItem.Click += (_, _) => { if (AutoItem.IsChecked) ScheduleEvaluate(); };

        InsertItem.Click += (_, _) => InsertLayer();
        InsertButton.Click += (_, _) => InsertLayer();
        DeleteItem.Click += (_, _) => DeleteLayers();
        DeleteButton.Click += (_, _) => DeleteLayers();
        UpItem.Click += (_, _) => MoveLayer(-1);
        UpButton.Click += (_, _) => MoveLayer(-1);
        DownItem.Click += (_, _) => MoveLayer(+1);
        DownButton.Click += (_, _) => MoveLayer(+1);
        PropertiesItem.Click += async (_, _) => await LayerProperties(SelectedRow);
        PropertiesButton.Click += async (_, _) => await LayerProperties(SelectedRow);
        DesignStringItem.Click += async (_, _) => await InputDesignString();
        DesignStringButton.Click += async (_, _) => await InputDesignString();
        ReverseItem.Click += (_, _) => Try(() => _ws.Edit(m => m.ReverseLayers(withMedia: false)), "Layers reversed.");
        ReverseMediaItem.Click += (_, _) => Try(() => _ws.Edit(m => m.ReverseLayers(withMedia: true)), "Layers and media reversed: the light now arrives from the other side.");
        DeleteAllItem.Click += async (_, _) =>
        {
            if (_ws.Model.Layers.Count == 0) return;
            if (await Dialogs.Ask(this, "Delete all layers", $"Delete all {_ws.Model.Layers.Count} layers? Undo (Ctrl+Z) brings them back.", "Delete all", "Cancel") == 0)
                Try(() => _ws.Edit(m => m.Layers.Clear()), "All layers deleted.");
        };

        var samples = SampleCatalog.List(SampleCatalog.DefaultRoot);
        SamplesItem.ItemsSource = samples.Select(s =>
        {
            var item = new MenuItem { Header = s.Name };
            item.Click += async (_, _) => await OpenSample(s);
            return item;
        }).ToList();
        SamplesItem.IsEnabled = samples.Count > 0;
        BrowseSamplesItem.IsEnabled = samples.Count > 0;
        BrowseSamplesItem.Click += async (_, _) => await BrowseSamples();

        // ---- results ---------------------------------------------------------------------------
        foreach (var box in new[] { ShowR, ShowT, ShowA, ShowP, ShowS, ShowMean, ShowEllipsometry })
            box.IsCheckedChanged += (_, _) => Redraw();
        _autoTimer.Tick += async (_, _) => { _autoTimer.Stop(); await Evaluate(auto: true); };

        // ---- the stack tab ---------------------------------------------------------------------
        LengthUnitBox.ItemsSource = StackModel.LengthUnits.Select(u => u == "um" ? "µm" : u).ToList();
        SpectralUnitBox.ItemsSource = StackModel.SpectralUnits;
        AngleModeBox.ItemsSource = new[] { "Angle (°)", "ξ = kx/k₀" };

        LengthUnitBox.SelectionChanged += (_, _) => { if (!_refreshing && LengthUnitBox.SelectedItem is string u) Try(() => _ws.SetLengthUnit(u == "µm" ? "um" : u)); };
        SpectralUnitBox.SelectionChanged += (_, _) => { if (!_refreshing && SpectralUnitBox.SelectedItem is string u) Try(() => _ws.SetSpectralUnit(u)); };
        AngleModeBox.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            bool xi = AngleModeBox.SelectedIndex == 1;
            Try(() => _ws.Edit(m =>
            {
                if (m.AnglesAreXi == xi) return;
                // Converting needs the incident index at each wavelength; start the other way from 0.
                m.AnglesAreXi = xi;
                m.Angles = "0";
            }), xi ? "The sweep now gives ξ = kx/k₀ directly (needed when the incident medium is anisotropic or lossy)." : "The sweep now gives angles of incidence.");
        };

        Commit(TitleBox, t => _ws.Edit(m => m.Title = t));
        Commit(SourceBox, t => _ws.Edit(m => m.Source = t));
        Commit(NotesBox, t => _ws.Edit(m => m.Notes = t), enterCommits: false);
        Commit(SpectralBox, t => _ws.Edit(m => m.Spectral = t));
        Commit(AnglesBox, t => _ws.Edit(m => m.Angles = t));
        Commit(AzimuthBox, t => _ws.Edit(m => m.Azimuths = t.Trim() == "0" ? "" : t));

        IncidentBox.SelectionChanged += (_, _) => { if (!_refreshing && IncidentBox.SelectedItem is string s) Try(() => _ws.Edit(m => m.Incident.Material = s)); };
        ExitBox.SelectionChanged += (_, _) => { if (!_refreshing && ExitBox.SelectedItem is string s) Try(() => _ws.Edit(m => m.Exit.Material = s)); };
        var angleNames = new[] { "φ", "θ", "ψ" };
        foreach (var (box, k) in new[] { (IncidentPhi, 0), (IncidentTheta, 1), (IncidentPsi, 2) })
            Commit(box, t => _ws.Edit(m => m.Incident.EulerDeg[k] = StackWorkspace_Parse(t, $"incident medium {angleNames[k]}")));
        foreach (var (box, k) in new[] { (ExitPhi, 0), (ExitTheta, 1), (ExitPsi, 2) })
            Commit(box, t => _ws.Edit(m => m.Exit.EulerDeg[k] = StackWorkspace_Parse(t, $"exit medium {angleNames[k]}")));

        /* LayerRow.Error is static: a window that closed must stop listening, or a stale handler
           runs ahead of this one (and keeps the closed window alive). */
        Action<string> rowError = message => Status(message, error: true);
        LayerRow.Error += rowError;
        Closed += (_, _) => LayerRow.Error -= rowError;
        LayerGrid.AddHandler(Button.ClickEvent, async (_, e) =>
        {
            if (e.Source is Button { Classes: var c, DataContext: LayerRow row } && c.Contains("layerProperties"))
                await LayerProperties(row.Index);
        });
        LayerGrid.DoubleTapped += async (_, e) =>
        {
            if (e.Source is Avalonia.Visual v && v.FindAncestorOfType<TextBox>() is null && v.FindAncestorOfType<ComboBox>() is null && SelectedRow >= 0)
                await LayerProperties(SelectedRow);
        };

        // ---- the materials tab -----------------------------------------------------------------
        MaterialList.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selectedMaterial = (MaterialList.SelectedItem as MaterialItem)?.Name;
            ShowMaterial();
        };
        NewMaterialButton.Click += (_, _) =>
        {
            var name = _ws.Model.UniqueName("material");
            Try(() => _ws.Edit(m => m.Materials.Add(new NamedMaterial(name, new JsonObject { ["n"] = 1.5 }))), $"Added '{name}', n = 1.5. Rename it and give its constants.");
            _selectedMaterial = name;
            ShowMaterials();
        };
        DuplicateMaterialButton.Click += (_, _) =>
        {
            if (_selectedMaterial is not { } from || _ws.Model.Material(from) is not { } src) return;
            var name = _ws.Model.UniqueName(from);
            Try(() => _ws.Edit(m => m.Materials.Add(new NamedMaterial(name, (JsonObject)src.Spec.DeepClone()))), $"'{from}' copied as '{name}'.");
            _selectedMaterial = name;
            ShowMaterials();
        };
        RemoveMaterialButton.Click += (_, _) =>
        {
            if (_selectedMaterial is not { } name) return;
            Try(() => _ws.Edit(m => m.RemoveMaterial(name)), $"'{name}' removed.");
        };
        RenameMaterialButton.Click += (_, _) =>
        {
            if (_selectedMaterial is not { } name) return;
            var to = (MaterialNameBox.Text ?? "").Trim();
            Try(() => _ws.Edit(m => m.RenameMaterial(name, to)), $"'{name}' renamed '{to}', everywhere it is used.");
            if (_ws.Model.Material(to) is not null) { _selectedMaterial = to; ShowMaterials(); }
        };
        ApplyMaterialButton.Click += (_, _) =>
        {
            if (_selectedMaterial is not { } name) return;
            Try(() =>
            {
                JsonObject spec;
                try { spec = JsonNode.Parse(MaterialSpecBox.Text ?? "", documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? throw new StackFormatException("a material is a JSON object { … }"); }
                catch (System.Text.Json.JsonException e) { throw new StackFormatException($"not valid JSON: {e.Message}"); }
                MaterialParser.Parse(spec, $"materials.{name}");   // refuse what would not evaluate
                _ws.Edit(m => m.Material(name)!.Spec = spec);
            }, $"'{name}' updated.");
        };
        RevertMaterialButton.Click += (_, _) => ShowMaterial();
        InitMaterialEditor();
        InitResults();

        // ---- the file tab ----------------------------------------------------------------------
        ApplyTextButton.Click += (_, _) => Try(() => _ws.EditText(Editor.Text ?? ""), "The file text is applied.");
        RevertTextButton.Click += (_, _) => Editor.Text = _ws.Text;
        Tabs.SelectionChanged += (_, e) => { if (e.Source == Tabs) Editor.Text = _ws.Text; };

        // ---- keys ------------------------------------------------------------------------------
        KeyDown += async (_, e) =>
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool inText = FocusManager?.GetFocusedElement() is TextBox;
            if (e.Key == Key.F5) { e.Handled = true; await Evaluate(); }
            else if (ctrl && e.Key == Key.N) { e.Handled = true; NewItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
            else if (ctrl && e.Key == Key.O) { e.Handled = true; await OpenDialog(); }
            else if (ctrl && e.Key == Key.S) { e.Handled = true; await Save(shift); }
            else if (ctrl && !inText && e.Key == Key.Z) { e.Handled = true; _ws.Undo(); }
            else if (ctrl && !inText && e.Key == Key.Y) { e.Handled = true; _ws.Redo(); }
            else if (!inText && e.Key == Key.Insert) { e.Handled = true; InsertLayer(); }
        };

        Closing += async (_, e) =>
        {
            if (_closeConfirmed || !AskToSave || !_ws.IsDirty) return;
            e.Cancel = true;
            if (await ConfirmDiscard()) { _closeConfirmed = true; Close(); }
        };

        _ws.Changed += OnChanged;
        Refresh();

        var engine = EngineStatus.Probe();
        if (!engine.Loaded || engine.NativeAbi != engine.ManagedAbi) Status(engine.Summary, error: true);
        else Status("A new stack: air, one 100 nm layer, glass. Open a sample (File) or edit this one; it evaluates as you go.");
        ScheduleEvaluate();
    }

    private static double StackWorkspace_Parse(string text, string what) =>
        double.TryParse(text.Trim().Replace("°", ""), NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v
            : throw new StackFormatException($"{what}: '{text}' is not a number");

    // ---- refreshing from the workspace ---------------------------------------------------------

    private void OnChanged()
    {
        _result = null;
        Refresh();
        ScheduleEvaluate();
    }

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var m = _ws.Model;
            Title = $"BerremanCalculator - {_ws.Name}{(_ws.IsDirty ? " *" : "")}";
            SetText(TitleBox, m.Title);
            SetText(SourceBox, m.Source);
            SetText(NotesBox, m.Notes);
            LengthUnitBox.SelectedItem = m.LengthUnit == "um" ? "µm" : m.LengthUnit;
            SpectralUnitBox.SelectedItem = m.SpectralUnit;
            SpectralUnitLabel.Text = m.SpectralUnit;
            AngleModeBox.SelectedIndex = m.AnglesAreXi ? 1 : 0;
            AngleUnitLabel.Text = m.AnglesAreXi ? "" : "°";
            SetText(SpectralBox, m.Spectral);
            SetText(AnglesBox, m.Angles);
            SetText(AzimuthBox, m.Azimuths);

            var names = _ws.MaterialNames;
            IncidentBox.ItemsSource = names;
            IncidentBox.SelectedItem = m.Incident.Material;
            ExitBox.ItemsSource = names;
            ExitBox.SelectedItem = m.Exit.Material;
            SetText(IncidentPhi, Angle(m.Incident.EulerDeg[0])); SetText(IncidentTheta, Angle(m.Incident.EulerDeg[1])); SetText(IncidentPsi, Angle(m.Incident.EulerDeg[2]));
            SetText(ExitPhi, Angle(m.Exit.EulerDeg[0])); SetText(ExitTheta, Angle(m.Exit.EulerDeg[1])); SetText(ExitPsi, Angle(m.Exit.EulerDeg[2]));
            IncidentInfo.Text = _ws.Summary(m.Incident.Material);
            ExitInfo.Text = _ws.Summary(m.Exit.Material);

            var selected = LayerGrid.SelectedIndex;
            LayerGrid.Columns[2].Header = $"Thickness ({(m.LengthUnit == "um" ? "µm" : m.LengthUnit)})";
            LayerGrid.ItemsSource = _ws.Rows();
            if (selected >= 0 && selected < m.Layers.Count) LayerGrid.SelectedIndex = selected;

            UndoItem.IsEnabled = UndoButton.IsEnabled = _ws.CanUndo;
            RedoItem.IsEnabled = RedoButton.IsEnabled = _ws.CanRedo;
            SummaryText.Text = $"{m.Layers.Count} layer{(m.Layers.Count == 1 ? "" : "s")}, {_ws.TotalThickness.ToString("0.###", Inv)} {(m.LengthUnit == "um" ? "µm" : m.LengthUnit)} in all";

            if (!Editor.IsFocused) Editor.Text = _ws.Text;
            ShowMaterials();
        }
        finally { _refreshing = false; }
    }

    private static string Angle(double v) => v.ToString("0.####", Inv);

    // Sets a box's text unless the user is typing in it.
    private static void SetText(TextBox box, string text)
    {
        if (!box.IsFocused || box.Text is null) box.Text = text;
    }


    private void ShowMaterials()
    {
        bool was = _refreshing;
        _refreshing = true;
        var items = _ws.Model.Materials.Select(x => new MaterialItem(x.Name, _ws.Summary(x.Name))).ToList();
        MaterialList.ItemsSource = items;
        _selectedMaterial ??= items.FirstOrDefault()?.Name;
        if (_selectedMaterial is not null && _ws.Model.Material(_selectedMaterial) is null) _selectedMaterial = items.FirstOrDefault()?.Name;
        MaterialList.SelectedItem = items.FirstOrDefault(i => i.Name == _selectedMaterial);
        _refreshing = was;
        ShowMaterial();
    }

    private void ShowMaterial()
    {
        var mat = _selectedMaterial is null ? null : _ws.Model.Material(_selectedMaterial);
        MaterialNameBox.Text = mat?.Name ?? "";
        MaterialSummary.Text = mat is null ? "" : _ws.Summary(mat.Name);
        var uses = mat is null ? [] : _ws.Model.Uses(mat.Name);
        MaterialUses.Text = mat is null ? "" : uses.Count == 0 ? "Not used in the stack." : $"Used by the {string.Join(", ", uses.Take(8))}{(uses.Count > 8 ? $" and {uses.Count - 8} more" : "")}.";
        MaterialSpecBox.Text = mat is null ? "" : JsonText.Write(mat.Spec).TrimEnd();
        foreach (var c in new Control[] { RenameMaterialButton, ApplyMaterialButton, RevertMaterialButton, DuplicateMaterialButton, RemoveMaterialButton, MaterialSpecBox, MaterialNameBox })
            c.IsEnabled = mat is not null;
        ShowMaterialViews();
    }

    // ---- editing helpers -----------------------------------------------------------------------

    /// <summary>A text box that edits the stack when it is left, or when Enter is pressed in it.</summary>
    private void Commit(TextBox box, Action<string> apply, bool enterCommits = true)
    {
        string? before = null;
        box.GotFocus += (_, _) => before = box.Text;
        void Done()
        {
            if (box.Text == before) return;
            before = box.Text;
            Try(() => apply(box.Text ?? ""));
            if (!box.IsFocused) Refresh();
        }
        box.LostFocus += (_, _) => { Done(); Refresh(); };
        if (enterCommits)
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Done(); } };
    }

    private void Try(Action edit, string? done = null)
    {
        try
        {
            edit();
            if (done is not null) Status(done);
        }
        catch (StackFormatException e) { Status(e.Message, error: true); }
        catch (FormatException e) { Status(e.Message, error: true); }
    }

    private int SelectedRow => LayerGrid.SelectedIndex;

    private void InsertLayer()
    {
        int at = -2;
        Try(() => at = _ws.InsertLayer(SelectedRow >= 0 ? SelectedRow : _ws.Model.Layers.Count - 1));
        if (at >= 0) { LayerGrid.SelectedIndex = at; Status($"Layer {at + 1} inserted."); }
    }

    private void DeleteLayers()
    {
        var rows = LayerGrid.SelectedItems.OfType<LayerRow>().Select(r => r.Index).ToList();
        if (rows.Count == 0) { Status("Select the layers to delete.", error: true); return; }
        Try(() => _ws.DeleteLayers(rows), rows.Count == 1 ? $"Layer {rows[0] + 1} deleted." : $"{rows.Count} layers deleted.");
    }

    private void MoveLayer(int direction)
    {
        if (SelectedRow < 0) return;
        int to = _ws.MoveLayer(SelectedRow, direction);
        LayerGrid.SelectedIndex = to;
    }

    private async Task LayerProperties(int row)
    {
        if (row < 0 || row >= _ws.Model.Layers.Count) { Status("Select a layer first.", error: true); return; }
        var dialog = new LayerPropertiesDialog(_ws.Model, row);
        if (await dialog.ShowDialog<LayerModel?>(this) is { } layer)
            Try(() => _ws.Edit(m => m.Layers[row] = layer), $"Layer {row + 1} updated.");
    }

    private async Task InputDesignString()
    {
        var dialog = new DesignStringDialog(_ws.Model, SelectedRow);
        if (await dialog.ShowDialog<DesignStringResult?>(this) is not { } r) return;
        Try(() => _ws.Edit(m =>
        {
            if (r.Replace) m.Layers.Clear();
            int at = r.Replace ? 0 : (SelectedRow >= 0 ? SelectedRow + 1 : m.Layers.Count);
            m.Layers.InsertRange(at, r.Layers);
        }), $"{r.Layers.Count} layers from the design string {(r.Replace ? "replace the stack" : "inserted")}.");
    }

    // ---- files ---------------------------------------------------------------------------------

    public async Task OpenPath(string path, bool evaluate = false)
    {
        if (!await ConfirmDiscard()) return;
        try { _ws.Load(await File.ReadAllTextAsync(path), path); }
        catch (Exception e) when (e is StackFormatException or IOException or UnauthorizedAccessException)
        { Status($"Could not open {path}: {e.Message}", error: true); return; }
        Status($"Opened {path}.");
        if (evaluate) await Evaluate();
    }

    /// <summary>File > Browse samples; the chosen one opens as a copy.</summary>
    public async Task BrowseSamples()
    {
        var infos = new List<SampleInfo>();
        foreach (var s in SampleCatalog.List(SampleCatalog.DefaultRoot))
        {
            try { infos.Add(SampleCatalog.Describe(s)); }
            catch (Exception e) when (e is StackFormatException or IOException) { }
        }
        var chosen = await new SampleBrowser(infos).ShowDialog<SampleInfo?>(this);
        if (chosen is not null) await OpenSample(chosen.Entry);
    }

    private async Task OpenSample(SampleEntry s)
    {
        if (!await ConfirmDiscard()) return;
        try { LoadSample(await File.ReadAllTextAsync(s.Path), s.Name); }
        catch (Exception e) when (e is StackFormatException or IOException) { Status($"Could not open the sample: {e.Message}", error: true); }
    }

    private static readonly FilePickerFileType TfdFiles = new("ThinFilmDesigner designs") { Patterns = ["*.tfd"] };

    private async Task ImportTfd()
    {
        if (!await ConfirmDiscard()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import a ThinFilmDesigner design", FileTypeFilter = [TfdFiles] });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        Try(() => ImportTfd(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path)));
    }

    /// <summary>A .tfd read as a new, unsaved stack (Save as… keeps it); the tests call it directly.</summary>
    public void ImportTfd(string text, string name)
    {
        var model = TfdFile.Read(text);
        _ws.Load(model.ToJson(), null, name + ".json");
        Status($"'{name}' read from ThinFilmDesigner: {model.Layers.Count} layers. The merit function is not carried over; Save as… keeps the stack.");
    }

    private async Task ExportReport()
    {
        string text;
        try { var (f, t, s) = DesignReport.DefaultRange(_ws.Model); text = DesignReport.Build(_ws.Model, f, t, s); }
        catch (StackFormatException e) { Status($"No report: {e.Message}", error: true); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export a design report", DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
            SuggestedFileName = Path.GetFileNameWithoutExtension(_ws.Name) + "-report.txt",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, text);
        Status($"The report is written to {path}: the layers, and every material over the sweep's range.");
    }

    private async Task ExportTfd()
    {
        string text;
        try { text = TfdFile.Write(_ws.Model); }
        catch (StackFormatException e) { Status($"Not written: {e.Message}", error: true); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export as a ThinFilmDesigner design", DefaultExtension = "tfd", FileTypeChoices = [TfdFiles],
            SuggestedFileName = Path.GetFileNameWithoutExtension(_ws.Name) + ".tfd",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, text);
        Status($"Written to {path}; ThinFilmDesigner opens it with no merit function.");
    }

    /// <summary>A sample opened as a copy, with no file of its own: Save asks where.</summary>
    public void LoadSample(string json, string name)
    {
        _ws.Load(json, null, name.Replace(" / ", "-") + ".json");
        Status($"Sample {name}: a copy; Save as… keeps it.");
    }

    private async Task OpenDialog()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a stack file", AllowMultiple = false, FileTypeFilter = [StackFiles],
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path) await OpenPath(path);
    }

    private async Task<bool> Save(bool asNew)
    {
        var path = _ws.Path;
        if (path is null || asNew)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the stack file", DefaultExtension = "json", FileTypeChoices = [StackFiles],
                SuggestedFileName = _ws.Name.EndsWith(".json") ? _ws.Name : "stack.json",
            });
            if (file?.TryGetLocalPath() is not { } chosen) return false;
            path = chosen;
        }
        try { await File.WriteAllTextAsync(path, _ws.Text); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status($"Could not save: {e.Message}", error: true); return false; }
        _ws.MarkSaved(path);
        Status($"Saved {path}.");
        return true;
    }

    /// <summary>True when there is nothing unsaved, or the user saved it or chose to discard it.</summary>
    private async Task<bool> ConfirmDiscard()
    {
        if (!_ws.IsDirty) return true;
        int choice = await Dialogs.Ask(this, "Unsaved changes", $"Save the changes to {_ws.Name}?", "Save", "Don't save", "Cancel");
        return choice switch
        {
            0 => await Save(false),
            1 => true,
            _ => false,
        };
    }

    // ---- evaluation ------------------------------------------------------------------------------

    private void ScheduleEvaluate()
    {
        if (!AutoItem.IsChecked) return;
        _autoTimer.Stop();
        _autoTimer.Start();
    }

    private async Task Evaluate(bool auto = false)
    {
        if (_busy) { _again = true; return; }
        StackDocument doc;
        try { doc = _ws.Document(); }
        catch (StackFormatException e)
        {
            Status(auto ? $"Not evaluated yet: {e.Message}" : e.Message, error: true);
            _result = null;
            Redraw();
            return;
        }

        _busy = true;
        EvaluateButton.IsEnabled = false;
        if (!auto) Status("Evaluating…");
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() => Evaluator.Evaluate(doc));
            _result = result;
            Redraw();
            int failed = result.Points.Count(p => p.Status != BcStatus.Ok);
            var msg = $"{result.Points.Count} points in {clock.Elapsed.TotalSeconds:F2} s.";
            if (failed > 0)
            {
                var first = result.Points.First(p => p.Status != BcStatus.Ok);
                msg += $" {failed} failed (first: {first.Status}); they show as gaps.";
            }
            if (result.SublayerChange is { } dc) msg += $" Sublayers: doubling them changes R, T and A by at most {dc:G3}.";
            /* A clean evaluation reports beside the summary, so it never hides what the status line
               says about the last edit; a failure is the status. */
            EvaluationText.Text = msg;
            ToolTip.SetTip(EvaluationText, msg);
            if (failed > 0) Status(msg, error: true);
            else if (!auto) Status("Evaluated.");
        }
        catch (StackFormatException e) { Status(e.Message, error: true); }
        catch (Exception e) { Status($"Evaluation failed: {e.Message}", error: true); }
        finally
        {
            _busy = false;
            EvaluateButton.IsEnabled = true;
        }
        if (_again) { _again = false; await Evaluate(auto: true); }
    }


    private async Task Export()
    {
        if (_result is null) { Status("Evaluate first (F5), then export.", error: true); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the results as CSV", DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            SuggestedFileName = Path.GetFileNameWithoutExtension(_ws.Name) + ".csv",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, ResultTable.Build(_result, jones: true, ellipsometry: true, absorption: true, quantities: _moreQuantities).ToCsv());
        Status($"{_result.Points.Count} points written to {path}.");
    }

    private void Status(string text, bool error = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = text;
            StatusText.Foreground = error ? Avalonia.Media.Brushes.Firebrick : Avalonia.Media.Brushes.Black;
        });
    }
}

/// <summary>One entry of the materials list: its name, and a few words about it.</summary>
public sealed record MaterialItem(string Name, string Summary)
{
    public override string ToString() => $"{Name}  —  {Summary}";
}
