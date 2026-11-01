// FieldProfile.cs — the fields against depth at one point of the sweep, and the stack's index
// profile, as curves. No Avalonia; tested.
//
// DEPTH runs from the first interface into the stack, in the stack's length unit; negative depths
// are in the incident medium and depths past the last interface in the exit medium. The engine
// gives the six field components per unit incident amplitude (contract, Section 5). |E|² is then
// the intensity relative to the incident wave's: 1 far into an index-matched incident medium with
// no reflection, 4 at the antinodes of a standing wave of total reflection.

using System.Globalization;
using System.Numerics;
using Berreman.Api;

namespace Berreman.Gui.Core;

/// <summary>One point's fields at many depths, and where the layers are.</summary>
public sealed record FieldProfileResult(
    double[] Depths,                 // in the stack's length unit
    PointResult Point,
    double[] Boundaries,             // the interfaces, in the length unit
    string[] LayerNames,             // the document layers, in order
    string LengthUnit,
    StackDocument Document);

public static class FieldProfile
{
    public static readonly string[] QuantityNames = ["|E|²", "|Ex|², |Ey|², |Ez|²", "|H̃|²", "Re Ex, Re Ey, Re Ez"];
    public static readonly string[] WaveNames = ["incident p (mode 0)", "incident s (mode 1)", "unpolarised (mean of the two intensities)"];

    /// <summary>
    /// The stack evaluated at one spectral value, one angle (or ξ) and one azimuth, at
    /// <paramref name="samples"/> depths from <paramref name="before"/> in front of the stack to
    /// <paramref name="after"/> behind it (all in the length unit).
    /// </summary>
    public static FieldProfileResult Compute(StackModel model, double spectral, double incidence, double azimuth,
                                             int samples, double before, double after)
    {
        if (samples < 2) throw new StackFormatException("a profile needs at least two depths");
        var inv = CultureInfo.InvariantCulture;
        var m = model.Clone();
        m.Spectral = spectral.ToString("R", inv);
        m.Angles = incidence.ToString("R", inv);
        m.Azimuths = azimuth.ToString("R", inv);
        var doc = m.ToDocument();
        double nm = Units.NmPerLength(doc.LengthUnit);
        var bounds = new List<double> { 0 };
        foreach (var l in doc.Layers) bounds.Add(bounds[^1] + l.ThicknessNm / nm);
        double total = bounds[^1];
        var depths = Enumerable.Range(0, samples).Select(i => -before + (total + before + after) * i / (samples - 1)).ToArray();
        var result = Evaluator.Evaluate(doc, depthsNm: depths.Select(z => z * nm).ToArray());
        var point = result.Points[0];
        if (point.Fields is null) throw new StackFormatException($"the point was not evaluated: {point.Status}");
        return new FieldProfileResult(depths, point, bounds.ToArray(), doc.Layers.Select(l => l.Material).ToArray(), doc.LengthUnit, doc);
    }

    /// <summary>The curves of one quantity (QuantityNames) for one wave (WaveNames: 0, 1 or the mean).</summary>
    public static List<PlotSeries> Curves(FieldProfileResult f, int quantity, int wave)
    {
        int n = f.Depths.Length;
        double I(int w, int d, int a, int b) { double s = 0; for (int c = a; c < b; ++c) { var z = f.Point.Field(w, d, c, n); s += z.Real * z.Real + z.Imaginary * z.Imaginary; } return s; }
        double Of(Func<int, int, double> v, int d) => wave == 2 ? (v(0, d) + v(1, d)) / 2 : v(wave, d);
        PlotSeries S(string name, Func<int, int, double> v, uint colour, bool dashed = false) =>
            new(name, f.Depths, Enumerable.Range(0, n).Select(d => Of(v, d)).ToArray(), colour, dashed);
        return quantity switch
        {
            0 => [S("|E|²", (w, d) => I(w, d, 0, 3), 0xFF1F77B4)],
            1 =>
            [
                S("|Ex|²", (w, d) => I(w, d, 0, 1), 0xFF1F77B4),
                S("|Ey|²", (w, d) => I(w, d, 1, 2), 0xFFD62728),
                S("|Ez|²", (w, d) => I(w, d, 2, 3), 0xFF2CA02C),
            ],
            2 => [S("|H̃|²", (w, d) => I(w, d, 3, 6), 0xFF9467BD)],
            _ => wave == 2 ? [] :
            [
                S("Re Ex", (w, d) => f.Point.Field(w, d, 0, n).Real, 0xFF1F77B4),
                S("Re Ey", (w, d) => f.Point.Field(w, d, 1, n).Real, 0xFFD62728),
                S("Re Ez", (w, d) => f.Point.Field(w, d, 2, n).Real, 0xFF2CA02C),
            ],
        };
    }

    /// <summary>
    /// The index profile at the profile's wavelength and azimuth: n and k along x, y and z, √ε_ii
    /// of each medium as placed in the stack (its rotation and the sample's azimuth applied),
    /// drawn as steps across the same depths.
    /// </summary>
    public static List<PlotSeries> IndexProfile(FieldProfileResult f)
    {
        var doc = f.Document;
        double lambda = f.Point.WavelengthNm, az = f.Point.AzimuthDeg;
        MaterialMatrix Placed(string material, double[] euler, double thicknessNm)
        {
            var mm = doc.Materials[material].At(lambda, thicknessNm);
            var r = MaterialMatrix.Mul(MaterialMatrix.EulerRotation(az, 0, 0), MaterialMatrix.EulerRotation(euler[0], euler[1], euler[2]));
            return mm.Rotated(r);
        }
        var media = new List<(double From, double To, MaterialMatrix M)>
        {
            (f.Depths[0], 0, Placed(doc.Incident.Material, doc.Incident.EulerDeg, 0)),
        };
        double nm = Units.NmPerLength(f.LengthUnit);
        for (int k = 0; k < doc.Layers.Count; ++k)
            media.Add((f.Boundaries[k], f.Boundaries[k + 1], Placed(doc.Layers[k].Material, doc.Layers[k].EulerDeg, doc.Layers[k].ThicknessNm)));
        media.Add((f.Boundaries[^1], f.Depths[^1], Placed(doc.Exit.Material, doc.Exit.EulerDeg, 0)));

        string[] axis = ["x", "y", "z"];
        uint[] colours = [0xFF1F77B4, 0xFFD62728, 0xFF2CA02C];
        var list = new List<PlotSeries>();
        for (int c = 0; c < 3; ++c)
        {
            var x = new List<double>(); var yn = new List<double>(); var yk = new List<double>();
            foreach (var (from, to, mm) in media)
            {
                if (to < from) continue;
                var ix = Complex.Sqrt(mm[c, c]);
                if (ix.Imaginary < 0) ix = -ix;
                x.Add(from); x.Add(to); yn.Add(ix.Real); yn.Add(ix.Real); yk.Add(ix.Imaginary); yk.Add(ix.Imaginary);
            }
            list.Add(new PlotSeries($"n_{axis[c]}", x.ToArray(), yn.ToArray(), colours[c]));
            list.Add(new PlotSeries($"k_{axis[c]}", x.ToArray(), yk.ToArray(), colours[c], Dashed: true, Axis: "k"));
        }
        return list;
    }
}
