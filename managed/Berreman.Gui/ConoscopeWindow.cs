// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// ConoscopeWindow.cs — Analysis > Conoscopic map: one quantity over every direction of incidence,
// at one spectral value. The sweep and the quantities are Gui.Core's (Conoscope, tested); this
// window takes the settings, evaluates in the background and draws.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Gui;

/// <summary>The map itself: the angle of incidence as radius, the azimuth as polar angle, a colour scale beside it.</summary>
public sealed class ConoscopeView : Control
{
    private ConoscopeMap? _map;
    private WriteableBitmap? _image;
    private Point? _hover;

    public ConoscopeMap? Map
    {
        get => _map;
        set { _map = value; _image?.Dispose(); _image = null; InvalidateVisual(); }
    }

    public string EmptyText { get; set; } = "Choose the settings and Compute.";

    private (Point Centre, double Radius) Disc()
    {
        double w = Bounds.Width - 110, h = Bounds.Height - 30;
        double r = Math.Max(10, Math.Min(w, h) / 2 - 16);
        return (new Point(16 + r + 14, 16 + r), r);
    }

    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); _hover = e.GetPosition(this); InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = null; InvalidateVisual(); }

    /// <summary>The direction under a point of the control: angle of incidence and azimuth, in degrees.</summary>
    public (double Angle, double Azimuth)? DirectionAt(Point p)
    {
        if (_map is null) return null;
        var (c, r) = Disc();
        double dx = p.X - c.X, dy = c.Y - p.Y, rr = Math.Sqrt(dx * dx + dy * dy);
        if (rr > r) return null;
        double az = Math.Atan2(dy, dx) * 180 / Math.PI;
        return (rr / r * _map.AnglesDeg[^1], az < 0 ? az + 360 : az);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (_map is null) { Text(context, EmptyText, new Point(16, 16), Brushes.Gray); return; }
        var (c, r) = Disc();
        int n = (int)(2 * r);
        if (_image is null || _image.PixelSize.Width != n)
        {
            _image?.Dispose();
            _image = new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var fb = _image.Lock();
            double span = _map.Max - _map.Min;
            unsafe
            {
                var px = (uint*)fb.Address;
                for (int y = 0; y < n; ++y)
                    for (int x = 0; x < n; ++x)
                    {
                        double dx = x + 0.5 - r, dy = r - (y + 0.5), rr = Math.Sqrt(dx * dx + dy * dy);
                        uint colour = 0x00000000;
                        if (rr <= r)
                        {
                            double v = Conoscope.At(_map, rr / r * _map.AnglesDeg[^1], Math.Atan2(dy, dx) * 180 / Math.PI);
                            colour = double.IsFinite(v) ? Conoscope.Colour(span > 0 ? (v - _map.Min) / span : 0.5) : 0xFFFFFFFF;
                        }
                        px[y * (fb.RowBytes / 4) + x] = colour;
                    }
            }
        }
        context.DrawImage(_image, new Rect(c.X - r, c.Y - r, n, n));

        /* Circles every 10° of incidence, the four azimuths, and their labels. */
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), 1);
        double max = _map.AnglesDeg[^1];
        for (double a = 10; a < max + 1e-9; a += 10)
        {
            double rr = a / max * r;
            context.DrawEllipse(null, grid, c, rr, rr);
            /* Along the diagonal, clear of the azimuth labels at the ends of the axes. */
            if (a < max - 1e-9) Text(context, $"{a:G3}°", new Point(c.X + rr * 0.707 + 2, c.Y - rr * 0.707 - 12), Brushes.White);
        }
        context.DrawLine(grid, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        context.DrawLine(grid, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));
        Text(context, "0°", new Point(c.X + r + 3, c.Y - 7), Brushes.Black);
        Text(context, "90°", new Point(c.X - 9, c.Y - r - 15), Brushes.Black);
        Text(context, "180°", new Point(c.X - r - 30, c.Y - 7), Brushes.Black);
        Text(context, "270°", new Point(c.X - 12, c.Y + r + 1), Brushes.Black);

        /* The colour scale. */
        double bx = c.X + r + 40, by = c.Y - r, bh = 2 * r;
        for (int k = 0; k < 100; ++k)
        {
            var colour = Color.FromUInt32(Conoscope.Colour(1 - k / 99.0));
            context.FillRectangle(new SolidColorBrush(colour), new Rect(bx, by + k * bh / 100, 14, bh / 100 + 1));
        }
        /* A minimum many orders below the maximum is a zero in all but round-off. */
        double shownMin = Math.Abs(_map.Min) < 1e-12 * Math.Max(Math.Abs(_map.Max), 1e-300) ? 0 : _map.Min;
        Text(context, _map.Max.ToString("G4", CultureInfo.InvariantCulture), new Point(bx + 18, by - 2), Brushes.Black);
        Text(context, shownMin.ToString("G4", CultureInfo.InvariantCulture), new Point(bx + 18, by + bh - 12), Brushes.Black);
        Text(context, $"max angle {max:G3}°", new Point(c.X + r * 0.707 + 2, c.Y - r * 0.707 - 14), Brushes.Black);

        if (_hover is { } h && DirectionAt(h) is { } d)
        {
            double v = Conoscope.At(_map, d.Angle, d.Azimuth);
            string label = $"θ {d.Angle:F1}°, azimuth {d.Azimuth:F1}°: {v:G5}";
            var ft = Formatted(label, Brushes.Black);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), new Rect(8, Bounds.Height - 22, ft.Width + 8, 18));
            context.DrawText(ft, new Point(12, Bounds.Height - 21));
        }
    }

    private static FormattedText Formatted(string s, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush);

    private static void Text(DrawingContext context, string s, Point at, IBrush brush) => context.DrawText(Formatted(s, brush), at);
}

