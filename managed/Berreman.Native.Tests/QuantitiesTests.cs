// QuantitiesTests.cs — the derived quantities (Api/Quantities.cs), above all the Mueller matrix's
// convention: an isotropic sample must give the standard ellipsometric form with the program's Ψ
// and Δ, element by element.

using System.Numerics;
using Berreman.Api;

namespace Berreman.Native.Tests;

internal static class QuantitiesTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static bool Same(double[,] m, double[,] e, double tol = 1e-13)
    {
        for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) if (!(Math.Abs(m[i, j] - e[i, j]) <= tol)) return false;
        return true;
    }

    public static void Run()
    {
        /* Simple Jones matrices. */
        Check(Same(Quantities.Mueller(1, 0, 0, 1), new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } }), "the identity's Mueller matrix is the identity");
        Check(Same(Quantities.Mueller(1, 0, 0, -1), new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, -1, 0 }, { 0, 0, 0, -1 } }), "diag(1, −1) turns S2 and S3 over");
        double t = 0.3, c = Math.Cos(t), s = Math.Sin(t);
        var rot = Quantities.Mueller(c, -s, s, c);
        Check(Math.Abs(rot[1, 1] - Math.Cos(2 * t)) < 1e-14 && Math.Abs(rot[1, 2] + Math.Sin(2 * t)) < 1e-14
              && Math.Abs(rot[2, 1] - Math.Sin(2 * t)) < 1e-14 && Math.Abs(rot[3, 3] - 1) < 1e-14, "a rotator by t turns (S1, S2) by 2t");

        /* An isotropic sample: the standard form with this program's Ψ and Δ. */
        var doc = StackDocument.Parse("""
            { "format": "berreman-stack", "version": 1, "units": { "length": "nm", "spectral": "nm" },
              "materials": { "air": { "n": 1 }, "film": { "n": [2.1, 0.05] }, "glass": { "n": 1.52 } },
              "incident": { "material": "air" }, "layers": [ { "material": "film", "thickness": 83 } ],
              "exit": { "material": "glass" }, "sweep": { "spectral": [450, 633], "angle_deg": [0, 45, 70] } }
            """);
        var result = Evaluator.Evaluate(doc);
        bool standard = true;
        foreach (var p in result.Points)
        {
            var (psi, delta) = p.Ellipsometry();
            double P = psi * Math.PI / 180, D = delta * Math.PI / 180;
            double N = Math.Cos(2 * P), C = Math.Sin(2 * P) * Math.Cos(D), S = Math.Sin(2 * P) * Math.Sin(D);
            standard &= Same(Quantities.Mueller(p), new double[,] { { 1, -N, 0, 0 }, { -N, 1, 0, 0 }, { 0, 0, C, -S }, { 0, 0, S, C } }, 1e-12);
        }
        Check(standard, "isotropic: Mr = [[1, −N, 0, 0], [−N, 1, 0, 0], [0, 0, C, −S], [0, 0, S, C]] with N = cos 2Ψ, C = sin 2Ψ cos Δ, S = sin 2Ψ sin Δ");

        /* Any Jones-derived matrix: Σ M_ij² = 4 M11². On the monoclinic sample, off-diagonal blocks and all. */
        var ga = Evaluator.Evaluate(ApiTests.Sample("schubert-2016/ga2o3-010.json"));
        double worst = 0, offBlock = 0;
        foreach (var p in ga.Points.Where((_, i) => i % 50 == 0))
        {
            var m = Quantities.Mueller(p);
            double sum = 0;
            for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) sum += m[i, j] * m[i, j];
            worst = Math.Max(worst, Math.Abs(sum - 4));
            offBlock = Math.Max(offBlock, Math.Abs(m[0, 2]) + Math.Abs(m[1, 3]));
        }
        Check(worst < 1e-10, $"a Jones-derived Mueller matrix has Σ M_ij² = 4 (worst {worst:G3})");
        Check(offBlock > 0.05, $"the monoclinic sample converts p and s (off-diagonal block up to {offBlock:G3})");

        /* Names. */
        var all = Quantities.All();
        Check(all.Select(q => q.Name).Distinct().Count() == all.Count, $"the {all.Count} quantity names are distinct");
        Check(Quantities.Find("mr34") is { Group: "normalised" } && Quantities.Find("Psi_ps") is not null, "quantities are found by name, in any case");
        Check(Quantities.Find("Psi_ps", "0", "1")?.Name == "Psi_01" && Quantities.Find("R_01")?.Name == "R_ps", "and in either naming of the waves");
    }
}
