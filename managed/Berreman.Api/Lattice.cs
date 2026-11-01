// Lattice.cs — directions given in a crystal's own axes (docs/material-model.md, "Crystal axes").
//
// A monoclinic or triclinic crystal's oscillators are often given as directions in its lattice:
// [u v w] is the direction u a + v b + w c. To turn that into the material's x, y, z the lattice
// must be placed in them, and the program uses the usual crystallographic placement:
//     a along x;  b in the x–y plane, at γ from a;  c completing the cell, with positive z.
//     a = a (1, 0, 0)
//     b = b (cos γ, sin γ, 0)
//     c = c (cos β, (cos α − cos β cos γ)/sin γ, √(1 − cos²β − ((cos α − cos β cos γ)/sin γ)²))
// Papers place the lattice in other ways. Schubert et al. (2016) put β-Ga₂O₃'s a along x, c* along
// y and b along −z; that frame is this one turned by −90° about x, so a material written in crystal
// axes here is the paper's with Euler angles [0, −90, 0] (the managed tests check this).

using System.Text.Json.Nodes;

namespace Berreman.Api;

/// <summary>Cell lengths (any one unit) and angles α (b to c), β (a to c), γ (a to b) in degrees.</summary>
public sealed record Lattice(double A, double B, double C, double AlphaDeg, double BetaDeg, double GammaDeg)
{
    /// <summary>The three cell vectors in the material's x, y, z: rows a, b, c.</summary>
    public double[][] Vectors()
    {
        const double d = Math.PI / 180;
        double ca = Math.Cos(AlphaDeg * d), cb = Math.Cos(BetaDeg * d), cg = Math.Cos(GammaDeg * d), sg = Math.Sin(GammaDeg * d);
        double cy = (ca - cb * cg) / sg;
        double cz2 = 1 - cb * cb - cy * cy;
        if (!(cz2 > 0)) throw new StackFormatException("the lattice angles do not make a cell (they must satisfy the triangle inequalities)");
        return [[A, 0, 0], [B * cg, B * sg, 0], [C * cb, C * cy, C * Math.Sqrt(cz2)]];
    }

    /// <summary>The unit direction of u a + v b + w c.</summary>
    public double[] Direction(double[] uvw, string where)
    {
        var v = Vectors();
        var e = new double[3];
        for (int i = 0; i < 3; ++i) e[i] = uvw[0] * v[0][i] + uvw[1] * v[1][i] + uvw[2] * v[2][i];
        double len = Math.Sqrt(e[0] * e[0] + e[1] * e[1] + e[2] * e[2]);
        if (!(len > 0)) throw new StackFormatException($"{where} is the zero direction");
        return [e[0] / len, e[1] / len, e[2] / len];
    }

    /// <summary>{"a", "b", "c", "alpha", "beta", "gamma"}; the angles default to 90°.</summary>
    public static Lattice Parse(JsonNode? node, string where)
    {
        if (node is not JsonObject o) throw new StackFormatException($"{where} must be an object {{ a, b, c, alpha, beta, gamma }}");
        double N(string k, double? fallback = null)
        {
            if (o[k] is null)
                return fallback ?? throw new StackFormatException($"{where}.{k} is missing");
            double v = StackDocument.Num(o[k], $"{where}.{k}");
            if (!(v > 0) || (k.Length > 1 && v >= 180)) throw new StackFormatException($"{where}.{k} must be positive{(k.Length > 1 ? " and below 180°" : "")}");
            return v;
        }
        var lattice = new Lattice(N("a"), N("b"), N("c"), N("alpha", 90), N("beta", 90), N("gamma", 90));
        lattice.Vectors();   // refuse an impossible cell here, where its place in the file is known
        return lattice;
    }
}
