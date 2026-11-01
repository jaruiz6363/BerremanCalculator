// MainWindow.Results.cs — the results pane: which quantities, against which swept variable, as
// curves or as the Mueller matrix's sixteen panels. What to draw is decided in Gui.Core
// (ResultPlot, tested); this file only wires the controls to it.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Gui;

public partial class MainWindow
{
    private List<string> _moreQuantities = [];
    private readonly SpectrumPlot[] _muellerPlots = new SpectrumPlot[16];
    private List<XAxis> _axisChoices = [];
    private bool _settingAxes;

    /// <summary>The quantities chosen with More…, by name; the tests set it.</summary>
    public List<string> MoreQuantities
    {
        get => _moreQuantities;
        set { _moreQuantities = value; ShowMoreQuantities(); Redraw(); }
    }

    private void InitResults()
    {
        for (int k = 0; k < 16; ++k)
        {
            _muellerPlots[k] = new SpectrumPlot { Compact = true, Title = k == 0 ? "M11" : $"M{k / 4 + 1}{k % 4 + 1}", Margin = new Thickness(1), Name = $"Mueller{k / 4 + 1}{k % 4 + 1}" };
            MuellerGrid.Children.Add(_muellerPlots[k]);
        }
        ViewBox.SelectionChanged += (_, _) => Redraw();
        ConoscopeItem.Click += (_, _) => OpenConoscope();
        FieldProfileItem.Click += (_, _) => OpenFieldProfile();
        XAxisBox.SelectionChanged += (_, _) => { if (!_settingAxes) Redraw(); };
        MoreQuantitiesButton.Click += async (_, _) =>
        {
            var chosen = await new QuantityDialog(_moreQuantities, OuterWaveNames()).ShowDialog<List<string>?>(this);
            if (chosen is not null) MoreQuantities = chosen;
        };
        ShowMoreQuantities();
    }

    private (string A, string B) OuterWaveNames()
    {
        if (_result is null) return ("p", "s");
        var doc = _result.Document;
        double l = _result.Points.Count > 0 ? _result.Points[0].WavelengthNm : 500;
        try
        {
            bool iso = doc.Materials[doc.Incident.Material].At(l, 0).IsIsotropic(out _, out _)
                    && doc.Materials[doc.Exit.Material].At(l, 0).IsIsotropic(out _, out _);
            return iso ? ("p", "s") : ("0", "1");
        }
        catch (StackFormatException) { return ("p", "s"); }
    }

    private void ShowMoreQuantities() =>
        MoreQuantitiesText.Text = _moreQuantities.Count == 0 ? "" : $"Also plotted: {string.Join(", ", _moreQuantities)}";

    /// <summary>The x-axis choices for the sweep evaluated: only the variables it varies.</summary>
    private XAxis? ChosenAxis()
    {
        if (_result is null) return null;
        var axes = ResultPlot.Axes(_result.Document).ToList();
        if (!axes.SequenceEqual(_axisChoices))
        {
            var keep = XAxisBox.SelectedIndex >= 0 && XAxisBox.SelectedIndex < _axisChoices.Count ? _axisChoices[XAxisBox.SelectedIndex] : (XAxis?)null;
            _settingAxes = true;
            _axisChoices = axes;
            XAxisBox.ItemsSource = axes.Select(a => a switch
            {
                XAxis.Spectral => ResultPlot.SpectralTitle(_result.Document.SpectralUnit),
                XAxis.Incidence => _result.Document.AnglesDeg is null ? "ξ" : "angle of incidence",
                _ => "azimuth",
            }).ToList();
            XAxisBox.SelectedIndex = keep is { } k && axes.Contains(k) ? axes.IndexOf(k) : axes.Count > 0 ? axes.IndexOf(ResultPlot.ChooseAxis(_result.Document)) : -1;
            _settingAxes = false;
        }
        XAxisBox.IsEnabled = axes.Count > 1;
        return XAxisBox.SelectedIndex >= 0 && XAxisBox.SelectedIndex < axes.Count ? axes[XAxisBox.SelectedIndex] : null;
    }

    private void Redraw() => RedrawCore();

    /// <summary>Analysis > Field profile: a window of its own, on a copy of the stack as it is now.</summary>
    public FieldProfileWindow OpenFieldProfile()
    {
        var w = new FieldProfileWindow(_ws.Model);
        w.Show(this);
        return w;
    }

    /// <summary>Analysis > Conoscopic map: a window of its own, on a copy of the stack as it is now.</summary>
    public ConoscopeWindow OpenConoscope()
    {
        var w = new ConoscopeWindow(_ws.Model);
        w.Show(this);
        return w;
    }

