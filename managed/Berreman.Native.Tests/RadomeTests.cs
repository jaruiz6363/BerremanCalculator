// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// RadomeTests.cs — microwave radome walls with complete inputs: Anshin et al. (2024), isotropic,
// against the paper's calculated curves; Peshlov et al. (2006), uniaxial layers, against MEASURED
// insertion loss. Figure values were read off the figures enlarged to 300 dpi.

using Berreman.Api;
using Berreman.Native;

namespace Berreman.Native.Tests;

internal static class RadomeTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static void Near(double got, double expected, double tol, string what)
    {
        ++Checks;
        if (!(Math.Abs(got - expected) <= tol)) { ++Failures; Console.WriteLine($"  FAIL  {what}: got {got:G5}, expected {expected:G5} ± {tol:G3}"); }
    }

    public static void Run()
    {
        AnshinSandwich();
        PeshlovHoneycombAgainstMeasurement();
        PeshlovTwintexSWaveSeesOnlyTheInPlanePermittivity();
    }

    private static double Db(double t) => 10 * Math.Log10(t);

    /// <summary>
    /// Anshin, Figs. 4 and 5: the dip of |T| for each angle, from the paper's calculated (dotted)
    /// curves. The paper's thicknesses are approximate ("≈ 1.8 mm", "≈ 6.9 mm"), so the tolerance is
    /// 0.4 dB in depth and 0.6 GHz in position.
    /// </summary>
    private static void AnshinSandwich()
    {
        var res = Evaluator.Evaluate(ApiTests.Sample("anshin-2024/sandwich-radome.berr"));
        Check(res.Status == BcStatus.Ok, "Anshin evaluates");
        var figure = new (string Pol, double Angle, double Db, double Ghz)[]
        {
            ("p", 0, -2.66, 14.3), ("p", 20, -2.40, 15.0), ("p", 40, -1.62, 16.8),
            ("s", 0, -2.65, 14.4), ("s", 20, -3.13, 14.9), ("s", 40, -4.78, 16.8), ("s", 60, -8.98, 19.9),
        };
        foreach (var (pol, angle, db, ghz) in figure)
        {
            int col = pol == "p" ? 1 : 4;
            var dip = res.Points.Where(p => p.AngleDeg == angle && p.Spectral >= 10 && p.Spectral <= 22.5)
                                .MinBy(p => p.Power[col])!;
            Console.WriteLine($"  Anshin {pol} {angle,2}°: dip {Db(dip.Power[col]):F2} dB at {dip.Spectral} GHz (figure {db:F2} at {ghz})");
            Near(Db(dip.Power[col]), db, 0.4, $"Anshin {pol} {angle}°: dip depth");
            Near(dip.Spectral, ghz, 0.6, $"Anshin {pol} {angle}°: dip frequency");
        }
        /* p at 60°: no dip in the band; the paper's curve slides to about −0.47 dB at 24 GHz. */
        var p60 = res.Points.Where(p => p.AngleDeg == 60).MinBy(p => Math.Abs(p.Spectral - 24))!;
        Near(Db(p60.Power[1]), -0.47, 0.1, "Anshin p 60°: |T| at 24 GHz");
    }

    /// <summary>
    /// Peshlov, Fig. 11: insertion loss at 12.5 GHz, MEASURED, with error bars, for normal (s) and
    /// parallel (p) polarisation. Tolerance: the error bar plus 0.03 dB for reading the figure.
    /// </summary>
    private static void PeshlovHoneycombAgainstMeasurement()
    {
        var res = Evaluator.Evaluate(ApiTests.Sample("peshlov-2006/honeycomb-model3.berr"));
        Check(res.Status == BcStatus.Ok, "Peshlov Model 3 evaluates");
        var measured = new (string Pol, double Angle, double Db, double Bar)[]
        {
            ("s", 0, -0.08, 0.05), ("s", 25, -0.11, 0.05), ("s", 45, -0.23, 0.06), ("s", 65, -0.60, 0.10),
            ("p", 0, -0.07, 0.05), ("p", 25, -0.09, 0.05), ("p", 45, -0.08, 0.05), ("p", 70, -0.18, 0.07),
        };
        foreach (var (pol, angle, db, bar) in measured)
        {
            var pt = res.Points.First(p => p.AngleDeg == angle);
            double il = Db(pt.Power[pol == "p" ? 1 : 4]);
            bool inBar = Math.Abs(il - db) <= bar;
            Console.WriteLine($"  Peshlov Fig. 11 {(pol == "s" ? "NP" : "PP")} {angle,2}°: {il:F3} dB, measured {db:F2} ± {bar:F2}{(inBar ? "" : "  (outside the error bar)")}");
            Near(il, db, bar + 0.03, $"Peshlov {pol} {angle}°: IL against measurement");
        }
    }

    /// <summary>
    /// An s wave (the paper's NP) has E in the plane of the wall, so a uniaxial wall with its axis
    /// normal to the wall acts on it exactly as an isotropic wall of ε∥. Peshlov's Fig. 5a shows
    /// his anisotropic Model 1a between the isotropic models instead, because his formulas use ε⊥
    /// for the phase (his Eqs. 2–3); the exact physics puts it on Model 3i.
    /// </summary>
    private static void PeshlovTwintexSWaveSeesOnlyTheInPlanePermittivity()
    {
        var doc = ApiTests.Sample("peshlov-2006/twintex.berr");
        var aniso = Evaluator.Evaluate(doc);
        var iso = Evaluator.Evaluate(StackDocument.Parse("""
            { "format": "berreman-stack", "version": 1,
              "units": { "length": "mm", "spectral": "GHz" },
              "materials": { "air": { "n": 1 }, "par": { "eps": [3.25, -0.02015], "convention": "exp(+jwt)" } },
              "incident": { "material": "air" }, "exit": { "material": "air" },
              "layers": [ { "material": "par", "thickness": 1.9 } ],
              "sweep": { "spectral": { "from": 2, "to": 15, "step": 0.5 }, "angle_deg": [0, 30, 60] } }
            """));
        double worstS = 0, pDiff = 0;
        for (int i = 0; i < aniso.Points.Count; ++i)
        {
            worstS = Math.Max(worstS, Math.Abs(aniso.Points[i].Power[4] - iso.Points[i].Power[4]));
            pDiff = Math.Max(pDiff, Math.Abs(aniso.Points[i].Power[1] - iso.Points[i].Power[1]));
        }
        Near(worstS, 0, 1e-14, "TWINTEX: the s wave sees only ε∥ (anisotropic = isotropic ε∥)");
        Check(pDiff > 1e-4, "TWINTEX: the p wave does see ε⊥ (anisotropic ≠ isotropic ε∥ at oblique incidence)");
        Console.WriteLine($"  Peshlov TWINTEX: s wave, anisotropic against isotropic ε∥: {worstS:E1}; p wave differs by up to {pDiff:E1}");
    }
}
