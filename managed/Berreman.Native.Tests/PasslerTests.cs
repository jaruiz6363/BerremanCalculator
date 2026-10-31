// PasslerTests.cs — Passler and Paarmann (2017), the Otto-geometry samples.
//
// Two kinds of check:
//   1. The engine against an independent reference FOR THESE INPUTS. The crystals are c-cut, so
//      every M is diagonal and the p wave stays in its own 2×2 block, whose propagator has a closed
//      form (Berreman's Eq. 41). This settles that the engine computes these stacks correctly.
//   2. The paper's stated mode frequencies, and Fig. 2(a) read off the figure. The figure's dips
//      are at the same frequencies, but not the same depths. The authors' own code, run with the
//      same prism data, agrees with this program to 5e-11 and gives these depths, not the
//      figure's (samples/README.md). So the depths are printed, not asserted.

using System.Numerics;
using Berreman.Api;
using Berreman.Native;

namespace Berreman.Native.Tests;

internal static class PasslerTests
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
        if (!(Math.Abs(got - expected) <= tol)) { ++Failures; Console.WriteLine($"  FAIL  {what}: got {got:G6}, expected {expected:G6} ± {tol:G3}"); }
    }

    public static void Run()
    {
        foreach (var s in new[] { "sic-otto.json", "gan-sic-otto.json", "sic-gan-sic-otto.json" })
            AgreesWithTheBlockReference(s);
        SiCCriticalCoupling();
        GaNOnSiCModes();
        SiCGaNSiCModes();
    }

    private static StackDocument Doc(string name, double? gapUm = null)
    {
        var doc = ApiTests.Sample("passler-2017/" + name);
        if (gapUm is null) return doc;
        var layers = doc.Layers.ToList();
        layers[0] = new StackLayer { Material = layers[0].Material, ThicknessNm = gapUm.Value * 1000, EulerDeg = layers[0].EulerDeg };
        return new StackDocument
        {
            Title = doc.Title, Source = doc.Source, Notes = doc.Notes, LengthUnit = doc.LengthUnit, SpectralUnit = doc.SpectralUnit,
            Materials = doc.Materials, Incident = doc.Incident, Exit = doc.Exit, Layers = layers,
            Spectral = doc.Spectral, AzimuthsDeg = doc.AzimuthsDeg, AnglesDeg = doc.AnglesDeg, Xi = doc.Xi,
        };
    }

    // ---- 1. the p block, in closed form ------------------------------------------------------

    private static Complex Kappa(Complex a, Complex b)
    {
        var k = Complex.Sqrt(a * b);
        if (k.Imaginary < 0) k = -k;
        if (k.Imaginary == 0 && (a * Complex.Conjugate(k)).Real < 0) k = -k;
        return k;
    }

    /// <summary>R_p for a stack of diagonal M, from the p block [[0, a], [b, 0]], a = μyy − ξ²/εzz, b = εxx.</summary>
    private static double BlockRp(StackDocument doc, double lambdaNm, double xi)
    {
        (Complex a, Complex b) Blk(MaterialMatrix m) => (m[4, 4] - xi * xi / m[2, 2], m[0, 0]);
        var (ai, bi) = Blk(doc.Materials[doc.Incident.Material].At(lambdaNm, 0));
        var (ae, be) = Blk(doc.Materials[doc.Exit.Material].At(lambdaNm, 0));
        Complex ki = Kappa(ai, bi), ke = Kappa(ae, be);
        Complex x0 = ae, x1 = ke;   /* the exit's forward mode, carried back to the front */
        double k0 = 2 * Math.PI / lambdaNm;
        for (int k = doc.Layers.Count - 1; k >= 0; --k)
        {
            var (a, b) = Blk(doc.Materials[doc.Layers[k].Material].At(lambdaNm, doc.Layers[k].ThicknessNm));
            var kap = Complex.Sqrt(a * b);
            var th = k0 * doc.Layers[k].ThicknessNm * kap;
            var c = Complex.Cos(th);
            var s = kap == Complex.Zero ? k0 * doc.Layers[k].ThicknessNm : Complex.Sin(th) / kap;
            var i = Complex.ImaginaryOne;
            (x0, x1) = (c * x0 - i * a * s * x1, -i * b * s * x0 + c * x1);   /* inverse propagator */
        }
        /* [X, −bk] [t; r] = f, with f = (ai, ki), bk = (ai, −ki) */
        Complex det = x0 * ki - (-ai) * x1;
        Complex r = (x0 * ki - x1 * ai) / det;
        return r.Magnitude * r.Magnitude;
    }

    private static void AgreesWithTheBlockReference(string name)
    {
        var doc = Doc(name);
        var res = Evaluator.Evaluate(doc);
        double worst = 0;
        foreach (var p in res.Points)
        {
            Check(p.Status == BcStatus.Ok, $"{name}: evaluates");
            worst = Math.Max(worst, Math.Abs(p.Power[0] - BlockRp(doc, p.WavelengthNm, p.Xi)));
        }
        Near(worst, 0, 1e-11, $"{name}: R_p against the closed-form p block");
        Console.WriteLine($"  Passler {name}: R_p against the block reference, worst {worst:E1}");
    }

    // ---- 2. the paper ------------------------------------------------------------------------

    /// <summary>Local minima of R_p below a ceiling, as (frequency, R).</summary>
    private static List<(double F, double R)> Minima(EvaluationResult res, double below)
    {
        var p = res.Points;
        var m = new List<(double, double)>();
        for (int i = 1; i + 1 < p.Count; ++i)
            if (p[i].Power[0] < p[i - 1].Power[0] && p[i].Power[0] < p[i + 1].Power[0] && p[i].Power[0] < below)
                m.Add((p[i].Spectral, p[i].Power[0]));
        return m;
    }

    private static (double F, double R) DeepestIn(EvaluationResult res, double lo, double hi) =>
        res.Points.Where(p => p.Spectral >= lo && p.Spectral <= hi).Select(p => (p.Spectral, p.Power[0])).MinBy(x => x.Item2);

    /// <summary>
    /// Fig. 2(a): the SPhP dip near 910 cm⁻¹ for every gap, deepest (critical coupling) at 5.5 µm.
    /// Read off the figure: dips at about 907, 910 and 911 cm⁻¹ for 3.5, 5.5 and 7.5 µm, with
    /// R ≈ 0.41, 0.0 and 0.39; the 2 µm curve is broad, about 0.77 near 900.
    /// </summary>
    private static void SiCCriticalCoupling()
    {
        var figure = new Dictionary<double, (double F, double R)> { [2] = (900, 0.77), [3.5] = (907, 0.41), [5.5] = (910, 0.0), [7.5] = (911, 0.39) };
        var depth = new Dictionary<double, double>();
        foreach (var (gap, (f, r)) in figure)
        {
            var d = DeepestIn(Evaluator.Evaluate(Doc("sic-otto.json", gap)), 893, 930);
            depth[gap] = d.R;
            Near(d.F, f, gap == 2 ? 8 : 4, $"Fig. 2(a): the SPhP dip at {gap} µm is at the figure's frequency");
            Console.WriteLine($"  Passler Fig. 2(a), gap {gap} µm: dip at {d.F} cm-1, R_p {d.R:F2} (figure: about {f}, {r:F2})");
        }
        Check(depth[5.5] < depth[3.5] && depth[5.5] < depth[7.5] && depth[5.5] < depth[2], "Fig. 2(a): critical coupling at 5.5 µm");
    }

    private static void HasMinimumNear(List<(double F, double R)> minima, double f, double tol, string what)
    {
        var best = minima.Count == 0 ? double.NaN : minima.MinBy(m => Math.Abs(m.F - f)).F;
        Near(best, f, tol, what);
    }

    /// <summary>Fig. 3: resonances near 680 (broad), 810–820 and 1000 cm⁻¹ at the 3.5 µm gap.</summary>
    private static void GaNOnSiCModes()
    {
        var m = Minima(Evaluator.Evaluate(Doc("gan-sic-otto.json")), 0.7);
        Console.WriteLine($"  Passler Fig. 3: minima at {string.Join(", ", m.Select(x => $"{x.F} ({x.R:F2})"))}");
        HasMinimumNear(m, 680, 20, "Fig. 3: mode (i), the broad GaN SPhP, near 680");
        HasMinimumNear(m, 815, 8, "Fig. 3: mode (ii) near 810–820");
        HasMinimumNear(m, 1000, 8, "Fig. 3: mode (iii) near 1000");
    }

    /// <summary>
    /// Fig. 4: mode (ii) at the upper GaN Reststrahlen edge (text ≈ 750; the figure's label sits
    /// near 760–770), modes (iii) and (iv) around 900, mode (v) near 1000.
    /// </summary>
    private static void SiCGaNSiCModes()
    {
        var m = Minima(Evaluator.Evaluate(Doc("sic-gan-sic-otto.json")), 0.7);
        Console.WriteLine($"  Passler Fig. 4: minima at {string.Join(", ", m.Select(x => $"{x.F} ({x.R:F2})"))}");
        HasMinimumNear(m, 765, 15, "Fig. 4: mode (ii) at the upper GaN edge");
        Check(m.Count(x => x.F > 860 && x.F < 950) == 2, "Fig. 4: two modes, (iii) and (iv), around 900");
        HasMinimumNear(m, 1000, 8, "Fig. 4: mode (v) near 1000");
    }
}
