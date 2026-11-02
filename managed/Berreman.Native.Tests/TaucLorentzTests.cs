// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// TaucLorentzTests.cs — the Tauc–Lorentz law's real part against its own definition.
//
// The closed form of Re χ (Jellison and Modine 1996, with its erratum) is long, and was
// transcribed from a secondary source. Its definition is short: Re χ(E) is the Kramers–Kronig
// transform of Im χ,
//     Re χ(E) = (2/π) P∫_{Eg}^{∞} ξ Im χ(ξ) / (ξ² − E²) dξ.
// This computes that integral numerically (the pole subtracted, adaptive Simpson on pieces) and
// compares. A wrong sign or a misplaced factor anywhere in the closed form shows as a difference
// far above the integral's accuracy.

using Berreman.Api;

namespace Berreman.Native.Tests;

internal static class TaucLorentzTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    public static void Run()
    {
        // The Wikipedia article's example (Eg 3.2, E0 4.5, A 100, C 1), and a broad, low-gap one.
        foreach (var (a, e0, c, eg) in new[] { (100.0, 4.5, 1.0, 3.2), (150.0, 3.6, 2.5, 1.5), (40.0, 6.0, 0.4, 5.0) })
        {
            double worst = 0, scale = 0;
            foreach (var e in new[] { 0.3, 1.0, 2.0, eg - 0.2, eg - 1e-3, eg + 1e-3, eg + 0.2, e0 - 0.3, e0, e0 + 0.3, 1.5 * e0, 3 * e0, 10 * e0 })
            {
                if (e <= 0) continue;
                double closed = TaucLorentzDispersion.Chi(e, a, e0, c, eg).Real;
                double kk = KramersKronig(e, a, e0, c, eg);
                worst = Math.Max(worst, Math.Abs(closed - kk));
                scale = Math.Max(scale, Math.Abs(kk));
            }
            Check(worst < 1e-7 * Math.Max(1, scale), $"Tauc–Lorentz (A {a}, E0 {e0}, C {c}, Eg {eg}): the closed form is the Kramers–Kronig transform of Im χ (worst difference {worst:G3}, largest |Re χ| {scale:G3})");
        }

        var m = MaterialParser.Parse(System.Text.Json.Nodes.JsonNode.Parse(
            """{ "tauc_lorentz": { "unit": "eV", "eps_inf": 3.5, "a": 100, "e0": 4.5, "c": 1, "eg": 3.2 } }"""), "tl");
        var below = m.At(Units.ToWavelengthNm(2.0, "eV"), 0)[0, 0];
        var above = m.At(Units.ToWavelengthNm(4.5, "eV"), 0)[0, 0];
        Check(below.Imaginary == 0 && below.Real > 3.5, $"below the gap ε is real and above ε∞ ({below})");
        Check(above.Imaginary > 0, $"above it, it absorbs ({above})");
        bool refused = false;
        try { MaterialParser.Parse(System.Text.Json.Nodes.JsonNode.Parse("""{ "tauc_lorentz": { "unit": "eV", "eps_inf": 1, "a": 1, "e0": 1, "c": 3, "eg": 0.5 } }"""), "x"); }
        catch (StackFormatException e) { refused = e.Message.Contains("2 e0"); }
        Check(refused, "a broadening of 2 E0 or more is refused, with the reason");
    }

    private static double ImChi(double x, double a, double e0, double c, double eg) =>
        x > eg ? a * e0 * c * (x - eg) * (x - eg) / ((x * x - e0 * e0) * (x * x - e0 * e0) + c * c * x * x) / x : 0;

    /// <summary>(2/π) P∫ ξ Im χ(ξ)/(ξ² − E²) dξ, written as (1/πE)[P∫ g/(ξ − E) − ∫ g/(ξ + E)] with g = ξ Im χ.</summary>
    private static double KramersKronig(double e, double a, double e0, double c, double eg)
    {
        double G(double x) => x * ImChi(x, a, e0, c, eg);
        double ge = G(e);
        double top = 2e4 * Math.Max(e0, e);
        /* Pieces: fine near the gap, the pole and the peak; geometric out to the top. */
        var cuts = new SortedSet<double> { eg, top };
        foreach (var p in new[] { e, e0, e0 - c, e0 + c, e0 - 0.3 * c, e0 + 0.3 * c, e0 - 3 * c, e0 + 3 * c, 2 * e0, 4 * e0 }) if (p > eg && p < top) cuts.Add(p);
        for (double x = 4 * e0; x < top; x *= 1.5) if (x > eg) cuts.Add(x);
        var pts = cuts.ToArray();
        double i1 = 0, i2 = 0;
        for (int k = 0; k + 1 < pts.Length; ++k)
        {
            i1 += Simpson(x => x == e ? 0 : (G(x) - ge) / (x - e), pts[k], pts[k + 1], 1e-11);
            i2 += Simpson(x => G(x) / (x + e), pts[k], pts[k + 1], 1e-11);
        }
        /* The subtracted pole, P∫ ge/(ξ − E) over [Eg, top]; and the tails beyond top, where g ≈ A E0 C/ξ². */
        if (ge != 0) i1 += ge * Math.Log((top - e) / Math.Abs(e - eg));
        double tail = a * e0 * c / (2 * top * top);
        i1 += tail; i2 += tail;
        return (i1 - i2) / (Math.PI * e);
    }

    private static double Simpson(Func<double, double> f, double lo, double hi, double tol)
    {
        double m = 0.5 * (lo + hi), flo = f(lo), fhi = f(hi), fm = f(m);
        return Adaptive(f, lo, hi, flo, fm, fhi, (hi - lo) / 6 * (flo + 4 * fm + fhi), tol, 22);
    }

    private static double Adaptive(Func<double, double> f, double lo, double hi, double flo, double fm, double fhi, double whole, double tol, int depth)
    {
        double m = 0.5 * (lo + hi), lm = 0.5 * (lo + m), rm = 0.5 * (m + hi);
        double flm = f(lm), frm = f(rm);
        double left = (m - lo) / 6 * (flo + 4 * flm + fm), right = (hi - m) / 6 * (fm + 4 * frm + fhi);
        if (depth <= 0 || Math.Abs(left + right - whole) <= 15 * tol) return left + right + (left + right - whole) / 15;
        return Adaptive(f, lo, m, flo, flm, fm, left, tol / 2, depth - 1) + Adaptive(f, m, hi, fm, frm, fhi, right, tol / 2, depth - 1);
    }
}
