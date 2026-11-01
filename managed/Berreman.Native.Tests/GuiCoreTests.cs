// GuiCoreTests.cs — the testable half of the GUI: the plot arithmetic, the choice of curves, and
// the sample list.

using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Native.Tests;

internal static class GuiCoreTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    public static void Run()
    {
        PlotModelDegenerateCases();
        PlotModelZoom();
        AxisFollowsTheSweep();
        CurvesAreLabelledAndCounted();
        TooManyCurvesIsSaidNotDrawn();
        SamplesAreListed();
    }

    private static void PlotModelZoom()
    {
        var x = Enumerable.Range(0, 101).Select(i => 400.0 + 4 * i).ToArray();
        var y = x.Select(v => Math.Sin(v / 50)).ToArray();
        var m = new PlotModel { XZoom = (500, 600) };
        m.Update([new PlotSeries("s", x, y, 0xFF000000)], 800, 400);
        Check(m.XMin == 500 && m.XMax == 600, "an x zoom is shown exactly");
        var inside = x.Zip(y).Where(t => t.First >= 500 && t.First <= 600).Select(t => t.Second).ToList();
        Check(m.YMin <= inside.Min() && m.YMax >= inside.Max() && m.YMax - m.YMin < 2.0, "and y fits the points inside it, not all of them");
        Check(Math.Abs(m.FromPixelX(m.ToPixelX(537.5)) - 537.5) < 1e-9 && Math.Abs(m.FromPixelY(m.ToPixelY(0.3)) - 0.3) < 1e-12, "pixels map back to data");
        var near = m.Nearest(m.ToPixelX(548), m.ToPixelY(Math.Sin(548.0 / 50)) + 3);
        Check(near is { X: 548 }, "the cursor finds the nearest point");
        Check(m.Nearest(m.ToPixelX(548), m.ToPixelY(5)) is null, "and nothing when no point is near");
        m.YZoom = (-0.25, 0.25);
        m.Update(m.Series, 800, 400);
        Check(m.YMin == -0.25 && m.YMax == 0.25, "a y zoom is shown exactly");
    }

    private static void PlotModelDegenerateCases()
    {
        var m = new PlotModel();
        m.Update([new PlotSeries("flat", [1, 2, 3], [0.5, 0.5, 0.5], 0)], 400, 300);
        Check(m.YMax > m.YMin, "a flat curve gets a non-zero y range");
        m.Update([new PlotSeries("one", [2], [0.3], 0)], 400, 300);
        Check(m.XMax > m.XMin && m.YMax > m.YMin, "a single point gets non-zero ranges");
        m.Update([new PlotSeries("gap", [1, 2, 3, 4], [0.1, double.NaN, 0.3, 0.4], 0)], 400, 300);
        Check(m.Segments(m.Series[0]).Count == 2, "a NaN breaks a curve into two");
        Check(m.YMin <= 0.1 && m.YMax >= 0.4, "the axis ignores the NaN and holds the data");
        var y0 = m.ToPixelY(m.YMin); var y1 = m.ToPixelY(m.YMax);
        Check(y0 > y1, "pixels grow downward, data upward");
    }

    private static StackDocument Doc(string sweep) => StackDocument.Parse($$"""
        { "format": "berreman-stack", "version": 1,
          "materials": { "a": { "n": 1 }, "g": { "n": 1.5 } },
          "incident": { "material": "a" }, "exit": { "material": "g" }, "layers": [],
          "sweep": {{sweep}} }
        """);

    private static void AxisFollowsTheSweep()
    {
        Check(ResultPlot.ChooseAxis(Doc("""{ "spectral": [500, 600], "angle_deg": [0, 30] }""")) == XAxis.Spectral, "several wavelengths: x is spectral");
        Check(ResultPlot.ChooseAxis(Doc("""{ "spectral": 500, "angle_deg": [0, 30] }""")) == XAxis.Incidence, "one wavelength, several angles: x is the angle");
        Check(ResultPlot.ChooseAxis(Doc("""{ "spectral": 500, "angle_deg": 30, "azimuth_deg": [0, 45] }""")) == XAxis.Azimuth, "only azimuths vary: x is the azimuth");
        Check(ResultPlot.ChooseAxis(Doc("""{ "spectral": 500, "angle_deg": 30 }""")) == XAxis.None, "one point: no axis");
        var one = ResultPlot.Build(Evaluator.Evaluate(Doc("""{ "spectral": 500, "angle_deg": 30 }""")), new PlotChoice());
        Check(one.Series.Count == 0 && one.Message is not null, "one point: no curves, and a message");
    }

    private static void CurvesAreLabelledAndCounted()
    {
        var r = Evaluator.Evaluate(Doc("""{ "spectral": { "from": 400, "to": 700, "step": 50 }, "angle_deg": [0, 30, 60] }"""));
        var c = ResultPlot.Build(r, new PlotChoice(R: true, T: false, P: true, S: true));
        Check(c.Series.Count == 6, "three angles × R_p, R_s: six curves");
        Check(c.Series.All(s => s.X.Length == 7), "each curve has every wavelength");
        Check(c.Series.Any(s => s.Name == "R_p, 30°"), "curves are labelled by the angle they belong to");
        Check(c.XTitle == "wavelength (nm)", "the x title carries the spectral unit");
        var e = ResultPlot.Build(r, new PlotChoice(R: false, T: false, P: false, S: false, Ellipsometry: true));
        Check(e.Series.All(s => s.Axis == "degrees"), "Ψ and Δ go on their own axis in degrees");
    }

    private static void TooManyCurvesIsSaidNotDrawn()
    {
        var r = Evaluator.Evaluate(Doc("""{ "spectral": [500, 600], "angle_deg": { "from": 0, "to": 80, "step": 2 } }"""));
        var c = ResultPlot.Build(r, new PlotChoice(R: true, T: true, A: true, P: true, S: true, Mean: true));
        Check(c.Series.Count == 0 && c.Message is not null, "more than the curve limit: a message, not a tangle");
    }

    private static void SamplesAreListed()
    {
        var list = SampleCatalog.List(SampleCatalog.DefaultRoot);
        Check(list.Count >= 17, $"the installed samples are listed ({list.Count})");
        Check(list.Any(s => s.Name == "mansuripur-1990 / quadrilayer"), "a sample is named by paper and stack");
        Check(list.All(s => { try { StackDocument.Load(s.Path); return true; } catch { return false; } }), "every installed sample parses");
    }
}
