// CholestericTests.cs — twisted layers (contract, Section 2: sublayers), against the exact solution
// at normal incidence.
//
// THE EXACT SOLUTION. At normal incidence a helix of pitch P, director angle φ = φ0 + βz
// (β = ±2π/P), is uniform in the frame that turns with it. With ψ = S(φ) ψ′, S the rotation of the
// in-plane E and H̃ by φ, the field obeys dψ′/dz = i k0 Δ_rot ψ′ with the CONSTANT
//
//     Δ_rot = Δ0 + i (β/k0) G,       S(φ) = exp(φ G),
//
// Δ0 the Δ of the untwisted layer. So ψ(d) = S(φ(d)) exp(i k0 d Δ_rot) S(φ0)ᵀ ψ(0), exactly
// (Berreman 1972 solves the same problem in the same frame, Eqs. 61–70). The test computes this
// with its own matrix exponential and its own 4×4 solve; the engine computes the same slab as
// uniform sublayers, and must converge to it as 1/n².

using System.Numerics;
using Berreman.Api;
using Berreman.Native;

namespace Berreman.Native.Tests;

internal static class CholestericTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static void Small(double v, double tol, string what)
    {
        ++Checks;
        if (!(Math.Abs(v) <= tol)) { ++Failures; Console.WriteLine($"  FAIL  {what}: {v:G4} exceeds {tol:G3}"); }
    }

    public static void Run()
    {
        RotatingFrameEigenvalues();
        SublayersConvergeToTheExactSlab();
        TingFig2Band();
        HandednessSelectsTheReflectedCircularWave();
    }

    // ---- small dense linear algebra, independent of the engine --------------------------------

    private static Complex[,] Mul(Complex[,] a, Complex[,] b)
    {
        int n = a.GetLength(0), m = b.GetLength(1), k = a.GetLength(1);
        var c = new Complex[n, m];
        for (int i = 0; i < n; ++i) for (int j = 0; j < m; ++j) for (int l = 0; l < k; ++l) c[i, j] += a[i, l] * b[l, j];
        return c;
    }

    private static Complex[,] Expm(Complex[,] a)
    {
        double norm = 0;
        for (int j = 0; j < 4; ++j) { double s = 0; for (int i = 0; i < 4; ++i) s += a[i, j].Magnitude; norm = Math.Max(norm, s); }
        int sq = 0;
        while (norm / Math.Pow(2, sq) > 0.05) ++sq;
        var b = new Complex[4, 4];
        for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) b[i, j] = a[i, j] / Math.Pow(2, sq);
        var e = new Complex[4, 4];
        var t = new Complex[4, 4];
        for (int i = 0; i < 4; ++i) { e[i, i] = 1; t[i, i] = 1; }
        for (int k = 1; k <= 20; ++k)
        {
            t = Mul(t, b);
            for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) { t[i, j] /= k; e[i, j] += t[i, j]; }
        }
        for (int s = 0; s < sq; ++s) e = Mul(e, e);
        return e;
    }

    /// <summary>Solves A x = y (4×4) by Gaussian elimination with partial pivoting.</summary>
    private static Complex[] Solve(Complex[,] a0, Complex[] y0)
    {
        var a = (Complex[,])a0.Clone();
        var y = (Complex[])y0.Clone();
        for (int k = 0; k < 4; ++k)
        {
            int p = k;
            for (int i = k + 1; i < 4; ++i) if (a[i, k].Magnitude > a[p, k].Magnitude) p = i;
            for (int j = 0; j < 4; ++j) (a[k, j], a[p, j]) = (a[p, j], a[k, j]);
            (y[k], y[p]) = (y[p], y[k]);
            for (int i = k + 1; i < 4; ++i)
            {
                var f = a[i, k] / a[k, k];
                for (int j = k; j < 4; ++j) a[i, j] -= f * a[k, j];
                y[i] -= f * y[k];
            }
        }
        var x = new Complex[4];
        for (int k = 3; k >= 0; --k)
        {
            var s = y[k];
            for (int j = k + 1; j < 4; ++j) s -= a[k, j] * x[j];
            x[k] = s / a[k, k];
        }
        return x;
    }

    private static Complex Det(Complex[,] a0)
    {
        var a = (Complex[,])a0.Clone();
        Complex det = 1;
        for (int k = 0; k < 4; ++k)
        {
            int p = k;
            for (int i = k + 1; i < 4; ++i) if (a[i, k].Magnitude > a[p, k].Magnitude) p = i;
            if (p != k) { for (int j = 0; j < 4; ++j) (a[k, j], a[p, j]) = (a[p, j], a[k, j]); det = -det; }
            det *= a[k, k];
            for (int i = k + 1; i < 4; ++i)
            {
                var f = a[i, k] / a[k, k];
                for (int j = k; j < 4; ++j) a[i, j] -= f * a[k, j];
            }
        }
        return det;
    }

    /// <summary>S(φ): the in-plane rotation of ψ = (Ex, H̃y, Ey, −H̃x).</summary>
    private static Complex[,] S(double phi)
    {
        double c = Math.Cos(phi), s = Math.Sin(phi);
        return new Complex[,] { { c, 0, -s, 0 }, { 0, c, 0, -s }, { s, 0, c, 0 }, { 0, s, 0, c } };
    }

    private static readonly Complex[,] G = { { 0, 0, -1, 0 }, { 0, 0, 0, -1 }, { 1, 0, 0, 0 }, { 0, 1, 0, 0 } };

    private static Complex[,] DeltaRot(double ne, double no, double m) => new Complex[,]
    {
        { 0, 1, -Complex.ImaginaryOne * m, 0 },
        { ne * ne, 0, 0, -Complex.ImaginaryOne * m },
        { Complex.ImaginaryOne * m, 0, 0, 1 },
        { 0, Complex.ImaginaryOne * m, no * no, 0 },
    };

    // ---- the tests -------------------------------------------------------------------------------

    /// <summary>
    /// The eigenvalues of Δ_rot are Berreman's q/k0. His Eqs. 68–69 give
    /// κ² = ε + m² ± √(4εm² + δ²), with ε ± δ the principal values and m = β/k0. His Eq. 70, as
    /// printed, has δ²/ε in place of δ², which Eqs. 68–69 do not give; the test shows which holds.
    /// </summary>
    private static void RotatingFrameEigenvalues()
    {
        double ne = 1.616, no = 1.494, eps = (ne * ne + no * no) / 2, del = (ne * ne - no * no) / 2;
        double worst6869 = 0, worst70 = 0;
        foreach (var m in new[] { 0.3, 0.6, 0.65, 1.2 })
        {
            var d = DeltaRot(ne, no, m);
            foreach (var sgn in new[] { 1.0, -1.0 })
            {
                var k2 = eps + m * m + sgn * Math.Sqrt(4 * eps * m * m + del * del);
                var k2printed = eps + m * m + sgn * Math.Sqrt(4 * eps * m * m + del * del / eps);
                double DetAt(double x)
                {
                    var k = Complex.Sqrt(x);
                    var a = (Complex[,])d.Clone();
                    for (int i = 0; i < 4; ++i) a[i, i] -= k;
                    return Det(a).Magnitude;
                }
                worst6869 = Math.Max(worst6869, DetAt(k2));
                worst70 = Math.Max(worst70, DetAt(k2printed));
            }
        }
        Small(worst6869, 1e-12, "Δ_rot eigenvalues satisfy Berreman's Eqs. 68–69");
        Check(worst70 > 1e-4, "Eq. 70 as printed (δ²/ε) does not give the eigenvalues; Eqs. 68–69 (δ²) do");
        Console.WriteLine($"  cholesteric: |det(Δ_rot − κ)| at Eqs. 68–69's κ {worst6869:E1}; at Eq. 70 as printed {worst70:E1}");
    }

    /// <summary>The exact Jones r and t of the Ting slab at one wavelength, by the rotating frame.</summary>
    private static (Complex[] R, Complex[] T) Exact(double lambda, double ne, double no, double pitch, double d, double n0, int sense)
    {
        double k0 = 2 * Math.PI / lambda, beta = sense * 2 * Math.PI / pitch;
        var dr = DeltaRot(ne, no, beta / k0);
        for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) dr[i, j] *= Complex.ImaginaryOne * k0 * d;
        var sT = S(0);   /* φ0 = 0, and S(0)ᵀ = I */
        var T = Mul(S(beta * d), Mul(Expm(dr), sT));
        /* modes at normal incidence, μ = 1: p± = (±1, n0, 0, 0), s± = (0, 0, 1, ±n0) */
        Complex[][] fwd = { new Complex[] { 1, n0, 0, 0 }, new Complex[] { 0, 0, 1, n0 } };
        Complex[][] bwd = { new Complex[] { -1, n0, 0, 0 }, new Complex[] { 0, 0, 1, -n0 } };
        var r = new Complex[4];
        var t = new Complex[4];
        for (int inc = 0; inc < 2; ++inc)
        {
            /* T (inc + Σ r_o bwd_o) = Σ t_o fwd_o  →  [T bwd_p, T bwd_s, −fwd_p, −fwd_s] [r; t] = −T inc */
            var a = new Complex[4, 4];
            var y = new Complex[4];
            for (int i = 0; i < 4; ++i)
            {
                for (int k = 0; k < 4; ++k)
                {
                    a[i, 0] += T[i, k] * bwd[0][k];
                    a[i, 1] += T[i, k] * bwd[1][k];
                    y[i] -= T[i, k] * fwd[inc][k];
                }
                a[i, 2] = -fwd[0][i];
                a[i, 3] = -fwd[1][i];
            }
            var x = Solve(a, y);
            r[inc * 2 + 0] = x[0]; r[inc * 2 + 1] = x[1];   /* [incident][outgoing] */
            t[inc * 2 + 0] = x[2]; t[inc * 2 + 1] = x[3];
        }
        return (r, t);
    }

    private static StackDocument Ting(int perPitch, string hand = "right", double[]? spectral = null)
    {
        var doc = ApiTests.Sample("ting-2006/cholesteric-fig2.berr");
        var l = doc.Layers[0];
        var layers = new List<StackLayer>
        {
            new() { Material = l.Material, ThicknessNm = l.ThicknessNm, EulerDeg = l.EulerDeg,
                    Twist = new LayerTwist(338, hand == "right" ? 1 : -1, 0, 12 * perPitch) },
        };
        return new StackDocument
        {
            Title = doc.Title, Source = doc.Source, Notes = doc.Notes, LengthUnit = doc.LengthUnit, SpectralUnit = doc.SpectralUnit,
            Materials = doc.Materials, Incident = doc.Incident, Exit = doc.Exit, Layers = layers,
            Spectral = spectral ?? doc.Spectral, AzimuthsDeg = doc.AzimuthsDeg, AnglesDeg = doc.AnglesDeg, Xi = doc.Xi,
        };
    }

    private static void SublayersConvergeToTheExactSlab()
    {
        var spectral = new[] { 480.0, 505.0, 515.0, 525.6, 540.0, 560.0 };
        double prev = double.NaN;
        var errors = new List<double>();
        foreach (var per in new[] { 16, 32, 64, 128 })
        {
            var res = Evaluator.Evaluate(Ting(per, spectral: spectral));
            double worst = 0;
            foreach (var p in res.Points)
            {
                var (r, t) = Exact(p.WavelengthNm, 1.616, 1.494, 338, 4056, 1.555, +1);
                for (int k = 0; k < 4; ++k)
                    worst = Math.Max(worst, Math.Max((p.Jones[k] - r[k]).Magnitude, (p.Jones[4 + k] - t[k]).Magnitude));
            }
            errors.Add(worst);
            if (!double.IsNaN(prev))
                Check(worst < prev / 3, $"sublayers {per}/pitch: the error falls about 4× per doubling (midpoint rule)");
            prev = worst;
            Console.WriteLine($"  cholesteric, {per,3} sublayers per pitch: worst Jones error against the exact slab {worst:E2}; reported doubling change {res.SublayerChange:E2}");
        }
        /* The midpoint rule is second order: 1.6e-3 at 64 per pitch predicts 4e-4 at 128. */
        Small(errors[^1], 5e-4, "128 sublayers per pitch: within 5e-4 of the exact slab");
    }

    /// <summary>
    /// Ting's Fig. 2: a reflection band for unpolarised light of height about 0.5, centred near
    /// n̄P = 525.6 nm, of width ΔnP = 41.2 nm (the paper's approximate formulas). ΔnP is the STOP
    /// band: between the infinite helix's band edges n_oP = 505.0 and n_eP = 546.2 nm, where R stays
    /// near its plateau and outside which it falls. A 12-pitch slab's skirts make its half-height
    /// width wider (about 51 nm), so the test checks the edges, not a half-width.
    /// </summary>
    private static void TingFig2Band()
    {
        var res = Evaluator.Evaluate(ApiTests.Sample("ting-2006/cholesteric-fig2.berr"));
        var pts = res.Points.Select(p => (L: p.Spectral, R: (p.Power[0] + p.Power[3]) / 2)).ToList();
        var peak = pts.MaxBy(x => x.R);
        double RAt(double l) => pts.MinBy(x => Math.Abs(x.L - l)).R;
        double half = peak.R / 2;
        double lo = pts.First(x => x.R >= half).L, hi = pts.Last(x => x.R >= half).L;
        Console.WriteLine($"  Ting Fig. 2: peak unpolarised R {peak.R:F3} at {peak.L} nm; at n_oP 505.0 nm R/peak {RAt(505) / peak.R:F2}, at n_eP 546.2 nm {RAt(546) / peak.R:F2}; 10 nm outside: {RAt(495) / peak.R:F2} and {RAt(556) / peak.R:F2}; half-height centre {(lo + hi) / 2:F1} nm; sublayer change {res.SublayerChange:E1}");
        Small(peak.R - 0.5, 0.02, "Ting: unpolarised peak reflectance about 0.5");
        Small((lo + hi) / 2 - 525.6, 2, "Ting: band centre n̄P = 525.6 nm");
        Check(RAt(505) > 0.85 * peak.R && RAt(546) > 0.85 * peak.R, "Ting: R stays near its plateau out to n_oP and n_eP (the stop band, ΔnP = 41.2 nm)");
        Check(RAt(495) < 0.2 * peak.R && RAt(556) < 0.2 * peak.R, "Ting: and falls steeply within 10 nm outside them");
        Check(res.SublayerChange < 1e-3, "Ting: doubling the sublayers changes R and T by under 1e-3");
    }

    /// <summary>
    /// At the band centre one circular polarisation is reflected and the other transmitted, and
    /// reversing the helix exchanges them. (Which is which depends on the definition of circular
    /// handedness; the swap does not.)
    /// </summary>
    private static void HandednessSelectsTheReflectedCircularWave()
    {
        double Rcirc(PointResult p, int sign)
        {
            var ep = 1 / Math.Sqrt(2);
            var es = sign * Complex.ImaginaryOne / Math.Sqrt(2);
            var outP = p.Jones[0] * ep + p.Jones[2] * es;
            var outS = p.Jones[1] * ep + p.Jones[3] * es;
            return outP.Magnitude * outP.Magnitude + outS.Magnitude * outS.Magnitude;
        }
        var right = Evaluator.Evaluate(Ting(64, "right", [525.6])).Points[0];
        var left = Evaluator.Evaluate(Ting(64, "left", [525.6])).Points[0];
        double rP = Rcirc(right, +1), rM = Rcirc(right, -1), lP = Rcirc(left, +1), lM = Rcirc(left, -1);
        Console.WriteLine($"  cholesteric at 525.6 nm: right helix R(+) {rP:F4} R(−) {rM:F4}; left helix R(+) {lP:F4} R(−) {lM:F4}");
        Check(Math.Max(rP, rM) > 0.95 && Math.Min(rP, rM) < 0.01, "one circular wave reflected, the other not");
        Small(rP - lM, 1e-6, "reversing the helix exchanges the circular waves (+ ↔ −)");
        Small(rM - lP, 1e-6, "reversing the helix exchanges the circular waves (− ↔ +)");
    }
}
