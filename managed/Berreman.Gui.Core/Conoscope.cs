// Conoscope.cs — a conoscopic map: one quantity over every direction of incidence at one
// wavelength, the angle of incidence as the radius and the azimuth as the polar angle. No Avalonia:
// the sweep, the quantities and the colours are arithmetic, and tested.
//
// THE AZIMUTH. The program turns the sample about z (azimuth_deg) and keeps the plane of incidence
// x–z, so the map's polar angle is the sample's azimuth: at polar angle φ, light comes in along a
// plane at −φ to the sample's x axis. A polariser fixed to the sample at angle P from its x axis
// is therefore at P + φ from the plane of incidence.
//
// POLARISERS. Between a polariser at P and an analyser at A, both fixed to the sample, the
// transmitted fraction is |a · J p|² κ: J the transmission Jones matrix in the (p, s) basis,
// p = (cos(P + φ), sin(P + φ)) and a likewise the projections onto p̂ and ŝ, and κ the power factor
// of a transmitted amplitude, T_s / (|t_ss|² + |t_sp|²). This is exact for isotropic outer media,
// whose p and s waves carry power with the same factor, and is refused otherwise.

using System.Numerics;
using Berreman.Api;

namespace Berreman.Gui.Core;

/// <summary>One quantity a map can show.</summary>
public sealed record ConoscopeQuantity(string Name, string Description, Func<PointResult, double> Value);

/// <summary>A computed map: Values[angle index, azimuth index], with the grids and the value range.</summary>
public sealed record ConoscopeMap(double[] AnglesDeg, double[] AzimuthsDeg, double[,] Values, string Quantity, double Min, double Max);

public static class Conoscope
{
    /// <summary>The quantities offered: the polariser pairs first, then every derived quantity.</summary>
    public static IReadOnlyList<ConoscopeQuantity> Quantities(double polariserDeg = 0)
    {
        var list = new List<ConoscopeQuantity>
        {
            new($"T between crossed polarisers ({polariserDeg:G4}°, {polariserDeg + 90:G4}°)",
                "transmittance between a polariser and an analyser crossed with it, both fixed to the sample",
                p => Polarised(p, polariserDeg, polariserDeg + 90)),
            new($"T between parallel polarisers ({polariserDeg:G4}°)",
                "transmittance between a polariser and an analyser parallel to it, both fixed to the sample",
                p => Polarised(p, polariserDeg, polariserDeg)),
        };
        list.AddRange(Berreman.Api.Quantities.All().Select(q => new ConoscopeQuantity(q.Name, q.Description, q.Value)));
        return list;
    }

    /// <summary>|a · J p|² κ for a polariser at P and an analyser at A from the sample's x axis (header).</summary>
    public static double Polarised(PointResult p, double polariserDeg, double analyserDeg)
    {
        const double d = Math.PI / 180;
        double ap = (polariserDeg + p.AzimuthDeg) * d, aa = (analyserDeg + p.AzimuthDeg) * d;
        Complex tpp = p.Jones[4], tps = p.Jones[5], tsp = p.Jones[6], tss = p.Jones[7];
        /* Out = J in, J = [[tpp, tsp], [tps, tss]] (first subscript incident). */
        Complex inP = Math.Cos(ap), inS = Math.Sin(ap);
        Complex outP = tpp * inP + tsp * inS, outS = tps * inP + tss * inS;
        Complex amp = Math.Cos(aa) * outP + Math.Sin(aa) * outS;
        double norm = tss.Magnitude * tss.Magnitude + tsp.Magnitude * tsp.Magnitude;
        if (!(norm > 0)) return double.NaN;
        double kappa = p.Power[4] / norm;
        return amp.Magnitude * amp.Magnitude * kappa;
    }

