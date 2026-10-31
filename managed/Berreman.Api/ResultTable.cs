// ResultTable.cs — results as a table of named columns, for the CLI, the MCP server and the GUI
// alike, so all three show the same numbers under the same names.

using System.Globalization;
using System.Text;
using Berreman.Native;

namespace Berreman.Api;

public sealed class ResultTable
{
    public List<string> Columns { get; } = [];
    public List<string[]> Rows { get; } = [];

    public static ResultTable Build(EvaluationResult result, bool jones = false, bool ellipsometry = false, bool absorption = false)
    {
        var doc = result.Document;
        var t = new ResultTable();
        bool anyAzimuth = doc.AzimuthsDeg.Length > 1 || doc.AzimuthsDeg[0] != 0;
        double lambda0 = result.Points.Count > 0 ? result.Points[0].WavelengthNm : 500;
        bool isoOuter = doc.Materials[doc.Incident.Material].At(lambda0, 0).IsIsotropic(out _, out _)
                     && doc.Materials[doc.Exit.Material].At(lambda0, 0).IsIsotropic(out _, out _);
        string a = isoOuter ? "p" : "0", b = isoOuter ? "s" : "1";

        t.Columns.Add(SpectralName(doc.SpectralUnit));
        if (anyAzimuth) t.Columns.Add("azimuth_deg");
        t.Columns.Add(doc.AnglesDeg is null ? "xi" : "angle_deg");
        t.Columns.AddRange([$"R_{a}", $"T_{a}", $"A_{a}", $"R_{b}", $"T_{b}", $"A_{b}"]);
        if (jones)
            foreach (var n in new[] { $"r_{a}{a}", $"r_{a}{b}", $"r_{b}{a}", $"r_{b}{b}", $"t_{a}{a}", $"t_{a}{b}", $"t_{b}{a}", $"t_{b}{b}" })
                t.Columns.AddRange([$"{n}_re", $"{n}_im"]);
        if (ellipsometry) t.Columns.AddRange(["Psi_deg", "Delta_deg"]);
        if (absorption)
            for (int j = 0; j < 2; ++j)
                for (int k = 0; k < doc.Layers.Count; ++k)
                    t.Columns.Add($"A_{(j == 0 ? a : b)}_layer{k + 1}");
        t.Columns.Add("status");

        foreach (var p in result.Points)
        {
            var row = new List<string> { F(p.Spectral) };
            if (anyAzimuth) row.Add(F(p.AzimuthDeg));
            row.Add(F(p.AngleDeg ?? p.Xi));
            row.AddRange(p.Power.Select(F));
            if (jones) foreach (var z in p.Jones) row.AddRange([F(z.Real), F(z.Imaginary)]);
            if (ellipsometry) { var (psi, delta) = p.Ellipsometry(); row.AddRange([F(psi), F(delta)]); }
            if (absorption) row.AddRange(p.LayerAbsorption.Select(F));
            row.Add(p.Status == BcStatus.Ok ? (p.Diagnostics.Opaque != 0 ? "ok (opaque)" : "ok") : p.Status.ToString());
            t.Rows.Add([.. row]);
        }
        return t;
    }

    private static string SpectralName(string unit) => unit switch
    {
        "GHz" or "THz" => $"frequency_{unit}",
        "cm-1" => "wavenumber_cm-1",
        "eV" => "energy_eV",
        _ => $"wavelength_{unit}",
    };

    private static string F(double x) => double.IsNaN(x) ? "NaN" : x.ToString("G10", CultureInfo.InvariantCulture);

    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Columns)).Append('\n');
        foreach (var r in Rows) sb.Append(string.Join(",", r)).Append('\n');
        return sb.ToString();
    }

    public string ToText()
    {
        var w = Columns.Select((c, i) => Math.Max(c.Length, Rows.Count == 0 ? 0 : Rows.Max(r => r[i].Length))).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("  ", Columns.Select((c, i) => c.PadLeft(w[i]))));
        foreach (var r in Rows) sb.AppendLine(string.Join("  ", r.Select((c, i) => c.PadLeft(w[i]))));
        return sb.ToString();
    }
}
