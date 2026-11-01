// ConoscopeTests.cs — conoscopic maps (Gui.Core/Conoscope.cs) against closed forms.
//
// 1. A c-cut uniaxial plate between crossed polarisers: the isochromes. Along the azimuth at 45° to
//    the polariser the plate's e and o waves differ in phase by
//        Γ(ξ) = k₀ d [(nₒ/nₑ) √(nₑ² − ξ²) − √(nₒ² − ξ²)],   ξ = n_inc sin θ,
//    and the transmission is dark where Γ = 2πm. The outer media are index-matched between nₒ and
//    nₑ so reflections barely move the rings.
// 2. An isotropic stack: the map does not depend on azimuth, and between crossed polarisers it is
//    dark wherever a polariser lies along p or s (the Maltese cross).

using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Native.Tests;

internal static class ConoscopeTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    public static void Run()
    {
        Isochromes();
        IsotropicMaps();
        ColoursAndInterpolation();
    }

    private static StackModel Plate(string angles, string azimuths) => StackModel.FromJson($$"""
        { "format": "berreman-stack", "version": 1, "units": { "length": "um", "spectral": "nm" },
          "materials": { "oil": { "n": 1.55 }, "crystal": { "no": 1.5, "ne": 1.6 } },
          "incident": { "material": "oil" }, "layers": [ { "material": "crystal", "thickness": 60 } ],
          "exit": { "material": "oil" },
          "sweep": { "spectral": 600, "angle_deg": {{angles}}, "azimuth_deg": {{azimuths}} } }
        """);

    private static void Isochromes()
    {
        var doc = Plate("""{ "from": 0, "to": 50, "count": 4001 }""", "45").ToDocument();
        var crossed = Conoscope.Quantities(0)[0];
        var r = Evaluator.Evaluate(doc);
        var t = r.Points.Select(crossed.Value).ToArray();
        var th = r.Points.Select(p => p.AngleDeg!.Value).ToArray();
        double no = 1.5, ne = 1.6, k0d = 2 * Math.PI / 600 * 60000;
        double Gamma(double deg) { double xi = 1.55 * Math.Sin(deg * Math.PI / 180); return k0d * (no / ne * Math.Sqrt(ne * ne - xi * xi) - Math.Sqrt(no * no - xi * xi)); }
        /* The predicted dark rings, by bisection on Γ = 2πm. */
        var predicted = new List<double>();
        for (int m = 1; Gamma(50) > 2 * Math.PI * m; ++m)
        {
            double lo = 0, hi = 50;
            for (int k = 0; k < 60; ++k) { double mid = (lo + hi) / 2; if (Gamma(mid) < 2 * Math.PI * m) lo = mid; else hi = mid; }
            predicted.Add((lo + hi) / 2);
        }
        /* The computed minima. */
        var minima = new List<double>();
        double max = t.Max();
        for (int i = 1; i + 1 < t.Length; ++i)
            if (t[i] < t[i - 1] && t[i] <= t[i + 1] && t[i] < 0.05 * max) minima.Add(th[i]);
        Check(predicted.Count >= 6 && minima.Count == predicted.Count, $"the plate shows its {predicted.Count} dark rings ({minima.Count} found)");
        double worst = minima.Zip(predicted, (a, b) => Math.Abs(a - b)).DefaultIfEmpty(99).Max();
        Check(worst < 0.05, $"each where Γ = 2πm (worst {worst:G3}°, rings {string.Join(", ", predicted.Select(x => x.ToString("F2")))}°)");
        Check(t.Take(5).All(x => x < 1e-6), "the centre (the optic axis) is dark between crossed polarisers");
    }

    private static void IsotropicMaps()
    {
        var model = StackModel.FromJson("""
            { "format": "berreman-stack", "version": 1,
              "materials": { "air": { "n": 1 }, "film": { "n": 2.1 }, "glass": { "n": 1.52 } },
              "incident": { "material": "air" }, "layers": [ { "material": "film", "thickness": 120 } ], "exit": { "material": "glass" },
              "sweep": { "spectral": 550, "angle_deg": 0 } }
            """);
        var sweep = Conoscope.Sweep(model, 550, 70, 15, 24);
        var r = Evaluator.Evaluate(sweep.ToDocument());
        var rmean = Conoscope.Quantities().First(q => q.Name == "R_mean");
        var map = Conoscope.Build(r, rmean);
        Check(map.AnglesDeg.Length == 15 && map.AzimuthsDeg.Length == 24 && map.AzimuthsDeg[^1] == 345, "the map\x27s grid: 15 angles to 70°, 24 azimuths to 345°");
        double spread = 0;
        for (int i = 0; i < 15; ++i)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int j = 0; j < 24; ++j) { lo = Math.Min(lo, map.Values[i, j]); hi = Math.Max(hi, map.Values[i, j]); }
            spread = Math.Max(spread, hi - lo);
        }
        Check(spread < 1e-14, $"an isotropic stack\x27s map does not depend on azimuth ({spread:G3})");
        var crossed = Conoscope.Build(r, Conoscope.Quantities(0)[0]);
        double onAxes = 0, diagonal = 0;
        for (int i = 1; i < 15; ++i)
        {
            foreach (int j in new[] { 0, 6, 12, 18 }) onAxes = Math.Max(onAxes, crossed.Values[i, j]);   // 0°, 90°, 180°, 270°
            diagonal = Math.Max(diagonal, crossed.Values[i, 3]);                                          // 45°
        }
        Check(onAxes < 1e-25 && diagonal > 1e-4, $"between crossed polarisers, dark along the polariser\x27s axes and not at 45° (the Maltese cross: {onAxes:G3}, {diagonal:G3})");
        var parallel = Conoscope.Build(r, Conoscope.Quantities(0)[1]);
        Check(Math.Abs(parallel.Values[5, 0] - r.Points.First(p => p.AzimuthDeg == 0 && p.AngleDeg == map.AnglesDeg[5]).Power[1]) < 1e-12,
              "parallel polarisers along p transmit T_p");
    }

    private static void ColoursAndInterpolation()
    {
        Check(Conoscope.Colour(0) == 0xFF440154 && Conoscope.Colour(1) == 0xFFFDE725, "the colour scale runs from viridis\x27s dark end to its light end");
        var map = new ConoscopeMap([0, 10, 20], [0, 90, 180, 270], new double[,] { { 1, 1, 1, 1 }, { 2, 4, 2, 4 }, { 3, 3, 3, 3 } }, "x", 1, 4);
        Check(Conoscope.At(map, 10, 45) == 3 && Conoscope.At(map, 5, 0) == 1.5 && Conoscope.At(map, 10, 315) == 3, "the picture interpolates between grid points, across 360°");
        Check(double.IsNaN(Conoscope.At(map, 25, 0)), "and is empty beyond the largest angle");
    }
}
