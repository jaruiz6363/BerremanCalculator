// TermMaterial.cs — the general material: M(λ) = Σ f_k(λ) T_k (docs/material-model.md).
//
// Each term is a scalar law f_k (a constant 1, Sellmeier, Lorentz, Drude, a table, ...) times a
// constant complex 6×6 shape T_k. Any of the 36 elements may therefore disperse, and a law's shape
// may be any matrix: an oscillator along its own direction ê (T = ê êᵀ in the ε block), which is
// how a monoclinic or triclinic crystal's principal axes come to turn with wavelength; a gyration
// (an antisymmetric imaginary ε or μ); a chiral ρ and ρ′. Nothing is assumed about symmetry, so a
// material that is not passive or not reciprocal is evaluated as given, and only reported
// (MaterialChecks).

using System.Numerics;

namespace Berreman.Api;

/// <summary>One term: a law (null for the constant 1) times a 6×6 shape.</summary>
public sealed record MaterialTerm(Dispersion? Law, MaterialMatrix Shape);

public sealed class TermMaterial(IReadOnlyList<MaterialTerm> terms) : Material
{
    public IReadOnlyList<MaterialTerm> Terms { get; } = terms;

    public override bool IsConstant => Terms.All(t => t.Law is null);

    public override MaterialMatrix At(double wavelengthNm, double thicknessNm)
    {
        var m = new MaterialMatrix();
        foreach (var t in Terms)
        {
            Complex f = t.Law?.Eps(wavelengthNm) ?? Complex.One;
            for (int k = 0; k < 36; ++k)
                if (t.Shape.M[k] != Complex.Zero) m.M[k] += f * t.Shape.M[k];
        }
        return m;
    }
}

/// <summary>
/// Properties of one M, reported and never enforced (docs/material-model.md, "Checks").
/// </summary>
public static class MaterialChecks
{
    /// <summary>
    /// The least eigenvalue of the anti-Hermitian part (M − M†)/2i. In exp(−iωt) a passive medium
    /// absorbs power for every field, so this is ≥ 0; a negative value means gain for some field.
    /// Exactly 0 for a lossless medium.
    /// </summary>
    public static double LeastLoss(MaterialMatrix m) => LossRange(m).Least;

    /// <summary>
    /// The least and greatest eigenvalues of (M − M†)/2i. Both are 0 exactly when the medium is
    /// lossless. The least alone cannot tell: a lossless μ block already contributes zeros, so an
    /// absorbing ε with μ = 1 has least 0 and greatest > 0.
    /// </summary>
    public static (double Least, double Greatest) LossRange(MaterialMatrix m)
    {
        // The Hermitian H = (M − M†)/2i, as the real symmetric 12×12 [[A, −B], [B, A]], H = A + iB.
        var s = new double[12, 12];
        for (int i = 0; i < 6; ++i)
            for (int j = 0; j < 6; ++j)
            {
                var h = (m[i, j] - Complex.Conjugate(m[j, i])) / (2 * Complex.ImaginaryOne);
                s[i, j] = h.Real; s[i + 6, j + 6] = h.Real;
                s[i, j + 6] = -h.Imaginary; s[i + 6, j] = h.Imaginary;
            }
        var e = SymmetricEigen(s, out _);
        return (e.Min(), e.Max());
    }

    /// <summary>
    /// How far M is from reciprocal: the largest of |ε − εᵀ|, |μ − μᵀ| and |ρ′ + ρᵀ|, element by
    /// element, against the largest element of M. 0 for a reciprocal medium; a magneto-optic
    /// gyration or a Faraday-active ferrite is not.
    /// </summary>
    public static double NonReciprocity(MaterialMatrix m)
    {
        double worst = 0, scale = 0;
        for (int k = 0; k < 36; ++k) scale = Math.Max(scale, Complex.Abs(m.M[k]));
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
            {
                worst = Math.Max(worst, Complex.Abs(m[i, j] - m[j, i]));
                worst = Math.Max(worst, Complex.Abs(m[3 + i, 3 + j] - m[3 + j, 3 + i]));
                worst = Math.Max(worst, Complex.Abs(m[3 + i, j] + m[j, 3 + i]));
            }
        return scale > 0 ? worst / scale : 0;
    }

    /// <summary>
    /// The principal axes of the real symmetric part of ε, Re(ε + εᵀ)/2: its three values, largest
    /// first, and their unit directions (columns). In a monoclinic crystal near its resonances these
    /// directions turn with wavelength.
    /// </summary>
    public static (double[] Values, double[,] Axes) PrincipalAxes(MaterialMatrix m)
    {
        var s = new double[3, 3];
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j) s[i, j] = 0.5 * (m[i, j] + m[j, i]).Real;
        var values = SymmetricEigen(s, out var vectors);
        var order = Enumerable.Range(0, 3).OrderByDescending(k => values[k]).ToArray();
        var axes = new double[3, 3];
        for (int c = 0; c < 3; ++c)
        {
            /* A sign convention, so a direction does not flip between neighbouring wavelengths:
               the largest component is positive. */
            int big = 0;
            for (int r = 1; r < 3; ++r) if (Math.Abs(vectors[r, order[c]]) > Math.Abs(vectors[big, order[c]])) big = r;
            double sign = vectors[big, order[c]] < 0 ? -1 : 1;
            for (int r = 0; r < 3; ++r) axes[r, c] = sign * vectors[r, order[c]];
        }
        return (order.Select(k => values[k]).ToArray(), axes);
    }

    /// <summary>Eigenvalues of a real symmetric matrix by cyclic Jacobi rotations; eigenvectors as columns.</summary>
    public static double[] SymmetricEigen(double[,] a0, out double[,] v)
    {
        int n = a0.GetLength(0);
        var a = (double[,])a0.Clone();
        v = new double[n, n];
        for (int i = 0; i < n; ++i) v[i, i] = 1;
        for (int sweep = 0; sweep < 100; ++sweep)
        {
            double off = 0, total = 0;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) { total += a[i, j] * a[i, j]; if (i != j) off += a[i, j] * a[i, j]; }
            if (off <= 1e-30 * total || off == 0) break;
            for (int p = 0; p < n - 1; ++p)
                for (int q = p + 1; q < n; ++q)
                {
                    if (a[p, q] == 0) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < n; ++k)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < n; ++k)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < n; ++k)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                    }
                }
        }
        var d = new double[n];
        for (int i = 0; i < n; ++i) d[i] = a[i, i];
        return d;
    }
}
