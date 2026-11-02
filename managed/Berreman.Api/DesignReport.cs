// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// DesignReport.cs — a stack as numbers another program can take: the layers in physical units with
// their orientations, and every material as a table over a spectral range.
//
// ThinFilmDesigner's report is the model (the user, 2026-10-22, for that program: "something a user
// would need to input into another program where dispersion equations are not available"). Here a
// material may be anisotropic, so its table is the least that describes it:
//   - isotropic, μ = 1:               n, k
//   - diagonal ε, μ = 1 (aligned):    n and k along x, y, z
//   - anything else:                  every element of the 6×6 M that is not zero anywhere in the
//                                     range, real and imaginary parts, (Ex, Ey, Ez, H̃x, H̃y, H̃z)
// always in the MATERIAL's own axes; each layer's Euler angles (and, for its z axis, the tilt and
// azimuth they give) are listed with the layer, so the other program applies the rotation.
// One text file: lines that are not table rows start with '#' or '[', and tables are tab-separated.

using System.Globalization;
using System.Numerics;
using System.Text;

namespace Berreman.Api;

public static class DesignReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Field = ["Ex", "Ey", "Ez", "Hx", "Hy", "Hz"];

    /// <summary>The report over [from, to] in steps of <paramref name="step"/>, in the stack's spectral unit.</summary>
    public static string Build(StackModel model, double from, double to, double step)
    {
        if (!(step > 0) || !(to >= from)) throw new StackFormatException("the report's range needs from ≤ to and a positive step");
        int n = (int)Math.Floor((to - from) / step + 1e-9) + 1;
        if (n > 20001) throw new StackFormatException($"{n} spectral values is too many for a report; use a larger step");
        var x = Enumerable.Range(0, n).Select(i => from + i * step).ToArray();
        var doc = model.ToDocument();
        string lu = model.LengthUnit, su = model.SpectralUnit;
        var sb = new StringBuilder();
        sb.AppendLine("# BerremanCalculator design report");
        if (model.Title.Length > 0) sb.AppendLine($"# {model.Title}");
        if (model.Source.Length > 0) sb.AppendLine($"# Source: {model.Source}");
        sb.AppendLine($"# Time dependence exp(-iwt): an absorbing medium has k > 0. H is Z0 H. Lengths in {lu}; spectral values in {su}.");
        sb.AppendLine("# Materials are given in their own axes. A layer's Euler angles [phi, theta, psi] rotate them into the stack:");
        sb.AppendLine("# R = Rz(phi) Rx(theta) Rz(psi), M_stack = R M R^T, z normal to the layers, into the stack (docs/contract.md, Section 1).");
        sb.AppendLine();

        sb.AppendLine("[stack]");
        sb.AppendLine("# medium\tmaterial\tthickness\tphi\ttheta\tpsi\taxis tilt\taxis azimuth\ttwist");
        string Row(string label, string material, string thickness, double[] e, string twist)
        {
            var (tilt, az) = MaterialMatrix.AxisDirection(e[0], e[1], e[2]);
            return string.Join("\t", label, material, thickness, F(e[0]), F(e[1]), F(e[2]), F(tilt), F(az), twist);
        }
        sb.AppendLine(Row("incident", model.Incident.Material, "", model.Incident.EulerDeg, ""));
        double total = 0;
        for (int k = 0; k < model.Layers.Count; ++k)
        {
            var l = model.Layers[k];
            total += l.Thickness;
            string twist = l.Twist is { } t ? $"pitch {F(t.Pitch)} {lu}, {t.Handedness}, from {F(t.StartDeg)} deg" : "";
            sb.AppendLine(Row($"layer {k + 1}", l.Material, F(l.Thickness), l.EulerDeg, twist));
        }
        sb.AppendLine(Row("exit", model.Exit.Material, "", model.Exit.EulerDeg, ""));
        sb.AppendLine($"# total thickness {F(total)} {lu}");
        sb.AppendLine();

        var used = new[] { model.Incident.Material }.Concat(model.Layers.Select(l => l.Material)).Append(model.Exit.Material).Distinct();
        foreach (var name in used)
        {
            var mat = doc.Materials[name];
            var thickness = model.Layers.FirstOrDefault(l => l.Material == name)?.Thickness * Units.NmPerLength(lu) ?? 0;
            var ms = new MaterialMatrix?[n];
            var refused = new string?[n];
            for (int i = 0; i < n; ++i)
            {
                try { ms[i] = mat.At(Units.ToWavelengthNm(x[i], su), thickness); }
                catch (StackFormatException e) { refused[i] = e.Message; }
            }
            var ok = ms.Where(m => m is not null).Select(m => m!).ToList();
            bool iso = ok.All(m => m.IsIsotropic(out _, out var mu) && mu == Complex.One);
            bool diag = ok.All(m => Enumerable.Range(0, 36).All(k => k / 6 == k % 6 || m.M[k] == Complex.Zero)
                                    && m[3, 3] == Complex.One && m[4, 4] == Complex.One && m[5, 5] == Complex.One);
            var nonzero = Enumerable.Range(0, 36).Where(k => ok.Any(m => m.M[k] != Complex.Zero)).ToList();

            sb.AppendLine($"[material {name}]");
            if (model.Material(name)?.Spec["source"]?.GetValue<string>() is { Length: > 0 } src) sb.AppendLine($"# source: {src}");
            if (iso) sb.AppendLine($"# isotropic\n# {su}\tn\tk");
            else if (diag) sb.AppendLine($"# diagonal epsilon, mu = 1: n and k along the material's x, y, z\n# {su}\tn_x\tk_x\tn_y\tk_y\tn_z\tk_z");
            else
                sb.AppendLine("# general: the elements of the 6x6 M that are not zero, real and imaginary parts\n# "
                              + su + "\t" + string.Join("\t", nonzero.Select(k => $"M({Field[k / 6]},{Field[k % 6]})_re\tM({Field[k / 6]},{Field[k % 6]})_im")));
            for (int i = 0; i < n; ++i)
            {
                if (ms[i] is not { } m) { sb.AppendLine($"# {F(x[i])}: {refused[i]}"); continue; }
                var cells = new List<string> { F(x[i]) };
                if (iso || diag)
                    foreach (int c in iso ? new[] { 0 } : new[] { 0, 1, 2 })
                    {
                        var nk = Complex.Sqrt(m[c, c]);
                        if (nk.Imaginary < 0) nk = -nk;
                        cells.Add(F(nk.Real)); cells.Add(F(nk.Imaginary));
                    }
                else
                    foreach (int k in nonzero) { cells.Add(F(m.M[k].Real)); cells.Add(F(m.M[k].Imaginary)); }
                sb.AppendLine(string.Join("\t", cells));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>The stack's spectral sweep from its first to its last value, in about 100 steps (one row for one value).</summary>
    public static (double From, double To, double Step) DefaultRange(StackModel model)
    {
        var v = StackDocument.Values(SweepText.Parse(model.Spectral, "spectral"), "spectral");
        double lo = v.Min(), hi = v.Max();
        if (hi == lo) return (lo, hi, 1);
        double step = MathStep((hi - lo) / 100);
        return (lo, hi, step);
    }

    /// <summary>A round step: 1, 2 or 5 times a power of ten, at most the given size.</summary>
    private static double MathStep(double rough)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double r = rough / p;
        return (r >= 5 ? 5 : r >= 2 ? 2 : 1) * p;
    }

    private static string F(double v) => (v == 0 ? 0 : v).ToString("G10", Inv);
}
