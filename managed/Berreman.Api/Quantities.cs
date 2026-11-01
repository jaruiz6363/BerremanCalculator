// Quantities.cs — every number the program derives from a point, by name (contract, Section 3).
//
// The table, the plot and the MCP server all take their quantities from here, so a quantity has
// one definition and one name everywhere.
//
// MUELLER MATRICES. From the Jones matrix J of reflection (or transmission), with
// E_out = J E_in in the (p, s) basis, J = [[r_pp, r_sp], [r_ps, r_ss]] (first subscript incident),
// M = A (J ⊗ J*) A⁻¹ with the Stokes vector S = A (E ⊗ E*):
//     S0 = |Ep|² + |Es|²,  S1 = |Ep|² − |Es|²,  S2 = 2 Re(Ep Es*),  S3 = 2 Im(Ep Es*).
// With this program's Ψ and Δ (ρ = r_pp/r_ss = tan Ψ e^{iΔ}) an isotropic sample then gives
//     [[1, −N, 0, 0], [−N, 1, 0, 0], [0, 0, C, −S], [0, 0, S, C]] M11,
//     N = cos 2Ψ, C = sin 2Ψ cos Δ, S = sin 2Ψ sin Δ.
// THE SIGN OF S3 (the handedness of circular light) differs between books, and decides the sign
// of the fourth row and column. It is fixed here by comparison with Schubert et al., PRB 93,
// 125209 (2016), Fig. 5: with the β-Ga₂O₃ sample, M43 and M41 at the three azimuths P1–P3 have
// the paper's signs (and magnitudes, within reading accuracy, in the reststrahlen band) with this
// choice, and the opposite signs with the other (docs/literature.md). The paper's Ψ, Δ, p and s
// and Euler angles are this program's, so the convention is the one that paper's ellipsometry uses.
// The managed tests check the isotropic form. Elements are reported normalised by M11, as
// ellipsometers report them.
//
// For anisotropic outer media the waves are modes 0 and 1, not p and s, and Ψ, Δ and the Mueller
// matrix are those of the mode basis; their names say p and s only when the outer media are
// isotropic.

using System.Numerics;

namespace Berreman.Api;

/// <summary>One quantity: its name (as a column and a legend), its unit group (for the plot's y axis), and how to compute it.</summary>
public sealed record Quantity(string Name, string Group, string Description, Func<PointResult, double> Value);

public static class Quantities
{
    /// <summary>Every quantity, in the order a picker lists them. a and b name the two waves: p and s, or 0 and 1.</summary>
    public static IReadOnlyList<Quantity> All(string a = "p", string b = "s")
    {
        var list = new List<Quantity>();
        string[] what = ["R", "T", "A"];
        string[] desc = ["reflectance", "transmittance", "absorptance"];
        for (int k = 0; k < 3; ++k)
        {
            int kk = k;
            list.Add(new($"{what[k]}_{a}", "fraction", $"{desc[k]}, {a} incident", p => p.Power[kk]));
            list.Add(new($"{what[k]}_{b}", "fraction", $"{desc[k]}, {b} incident", p => p.Power[3 + kk]));
            list.Add(new($"{what[k]}_mean", "fraction", $"{desc[k]}, unpolarised", p => (p.Power[kk] + p.Power[3 + kk]) / 2));
        }
        list.Add(new($"R_{a}{b}", "fraction", $"conversion reflectance |r_{a}{b}|², {a} in, {b} out (same medium)", p => Sq(p.Jones[1])));
        list.Add(new($"R_{b}{a}", "fraction", $"conversion reflectance |r_{b}{a}|², {b} in, {a} out (same medium)", p => Sq(p.Jones[2])));
        list.Add(new("Psi", "degrees", "Ψ from r_pp/r_ss = tan Ψ e^{iΔ}", p => p.Ellipsometry().Psi));
        list.Add(new("Delta", "degrees", "Δ from r_pp/r_ss = tan Ψ e^{iΔ}", p => p.Ellipsometry().Delta));
        list.Add(new($"Psi_{a}{b}", "degrees", $"Ψ of r_{a}{b}/r_{a}{a} (generalised ellipsometry, Schubert 1996 Eq. 8)", p => GePsi(p.Jones[1] / p.Jones[0])));
        list.Add(new($"Delta_{a}{b}", "degrees", $"Δ of r_{a}{b}/r_{a}{a}", p => Phase(p.Jones[1] / p.Jones[0])));
        list.Add(new($"Psi_{b}{a}", "degrees", $"Ψ of r_{b}{a}/r_{b}{b}", p => GePsi(p.Jones[2] / p.Jones[3])));
        list.Add(new($"Delta_{b}{a}", "degrees", $"Δ of r_{b}{a}/r_{b}{b}", p => Phase(p.Jones[2] / p.Jones[3])));
        string[] jn = [$"r_{a}{a}", $"r_{a}{b}", $"r_{b}{a}", $"r_{b}{b}", $"t_{a}{a}", $"t_{a}{b}", $"t_{b}{a}", $"t_{b}{b}"];
        for (int k = 0; k < 8; ++k)
        {
            int kk = k;
            list.Add(new($"|{jn[k]}|", "amplitude", $"magnitude of {jn[k]}", p => p.Jones[kk].Magnitude));
            list.Add(new($"arg {jn[k]}", "degrees", $"phase of {jn[k]}", p => Phase(p.Jones[kk])));
        }
        foreach (var (prefix, offset, side) in new[] { ("Mr", 0, "reflection"), ("Mt", 4, "transmission") })
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                {
                    if (i == 0 && j == 0) continue;
                    int ii = i, jj = j, off = offset;
                    list.Add(new($"{prefix}{i + 1}{j + 1}", "normalised", $"Mueller element M{i + 1}{j + 1}/M11 in {side}",
                                 p => Mueller(p, off)[ii, jj]));
                }
        return list;
    }

