// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// SpectrumPlot.cs — a chart control that draws what PlotModel computed and decides nothing. (From
// ThinFilmDesigner, the same authors and licence.)
//
// NO CHARTING PACKAGE, and the reason is the same one that keeps the rest of the tree
// dependency-free: this is a polyline, two axes and some tick labels. A charting library would
// bring a dependency graph larger than the whole managed side of this project to draw a line.
//
// EVERY NUMBER IN HERE CAME FROM PlotModel, which is in a project with no Avalonia in it and a
// test suite that exercises the degenerate cases: a flat curve, a single point, a NaN. What is
// left here is DrawLine and DrawText, which is the part a display would be needed to check and is
// also the part that cannot be subtly wrong: a mis-drawn line is visible.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Berreman.Gui.Core;

namespace Berreman.Gui;

public sealed class SpectrumPlot : Control
{
    /// <summary>The x-axis title, which carries the unit of the swept variable.</summary>
    public static readonly StyledProperty<string> XTitleProperty =
        AvaloniaProperty.Register<SpectrumPlot, string>(nameof(XTitle), "wavelength (nm)");

    public string XTitle
    {
        get => GetValue(XTitleProperty);
        set => SetValue(XTitleProperty, value);
    }

    /// <summary>The y-axis title of the first axis, "fraction (1 = 100%)" by default.</summary>
    public static readonly StyledProperty<string> YTitleProperty =
        AvaloniaProperty.Register<SpectrumPlot, string>(nameof(YTitle), "fraction (1 = 100%)");

    /// <summary>What an empty plot says.</summary>
    public string EmptyText { get; set; } = "No results. Open a stack and evaluate it (F5).";

