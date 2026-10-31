// ClosedFormTests.cs — the closed-form literature samples, each against a formula computed here
// independently of the engine.

using System.Numerics;
using Berreman.Api;
using Berreman.Native;

namespace Berreman.Native.Tests;

internal static class ClosedFormTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static void Small(double value, double tol, string what)
    {
        ++Checks;
        if (!(Math.Abs(value) <= tol)) { ++Failures; Console.WriteLine($"  FAIL  {what}: {value:G4} exceeds {tol:G3}"); }
    }

    public static void Run()
    {
        GyrotropicSlab("berreman-1972/faraday-slab.json");
        GyrotropicSlab("poddar-2020/gyrotropic-slab.json");
        FerriteFaradaySlab();
        TransverseFerriteHalfSpace();
        AdamsResonanceField();
        MeiTiltedBilayer();
        RadiMatchedSlab();
        SalisburyScreen();
    }

    // ---- the circular Airy slab ---------------------------------------------------------------

    private static Complex Forward(Complex z)
    {
        var q = Complex.Sqrt(z);
        if (q.Imaginary < 0 || (q.Imaginary == 0 && q.Real < 0)) q = -q;
        return q;
    }

    /// <summary>
    /// A slab of (ε, μ) between media of index n0 at normal incidence: Airy's formulas with the
    /// admittance Y = n/μ. Returns (r, t) with t referred to the slab's two faces.
    /// </summary>
    private static (Complex R, Complex T) Airy(Complex eps, Complex mu, double n0, double k0d)
    {
        var n = Forward(eps * mu);
        var y = n / mu;
        var r = (n0 - y) / (n0 + y);
        var e = Complex.Exp(Complex.ImaginaryOne * k0d * n);
        var den = 1 - r * r * e * e;
        return (r * (1 - e * e) / den, (1 - r * r) * e / den);
    }

    /// <summary>Eigenvalues of a 2×2 matrix [[a, b], [c, d]].</summary>
    private static (Complex, Complex) Eig2(Complex a, Complex b, Complex c, Complex d)
    {
        var h = (a + d) / 2;
        var s = Complex.Sqrt((a - d) * (a - d) / 4 + b * c);
        return (h + s, h - s);
    }

    /// <summary>
    /// A slab whose ε and μ both have the form [[a, −ib, 0], [ib, a, 0], [0, 0, c]] at normal
    /// incidence. The circular waves see (ε ± g, μ ± κ), so the transmission Jones matrix has the
    /// two circular Airy transmissions as its eigenvalues, whatever the sign convention, and
    /// R for a linearly polarised wave is the mean of the two circular reflectances.
    /// </summary>
    private static void CircularCheck(EvaluationResult res, Func<double, (Complex e, Complex g, Complex u, Complex k, double n0, double d)> at, string what)
    {
        double worst = 0, worstR = 0;
        foreach (var p in res.Points)
        {
            Check(p.Status == BcStatus.Ok, $"{what}: evaluates");
            var (e, g, u, k, n0, d) = at(p.WavelengthNm);
            double k0d = 2 * Math.PI * d / p.WavelengthNm;
            var plus = Airy(e + g, u + k, n0, k0d);
            var minus = Airy(e - g, u - k, n0, k0d);
            var (l1, l2) = Eig2(p.Jones[4], p.Jones[6], p.Jones[5], p.Jones[7]);   /* T[out][in] */
            double err = Math.Min(
                Math.Max((l1 - plus.T).Magnitude, (l2 - minus.T).Magnitude),
                Math.Max((l1 - minus.T).Magnitude, (l2 - plus.T).Magnitude));
            worst = Math.Max(worst, err);
            double r = (plus.R.Magnitude * plus.R.Magnitude + minus.R.Magnitude * minus.R.Magnitude) / 2;
            worstR = Math.Max(worstR, Math.Max(Math.Abs(p.Power[0] - r), Math.Abs(p.Power[3] - r)));
        }
        Small(worst, 1e-11, $"{what}: Jones t eigenvalues are the circular Airy transmissions");
        Small(worstR, 1e-12, $"{what}: R is the mean circular reflectance");
        Console.WriteLine($"  {what}: worst eigenvalue error {worst:E1}, R error {worstR:E1}");
    }

    private static Complex[,] Block(MaterialMatrix m, int b) => new Complex[,]
    {
        { m[3 * b, 3 * b], m[3 * b, 3 * b + 1] },
        { m[3 * b + 1, 3 * b], m[3 * b + 1, 3 * b + 1] },
    };

    private static void GyrotropicSlab(string sample)
    {
        var doc = ApiTests.Sample(sample);
        var res = Evaluator.Evaluate(doc);
        var slab = doc.Materials[doc.Layers[0].Material];
        double n0 = Math.Sqrt(doc.Materials[doc.Incident.Material].At(500, 0)[0, 0].Real);
        CircularCheck(res, lambda =>
        {
            var m = slab.At(lambda, doc.Layers[0].ThicknessNm);
            var eb = Block(m, 0);
            var ub = Block(m, 1);
            return (eb[0, 0], eb[1, 0] / Complex.ImaginaryOne, ub[0, 0], ub[1, 0] / Complex.ImaginaryOne, n0, doc.Layers[0].ThicknessNm);
        }, sample);
    }

    /// <summary>A Polder ferrite slab, axial bias, with loss: the microwave Faraday case.</summary>
    private static void FerriteFaradaySlab()
    {
        const string json = """
            { "format": "berreman-stack", "version": 1,
              "units": { "length": "mm", "spectral": "GHz" },
              "materials": { "air": { "n": 1 },
                "yig": { "model": "ferrite", "four_pi_ms_gauss": 1780, "h0_oe": 1500, "alpha": 0.002, "eps_r": 15, "tan_delta": 0.0002 } },
              "incident": { "material": "air" }, "exit": { "material": "air" },
              "layers": [ { "material": "yig", "thickness": 3 } ],
              "sweep": { "spectral": { "from": 2, "to": 12, "step": 0.25 }, "angle_deg": 0 } }
            """;
        var doc = StackDocument.Parse(json);
        var res = Evaluator.Evaluate(doc);
        var yig = (FerriteMaterial)doc.Materials["yig"];
        CircularCheck(res, lambda =>
        {
            var m = yig.At(lambda, 0);
            var (mu, kappa) = yig.Polder(Units.FromWavelengthNm(lambda, "GHz"));
            return (m[0, 0], 0, mu, kappa, 1.0, 3e6);
        }, "Polder ferrite slab, axial bias");

        /* Passive: μ″ ≥ 0 and the circular permeabilities μ ± κ have non-negative imaginary parts. */
        bool passive = true;
        foreach (var f in new[] { 1.0, 3.0, 4.2, 4.5, 5.0, 9.0, 20.0 })
        {
            var (mu, kappa) = yig.Polder(f);
            passive &= mu.Imaginary >= 0 && (mu + kappa).Imaginary >= -1e-15 && (mu - kappa).Imaginary >= -1e-15;
        }
        Check(passive, "Polder ferrite with α > 0 is passive in exp(−iωt)");
    }

    /// <summary>
    /// Thompson (1956), Eq. 13: with the bias in the surface, E along it, the wave sees
    /// μ_eff = (μ² − κ²)/μ, and a lossless half-space reflects totally where μ_eff &lt; 0.
    /// </summary>
    private static void TransverseFerriteHalfSpace()
    {
        var doc = ApiTests.Sample("thompson-1956/transverse-ferrite.json");
        var res = Evaluator.Evaluate(doc);
        var fer = (FerriteMaterial)doc.Materials["ferrite"];
        double worst = 0;
        int negative = 0;
        foreach (var p in res.Points)
        {
            Check(p.Status == BcStatus.Ok, "transverse ferrite evaluates");
            var (mu, kappa) = fer.Polder(p.Spectral);
            var muEff = (mu * mu - kappa * kappa) / mu;
            var q2 = Forward(15 * muEff);
            var rss = (muEff - q2) / (muEff + q2);                 /* s: E along the bias */
            var rpp = (15 - Math.Sqrt(15)) / (15 + Math.Sqrt(15));  /* p: sees μ = 1 */
            worst = Math.Max(worst, Math.Max((p.Rss - rss).Magnitude, Math.Abs(p.Rpp.Real - rpp) + Math.Abs(p.Rpp.Imaginary)));
            if (muEff.Real < 0)
            {
                ++negative;
                Small(p.Power[3] - 1, 1e-12, "μ_eff < 0: total reflection (Thompson)");
            }
        }
        Small(worst, 1e-12, "transverse ferrite: r against Thompson's μ_eff");
        Check(negative > 0, "the sweep crosses a band of negative μ_eff");
        Console.WriteLine($"  transverse ferrite: worst r error {worst:E1}; {negative} points with μ_eff < 0");
    }

    /// <summary>
    /// Adam et al. (2023), Fig. 7.5: at 1 GHz and 4πMs = 1000 G the transverse μ_eff resonance (μ = 0)
    /// sits at about 115 Oe. Found here by bisection on the model's μ.
    /// </summary>
    private static void AdamsResonanceField()
    {
        /* μ changes sign twice at 1 GHz: through zero (the μ_eff resonance, the one Adam shows) and
         * through its pole at ω0 = ω (357 Oe). The bracket [1, 300] Oe holds only the zero. */
        double lo = 1, hi = 300;
        for (int i = 0; i < 100; ++i)
        {
            double mid = (lo + hi) / 2;
            var (mu, _) = new FerriteMaterial(1000, mid, 0, 2.8, MaterialMatrix.Scalar(1)).Polder(1.0);
            if (mu.Real > 0) lo = mid; else hi = mid;
        }
        Console.WriteLine($"  Adam Fig. 7.5: μ = 0 at H0 = {lo:F1} Oe (figure: about 115)");
        Small(lo - 115, 3, "μ_eff resonance near 115 Oe at 1 GHz, 4πMs = 1000 G");
    }

    /// <summary>
    /// Mei et al. (2012): each slab reflects nothing at any angle and adds the phase −k0 d ξ c to
    /// the vacuum one; the pair transmits exactly as vacuum.
    /// </summary>
    private static void MeiTiltedBilayer()
    {
        var doc = ApiTests.Sample("mei-2012/tilted-bilayer.json");
        var res = Evaluator.Evaluate(doc);
        double worstR = 0, worstT = 0;
        foreach (var p in res.Points)
        {
            Check(p.Status == BcStatus.Ok, "Mei bilayer evaluates");
            double k0 = 2 * Math.PI / p.WavelengthNm, cos = Math.Sqrt(1 - p.Xi * p.Xi);
            var vacuum = Complex.Exp(Complex.ImaginaryOne * k0 * cos * 880);
            worstR = Math.Max(worstR, p.Power[0]);
            worstT = Math.Max(worstT, (p.Jones[4] - vacuum).Magnitude);
        }
        Small(worstR, 1e-20, "Mei: R_p = 0 at every angle");
        /* c is given to 12 decimals and multiplied by k0 d ξ ≈ 7: about 1e-11 of phase. */
        Small(worstT, 1e-10, "Mei: the pair transmits p exactly as 880 nm of vacuum");

        /* One slab alone: no reflection, and the extra phase −k0 d ξ c. */
        var one = StackDocument.Parse(System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new
        {
            format = "berreman-stack", version = 1,
            materials = new { air = new { n = 1 }, slab1 = new { eps = new object[] { new object[] { 5.005506043777, 0, 2.001376037574 }, new object[] { 0, 1, 0 }, new object[] { 2.001376037574, 0, 1 } } } },
            incident = new { material = "air" }, exit = new { material = "air" },
            layers = new[] { new { material = "slab1", thickness = 440 } },
            sweep = new { spectral = 365, angle_deg = new[] { 0, 30, 60, 80 } },
        }))!.ToJsonString());
        double worst1 = 0;
        foreach (var p in Evaluator.Evaluate(one).Points)
        {
            double k0 = 2 * Math.PI / p.WavelengthNm, cos = Math.Sqrt(1 - p.Xi * p.Xi);
            var expect = Complex.Exp(Complex.ImaginaryOne * k0 * 440 * (cos - p.Xi * 2.001376037574));
            worst1 = Math.Max(worst1, Math.Max(p.Power[0], (p.Jones[4] - expect).Magnitude));
        }
        Small(worst1, 1e-10, "Mei: one slab, no reflection and phase k0 d (cos θ − c sin θ)");
        Console.WriteLine($"  Mei bilayer: worst R_p {worstR:E1}, worst t_pp error {worstT:E1}");
    }

    /// <summary>Ra'di et al. (2015), Section V A 1: ε = μ has the impedance of free space.</summary>
    private static void RadiMatchedSlab()
    {
        var doc = ApiTests.Sample("radi-2015/matched-slab.json");
        var res = Evaluator.Evaluate(doc);
        var a = new Complex(3, 0.4);
        double worst = 0;
        foreach (var p in res.Points)
        {
            var e2 = Complex.Exp(2 * Complex.ImaginaryOne * 2 * Math.PI / p.WavelengthNm * 2e6 * a);
            worst = Math.Max(worst, Math.Max((p.Rpp - 0.2 * e2).Magnitude, (p.Rss + 0.2 * e2).Magnitude));
        }
        Small(worst, 1e-12, "Ra'di: r = ±0.2 e^{2iδ}, only the back face reflects");

        /* And a half-space of ε = μ reflects nothing at normal incidence, whatever the loss. */
        var half = StackDocument.Parse("""
            { "format": "berreman-stack", "version": 1,
              "materials": { "air": { "n": 1 }, "m": { "eps": [3, 0.4], "mu": [3, 0.4] } },
              "incident": { "material": "air" }, "exit": { "material": "m" }, "layers": [],
              "sweep": { "spectral": 500, "angle_deg": 0 } }
            """);
        var h = Evaluator.Evaluate(half).Points[0];
        Small(h.Rpp.Magnitude + h.Rss.Magnitude, 1e-15, "Ra'di: an ε = μ half-space reflects nothing");
    }

    /// <summary>
    /// A Salisbury screen: for a perfect conductor and an infinitely thin 377-ohm sheet,
    /// R = c²/(4 + c²) with c = cot(k0 d). Copper and a 1 µm sheet differ from that ideal by
    /// about 10⁻⁴.
    /// </summary>
    private static void SalisburyScreen()
    {
        var doc = ApiTests.Sample("costa-2016/salisbury.json");
        var res = Evaluator.Evaluate(doc);
        double worst = 0;
        foreach (var p in res.Points)
        {
            double c = 1 / Math.Tan(2 * Math.PI / p.WavelengthNm * 5e6);
            worst = Math.Max(worst, Math.Abs(p.Power[0] - c * c / (4 + c * c)));
        }
        Small(worst, 1e-3, "Salisbury: R = c²/(4 + c²)");

        /* The −15 dB band, its edges interpolated linearly in dB between samples. */
        var pts = res.Points;
        double Db(int i) => 10 * Math.Log10(pts[i].Power[0]);
        double f1 = double.NaN, f2 = double.NaN;
        for (int i = 1; i < pts.Count; ++i)
        {
            double a = Db(i - 1) + 15, b = Db(i) + 15;
            if (a > 0 && b <= 0 && double.IsNaN(f1)) f1 = pts[i - 1].Spectral + (pts[i].Spectral - pts[i - 1].Spectral) * a / (a - b);
            if (a <= 0 && b > 0) f2 = pts[i - 1].Spectral + (pts[i].Spectral - pts[i - 1].Spectral) * a / (a - b);
        }
        double bw = (f2 - f1) / ((f1 + f2) / 2);
        Console.WriteLine($"  Salisbury: worst R error {worst:E1}; −15 dB bandwidth {100 * bw:F1}% (ideal 44.2%; Costa Table 1: 45.4%)");
        Small(bw - 0.442, 0.005, "Salisbury: −15 dB fractional bandwidth is the ideal 44.2%");
    }
}
