// MainWindow.axaml.cs — the stack file on the left, its results on the right.
//
// The window edits the stack file itself, as text: the file format is the program's one
// description of a stack (docs/stack-format.md), shared with the CLI and the MCP server, so what
// runs here is exactly what runs there. Every number shown comes from the API; the window parses,
// evaluates in the background, and draws.

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Berreman.Api;
using Berreman.Gui.Core;
using Berreman.Native;

namespace Berreman.Gui;

public partial class MainWindow : Window
{
    private string? _path;              // the file being edited; null for a sample or a new stack
    private string _savedText = "";
    private EvaluationResult? _result;
    private bool _busy;

    private static readonly FilePickerFileType StackFiles = new("Stack files") { Patterns = ["*.json"] };

    public MainWindow()
    {
        InitializeComponent();

        OpenButton.Click += async (_, _) => await OpenDialog();
        SaveButton.Click += async (_, _) => await Save(false);
        SaveAsButton.Click += async (_, _) => await Save(true);
        EvaluateButton.Click += async (_, _) => await Evaluate();
        ExportButton.Click += async (_, _) => await Export();

        foreach (var box in new[] { ShowR, ShowT, ShowA, ShowP, ShowS, ShowMean, ShowEllipsometry })
            box.IsCheckedChanged += (_, _) => Redraw();

        var samples = SampleCatalog.List(SampleCatalog.DefaultRoot);
        SampleBox.ItemsSource = samples.Select(s => s.Name).ToList();
        SampleBox.SelectionChanged += (_, _) =>
        {
            if (SampleBox.SelectedIndex >= 0) LoadText(File.ReadAllText(samples[SampleBox.SelectedIndex].Path), null);
        };

        Editor.TextChanged += (_, _) => UpdateTitle();

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5) { e.Handled = true; await Evaluate(); }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O) { e.Handled = true; await OpenDialog(); }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) { e.Handled = true; await Save(false); }
        };

        var engine = EngineStatus.Probe();
        if (!engine.Loaded || engine.NativeAbi != engine.ManagedAbi) Status(engine.Summary, error: true);
    }

    // ---- files ---------------------------------------------------------------------------------

    public async Task OpenPath(string path, bool evaluate = false)
    {
        try { LoadText(await File.ReadAllTextAsync(path), path); }
        catch (Exception e) { Status($"Could not open {path}: {e.Message}", error: true); return; }
        if (evaluate) await Evaluate();
    }

    private async Task OpenDialog()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a stack file", AllowMultiple = false, FileTypeFilter = [StackFiles],
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path) await OpenPath(path);
    }

    private void LoadText(string text, string? path)
    {
        _path = path;
        _savedText = text;
        Editor.Text = text;
        _result = null;
        Redraw();
        ShowDocument();
        Status(path is null ? "Sample loaded (read-only on disk: Save as… to keep a copy). Press F5 to evaluate." : $"Opened {path}. Press F5 to evaluate.");
    }

    private async Task Save(bool asNew)
    {
        if (_path is null || asNew)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the stack file", DefaultExtension = "json", FileTypeChoices = [StackFiles],
                SuggestedFileName = "stack.json",
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            _path = path;
        }
        await File.WriteAllTextAsync(_path, Editor.Text ?? "");
        _savedText = Editor.Text ?? "";
        UpdateTitle();
        Status($"Saved {_path}.");
    }

    private void UpdateTitle()
    {
        var name = _path is null ? (SampleBox.SelectedItem as string ?? "untitled") : Path.GetFileName(_path);
        var dirty = (Editor.Text ?? "") != _savedText ? " *" : "";
        Title = $"BerremanCalculator - {name}{dirty}";
    }

    private void ShowDocument()
    {
        try
        {
            var doc = StackDocument.Parse(Editor.Text ?? "");
            DocTitle.Text = doc.Title == "" ? "(untitled stack)" : doc.Title;
            DocSource.Text = doc.Source;
        }
        catch (StackFormatException)
        {
            DocTitle.Text = "Stack";
            DocSource.Text = "";
        }
        UpdateTitle();
    }

    // ---- evaluation ------------------------------------------------------------------------------

    private async Task Evaluate()
    {
        if (_busy) return;
        StackDocument doc;
        try { doc = StackDocument.Parse(Editor.Text ?? ""); }
        catch (StackFormatException e) { Status(e.Message, error: true); return; }

        ShowDocument();
        _busy = true;
        EvaluateButton.IsEnabled = false;
        Status("Evaluating…");
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
            Status(msg, error: failed > 0);
        }
        catch (StackFormatException e) { Status(e.Message, error: true); }
        catch (Exception e) { Status($"Evaluation failed: {e.Message}", error: true); }
        finally
        {
            _busy = false;
            EvaluateButton.IsEnabled = true;
        }
    }

    private void Redraw()
    {
        if (_result is null) { Plot.Series = null; return; }
        var choice = new PlotChoice(
            R: ShowR.IsChecked == true, T: ShowT.IsChecked == true, A: ShowA.IsChecked == true,
            P: ShowP.IsChecked == true, S: ShowS.IsChecked == true, Mean: ShowMean.IsChecked == true,
            Ellipsometry: ShowEllipsometry.IsChecked == true);
        var curves = ResultPlot.Build(_result, choice);
        Plot.XTitle = curves.XTitle;
        Plot.YTitle = curves.YTitle;
        Plot.EmptyText = curves.Message ?? "Nothing to plot.";
        Plot.Series = curves.Series;
    }

    private async Task Export()
    {
        if (_result is null) { Status("Evaluate first (F5), then export.", error: true); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the results as CSV", DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            SuggestedFileName = (_path is null ? "results" : Path.GetFileNameWithoutExtension(_path)) + ".csv",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, ResultTable.Build(_result, jones: true, ellipsometry: true, absorption: true).ToCsv());
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