    /// <summary>
    /// A quantity by name, in either naming: "Psi_ps" finds the same quantity as "Psi_01" when the
    /// waves are modes 0 and 1, so a choice survives a change of outer media.
    /// </summary>
    public static Quantity? Find(string name, string a = "p", string b = "s")
    {
        var all = All(a, b);
        int i = Index(all, name);
        if (i < 0 && (a, b) != ("p", "s")) i = Index(All("p", "s"), name);
        if (i < 0 && (a, b) != ("0", "1")) i = Index(All("0", "1"), name);
        return i >= 0 ? all[i] : null;

        static int Index(IReadOnlyList<Quantity> list, string n)
        {
            for (int k = 0; k < list.Count; ++k) if (list[k].Name.Equals(n, StringComparison.OrdinalIgnoreCase)) return k;
            return -1;
        }
    }

    private static double Sq(Complex z) => z.Real * z.Real + z.Imaginary * z.Imaginary;
    private static double Phase(Complex z) => z.Phase * 180 / Math.PI;
    private static double GePsi(Complex z) => Math.Atan(z.Magnitude) * 180 / Math.PI;

    /// <summary>
    /// The Mueller matrix of reflection (offset 0) or transmission (offset 4), normalised by M11.
    /// See the header for the convention.
    /// </summary>
    public static double[,] Mueller(PointResult p, int offset = 0)
    {
        /* J = [[pp, sp], [ps, ss]]: column = incident wave, row = outgoing. */
        Complex j00 = p.Jones[offset], j01 = p.Jones[offset + 2], j10 = p.Jones[offset + 1], j11 = p.Jones[offset + 3];
        return Mueller(j00, j01, j10, j11);
    }

    /// <summary>Mueller matrix of the Jones matrix [[j00, j01], [j10, j11]], normalised by M11.</summary>
    public static double[,] Mueller(Complex j00, Complex j01, Complex j10, Complex j11)
    {
        /* K = J ⊗ J*, indices (ab) with E⊗E* ordered (pp*, ps*, sp*, ss*). */
        Complex[] jj = [j00, j01, j10, j11];
        var K = new Complex[4, 4];
        for (int r = 0; r < 4; ++r)
            for (int c = 0; c < 4; ++c)
            {
                int i = r / 2, k = r % 2, j = c / 2, l = c % 2;
                K[r, c] = jj[i * 2 + j] * Complex.Conjugate(jj[k * 2 + l]);
            }
        /* A and its inverse, for S = A (E ⊗ E*). */
        var i1 = Complex.ImaginaryOne;
        Complex[,] A =
        {
            { 1, 0, 0, 1 },
            { 1, 0, 0, -1 },
            { 0, 1, 1, 0 },
            { 0, -i1, i1, 0 },
        };
        Complex[,] Ainv =
        {
            { 0.5, 0.5, 0, 0 },
            { 0, 0, 0.5, 0.5 * i1 },
            { 0, 0, 0.5, -0.5 * i1 },
            { 0.5, -0.5, 0, 0 },
        };
        var M = new double[4, 4];
        double m11 = 0;
        for (int r = 0; r < 4; ++r)
            for (int c = 0; c < 4; ++c)
            {
                Complex s = 0;
                for (int x = 0; x < 4; ++x)
                    for (int y = 0; y < 4; ++y) s += A[r, x] * K[x, y] * Ainv[y, c];
                M[r, c] = s.Real;
                if (r == 0 && c == 0) m11 = s.Real;
            }
        for (int r = 0; r < 4; ++r) for (int c = 0; c < 4; ++c) M[r, c] /= m11;
        return M;
    }
}
