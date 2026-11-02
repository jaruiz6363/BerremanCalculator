// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

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
                                bool Ellipsometry = false)
{
    /// <summary>More quantities by name (Api/Quantities.cs), beside the checkboxes'.</summary>
    public IReadOnlyList<string> More { get; init; } = [];

    /// <summary>The x axis asked for; null, or one the sweep does not vary, chooses as <see cref="ResultPlot.ChooseAxis"/> does.</summary>
    public XAxis? Axis { get; init; }
}

/// <summary>Sixteen panels, M11 to M44 row by row; Message when there is nothing to draw.</summary>
public sealed record MuellerPanels(IReadOnlyList<PlotSeries>[] Panels, string XTitle, XAxis Axis, string? Message);

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

    /// <summary>The axes this sweep can be plotted against: those it varies.</summary>
    public static IReadOnlyList<XAxis> Axes(StackDocument doc)
    {
        var list = new List<XAxis>();
        if (doc.Spectral.Length > 1) list.Add(XAxis.Spectral);
        if ((doc.AnglesDeg ?? doc.Xi!).Length > 1) list.Add(XAxis.Incidence);
        if (doc.AzimuthsDeg.Length > 1) list.Add(XAxis.Azimuth);
        return list;
    }

    public static XAxis ChooseAxis(StackDocument doc, XAxis? preferred)
    {
        var axes = Axes(doc);
        return preferred is { } p && axes.Contains(p) ? p : ChooseAxis(doc);
    }

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
        var axis = ChooseAxis(doc, choice.Axis);
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
        foreach (var name in choice.More)
        {
            if (Quantities.Find(name, a, b) is not { } q) continue;
            if (quantities.Any(x => x.Name == q.Name)) continue;
            quantities.Add((q.Name, q.Value, q.Group == "fraction" ? null : q.Group));
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

    /// <summary>
    /// The Mueller matrix as 16 panels, row by row, as ellipsometry papers draw it: each element
    /// normalised by M11 against the x axis, one curve per value of the other swept variables. The
    /// first panel is M11 itself, the unpolarised reflectance (or transmittance).
    /// </summary>
    public static MuellerPanels Mueller(EvaluationResult result, bool transmission, XAxis? preferred)
    {
        var doc = result.Document;
        var axis = ChooseAxis(doc, preferred);
        bool angles = doc.AnglesDeg is not null;
        string xTitle = axis switch
        {
            XAxis.Spectral => SpectralTitle(doc.SpectralUnit),
            XAxis.Incidence => angles ? "angle of incidence (°)" : "ξ = k_x / k₀",
            XAxis.Azimuth => "azimuth (°)",
            _ => "",
        };
        var empty = Enumerable.Range(0, 16).Select(_ => (IReadOnlyList<PlotSeries>)[]).ToArray();
        if (axis == XAxis.None) return new MuellerPanels(empty, xTitle, axis, "One point: see the table.");
        double X(PointResult p) => axis switch { XAxis.Spectral => p.Spectral, XAxis.Incidence => p.AngleDeg ?? p.Xi, _ => p.AzimuthDeg };
        string Label(PointResult p)
        {
            var parts = new List<string>();
            if (axis != XAxis.Spectral && doc.Spectral.Length > 1) parts.Add($"{p.Spectral:G6} {doc.SpectralUnit}");
            if (axis != XAxis.Azimuth && doc.AzimuthsDeg.Length > 1) parts.Add($"az {p.AzimuthDeg:G4}°");
            if (axis != XAxis.Incidence && (doc.AnglesDeg ?? doc.Xi!).Length > 1) parts.Add(angles ? $"{p.AngleDeg:G4}°" : $"ξ {p.Xi:G4}");
            return string.Join(", ", parts);
        }
        var groups = result.Points.GroupBy(Label).ToList();
        if (groups.Count > MaxCurves) return new MuellerPanels(empty, xTitle, axis, $"{groups.Count} curves per panel is too many; sweep fewer values.");
        int offset = transmission ? 4 : 0;
        var panels = new List<PlotSeries>[16];
        for (int k = 0; k < 16; ++k) panels[k] = [];
        int c = 0;
        foreach (var g in groups)
        {
            var pts = g.ToList();
            var x = pts.Select(X).ToArray();
            var ms = pts.Select(p => Quantities.Mueller(p, offset)).ToArray();
            uint colour = Palette[c++ % Palette.Length];
            for (int k = 0; k < 16; ++k)
            {
                int i = k / 4, j = k % 4;
                var y = k == 0
                    ? pts.Select(p => transmission ? (p.Power[1] + p.Power[4]) / 2 : (p.Power[0] + p.Power[3]) / 2).ToArray()
                    : ms.Select(m => m[i, j]).ToArray();
                string name = k == 0 ? (transmission ? "T" : "R") + " unpolarised" : $"M{i + 1}{j + 1}";
                panels[k].Add(new PlotSeries(g.Key == "" ? name : $"{name}, {g.Key}", x, y, colour));
            }
        }
        return new MuellerPanels(panels, xTitle, axis, null);
    }

    private static bool IsotropicOuter(EvaluationResult result)
    {
        var doc = result.Document;
        double lambda = result.Points.Count > 0 ? result.Points[0].WavelengthNm : 500;
        return doc.Materials[doc.Incident.Material].At(lambda, 0).IsIsotropic(out _, out _)
            && doc.Materials[doc.Exit.Material].At(lambda, 0).IsIsotropic(out _, out _);
    }
}
