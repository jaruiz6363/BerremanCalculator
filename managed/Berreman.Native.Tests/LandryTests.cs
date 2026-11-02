// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// LandryTests.cs — light incident from inside a crystal: Landry and Maldonado, J. Opt. Soc. Am. A
// 12, 2048 (1995), Section 4D (samples/literature/landry-1995/biaxial-biaxial.berr).
//
// The one published worked example in the samples with an ANISOTROPIC INCIDENT medium. Two biaxial
// crystals (principal indices 1.2, 1.7, 2.2) meet at a plane, differently oriented; the e wave of
// region 1's inner sheet arrives at 30°. The paper prints every intermediate result. Checked here:
//   1. The Euler convention: R = Rz(φ) Rx(θ) Rz(ψ) applied as R ε Rᵀ (this program's rotation) gives
//      the paper's laboratory tensors, Eqs. 34-35, from its principal indices and Euler angles.
//   2. The fields, compared as VECTORS, which holds whatever basis, sign or normalisation either side
//      uses. The program's fields against depth are split into plane waves with the paper's k_z
//      (Eqs. 38): in region 1 the incident wave and two reflected ones, in region 2 two transmitted
//      ones. Each is divided by the incident wave's amplitude along the paper's ê_i, and compared
//      with the paper's E ê (Eqs. 40-41, 43) for every outgoing wave.
//   3. The power: the paper's own amplitudes and angles in its Eq. 33 give the reflectance that the
//      program computes from the Poynting vector.
// The paper's figures have five or six digits; everything agrees to about 1e-5, and the
// tolerances are set just above that.

using System.Numerics;
using Berreman.Api;

namespace Berreman.Native.Tests;