public sealed class ConoscopeWindow : Window
{
    private readonly StackModel _model;
    private EvaluationResult? _result;
    private readonly ConoscopeView _view = new() { Name = "ConoscopeMapView" };
    private readonly ComboBox _quantity = new() { Name = "ConoscopeQuantityBox", Width = 330 };
    private readonly TextBox _spectral, _maxAngle, _angles, _azimuths, _polariser;
    private readonly TextBlock _status = new() { Name = "ConoscopeStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Button _compute = new() { Name = "ComputeMapButton", Content = "Compute", FontWeight = FontWeight.SemiBold };

    /// <summary>True while a map is being computed; the tests wait on it.</summary>
    public bool IsComputing { get; private set; }
    public ConoscopeView View => _view;

    public ConoscopeWindow(StackModel model)
    {
        _model = model.Clone();
        Title = "Conoscopic map";
        Width = 900;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        double first;
        try { first = StackDocument.Values(SweepText.Parse(model.Spectral, "spectral"), "spectral")[0]; } catch (StackFormatException) { first = 550; }
        string unit = model.SpectralUnit;
        _spectral = Box("ConoscopeSpectralBox", first.ToString("G6", CultureInfo.InvariantCulture), 90);
        _maxAngle = Box("ConoscopeMaxAngleBox", "60", 60);
        _angles = Box("ConoscopeAnglesBox", "31", 50);
        _azimuths = Box("ConoscopeAzimuthsBox", "72", 50);
        _polariser = Box("ConoscopePolariserBox", "0", 50);
        Quantities();
        _quantity.SelectionChanged += (_, _) => ShowMap();
        _polariser.LostFocus += (_, _) => { Quantities(); ShowMap(); };
        _compute.Click += async (_, _) => await Compute();
        var png = new Button { Name = "MapPngButton", Content = "Save PNG…" };
        png.Click += async (_, _) => await Save(png: true);
        var csv = new Button { Name = "MapCsvButton", Content = "Save CSV…" };
        csv.Click += async (_, _) => await Save(png: false);

        Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                Top(new WrapPanel
                {
                    Children =
                    {
                        Dialogs.Label("at"), _spectral, Dialogs.Label(unit + "   "),
                        Dialogs.Label("angles to"), _maxAngle, Dialogs.Label("°, "), _angles, Dialogs.Label("of them;   "),
                        _azimuths, Dialogs.Label("azimuths   "), _compute,
                    },
                }),
                Top(new WrapPanel
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    Children = { Dialogs.Label("show"), _quantity, Dialogs.Label("   polariser at"), _polariser, Dialogs.Label("° from the sample's x   "), png, new Border { Width = 6 }, csv },
                }),
                Top(Dialogs.Hint("The radius is the angle of incidence and the polar angle the sample's azimuth, the program's azimuth_deg: the sample turned about the normal, with the plane of incidence fixed. Polarisers are fixed to the sample. Hover over the map to read a value.")),
                Bottom(_status),
                _view,
            },
        };
    }

    private static TextBox Box(string name, string text, double width) => new() { Name = name, Text = text, Width = width, Margin = new Thickness(0, 0, 4, 0) };
    private static Control Top(Control c) { DockPanel.SetDock(c, Dock.Top); return c; }
    private static Control Bottom(Control c) { DockPanel.SetDock(c, Dock.Bottom); return c; }

    private double Polariser() =>
        double.TryParse((_polariser.Text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && double.IsFinite(p) ? p : 0;

    private void Quantities()
    {
        int keep = Math.Max(0, _quantity.SelectedIndex);
        _quantity.ItemsSource = Conoscope.Quantities(Polariser()).Select(q => q.Name).ToList();
        _quantity.SelectedIndex = keep;
    }

    /// <summary>Evaluates the map's sweep in the background; the quantity can then be changed without evaluating again.</summary>
    public async Task Compute()
    {
        try
        {
            double V(TextBox b, string what) => Dialogs.Read(b, what);
            var sweep = Conoscope.Sweep(_model, V(_spectral, "the spectral value"), V(_maxAngle, "the largest angle"),
                                        (int)V(_angles, "the number of angles"), (int)V(_azimuths, "the number of azimuths"));
            var doc = sweep.ToDocument();
            IsComputing = true;
            _compute.IsEnabled = false;
            _status.Text = $"Evaluating {doc.AnglesDeg!.Length * doc.AzimuthsDeg.Length} directions…";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _result = await Task.Run(() => Evaluator.Evaluate(doc));
            _status.Text = $"{_result.Points.Count} directions in {clock.Elapsed.TotalSeconds:F2} s.";
            _status.Foreground = Brushes.Black;
            ShowMap();
        }
        catch (StackFormatException e) { _status.Text = e.Message; _status.Foreground = Brushes.Firebrick; }
        finally { IsComputing = false; _compute.IsEnabled = true; }
    }

    private void ShowMap()
    {
        if (_result is null || _quantity.SelectedIndex < 0) return;
        try { _view.Map = Conoscope.Build(_result, Conoscope.Quantities(Polariser())[_quantity.SelectedIndex]); }
        catch (StackFormatException e) { _view.Map = null; _view.EmptyText = e.Message; _status.Text = e.Message; _status.Foreground = Brushes.Firebrick; }
    }

    /// <summary>The map as shown (PNG, at twice the screen's resolution) or as numbers (CSV: angle, azimuth, value).</summary>
    public void SaveTo(string path, bool png)
    {
        if (_view.Map is not { } map) throw new InvalidOperationException("there is no map to save");
        if (png)
        {
            var size = _view.Bounds.Size;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(size.Width * 2), (int)(size.Height * 2)), new Vector(192, 192));
            bitmap.Render(_view);
            bitmap.Save(path);
            return;
        }
        var sb = new System.Text.StringBuilder($"angle_deg,azimuth_deg,{map.Quantity.Replace(',', ';')}\n");
        for (int i = 0; i < map.AnglesDeg.Length; ++i)
            for (int j = 0; j < map.AzimuthsDeg.Length; ++j)
                sb.Append(FormattableString.Invariant($"{map.AnglesDeg[i]:G10},{map.AzimuthsDeg[j]:G10},{map.Values[i, j]:G10}\n"));
        File.WriteAllText(path, sb.ToString());
    }

    private async Task Save(bool png)
    {
        if (_view.Map is null) { _status.Text = "Compute a map first."; return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = png ? "Save the map as PNG" : "Save the map as CSV", DefaultExtension = png ? "png" : "csv",
            FileTypeChoices = [png ? new FilePickerFileType("PNG image") { Patterns = ["*.png"] } : new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            SuggestedFileName = "conoscope." + (png ? "png" : "csv"),
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        SaveTo(path, png);
        _status.Text = $"Saved to {path}.";
    }
}