    public string YTitle
    {
        get => GetValue(YTitleProperty);
        set => SetValue(YTitleProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<PlotSeries>?> SeriesProperty =
        AvaloniaProperty.Register<SpectrumPlot, IReadOnlyList<PlotSeries>?>(nameof(Series));

    public IReadOnlyList<PlotSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    // A wider left margin than PlotModel's default, to make room for the y-axis title beside the
    // tick labels.
    private PlotModel? _plotModel;
    private PlotModel _model => _plotModel ??= Compact ? new PlotModel { Left = 44.0, Top = 18.0, Bottom = 20.0, Right = 6.0 } : new PlotModel { Left = 72.0 };

    /// <summary>
    /// A small panel of a grid (the Mueller matrix): narrow margins, the panel's Title at the top,
    /// and no legend or axis titles, which the grid gives once for all its panels.
    /// </summary>
    public bool Compact { get; init; }

    /// <summary>x positions drawn as faint vertical lines under the curves: a profile's interfaces.</summary>
    public IReadOnlyList<double>? Markers { get; set; }

    /// <summary>The panel's name in compact mode, "M12".</summary>
    public string Title { get; set; } = "";

    // ---- zoom and cursor -------------------------------------------------------------------------
    // Drag a rectangle to zoom into it; double-click to see everything again. Hovering shows the
    // nearest point's value. The zoom stays when the curves change (an edit re-evaluates), so a
    // feature being studied stays in view.

    private Point? _dragFrom, _dragTo, _hover;

    /// <summary>The x range shown, or null for all of it.</summary>
    public (double Min, double Max)? XZoom
    {
        get => _model.XZoom;
        set { _model.XZoom = value; InvalidateVisual(); }
    }

    /// <summary>The first y axis's range shown, or null to fit.</summary>
    public (double Min, double Max)? YZoom
    {
        get => _model.YZoom;
        set { _model.YZoom = value; InvalidateVisual(); }
    }

    public void ResetZoom() { _model.XZoom = null; _model.YZoom = null; InvalidateVisual(); }

    private bool InPlot(Point p) =>
        p.X >= _model.Left && p.X <= _model.Left + _model.PlotWidth && p.Y >= _model.Top && p.Y <= _model.Top + _model.PlotHeight;

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (e.ClickCount == 2) { ResetZoom(); e.Handled = true; return; }
        if (Series is { Count: > 0 } && InPlot(p) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragFrom = _dragTo = p;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_dragFrom is not null) _dragTo = p;
        _hover = InPlot(p) ? p : null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is { } a && _dragTo is { } b && Math.Abs(b.X - a.X) > 6 && Math.Abs(b.Y - a.Y) > 6)
        {
            double x0 = _model.FromPixelX(Math.Min(a.X, b.X)), x1 = _model.FromPixelX(Math.Max(a.X, b.X));
            double y0 = _model.FromPixelY(Math.Max(a.Y, b.Y)), y1 = _model.FromPixelY(Math.Min(a.Y, b.Y));
            _model.XZoom = (x0, x1);
            _model.YZoom = (y0, y1);
        }
        _dragFrom = _dragTo = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    static SpectrumPlot()
    {
        AffectsRender<SpectrumPlot>(SeriesProperty, XTitleProperty, YTitleProperty);
        AffectsMeasure<SpectrumPlot>(SeriesProperty);
    }

    public override void Render(DrawingContext context)
    {
        var series = Series ?? System.Array.Empty<PlotSeries>();
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40) return;

        var background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        context.FillRectangle(background, new Rect(0, 0, w, h));

        if (series.Count == 0)
        {
            Text(context, EmptyText, new Point(16, 16), 0xFF808080);
            return;
        }

        _model.Update(series, w, h);

        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), 1.0);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), 1.0);

        double left = _model.Left, top = _model.Top;
        double right = left + _model.PlotWidth, bottom = top + _model.PlotHeight;

        foreach (var t in _model.YTicks())
        {
            context.DrawLine(gridPen, new Point(left, t.Position), new Point(right, t.Position));
            Text(context, t.Label, new Point(20, t.Position - 8), 0xFF404040);
        }

        /* About a tick per 45 px, so the labels of a narrow plot (a Mueller panel) do not run together. */
        foreach (var t in _model.XTicks(Math.Clamp((int)(_model.PlotWidth / 45), 3, 6)))
        {
            context.DrawLine(gridPen, new Point(t.Position, top), new Point(t.Position, bottom));
            Text(context, t.Label, new Point(t.Position - 16, bottom + 6), 0xFF404040);
        }

        context.DrawLine(axisPen, new Point(left, top), new Point(left, bottom));
        context.DrawLine(axisPen, new Point(left, bottom), new Point(right, bottom));

        // The axes after the first, on the right: a line, ticks and labels outside it, and the
        // title turned beside them. An axis with one colour of line on it takes that colour, so
        // the line and its scale read together.
        for (int a = 1; a < _model.Axes.Count; ++a)
        {
            double ax = _model.AxisPosition(a);
            uint colour = AxisColour(series, a);
            var pen = new Pen(new SolidColorBrush(Color.FromUInt32(colour)), 1.0);
            context.DrawLine(pen, new Point(ax, top), new Point(ax, bottom));
            foreach (var t in _model.YTicks(axis: a))
            {
                context.DrawLine(pen, new Point(ax, t.Position), new Point(ax + 4, t.Position));
                Text(context, t.Label, new Point(ax + 6, t.Position - 8), colour);
            }
            Vertical(context, _model.Axes[a].Title ?? "", new Point(ax + _model.AxisGap - 12, (top + bottom) / 2), colour);
        }

        var clip = context.PushClip(new Rect(left, top, right - left, bottom - top));   // curves stay inside the axes when zoomed
        if (Markers is { } marks)
        {
            var markPen = new Pen(new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)), 1, new DashStyle([3.0, 3.0], 0));
            foreach (var m in marks)
            {
                double mx = _model.ToPixelX(m);
                context.DrawLine(markPen, new Point(mx, top), new Point(mx, bottom));
            }
        }
        foreach (var s in series)
        {
            var runs = _model.Segments(s).Where(r => r.Count >= 2).ToList();
            if (runs.Count == 0) continue;

            var pen = new Pen(new SolidColorBrush(Color.FromUInt32(s.Colour)), 1.8,
                              s.Dashed ? new DashStyle([4.0, 3.0], 0.0) : null);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
                foreach (var points in runs)
                {
                    g.BeginFigure(new Point(points[0].X, points[0].Y), false);
                    for (int i = 1; i < points.Count; ++i)
                        g.LineTo(new Point(points[i].X, points[i].Y));
                    g.EndFigure(false);
                }
            context.DrawGeometry(null, pen, geometry);
        }

        clip.Dispose();
        DrawInteraction(context);

        if (Compact)
        {
            Text(context, Title, new Point(left + 4, 2), 0xFF202020);
            return;
        }

        // The legend, placed inside the plot so it cannot be cut off by a narrow window.
        // As wide as its longest name, so "R_p, 633 nm, az 45°" fits as well as "Ψ"; a dashed series
        // has a short dashed sample beside its name.
        double widest = series.Max(s => Formatted(s.Name, 0).Width);
        double x = right - widest - 30, y = top + 4;
        // On a translucent panel, so the curves it sits over do not run through the names.
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                              new Rect(x - 6, y - 2, widest + 34, series.Count * 16 + 4));
        foreach (var s in series)
        {
            var sample = new Pen(new SolidColorBrush(Color.FromUInt32(s.Colour)), 1.8,
                                 s.Dashed ? new DashStyle([4.0, 3.0], 0.0) : null);
            context.DrawLine(sample, new Point(x, y + 8), new Point(x + 20, y + 8));
            Text(context, s.Name, new Point(x + 24, y), s.Colour);
            y += 16;
        }

        Text(context, XTitle, new Point((left + right) / 2 - 44, bottom + 20), 0xFF404040);

        // The y-axis title, turned to read bottom to top. The quantities are fractions, and a
        // bare 0.004 on an axis is read as 0.004% as often as 0.4%.
        // A series that names its axis titles it; otherwise the control's own YTitle does.
        Vertical(context, _model.Axes[0].Title ?? YTitle, new Point(8, (top + bottom) / 2), 0xFF404040);
    }

    /// <summary>The zoom rectangle being dragged, and the hovered point's marker and value.</summary>
    private void DrawInteraction(DrawingContext context)
    {
        if (_dragFrom is { } a && _dragTo is { } b)
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0x1F, 0x77, 0xB4)), new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x77, 0xB4)), 1),
                                  new Rect(a, b).Normalize());
        else if (_hover is { } h && _model.Nearest(h.X, h.Y) is { } n)
        {
            int axis = _model.AxisOf(n.Series);
            var at = new Point(_model.ToPixelX(n.X), _model.ToPixelY(n.Y, axis));
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromUInt32(n.Series.Colour)), 1.5), at, 4, 4);
            string label = $"{n.Series.Name}: {n.X:G6}, {n.Y:G5}";
            var size = Formatted(label, 0).Width;
            double tx = Math.Min(at.X + 8, _model.Left + _model.PlotWidth - size - 4), ty = Math.Max(at.Y - 20, _model.Top);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), new Rect(tx - 3, ty - 1, size + 6, 16));
            Text(context, label, new Point(tx, ty), n.Series.Colour);
        }
        if (_model.XZoom is not null && !Compact)
            Text(context, "zoomed: double-click to see all", new Point(_model.Left + 6, _model.Top + 2), 0xFF808080);
    }

    /// <summary>The colour of the lines on an axis when they all share one, grey otherwise.</summary>
    private uint AxisColour(IReadOnlyList<PlotSeries> series, int axis)
    {
        var colours = series.Where(s => _model.AxisOf(s) == axis).Select(s => s.Colour).Distinct().ToList();
        return colours.Count == 1 ? colours[0] : 0xFF404040;
    }

    /// <summary>Text turned to read bottom to top, centred on a point.</summary>
    private static void Vertical(DrawingContext context, string text, Point centre, uint colour)
    {
        double width = Formatted(text, colour).Width;
        using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y)
                                     * Matrix.CreateRotation(-Math.PI / 2)
                                     * Matrix.CreateTranslation(centre.X, centre.Y)))
            Text(context, text, new Point(centre.X - width / 2, centre.Y - 7), colour);
    }

    private static void Text(DrawingContext context, string text, Point at, uint colour)
    {
        var formatted = new FormattedText(
            text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11,
            new SolidColorBrush(Color.FromUInt32(colour)));
        context.DrawText(formatted, at);
    }

    private static FormattedText Formatted(string text, uint colour) => new(
        text, System.Globalization.CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, Typeface.Default, 11, new SolidColorBrush(Color.FromUInt32(colour)));
}