    /// <summary>The stack with its sweep replaced by the map's: one spectral value, angles 0 to max, azimuths 0 to 360.</summary>
    public static StackModel Sweep(StackModel model, double spectral, double maxAngleDeg, int angles, int azimuths)
    {
        if (!(maxAngleDeg > 0 && maxAngleDeg < 90)) throw new StackFormatException("the largest angle of a map must be between 0 and 90°");
        if (angles < 2 || azimuths < 4) throw new StackFormatException("a map needs at least 2 angles and 4 azimuths");
        var m = model.Clone();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        m.Spectral = spectral.ToString("R", inv);
        m.AnglesAreXi = false;
        m.Angles = $"0 to {maxAngleDeg.ToString("R", inv)} count {angles}";
        /* 0 to 360 with the last value left out: it is the first. */
        double step = 360.0 / azimuths;
        m.Azimuths = $"0 to {(360 - step).ToString("R", inv)} count {azimuths}";
        return m;
    }

    /// <summary>The map of one quantity, from an evaluation of <see cref="Sweep"/>'s stack.</summary>
    public static ConoscopeMap Build(EvaluationResult result, ConoscopeQuantity quantity)
    {
        var doc = result.Document;
        var angles = doc.AnglesDeg ?? throw new StackFormatException("a map is swept over angles of incidence");
        if (quantity.Name.StartsWith("T between", StringComparison.Ordinal))
        {
            double l = result.Points.Count > 0 ? result.Points[0].WavelengthNm : 500;
            bool iso = doc.Materials[doc.Incident.Material].At(l, 0).IsIsotropic(out _, out _)
                    && doc.Materials[doc.Exit.Material].At(l, 0).IsIsotropic(out _, out _);
            if (!iso) throw new StackFormatException("transmission between polarisers needs isotropic incident and exit media (p and s waves)");
        }
        var az = doc.AzimuthsDeg;
        var v = new double[angles.Length, az.Length];
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var p in result.Points)
        {
            int i = Array.IndexOf(angles, p.AngleDeg ?? double.NaN), j = Array.IndexOf(az, p.AzimuthDeg);
            if (i < 0 || j < 0) continue;
            double x = quantity.Value(p);
            v[i, j] = x;
            if (double.IsFinite(x)) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
        }
        if (lo > hi) { lo = 0; hi = 1; }
        return new ConoscopeMap(angles, az, v, quantity.Name, lo, hi);
    }

    /// <summary>
    /// The value at a point of the polar picture, interpolated: radius r as an angle of incidence
    /// in degrees, polar angle φ in degrees. NaN outside the largest angle.
    /// </summary>
    public static double At(ConoscopeMap map, double rDeg, double phiDeg)
    {
        var a = map.AnglesDeg; var z = map.AzimuthsDeg;
        if (rDeg < a[0] || rDeg > a[^1]) return double.NaN;
        double fi = (rDeg - a[0]) / (a[^1] - a[0]) * (a.Length - 1);
        int i0 = Math.Min((int)Math.Floor(fi), a.Length - 2);
        double ti = fi - i0;
        double step = 360.0 / z.Length;
        double fj = ((phiDeg % 360) + 360) % 360 / step;
        int j0 = (int)Math.Floor(fj) % z.Length, j1 = (j0 + 1) % z.Length;
        double tj = fj - Math.Floor(fj);
        double v00 = map.Values[i0, j0], v01 = map.Values[i0, j1], v10 = map.Values[i0 + 1, j0], v11 = map.Values[i0 + 1, j1];
        return (1 - ti) * ((1 - tj) * v00 + tj * v01) + ti * ((1 - tj) * v10 + tj * v11);
    }

    /// <summary>A perceptually even colour scale (viridis, by its ten tabulated stops), for t in [0, 1].</summary>
    public static uint Colour(double t)
    {
        if (!double.IsFinite(t)) return 0xFFFFFFFF;
        t = Math.Clamp(t, 0, 1);
        double f = t * (Viridis.Length - 1);
        int k = Math.Min((int)f, Viridis.Length - 2);
        double u = f - k;
        uint a = Viridis[k], b = Viridis[k + 1];
        uint Mix(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - u) + ((b >> shift) & 0xFF) * u);
        return 0xFF000000 | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
    }

    private static readonly uint[] Viridis =
        [0x440154, 0x482878, 0x3E4A89, 0x31688E, 0x26828E, 0x1F9E89, 0x35B779, 0x6ECE58, 0xB5DE2B, 0xFDE725];
}
