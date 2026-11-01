// FieldProfileWindow.cs — Analysis > Field profile: the fields against depth at one point of the
// sweep, and the stack's index profile. The arithmetic is Gui.Core's (FieldProfile, tested).

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Gui;

public sealed class FieldProfileWindow : Window
{
    private readonly StackModel _model;
    private FieldProfileResult? _profile;
    private readonly SpectrumPlot _plot = new() { Name = "ProfilePlot", EmptyText = "Choose the point and Compute." };
    private readonly ComboBox _quantity = new() { Name = "ProfileQuantityBox", Width = 230 };
    private readonly ComboBox _wave = new() { Name = "ProfileWaveBox", Width = 270 };
    private readonly TextBox _spectral, _incidence, _azimuth, _samples, _before, _after;
    private readonly TextBlock _status = new() { Name = "ProfileStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Button _compute = new() { Name = "ComputeProfileButton", Content = "Compute", FontWeight = FontWeight.SemiBold };

    public SpectrumPlot Plot => _plot;
    public FieldProfileResult? Profile => _profile;

    public FieldProfileWindow(StackModel model)
    {
        _model = model.Clone();
        Title = "Field profile";
        Width = 1000;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var inv = CultureInfo.InvariantCulture;
        double First(string text, double fallback)
        {
            try { return StackDocument.Values(SweepText.Parse(text, "x"), "x")[0]; } catch (StackFormatException) { return fallback; }
        }
        string lu = model.LengthUnit == "um" ? "µm" : model.LengthUnit;
        double total = 0;
        try { var d = model.ToDocument(); total = d.Layers.Sum(l => l.ThicknessNm) / Units.NmPerLength(d.LengthUnit); } catch (StackFormatException) { }
        double margin = total > 0 ? Math.Round(total * 0.2, 6) : 100;
        _spectral = Box("ProfileSpectralBox", First(model.Spectral, 550).ToString("G6", inv), 90);
        _incidence = Box("ProfileIncidenceBox", First(model.Angles, 0).ToString("G6", inv), 60);
        _azimuth = Box("ProfileAzimuthBox", (model.Azimuths.Trim().Length == 0 ? 0 : First(model.Azimuths, 0)).ToString("G6", inv), 60);
        _samples = Box("ProfileSamplesBox", "1001", 60);
        _before = Box("ProfileBeforeBox", margin.ToString("G6", inv), 70);
        _after = Box("ProfileAfterBox", margin.ToString("G6", inv), 70);
        _quantity.ItemsSource = FieldProfile.QuantityNames.Append("index profile: n and k along x, y, z").ToList();
        _quantity.SelectedIndex = 0;
        _wave.ItemsSource = FieldProfile.WaveNames;
        _wave.SelectedIndex = 0;
        _quantity.SelectionChanged += (_, _) => Draw();
        _wave.SelectionChanged += (_, _) => Draw();
        _compute.Click += async (_, _) => await Compute();
        var png = new Button { Name = "ProfilePngButton", Content = "Save PNG…" };
        png.Click += async (_, _) => await Save(true);
        var csv = new Button { Name = "ProfileCsvButton", Content = "Save CSV…" };
        csv.Click += async (_, _) => await Save(false);

        Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                Top(new WrapPanel
                {
                    Children =
                    {
                        Dialogs.Label("at"), _spectral, Dialogs.Label(model.SpectralUnit + ",  "),
                        Dialogs.Label(model.AnglesAreXi ? "ξ" : "angle"), _incidence, Dialogs.Label(model.AnglesAreXi ? ",  " : "°,  "),
                        Dialogs.Label("azimuth"), _azimuth, Dialogs.Label("°;   "),
                        _samples, Dialogs.Label("depths, from"), _before, Dialogs.Label($"{lu} in front to"), _after, Dialogs.Label($"{lu} behind   "), _compute,
                    },
                }),
                Top(new WrapPanel
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    Children = { Dialogs.Label("show"), _quantity, Dialogs.Label("  for"), _wave, Dialogs.Label("   "), png, new Border { Width = 6 }, csv },
                }),
                Top(Dialogs.Hint("Depth runs from the first interface into the stack; the dashed lines are the interfaces. Fields are per unit incident amplitude, so |E|² is the intensity relative to the incident wave's. Drag to zoom; double-click to see all.")),
                Bottom(_status),
                _plot,
            },
        };
    }

    private static TextBox Box(string name, string text, double width) => new() { Name = name, Text = text, Width = width, Margin = new Thickness(0, 0, 4, 0) };
    private static Control Top(Control c) { DockPanel.SetDock(c, Dock.Top); return c; }
    private static Control Bottom(Control c) { DockPanel.SetDock(c, Dock.Bottom); return c; }

    public async Task Compute()
    {
        try
        {
            double V(TextBox b, string what) => Dialogs.Read(b, what);
            double spectral = V(_spectral, "the spectral value"), inc = V(_incidence, "the angle"), az = V(_azimuth, "the azimuth");
            int n = (int)V(_samples, "the number of depths");
            double before = V(_before, "the depth in front"), after = V(_after, "the depth behind");
            if (n < 2 || n > 200000) throw new StackFormatException("give between 2 and 200000 depths");
            if (before < 0 || after < 0) throw new StackFormatException("the depths in front and behind must not be negative");
            _compute.IsEnabled = false;
            _status.Text = "Evaluating…";
            _profile = await Task.Run(() => FieldProfile.Compute(_model, spectral, inc, az, n, before, after));
            var pt = _profile.Point;
            _status.Text = $"R = {pt.Power[0]:G5} (p), {pt.Power[3]:G5} (s);  T = {pt.Power[1]:G5}, {pt.Power[4]:G5};  status {pt.Status}.";
            _status.Foreground = Brushes.Black;
            Draw();
        }
        catch (StackFormatException e) { _status.Text = e.Message; _status.Foreground = Brushes.Firebrick; }
        finally { _compute.IsEnabled = true; }
    }

    private void Draw()
    {
        if (_profile is not { } f) return;
        string lu = f.LengthUnit == "um" ? "µm" : f.LengthUnit;
        _plot.XTitle = $"depth ({lu})";
        _plot.Markers = f.Boundaries;
        if (_quantity.SelectedIndex == FieldProfile.QuantityNames.Length)
        {
            _plot.YTitle = "n";
            _wave.IsEnabled = false;
            _plot.Series = FieldProfile.IndexProfile(f);
            return;
        }
        _wave.IsEnabled = true;
        _plot.YTitle = _quantity.SelectedIndex == 3 ? "field (per unit incident amplitude)" : "intensity (relative to the incident wave)";
        var series = FieldProfile.Curves(f, Math.Max(0, _quantity.SelectedIndex), Math.Max(0, _wave.SelectedIndex));
        _plot.EmptyText = series.Count == 0 ? "The real part has no unpolarised mean; choose p or s." : "Nothing to plot.";
        _plot.Series = series;
    }

    /// <summary>The plot as shown (PNG at twice the screen's resolution) or every depth's six components (CSV).</summary>
    public void SaveTo(string path, bool png)
    {
        if (_profile is not { } f) throw new InvalidOperationException("there is no profile to save");
        if (png)
        {
            var size = _plot.Bounds.Size;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(size.Width * 2), (int)(size.Height * 2)), new Vector(192, 192));
            bitmap.Render(_plot);
            bitmap.Save(path);
            return;
        }
        var sb = new System.Text.StringBuilder("depth");
        foreach (var w in new[] { "0", "1" })
            foreach (var c in new[] { "Ex", "Ey", "Ez", "Hx", "Hy", "Hz" })
                sb.Append($",{c}_{w}_re,{c}_{w}_im");
        sb.Append('\n');
        int n = f.Depths.Length;
        for (int d = 0; d < n; ++d)
        {
            sb.Append(f.Depths[d].ToString("G10", CultureInfo.InvariantCulture));
            for (int w = 0; w < 2; ++w)
                for (int c = 0; c < 6; ++c)
                {
                    var z = f.Point.Field(w, d, c, n);
                    sb.Append(',').Append(z.Real.ToString("G10", CultureInfo.InvariantCulture)).Append(',').Append(z.Imaginary.ToString("G10", CultureInfo.InvariantCulture));
                }
            sb.Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    private async Task Save(bool png)
    {
        if (_profile is null) { _status.Text = "Compute a profile first."; return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = png ? "Save the profile as PNG" : "Save the fields as CSV", DefaultExtension = png ? "png" : "csv",
            FileTypeChoices = [png ? new FilePickerFileType("PNG image") { Patterns = ["*.png"] } : new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            SuggestedFileName = "field-profile." + (png ? "png" : "csv"),
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        SaveTo(path, png);
        _status.Text = $"Saved to {path}.";
    }
}
