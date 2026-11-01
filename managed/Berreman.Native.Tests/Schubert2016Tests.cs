// Schubert2016Tests.cs — monoclinic β-Ga₂O₃ (Schubert et al., PRB 93, 125209 (2016)), the general
// material model's literature check.
//
// The paper derives numbers from its own model that a reader can recompute from the tables: the
// static constants (Table IV, from Eqs. 16–19), the LO frequencies (Table II, the zeros of ε_zz and
// of ε_xx ε_yy − ε_xy² with the broadenings set to zero, Eqs. 22–23), their polarisation angles
// (Eq. 24), and the LPP frequencies with the free carriers added (Table III, Eqs. 25–26). Each is
// computed here from the sample file, through the parser and the term model, so the check covers
// the way the material is written as well as the arithmetic.
//
// THE MISPRINT. Table IV has its a and c* columns switched (Gopalan et al., arXiv:2010.03107, p. 4,
// with Schubert as a co-author). The sample corrects ε∞; the static constants below are Table IV
// with the columns switched back. Table II prints the B_u LO angles from c*, not from a; they are
// compared as such. The B_u LPP frequencies of Table III are not reproduced by the model (the
// highest is 924.6 cm⁻¹, as Fig. 12 shows, not 967.1), so they are printed, not asserted.
//
// TOLERANCES. The tables round every parameter at its last printed digit, so a frequency the paper
// computed from unrounded values can differ from ours by a little more than that digit. Each check
// below allows the paper's own last digit and says so.

using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Berreman.Api;

namespace Berreman.Native.Tests;