internal static class LandryTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static readonly double Deg = Math.PI / 180;

    /// <summary>Least squares for one field component: E(z) ≈ Σ a_j exp(i k0 kz_j z).</summary>
    private static Complex[] Fit(double[] z, Complex[] e, double[] kz, double k0)
    {
        int m = kz.Length;
        var a = new Complex[m, m];
        var b = new Complex[m];
        for (int s = 0; s < z.Length; ++s)
        {
            var row = kz.Select(k => Complex.Exp(new Complex(0, k0 * k * z[s]))).ToArray();
            for (int i = 0; i < m; ++i)
            {
                b[i] += Complex.Conjugate(row[i]) * e[s];
                for (int j = 0; j < m; ++j) a[i, j] += Complex.Conjugate(row[i]) * row[j];
            }
        }
        /* Gaussian elimination with partial pivoting: m is 2 or 3. */
        for (int c = 0; c < m; ++c)
        {
            int p = Enumerable.Range(c, m - c).MaxBy(r => a[r, c].Magnitude);
            for (int j = 0; j < m; ++j) (a[c, j], a[p, j]) = (a[p, j], a[c, j]);
            (b[c], b[p]) = (b[p], b[c]);
            for (int r = c + 1; r < m; ++r)
            {
                var f = a[r, c] / a[c, c];
                for (int j = c; j < m; ++j) a[r, j] -= f * a[c, j];
                b[r] -= f * b[c];
            }
        }
        var x = new Complex[m];
        for (int r = m - 1; r >= 0; --r)
        {
            var s = b[r];
            for (int j = r + 1; j < m; ++j) s -= a[r, j] * x[j];
            x[r] = s / a[r, r];
        }
        return x;
    }

    public static void Run()
    {
        /* 1. Eqs. 34 and 35 from the principal indices and the Euler angles. */
        double[] principal = [1.2 * 1.2, 1.7 * 1.7, 2.2 * 2.2];
        double[,] Lab(double phi, double theta, double psi)
        {
            var r = MaterialMatrix.EulerRotation(phi, theta, psi);
            var e = new double[3, 3];
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    for (int k = 0; k < 3; ++k) e[i, j] += r[i, k] * principal[k] * r[j, k];
            return e;
        }
        double[,] eq34 = { { 4.44228, 0, 1.09274 }, { 0, 2.89, 0 }, { 1.09274, 0, 1.83772 } };
        double[,] eq35 = { { 2.59918, -0.83615, 0.22880 }, { -0.83615, 2.30894, -1.02415 }, { 0.22880, -1.02415, 4.26188 } };
        double TensorErr(double[,] a, double[,] b) { double w = 0; for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) w = Math.Max(w, Math.Abs(a[i, j] - b[i, j])); return w; }
        double e34 = TensorErr(Lab(90, 70, -90), eq34), e35 = TensorErr(Lab(30, 30, 30), eq35);
        Check(e34 < 1e-5 && e35 < 1e-5, $"Landry 1995: R = Rz(φ)Rx(θ)Rz(ψ), as R ε Rᵀ, gives the paper's tensors, Eqs. 34-35, to their printed digits ({e34:G2}, {e35:G2})");

        /* 2. The fields. Wavelength 1 µm, so k0 = 2π per µm and the paper's k (normalised to k0) are the kz here. */
        var doc = ApiTests.Sample("landry-1995/biaxial-biaxial.berr");
        double k0 = 2 * Math.PI;
        var front = Enumerable.Range(0, 61).Select(i => -1.5 + 1.49 * i / 60).ToArray();   // in region 1, µm
        var back = Enumerable.Range(0, 61).Select(i => 1.5 * i / 60).ToArray();           // in region 2 (an interface belongs to the medium behind it)
        var depths = front.Concat(back).ToArray();
        var point = Evaluator.Evaluate(doc, depthsNm: depths.Select(z => z * 1000).ToArray()).Points[0];
        Check(point.Status == BcStatus.Ok, $"the interface evaluates ({point.Status})");
        if (point.Fields is null) return;

        /* Eqs. 38-43: the wave vectors' z components, field directions and amplitudes (E_i = 1). */
        const double kzI = 1.23355, kzR1 = -1.54363, kzR2 = -2.08052, kzT1 = 1.54522, kzT2 = 1.11170;
        double[] ei = [-0.55944, 0, 0.82887];
        var want = new Dictionary<string, double[]>
        {
            ["t1"] = Scale(-0.60853, [0.80250, -0.43916, -0.40390]),
            ["t2"] = Scale(-0.35193, [0.45372, 0.88335, 0.11763]),
            ["r1"] = Scale(0.043632, [0, -1.00000, 0]),
            ["r2"] = Scale(0.092288, [-0.95982, 0, -0.28062]),
        };

        /* Which incident mode is the paper's e wave: the one whose field in region 1 has the
           incident k_z 1.23355 (the other, region 1's o wave, has 1.54363). */
        int n = depths.Length;
        Complex[][] Waves(int wave, double[] zs, int offset, double[] kz) =>
            Enumerable.Range(0, 3).Select(c => Fit(zs, zs.Select((_, s) => point.Field(wave, offset + s, c, n)).ToArray(), kz, k0)).ToArray();
        double worstResidual = 0;
        int eWave = -1;
        Complex[][]? inc = null, outT = null;
        for (int w = 0; w < 2 && eWave < 0; ++w)
        {
            var fit = Waves(w, front, 0, [kzI, kzR1, kzR2]);
            /* The fit is exact only if these three plane waves are the whole field. */
            double resid = 0, size = 0;
            for (int s = 0; s < front.Length; ++s)
                for (int c = 0; c < 3; ++c)
                {
                    var model = fit[c][0] * Complex.Exp(new Complex(0, k0 * kzI * front[s])) + fit[c][1] * Complex.Exp(new Complex(0, k0 * kzR1 * front[s])) + fit[c][2] * Complex.Exp(new Complex(0, k0 * kzR2 * front[s]));
                    resid = Math.Max(resid, (model - point.Field(w, s, c, n)).Magnitude);
                    size = Math.Max(size, point.Field(w, s, c, n).Magnitude);
                }
            if (resid < 1e-4 * size) { eWave = w; inc = fit; worstResidual = resid / size; }
        }
        Check(eWave == 0, $"the paper's incident e wave (k_z = 1.23355) is mode 0, and in region 1 the field is it and two reflected waves (residual {worstResidual:G2})");
        if (inc is null) return;
        outT = Waves(eWave, back, front.Length, [kzT1, kzT2]);

        /* The incident wave's amplitude along ê_i; its field must lie along ê_i. */
        var aI = new Vector3C(inc[0][0], inc[1][0], inc[2][0]);
        var c0 = aI.Dot(ei);
        var off = (aI - Vector3C.Along(c0, ei)).Norm / c0.Magnitude;
        Check(off < 2e-5, $"the incident field lies along the paper's ê_i, Eq. 40 ({off:G2})");

        var got = new Dictionary<string, Vector3C>
        {
            ["r1"] = new Vector3C(inc[0][1], inc[1][1], inc[2][1]) / c0,
            ["r2"] = new Vector3C(inc[0][2], inc[1][2], inc[2][2]) / c0,
            ["t1"] = new Vector3C(outT[0][0], outT[1][0], outT[2][0]) / c0,
            ["t2"] = new Vector3C(outT[0][1], outT[1][1], outT[2][1]) / c0,
        };
        foreach (var (name, w) in want)
        {
            var d = (got[name] - w).Norm;
            Check(d < 5e-5, $"the {name} field per unit incident field is the paper's E ê, Eqs. 40-43 ({got[name]} against [{string.Join(", ", w.Select(v => v.ToString("F5")))}]; {d:G2})");
        }

        /* 3. The paper's Eq. 33: normal power flux of each wave, n cos η [cos η cos ν − sin η sin ν sin θ] E²,
           with its characteristic angles (Section 4D), over the incident wave's. */
        static double Flux(double n, double nu, double theta, double eta, double e) =>
            n * Math.Cos(eta * Deg) * (Math.Cos(eta * Deg) * Math.Cos(nu * Deg) - Math.Sin(eta * Deg) * Math.Sin(nu * Deg) * Math.Sin(theta * Deg)) * e * e;
        double incident = Flux(1.42439, 30, -90, -25.98266, 1);
        double paperR = (Flux(1.70000, 24.76748, 180, 0, 0.043632) + Flux(2.19904, 18.89689, -90, 2.59990, 0.092288)) / incident;
        double paperT = (Flux(1.70145, 24.74495, 116.06349, 1.77120, 0.60853) + Flux(1.32026, 32.64507, 19.83282, -20.10835, 0.35193)) / incident;
        double r = point.Power[eWave * 3 + 0], t = point.Power[eWave * 3 + 1];
        Check(Math.Abs(r - paperR) < 2e-5 && Math.Abs(t - paperT) < 5e-5,
              $"the reflectance and transmittance are the paper's Eq. 33 from its own amplitudes and angles (R {r:F6} against {paperR:F6}, T {t:F6} against {paperT:F6})");
    }

    private static double[] Scale(double s, double[] v) => v.Select(x => s * x).ToArray();

    /// <summary>A complex 3-vector, for comparing fields.</summary>
    private readonly record struct Vector3C(Complex X, Complex Y, Complex Z)
    {
        public Complex Dot(double[] v) => X * v[0] + Y * v[1] + Z * v[2];
        public double Norm => Math.Sqrt(X.Magnitude * X.Magnitude + Y.Magnitude * Y.Magnitude + Z.Magnitude * Z.Magnitude);
        public static Vector3C operator /(Vector3C a, Complex s) => new(a.X / s, a.Y / s, a.Z / s);
        public static Vector3C operator -(Vector3C a, Vector3C b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3C operator -(Vector3C a, double[] b) => new(a.X - b[0], a.Y - b[1], a.Z - b[2]);
        public static Vector3C Along(Complex s, double[] v) => new(s * v[0], s * v[1], s * v[2]);
        public override string ToString() => $"[{F(X)}, {F(Y)}, {F(Z)}]";
        private static string F(Complex c) => Math.Abs(c.Imaginary) < 5e-6 ? c.Real.ToString("F5") : $"{c.Real:F5}{(c.Imaginary < 0 ? "-" : "+")}{Math.Abs(c.Imaginary):F5}i";
    }
}
