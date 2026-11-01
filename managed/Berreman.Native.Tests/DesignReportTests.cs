// DesignReportTests.cs — the design report (Api/DesignReport.cs): the right table for each kind of
// material, and the numbers in it are the material's.

using System.Globalization;
using Berreman.Api;

namespace Berreman.Native.Tests;

internal static class DesignReportTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static StackModel Sample(string rel) =>
        StackModel.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "literature", rel)));

    /// <summary>The rows of one material's table, as numbers.</summary>
    private static List<double[]> Table(string report, string material) =>
        report.Split('\n').SkipWhile(l => l.Trim() != $"[material {material}]").Skip(1)
              .TakeWhile(l => l.Trim().Length > 0).Where(l => !l.StartsWith('#'))
              .Select(l => l.Trim().Split('\t').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray()).ToList();

    public static void Run()
    {
        var ga = Sample("schubert-2016/ga2o3-010.berr");
        var text = DesignReport.Build(ga, 400, 800, 100);
        var rows = Table(text, "Ga2O3");
        var m = ga.ToDocument().Materials["Ga2O3"].At(1e7 / 500, 0);
        Check(rows.Count == 5 && rows[1][0] == 500, "a general material: a row per spectral value");
        Check(text.Contains("M(Ex,Ey)_re") && !text.Contains("M(Ex,Ez)_re"), "its columns are the elements that are not zero");
        Check(Math.Abs(rows[1][1] - m[0, 0].Real) < 1e-8 * Math.Abs(m[0, 0].Real) && Math.Abs(rows[1][4] - m[0, 1].Imaginary) < 1e-8 * Math.Abs(m[0, 1].Imaginary),
              "and the numbers are the material's");
        Check(Table(text, "air").All(r => r.Length == 3 && r[1] == 1 && r[2] == 0), "an isotropic material is n, k");
        Check(text.Contains("exit\tGa2O3\t\t62.5\t0.4\t0"), "the exit medium's Euler angles are listed");

        var sic = DesignReport.Build(Sample("passler-2017/sic-otto.berr"), 700, 1000, 100);
        Check(Table(sic, "SiC").All(r => r.Length == 7 && r[1] == r[3] && r[1] != r[5]), "a uniaxial crystal: n, k along x, y, z, x and y equal");

        var krs = DesignReport.Build(Sample("passler-2017/sic-otto.berr"), 100, 300, 100);
        Check(krs.Contains("# 100: ") && krs.Contains("outside the Sellmeier formula's range"), "a value outside a material's range is a comment saying why");

        var (f, t, s) = DesignReport.DefaultRange(ga);
        Check(f == 125 && t == 1200 && s == 10, $"the default range is the sweep's, in round steps ({f}, {t}, {s})");
    }
}