internal static class Schubert2016Tests
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
        StaticConstants();
        LongitudinalModes();
        CoupledModes();
        PrincipalAxesTurn();
        ChecksReportWhatTheyShould();
        Figure5();
    }

    // ---- Fig. 5: the (010) surface's Mueller matrix at azimuths P1, P2, P3 ------------------------

    /// <summary>
    /// Values read off Fig. 5 by eye (to about ±0.05), at features in the reststrahlen band away
    /// from the sharpest resonances, as (azimuth φ, element row, column, wavenumber, value). Each
    /// must lie within the range of the sample's three angles of incidence (50°, 60°, 70°), widened by
    /// 0.12 for the reading. M42 is the independent check: M41 and M43 were used to fix the sign of
    /// S3 (Api/Quantities.cs), M42 was not.
    /// </summary>
    private static readonly (double Phi, int I, int J, double W, double Read)[] Fig5 =
    [
        (62.5, 4, 1, 660, 0.20), (62.5, 4, 1, 730, -0.37), (107.5, 4, 1, 530, 0.25), (107.5, 4, 1, 660, -0.30),
        (152.5, 4, 1, 730, 0.30), (152.5, 4, 1, 660, -0.20),
        (62.5, 4, 3, 400, -0.90), (62.5, 4, 3, 750, -0.90), (107.5, 4, 3, 690, -0.80), (152.5, 4, 3, 520, -0.80),
        (62.5, 4, 2, 570, 0.33), (62.5, 4, 2, 690, -0.35), (107.5, 4, 2, 400, 0.55), (107.5, 4, 2, 570, -0.65),
        (107.5, 4, 2, 690, 0.45), (152.5, 4, 2, 690, 0.50), (152.5, 4, 2, 570, -0.20),
    ];

    private static void Figure5()
    {
        var root = JsonNode.Parse(SampleText())!.AsObject();
        foreach (var phi in Fig5.Select(f => f.Phi).Distinct())
        {
            var r = root.DeepClone().AsObject();
            r["exit"]!["euler_deg"] = new JsonArray(phi, 0.4, 0);
            r["sweep"] = new JsonObject
            {
                ["spectral"] = new JsonArray(Fig5.Where(f => f.Phi == phi).Select(f => (JsonNode)f.W).Distinct().Append(1000).ToArray()),
                ["angle_deg"] = new JsonArray(50, 60, 70),
            };
            var result = Evaluator.Evaluate(StackDocument.Parse(r.ToJsonString()));
            foreach (var f in Fig5.Where(f => f.Phi == phi))
            {
                var v = result.Points.Where(p => p.Spectral == f.W).Select(p => Quantities.Mueller(p)[f.I - 1, f.J - 1]).ToList();
                Check(f.Read >= v.Min() - 0.12 && f.Read <= v.Max() + 0.12,
                      $"Fig. 5, φ = {f.Phi}°, M{f.I}{f.J} at {f.W} cm⁻¹: read {f.Read:+0.00;-0.00}, computed {string.Join(" / ", v.Select(x => x.ToString("+0.00;-0.00")))} at 50/60/70°");
            }
            var hi = result.Points.Where(p => p.Spectral == 1000).Select(p => Quantities.Mueller(p)[3, 1]).ToList();
            Console.WriteLine($"  Schubert 2016 Fig. 5, φ = {phi}°: M42 at 1000 cm⁻¹ is {string.Join(" / ", hi.Select(x => x.ToString("+0.00;-0.00")))}; the figure shows about 0 (not reproduced above ~900 cm⁻¹)");
        }
    }

    private static string SampleText()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.Replace('\\', '/').EndsWith("samples/schubert-2016/ga2o3-010.berr"));
        using var st = asm.GetManifestResourceStream(name)!;
        return new StreamReader(st).ReadToEnd();
    }

    // ---- the material, as the sample writes it, and variants of it -------------------------------

    private static JsonObject Spec()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.Replace('\\', '/').EndsWith("samples/schubert-2016/ga2o3-010.berr"));
        using var s = asm.GetManifestResourceStream(name)!;
        var root = JsonNode.Parse(new StreamReader(s).ReadToEnd(), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        return root["materials"]!["Ga2O3"]!.AsObject();
    }

    /// <summary>The material with its broadenings set to zero and, optionally, without free carriers.</summary>
    private static Material Variant(bool lossless, bool carriers)
    {
        var spec = (JsonObject)Spec().DeepClone();
        var terms = spec["terms"]!.AsArray();
        for (int i = terms.Count - 1; i >= 0; --i)
        {
            var t = terms[i]!.AsObject();
            if (t["drude"] is not null && !carriers) { terms.RemoveAt(i); continue; }
            foreach (var law in new[] { "lorentz", "drude" })
                if (lossless && t[law] is JsonObject l) l["gamma"] = 0;
        }
        return MaterialParser.Parse(spec, "Ga2O3");
    }

    private static MaterialMatrix At(Material m, double wavenumber) => m.At(1e7 / wavenumber, 0);

    private static readonly double[] BuTo = [743.48, 692.44, 572.52, 432.57, 356.79, 279.15, 262.34, 213.79];
    private static readonly double[] AuTo = [663.17, 448.66, 296.63, 154.84];

    // ---- static constants (Table IV) -------------------------------------------------------------

    private static void StaticConstants()
    {
        var m = At(Variant(lossless: false, carriers: false), 1e-6);
        // Printed as 12.(7) for a, 10.(9) for c*, which are switched; 11.(2) and −0.1(3).
        Near(m[0, 0].Real, 10.9, 0.1, "ε_DC,xx along a (Table IV, printed under c*: 10.9)");
        Near(m[1, 1].Real, 12.7, 0.1, "ε_DC,yy along c* (Table IV, printed under a: 12.7)");
        Near(m[2, 2].Real, 11.2, 0.1, "ε_DC,zz (Table IV, 11.2)");
        Near(m[0, 1].Real, -0.13, 0.1, "ε_DC,xy (Table IV, −0.13)");
        Check(m[0, 1] == m[1, 0], "ε is symmetric");
    }

    // ---- LO modes (Table II) ---------------------------------------------------------------------

    /// <summary>ε_zz and ε_xx ε_yy − ε_xy², with their poles removed, so their zeros are bracketed by sign changes.</summary>
    private static double AuFunction(Material m, double w, bool carriers)
    {
        var e = At(m, w);
        double g = e[2, 2].Real * (carriers ? w * w : 1);
        foreach (var t in AuTo) g *= t * t - w * w;
        return g;
    }

    private static double BuFunction(Material m, double w, bool carriers)
    {
        var e = At(m, w);
        double g = (e[0, 0] * e[1, 1] - e[0, 1] * e[1, 0]).Real * (carriers ? w * w * w * w : 1);
        foreach (var t in BuTo) g *= t * t - w * w;
        return g;
    }

    /// <summary>The zeros of f on [lo, hi], by a fine scan and bisection, highest first.</summary>
    private static List<double> Zeros(Func<double, double> f, double lo, double hi)
    {
        var zeros = new List<double>();
        const double step = 0.05;
        double a = lo, fa = f(a);
        for (double b = lo + step; b <= hi; b += step)
        {
            double fb = f(b);
            if (Math.Sign(fa) != Math.Sign(fb) && fa != 0)
            {
                double x0 = a, x1 = b, f0 = fa;
                for (int k = 0; k < 60; ++k)
                {
                    double mid = 0.5 * (x0 + x1), fm = f(mid);
                    if (Math.Sign(fm) == Math.Sign(f0)) { x0 = mid; f0 = fm; } else x1 = mid;
                }
                zeros.Add(0.5 * (x0 + x1));
            }
            a = b; fa = fb;
        }
        zeros.Reverse();
        return zeros;
    }

    /// <summary>Eq. 24: the LO polarisation in the a–c plane, tan α = −ε_xx/ε_xy, in (−90°, 90°].</summary>
    private static double Alpha(Material m, double w)
    {
        var e = At(m, w);
        double a = Math.Atan(-e[0, 0].Real / e[0, 1].Real) * 180 / Math.PI;
        return a;
    }

    private static double AngleDiff(double a, double b)
    {
        double d = (a - b) % 180;
        if (d > 90) d -= 180;
        if (d < -90) d += 180;
        return Math.Abs(d);
    }

    private static void LongitudinalModes()
    {
        var m = Variant(lossless: true, carriers: false);

        // A_u: 781.(3), 562.(8), 345.(9), 156.(3); the last digit is the first decimal.
        var au = Zeros(w => AuFunction(m, w, false), 100, 1200);
        double[] auLo = [781.3, 562.8, 345.9, 156.3];
        Check(au.Count == 4, $"4 A_u LO modes (found {au.Count}: {string.Join(", ", au.Select(x => x.ToString("F2")))})");
        for (int k = 0; k < Math.Min(au.Count, 4); ++k)
            Near(au[k], auLo[k], 0.5, $"A_u LO {k + 1} (Table II, {auLo[k]})");

        // B_u: 81(0), 77(0), 70(9), 59(5), 38(9), 30(5), 28(6), 26(9); the last digit is the units.
        var bu = Zeros(w => BuFunction(m, w, false), 100, 1200);
        double[] buLo = [810, 770, 709, 595, 389, 305, 286, 269];
        double[] buAlpha = [73, -30, 6, 73, -31, -42, 21, 27];   // 7(3), −3(0), (6), 7(3), −3(1), −4(2), 2(1), 2(7)
        Check(bu.Count == 8, $"8 B_u LO modes (found {bu.Count}: {string.Join(", ", bu.Select(x => x.ToString("F1")))})");
        for (int k = 0; k < Math.Min(bu.Count, 8); ++k)
        {
            Near(bu[k], buLo[k], 2, $"B_u LO {k + 1} (Table II, {buLo[k]})");
            Check(AngleDiff(90 - Alpha(m, bu[k]), buAlpha[k]) <= 3,
                  $"B_u LO {k + 1} polarisation from c* (Table II, {buAlpha[k]}°): got {90 - Alpha(m, bu[k]):F1}°");
        }
    }

    // ---- LPP modes with the free carriers (Table III) ---------------------------------------------

    private static void CoupledModes()
    {
        var m = Variant(lossless: true, carriers: true);

        // A_u: 88(5), 60(3), 38(0), 23(9), 15(4).
        var au = Zeros(w => AuFunction(m, w, true), 50, 1500);
        double[] auLpp = [885, 603, 380, 239, 154];
        Check(au.Count == 5, $"5 A_u LPP modes (found {au.Count}: {string.Join(", ", au.Select(x => x.ToString("F1")))})");
        for (int k = 0; k < Math.Min(au.Count, 5); ++k)
            Near(au[k], auLpp[k], 2, $"A_u LPP {k + 1} (Table III, {auLpp[k]})");

        // B_u: 967.(1) ... 188.(8); the last digit is the first decimal.
        var bu = Zeros(w => BuFunction(m, w, true), 50, 1500);
        double[] buLpp = [967.1, 872.9, 730.7, 638.2, 458.1, 357.9, 331.8, 277.4, 226.4, 188.8];
        Check(bu.Count == 10, $"10 B_u LPP modes (found {bu.Count}: {string.Join(", ", bu.Select(x => x.ToString("F1")))})");
        Console.WriteLine("  Schubert 2016 B_u LPP, model against Table III: "
            + string.Join(", ", bu.Zip(buLpp, (g, t) => $"{g:F1}/{t}")));
    }

    // ---- the reason for the general model: the principal axes turn with wavelength ---------------

    private static void PrincipalAxesTurn()
    {
        var m = Variant(lossless: false, carriers: false);
        double InPlane(double w)
        {
            var (_, axes) = MaterialChecks.PrincipalAxes(At(m, w));
            /* The axis of the largest value that lies in the a–c plane. */
            for (int c = 0; c < 3; ++c)
                if (Math.Abs(axes[2, c]) < 0.5) return Math.Atan2(axes[1, c], axes[0, c]) * 180 / Math.PI;
            return double.NaN;
        }
        double far = InPlane(1200), mid = InPlane(500), low = InPlane(150);
        Check(AngleDiff(far, mid) > 5 && AngleDiff(mid, low) > 5,
              $"the in-plane principal axis turns with wavelength (1200: {far:F1}°, 500: {mid:F1}°, 150: {low:F1}° from a)");
    }

    // ---- passivity and reciprocity are reported, not enforced ------------------------------------

    private static void ChecksReportWhatTheyShould()
    {
        var lossy = At(Variant(lossless: false, carriers: true), 600);
        Check(MaterialChecks.LeastLoss(lossy) >= -1e-12, $"Ga₂O₃ is passive (least loss {MaterialChecks.LeastLoss(lossy):G3})");
        Check(MaterialChecks.NonReciprocity(lossy) == 0, "Ga₂O₃ is reciprocal");
        var (least, greatest) = MaterialChecks.LossRange(At(Variant(lossless: true, carriers: false), 600));
        Check(Math.Abs(least) < 1e-12 && Math.Abs(greatest) < 1e-12, "with no broadening it is lossless");
        Check(MaterialChecks.LossRange(lossy).Greatest > 0.1, "with its broadening it absorbs (the greatest loss is positive, though the least is 0 from μ)");

        var gain = MaterialParser.Parse(JsonNode.Parse("""{ "terms": [ { "eps": [[2, [0, -0.1], 0], [[0, 0.1], 2, 0], [0, 0, [2, -0.01]]] } ] }"""), "g");
        var g = gain.At(500, 0);
        Check(MaterialChecks.LeastLoss(g) < 0, "a negative imaginary ε is reported as gain");
        Check(MaterialChecks.NonReciprocity(g) > 0, "an imaginary antisymmetric ε (gyration) is reported as non-reciprocal");
    }
}
