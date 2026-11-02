// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// MaterialMatrix.cs — the 6×6 constitutive matrix M = [[ε, ρ], [ρ', μ]] the engine sees, and its
// rotation (contract, Sections 1 and 2; derivation note, Section 8).

using System.Numerics;

namespace Berreman.Api;

/// <summary>A 6×6 complex matrix, row-major, in the order (Ex, Ey, Ez, H̃x, H̃y, H̃z).</summary>
public sealed class MaterialMatrix
{
    public Complex[] M { get; } = new Complex[36];

    public Complex this[int i, int j]
    {
        get => M[i * 6 + j];
        set => M[i * 6 + j] = value;
    }

    public static MaterialMatrix FromBlocks(Complex[,] eps, Complex[,] mu, Complex[,]? rho = null, Complex[,]? rhoPrime = null)
    {
        var m = new MaterialMatrix();
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
            {
                m[i, j] = eps[i, j];
                m[i + 3, j + 3] = mu[i, j];
                m[i, j + 3] = rho?[i, j] ?? Complex.Zero;
                m[i + 3, j] = rhoPrime?[i, j] ?? Complex.Zero;
            }
        return m;
    }

    public static Complex[,] Diagonal(Complex a, Complex b, Complex c) =>
        new Complex[,] { { a, 0, 0 }, { 0, b, 0 }, { 0, 0, c } };

    public static Complex[,] Scalar(Complex a) => Diagonal(a, a, a);

    /// <summary>
    /// R = Rz(φ) Rx(θ) Rz(ψ), active and right-handed, angles in degrees (contract, Section 1; Yeh
    /// 1979, Eq. 2).
    /// </summary>
    public static double[,] EulerRotation(double phiDeg, double thetaDeg, double psiDeg)
    {
        static double[,] Rz(double a) => new[,] { { Math.Cos(a), -Math.Sin(a), 0 }, { Math.Sin(a), Math.Cos(a), 0 }, { 0, 0, 1 } };
        static double[,] Rx(double a) => new[,] { { 1, 0, 0 }, { 0, Math.Cos(a), -Math.Sin(a) }, { 0, Math.Sin(a), Math.Cos(a) } };
        const double d = Math.PI / 180.0;
        return Mul(Mul(Rz(phiDeg * d), Rx(thetaDeg * d)), Rz(psiDeg * d));
    }

    /// <summary>
    /// Where the material's own z axis points after the rotation (the optic axis of a uniaxial
    /// material): its tilt from the stack normal z and its azimuth from x towards y, in degrees.
    /// R ẑ = (sin φ sin θ, −cos φ sin θ, cos θ), so the tilt is θ and the azimuth φ − 90°.
    /// </summary>
    public static (double TiltDeg, double AzimuthDeg) AxisDirection(double phiDeg, double thetaDeg, double psiDeg)
    {
        var r = EulerRotation(phiDeg, thetaDeg, psiDeg);
        double tilt = Math.Acos(Math.Clamp(r[2, 2], -1, 1)) * 180 / Math.PI;
        /* A component that is zero but for rounding (cos 90° is 6e-17) is taken as zero, so the
           azimuth is not -0.000…, which reads as "-0°". */
        double x = Math.Abs(r[0, 2]) < 1e-12 ? 0 : r[0, 2], y = Math.Abs(r[1, 2]) < 1e-12 ? 0 : r[1, 2];
        double az = x == 0 && y == 0 ? 0 : Math.Atan2(y, x) * 180 / Math.PI;
        return (tilt, az);
    }

    /// <summary>Euler angles that point the material's z axis at a tilt and azimuth (the inverse of
    /// <see cref="AxisDirection"/>, with ψ = 0).</summary>
    public static double[] EulerForAxis(double tiltDeg, double azimuthDeg)
    {
        double phi = azimuthDeg + 90;
        phi -= 360 * Math.Floor((phi + 180) / 360);
        return [phi, tiltDeg, 0];
    }

    public static double[,] Mul(double[,] a, double[,] b)
    {
        var c = new double[3, 3];
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
                for (int k = 0; k < 3; ++k) c[i, j] += a[i, k] * b[k, j];
        return c;
    }

