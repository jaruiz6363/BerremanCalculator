// ResultPlot.cs — which curves a result becomes. No Avalonia: the choices are arithmetic and tested.
//
// THE X AXIS IS WHATEVER THE SWEEP VARIES MOST NATURALLY: the spectral variable if there is more
// than one, otherwise the angle (or ξ), otherwise the azimuth. Every other combination of sweep
// values becomes its own curve, labelled by what distinguishes it, so a sweep over four angles and
// a spectrum shows four curves against wavelength.

using Berreman.Api;

namespace Berreman.Gui.Core;

public enum XAxis { Spectral, Incidence, Azimuth, None }

/// <summary>What to draw. Polarisations: the p and s waves (or modes 0 and 1), and their mean,
/// which is the unpolarised value.</summary>
public sealed record PlotChoice(bool R = true, bool T = true, bool A = false,
                                bool P = true, bool S = true, bool Mean = false,
                                bool Ellipsometry = false);

public sealed record ResultCurves(IReadOnlyList<PlotSeries> Series, string XTitle, string YTitle, XAxis Axis, string? Message);

public static class ResultPlot
{
    /// <summary>The 10 distinct colours the curves cycle through.</summary>
    public static readonly uint[] Palette =
    [
        0xFF1F77B4, 0xFFD62728, 0xFF2CA02C, 0xFFFF7F0E, 0xFF9467BD,
        0xFF8C564B, 0xFFE377C2, 0xFF7F7F7F, 0xFFBCBD22, 0xFF17BECF,
    ];

    /// <summary>The most curves one plot will hold before it says so instead of drawing a tangle.</summary>
    public const int MaxCurves = 40;

    public static XAxis ChooseAxis(StackDocument doc)
    {
        int nIncidence = (doc.AnglesDeg ?? doc.Xi!).Length;
        if (doc.Spectral.Length > 1) return XAxis.Spectral;
        if (nIncidence > 1) return XAxis.Incidence;
        if (doc.AzimuthsDeg.Length > 1) return XAxis.Azimuth;
        return XAxis.None;
    }

    public static string SpectralTitle(string unit) => unit switch
    {
        "GHz" or "THz" => $"frequency ({unit})",
        "cm-1" => "wavenumber (cm⁻¹)",
        "eV" => "photon energy (eV)",
        _ => $"wavelength ({unit})",
    };

    public static ResultCurves Build(EvaluationResult result, PlotChoice choice)
    {
        var doc = result.Document;
        var axis = ChooseAxis(doc);
        bool angles = doc.AnglesDeg is not null;
        string xTitle = axis switch
        {
            XAxis.Spectral => SpectralTitle(doc.SpectralUnit),
            XAxis.Incidence => angles ? "angle of incidence (°)" : "ξ = k_x / k₀",
            XAxis.Azimuth => "azimuth (°)",
            _ => "",
        };
        if (axis == XAxis.None)
            return new ResultCurves([], xTitle, "", axis, "One point: see the table.");

        double X(PointResult p) => axis switch
        {
            XAxis.Spectral => p.Spectral,
            XAxis.Incidence => p.AngleDeg ?? p.Xi,
            _ => p.AzimuthDeg,
        };

        /* The other sweep values label each curve. */
        string Label(PointResult p)
        {
            var parts = new List<string>();
            if (axis != XAxis.Spectral && doc.Spectral.Length > 1) parts.Add($"{p.Spectral:G6} {doc.SpectralUnit}");
            if (axis != XAxis.Azimuth && doc.AzimuthsDeg.Length > 1) parts.Add($"az {p.AzimuthDeg:G4}°");
            if (axis != XAxis.Incidence && (doc.AnglesDeg ?? doc.Xi!).Length > 1)
                parts.Add(angles ? $"{p.AngleDeg:G4}°" : $"ξ {p.Xi:G4}");
            return string.Join(", ", parts);
        }

        var groups = result.Points.GroupBy(Label).ToList();
        bool iso = IsotropicOuter(result);
        string a = iso ? "p" : "0", b = iso ? "s" : "1";

        var quantities = new List<(string Name, Func<PointResult, double> Value, string? Axis)>();
        void Add(bool on, string q, int offset)
        {
            if (!on) return;
            if (choice.P) quantities.Add(($"{q}_{a}", p => p.Power[offset], null));
            if (choice.S) quantities.Add(($"{q}_{b}", p => p.Power[3 + offset], null));
            if (choice.Mean) quantities.Add(($"{q} mean", p => (p.Power[offset] + p.Power[3 + offset]) / 2, null));
        }
        Add(choice.R, "R", 0);
        Add(choice.T, "T", 1);
        Add(choice.A, "A", 2);
        if (choice.Ellipsometry)
        {
            quantities.Add(("Ψ", p => p.Ellipsometry().Psi, "degrees"));
            quantities.Add(("Δ", p => p.Ellipsometry().Delta, "degrees"));
        }

        int curves = groups.Count * quantities.Count;
        if (curves == 0) return new ResultCurves([], xTitle, "", axis, "Nothing selected to plot.");
        if (curves > MaxCurves)
            return new ResultCurves([], xTitle, "", axis,
                $"{curves} curves is too many to read in one plot; select fewer quantities, or sweep fewer values.");

        var series = new List<PlotSeries>();
        int c = 0;
        foreach (var g in groups)
            foreach (var (name, value, yAxis) in quantities)
            {
                var pts = g.ToList();
                var label = g.Key == "" ? name : $"{name}, {g.Key}";
                series.Add(new PlotSeries(label, pts.Select(X).ToArray(), pts.Select(value).ToArray(),
                                          Palette[c++ % Palette.Length], Axis: yAxis));
            }
        return new ResultCurves(series, xTitle, "fraction (1 = 100%)", axis, null);
    }

    private static bool IsotropicOuter(EvaluationResult result)
    {
        var doc = result.Document;
        double lambda = result.Points.Count > 0 ? result.Points[0].WavelengthNm : 500;
        return doc.Materials[doc.Incident.Material].At(lambda, 0).IsIsotropic(out _, out _)
            && doc.Materials[doc.Exit.Material].At(lambda, 0).IsIsotropic(out _, out _);
    }
}