    private async Task ExportPlot()
    {
        if (_result is null) { Status("Evaluate first (F5), then export.", error: true); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export the plot as PNG", DefaultExtension = "png",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
            SuggestedFileName = Path.GetFileNameWithoutExtension(Workspace.Name) + ".png",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        SavePlotPng(path);
        Status($"The plot is written to {path}.");
    }

    /// <summary>
    /// The plot as it is shown (the curves, or the Mueller panels), at twice the screen's
    /// resolution so it prints sharply; the tests call it directly.
    /// </summary>
    public void SavePlotPng(string path)
    {
        Control target = MuellerPanel.IsVisible ? MuellerPanel : Plot;
        var size = target.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) throw new InvalidOperationException("the plot has not been laid out");
        const double scale = 2;
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(target);
        bitmap.Save(path);
    }
    private void RedrawCore()
    {
        bool mueller = ViewBox.SelectedIndex > 0;
        Plot.IsVisible = !mueller;
        MuellerPanel.IsVisible = mueller;
        if (_result is null)
        {
            Plot.Series = null;
            foreach (var p in _muellerPlots) p.Series = null;
            return;
        }
        var axis = ChosenAxis();
        if (mueller)
        {
            var panels = ResultPlot.Mueller(_result, transmission: ViewBox.SelectedIndex == 2, axis);
            for (int k = 0; k < 16; ++k)
            {
                _muellerPlots[k].EmptyText = k == 0 ? panels.Message ?? "" : "";
                _muellerPlots[k].Series = panels.Panels[k];
            }
            MuellerTitle.Text = panels.XTitle;
            var names = panels.Panels[0].Select(s => s.Name.Contains(", ") ? s.Name[(s.Name.IndexOf(", ") + 2)..] : "").Where(n => n.Length > 0).ToList();
            MuellerLegend.Text = (ViewBox.SelectedIndex == 2 ? "Transmission" : "Reflection")
                + ": each element divided by M11; M11 itself is the unpolarised " + (ViewBox.SelectedIndex == 2 ? "transmittance" : "reflectance") + "."
                + (names.Count > 0 ? " Curves, in the order of the colours: " + string.Join("; ", names) + "." : "");
            return;
        }
        var choice = new PlotChoice(
            R: ShowR.IsChecked == true, T: ShowT.IsChecked == true, A: ShowA.IsChecked == true,
            P: ShowP.IsChecked == true, S: ShowS.IsChecked == true, Mean: ShowMean.IsChecked == true,
            Ellipsometry: ShowEllipsometry.IsChecked == true)
        { More = _moreQuantities, Axis = axis };
        var curves = ResultPlot.Build(_result, choice);
        Plot.XTitle = curves.XTitle;
        Plot.YTitle = curves.YTitle;
        Plot.EmptyText = curves.Message ?? "Nothing to plot.";
        Plot.Series = curves.Series;
    }
}

/// <summary>Every quantity the program derives (Api/Quantities.cs), as checkboxes by group; returns the names chosen.</summary>
public sealed class QuantityDialog : Window
{
    public QuantityDialog(IReadOnlyList<string> chosen, (string A, string B) waves)
    {
        Title = "More quantities to plot";
        Width = 620;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var boxes = new List<CheckBox>();
        var list = new StackPanel { Spacing = 2 };
        string[] groupTitles = ["fraction", "degrees", "amplitude", "normalised"];
        foreach (var group in Quantities.All(waves.A, waves.B).GroupBy(q => q.Group).OrderBy(g => Array.IndexOf(groupTitles, g.Key)))
        {
            list.Children.Add(new TextBlock
            {
                Text = group.Key switch { "fraction" => "Powers", "degrees" => "Angles (ellipsometry, phases)", "amplitude" => "Amplitudes", _ => "Mueller elements, divided by M11" },
                FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 2),
            });
            var wrap = new WrapPanel();
            foreach (var q in group)
            {
                var box = new CheckBox
                {
                    Name = $"Q_{q.Name}", Content = q.Name, Tag = q.Name, Margin = new Thickness(0, 0, 10, 0),
                    MinWidth = group.Key == "normalised" ? 60 : 110,
                    IsChecked = chosen.Contains(q.Name, StringComparer.OrdinalIgnoreCase),
                };
                ToolTip.SetTip(box, q.Description);
                boxes.Add(box);
                wrap.Children.Add(box);
            }
            list.Children.Add(wrap);
        }
        var clear = new Button { Content = "Clear all", Name = "ClearButton" };
        clear.Click += (_, _) => { foreach (var b in boxes) b.IsChecked = false; };
        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                Dock(Dialogs.Hint("Chosen quantities are plotted beside the R, T, A and Ψ, Δ boxes. Each unit (fraction, degrees, amplitude, normalised) gets its own y axis. Hover over a name for what it is. For the whole Mueller matrix at once, use the view \"Mueller matrix\"."), Avalonia.Controls.Dock.Top),
                Dock(Dialogs.Row(clear, new Border { Width = 12 }, Dialogs.Buttons(this, () => { Close(boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList()); return true; })), Avalonia.Controls.Dock.Bottom),
                new ScrollViewer { Content = list, Margin = new Thickness(0, 8) },
            },
        };
    }

    private static Control Dock(Control c, Avalonia.Controls.Dock d) { DockPanel.SetDock(c, d); return c; }
}