    /// <summary>
    /// M_lab = 𝓡 M 𝓡ᵀ with 𝓡 = diag(R, R): every 3×3 block rotated the same way.
    ///
    /// EXACTNESS. A block that is exactly Hermitian before rotation is made exactly Hermitian after
    /// it, by computing the upper triangle and mirroring it, and ρ' = ρ† is likewise restored when
    /// it held. The engine's losslessness test is exact, and floating-point rotation alone leaves
    /// a lossless medium non-Hermitian in the last bit (contract, Section 2).
    /// </summary>
    public MaterialMatrix Rotated(double[,] r)
    {
        var o = new MaterialMatrix();
        for (int bi = 0; bi < 2; ++bi)
            for (int bj = 0; bj < 2; ++bj)
            {
                /* A block that is a scalar times the identity is unchanged by any rotation, so it is
                 * copied, not computed: R (a I) Rᵀ in floating point leaves off-diagonal round-off,
                 * and the engine's isotropy test is exact. Without this a sample azimuth turned an
                 * isotropic outer medium into a "anisotropic" one, whose degenerate modes the engine
                 * then labels in its own basis instead of p and s. */
                if (BlockIsScalar(bi, bj))
                {
                    for (int i = 0; i < 3; ++i)
                        for (int j = 0; j < 3; ++j) o[3 * bi + i, 3 * bj + j] = this[3 * bi + i, 3 * bj + j];
                    continue;
                }
                bool wasHermitian = bi == bj && BlockIsHermitian(bi);
                for (int i = 0; i < 3; ++i)
                    for (int j = 0; j < 3; ++j)
                    {
                        Complex s = 0;
                        for (int k = 0; k < 3; ++k)
                            for (int l = 0; l < 3; ++l)
                                s += r[i, k] * this[3 * bi + k, 3 * bj + l] * r[j, l];
                        o[3 * bi + i, 3 * bj + j] = s;
                    }
                if (wasHermitian)
                    for (int i = 0; i < 3; ++i)
                    {
                        o[3 * bi + i, 3 * bj + i] = new Complex(o[3 * bi + i, 3 * bj + i].Real, 0);
                        for (int j = 0; j < i; ++j)
                            o[3 * bi + i, 3 * bj + j] = Complex.Conjugate(o[3 * bi + j, 3 * bj + i]);
                    }
            }
        if (RhoPrimeIsRhoDagger())
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    o[3 + i, j] = Complex.Conjugate(o[j, 3 + i]);
        return o;
    }

    /// <summary>True when block (bi, bj) is exactly a scalar times the identity (zero included).</summary>
    private bool BlockIsScalar(int bi, int bj)
    {
        var a = this[3 * bi, 3 * bj];
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
                if (this[3 * bi + i, 3 * bj + j] != (i == j ? a : Complex.Zero)) return false;
        return true;
    }

    private bool BlockIsHermitian(int b)
    {
        for (int i = 0; i < 3; ++i)
            for (int j = i; j < 3; ++j)
                if (this[3 * b + i, 3 * b + j] != Complex.Conjugate(this[3 * b + j, 3 * b + i])) return false;
        return true;
    }

    private bool RhoPrimeIsRhoDagger()
    {
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
                if (this[3 + i, j] != Complex.Conjugate(this[j, 3 + i])) return false;
        return true;
    }

    /// <summary>Conjugates every element: the conversion of exp(+jωt) data to exp(−iωt).</summary>
    public MaterialMatrix Conjugated()
    {
        var o = new MaterialMatrix();
        for (int k = 0; k < 36; ++k) o.M[k] = Complex.Conjugate(M[k]);
        return o;
    }

    /// <summary>Exactly isotropic: ε and μ scalar multiples of I, ρ = ρ' = 0 (the engine's own test).</summary>
    public bool IsIsotropic(out Complex eps, out Complex mu)
    {
        eps = this[0, 0];
        mu = this[3, 3];
        for (int i = 0; i < 6; ++i)
            for (int j = 0; j < 6; ++j)
            {
                if (i == j)
                {
                    if (this[i, i] != (i < 3 ? eps : mu)) return false;
                }
                else if (this[i, j] != Complex.Zero) return false;
            }
        return true;
    }
}
