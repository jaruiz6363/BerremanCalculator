// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// MoreClosedFormTests.cs — Catarina (2020), Lakhtakia (2018) and Schubert (1996): each sample
// against the paper's closed form, computed here.

using System.Numerics;
using Berreman.Api;
using Berreman.Native;

namespace Berreman.Native.Tests;

internal static class MoreClosedFormTests
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
        CatarinaPolarKerr();
        LakhtakiaHalfSpace();
        SchubertBiaxialFilm();
    }

    private static readonly Complex I = Complex.ImaginaryOne;

    private static Complex Fwd(Complex z)
    {
        var q = Complex.Sqrt(z);
        if (q.Imaginary < 0 || (q.Imaginary == 0 && q.Real < 0)) q = -q;
        return q;
    }

    /// <summary>
    /// Catarina, Eqs. B.13–B.16. For x-polarised light at normal incidence the reflected field's
    /// circular components E_x ± iE_y are r± = (1 − h(n∓)) / (1 + h(n∓)), with
    /// h(n) = n (f − g)/(f + g), f = (n + √ε_r) e^{−ik0nd}, g = (n − √ε_r) e^{ik0nd}, n± = √(ε_xx ± iε_xy).
    /// Here: incident p has E_x = 1; the reflected p wave has E_x = −r_pp and the reflected s wave
    /// E_y = r_ps (the contract's p̂ for the reflected wave), so E_x ± iE_y = −r_pp ± i r_ps.
    /// </summary>
    private static void CatarinaPolarKerr()
    {
        var doc = ApiTests.Sample("catarina-2020/polar-kerr-film.berr");
        var res = Evaluator.Evaluate(doc);
        var film = doc.Materials["film"].At(500, 0);
        Complex exx = film[0, 0], exy = film[0, 1];
        double sub = Math.Sqrt(2.25), d = 10;
        double worst = 0, worstKerr = 0;
        foreach (var p in res.Points)
        {
            Check(p.Status == BcStatus.Ok, "Catarina evaluates");
            double k0d = 2 * Math.PI / p.WavelengthNm * d;
            Complex H(Complex n)
            {
                var f = (n + sub) * Complex.Exp(-I * k0d * n);
                var g = (n - sub) * Complex.Exp(I * k0d * n);
                return n * (f - g) / (f + g);
            }
            Complex np = Fwd(exx + I * exy), nm = Fwd(exx - I * exy);
            Complex rPlus = (1 - H(nm)) / (1 + H(nm)), rMinus = (1 - H(np)) / (1 + H(np));
            Complex aPlus = -p.Jones[0] + I * p.Jones[1], aMinus = -p.Jones[0] - I * p.Jones[1];
            worst = Math.Max(worst, Math.Max((aPlus - rPlus).Magnitude, (aMinus - rMinus).Magnitude));
            /* Kerr rotation, Eq. B.9: (φ− − φ+)/2 */
            double kerrPaper = (rMinus.Phase - rPlus.Phase) / 2, kerrHere = (aMinus.Phase - aPlus.Phase) / 2;
            worstKerr = Math.Max(worstKerr, Math.Abs(kerrPaper - kerrHere));
        }
        Small(worst, 1e-13, "Catarina: E_x ± iE_y of the reflected wave equal r± of Eqs. B.13–B.16");
        Small(worstKerr, 1e-12, "Catarina: Kerr rotation (Eq. B.9)");
        Console.WriteLine($"  Catarina polar Kerr: worst r± error {worst:E1}, Kerr rotation error {worstKerr:E1}");
    }

    /// <summary>
    /// Lakhtakia, Eqs. 17–29, with k0 = 1: the reflectances R_ss + R_ps (s incident) and
    /// R_sp + R_pp (p incident), which do not depend on how p and s are signed. A case with Γ = 0 is
    /// checked first, against Fresnel, to make sure the transcription of Eqs. 22–29 is right.
    /// </summary>
    private static (double Rs, double Rp) LakhtakiaR(double theta, double psi0, Complex epsr, Complex mur, Complex gx, Complex gy)
    {
        Complex k = Fwd(epsr * mur), eta = Complex.Sqrt(mur / epsr);
        Complex qx = Math.Sin(theta) * Math.Cos(psi0) - gx, qy = Math.Sin(theta) * Math.Sin(psi0) - gy;
        Complex q2 = qx * qx + qy * qy;
        Complex alpha = Complex.Sqrt(k * k - q2);
        if (alpha.Imaginary < 0) alpha = -alpha;
        double c = Math.Cos(theta);
        Complex A = qx * Math.Sin(psi0) - qy * Math.Cos(psi0), B = qx * Math.Cos(psi0) + qy * Math.Sin(psi0);
        Complex D = (1 + eta * eta) * k * q2 * alpha * c
                  + eta * ((k * k + alpha * alpha * c * c) * A * A + (alpha * alpha + k * k * c * c) * B * B);
        Complex common = eta * ((k * k - alpha * alpha * c * c) * A * A + (alpha * alpha - k * k * c * c) * B * B);
        Complex rss = (-(1 - eta * eta) * k * q2 * alpha * c - common) / D;
        Complex rpp = ((1 - eta * eta) * k * q2 * alpha * c - common) / D;
        Complex rps = 2 * eta * q2 * A * B * c / D, rsp = -rps;
        double Rs = rss.Magnitude * rss.Magnitude + rps.Magnitude * rps.Magnitude;
        double Rp = rsp.Magnitude * rsp.Magnitude + rpp.Magnitude * rpp.Magnitude;
        return (Rs, Rp);
    }

    private static void LakhtakiaHalfSpace()
    {
        /* Γ = 0: Fresnel for a magnetic half space, R_s = |(q1 μ − q2)/(q1 μ + q2)|², R_p = |(ε q1 − q2)/(ε q1 + q2)|². */
        Complex eps = new(5, 0.4), mu = new(1.1, 0.05);
        double worstFresnel = 0;
        foreach (var th in new[] { 0.2, 0.7, 1.2 })
        {
            var (rs, rp) = LakhtakiaR(th, 0, eps, mu, 0, 0);
            double q1 = Math.Cos(th);
            var q2 = Fwd(eps * mu - Math.Sin(th) * Math.Sin(th));
            double fs = Math.Pow(((q1 * mu - q2) / (q1 * mu + q2)).Magnitude, 2), fp = Math.Pow(((eps * q1 - q2) / (eps * q1 + q2)).Magnitude, 2);
            worstFresnel = Math.Max(worstFresnel, Math.Max(Math.Abs(rs - fs), Math.Abs(rp - fp)));
        }
        Small(worstFresnel, 1e-14, "Lakhtakia Eqs. 22–29 as transcribed reduce to Fresnel at Γ = 0");

        var doc = ApiTests.Sample("lakhtakia-2018/magnetoelectric-halfspace.berr");
        var res = Evaluator.Evaluate(doc);
        double worst = 0;
        foreach (var p in res.Points)
        {
            Check(p.Status == BcStatus.Ok, $"Lakhtakia evaluates at {p.AngleDeg}°");
            var (rs, rp) = LakhtakiaR(p.AngleDeg!.Value * Math.PI / 180, Math.PI / 3, eps, mu, new Complex(0.9, 0.01), new Complex(0.3, 0.01));
            worst = Math.Max(worst, Math.Max(Math.Abs(p.Power[3] - rs), Math.Abs(p.Power[0] - rp)));
        }
        Small(worst, 1e-12, "Lakhtakia: reflectances against Eqs. 22–29, Fig. 3's parameters, 0–89°");
        Console.WriteLine($"  Lakhtakia half space: Γ = 0 check {worstFresnel:E1}; worst reflectance error {worst:E1} over {res.Points.Count} angles");
    }

    /// <summary>
    /// Schubert, Eqs. C2, C5 and C6: ρ = r_pp/r_ss for an aligned biaxial film on an aligned biaxial
    /// substrate, with N_ij = n_i cos Φ_j. Eq. C5 is used with sums in the r_12 denominators; as
    /// printed (differences) each r_12 would be 1, which the test shows is not the engine's result.
    /// </summary>
    private static void SchubertBiaxialFilm()
    {
        var doc = ApiTests.Sample("schubert-1996/biaxial-film.berr");
        var res = Evaluator.Evaluate(doc);
        Complex nx = new(1.6, 0.01), ny = new(1.7, 0.02), nz = new(1.5, 0.005);
        Complex sx = new(2.0, 0.05), sy = new(2.1, 0.03), sz = new(1.9, 0.04);
        double worst = 0, worstPrinted = 0;
        foreach (var p in res.Points)
        {
            Check(p.Status == BcStatus.Ok, "Schubert evaluates");
            double sa = Math.Sin(p.AngleDeg!.Value * Math.PI / 180), na = 1;
            Complex Cos(Complex nj) => Complex.Sqrt(1 - (na * sa / nj) * (na * sa / nj));
            Complex N(Complex ni, Complex nj) => ni * Cos(nj);
            Complex Naa = na * Math.Sqrt(1 - sa * sa);
            Complex Nxa = nx * Math.Sqrt(1 - sa * sa), Naz = na * Cos(nz);
            Complex Nyy = N(ny, ny), Nxz = N(nx, nz);
            Complex Nsx_z = N(sx, nz), Nx_sz = N(nx, sz), Nsy_sy = N(sy, sy);
            Complex r01p = (Nxa - Naz) / (Nxa + Naz), r01s = (Naa - Nyy) / (Naa + Nyy);
            Complex r12p = (Nsx_z - Nx_sz) / (Nsx_z + Nx_sz), r12s = (Nyy - Nsy_sy) / (Nyy + Nsy_sy);
            double k0d = 2 * Math.PI / p.WavelengthNm * 300;
            Complex ep = Complex.Exp(2 * I * k0d * Nxz), es = Complex.Exp(2 * I * k0d * Nyy);
            Complex rho = (r01p + r12p * ep) / (1 + r01p * r12p * ep) * ((1 + r01s * r12s * es) / (r01s + r12s * es));
            Complex rhoHere = p.Jones[0] / p.Jones[3];
            worst = Math.Max(worst, (rhoHere - rho).Magnitude / rho.Magnitude);

            /* As printed: r_12 = 1. */
            Complex rhoPrinted = (r01p + ep) / (1 + r01p * ep) * ((1 + r01s * es) / (r01s + es));
            worstPrinted = Math.Max(worstPrinted, (rhoHere - rhoPrinted).Magnitude / rhoPrinted.Magnitude);
        }
        Small(worst, 1e-12, "Schubert: ρ = r_pp/r_ss against Eq. C6 (with sums in C5's denominators)");
        Check(worstPrinted > 1e-2, "Schubert: C5 as printed (r_12 = 1) does not give the result");
        Console.WriteLine($"  Schubert biaxial film: worst relative ρ error {worst:E1}; with C5 as printed {worstPrinted:E1}");
    }
}
