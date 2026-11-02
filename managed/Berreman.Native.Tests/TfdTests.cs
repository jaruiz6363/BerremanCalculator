// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// TfdTests.cs — ThinFilmDesigner designs in and out (Api/TfdFile.cs).
//
// THE REFERENCE IS THINFILMDESIGNER ITSELF. Three of its samples (tests/fixtures/tfd, its own files
// byte for byte) were evaluated by ThinFilmDesigner's engine through its MCP server on 2026-11-01,
// and the numbers below are what it returned. The designs cover a constant absorbing metal
// (Low-e), constant dielectrics (AR-visible), and Cauchy, Sellmeier and a catalogue glass over 52
// layers (narrow-bandpass-550, whose 8 nm passband makes it sensitive to every thickness and index).
// Two independent engines and two material implementations must agree.

using System.Reflection;
using Berreman.Api;

namespace Berreman.Native.Tests;

internal static class TfdTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static string Fixture(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().First(n => n.Replace('\\', '/').EndsWith("fixtures/tfd/" + name));
        using var s = asm.GetManifestResourceStream(res)!;
        return new StreamReader(s).ReadToEnd();
    }

    /// <summary>ThinFilmDesigner's RS, RP, TS, TP at the wavelengths and angle given.</summary>
    private sealed record Reference(string File, double Angle, double[] Wavelengths, double[] Rs, double[] Rp, double[] Ts, double[] Tp);

    private static readonly Reference[] References =
    [
        new("Low-e.tfd", 45, [400, 900, 1400, 1900, 2400],
            [0.5490644331061786, 0.1086967567069407, 0.11168303479441621, 0.11055062023282042, 0.10872715875001057],
            [0.525029964019883, 0.13924414371552077, 0.0663582165566705, 0.04201685373146314, 0.03081678984585463],
            [0.24070653106157242, 0.7632519013032317, 0.8131429401998523, 0.8353307419323477, 0.8486950850857791],
            [0.2981640089562778, 0.759732150303634, 0.8698608200552916, 0.9112207431521019, 0.9321709831242406]),
        new("AR-visible.tfd", 30, [420, 485, 550, 615, 680],
            [0.012765605603989464, 0.0008351553256452111, 0.005529750918015666, 0.006653290628614722, 0.00960169545161986],
            [0.005217177495322149, 0.0005298826006999802, 0.00185926977630691, 0.0023417739671184033, 0.006533354653240211],
            [0.9872343943960108, 0.9991648446743547, 0.9944702490819842, 0.9933467093713855, 0.9903983045483801],
            [0.9947828225046778, 0.9994701173992998, 0.998140730223693, 0.9976582260328818, 0.9934666453467592]),
        new("narrow-bandpass-550.tfd", 20, [540, 545, 550, 555, 560],
            [0.0907539927923847, 0.7006499525376176, 0.9855482259562685, 0.9964173326131114, 0.9968853197015581],
            [0.07129479809510311, 0.4452322266278464, 0.9739270990810065, 0.9933502895701547, 0.9943213847320569],
            [0.9092460072076143, 0.29935004746238286, 0.014451774043731479, 0.003582667386888922, 0.0031146802984419915],
            [0.9287052019048968, 0.5547677733721545, 0.026072900918993322, 0.006649710429845559, 0.005678615267943026]),
    ];

    public static void Run()
    {
        foreach (var r in References)
        {
            var model = TfdFile.Read(Fixture(r.File));
            model.Spectral = string.Join(", ", r.Wavelengths);
            model.AnglesAreXi = false;
            model.Angles = r.Angle.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var result = Evaluator.Evaluate(model.ToDocument());
            double worst = 0;
            for (int i = 0; i < r.Wavelengths.Length; ++i)
            {
                var p = result.Points[i].Power;
                worst = Math.Max(worst, Math.Max(Math.Max(Math.Abs(p[0] - r.Rp[i]), Math.Abs(p[1] - r.Tp[i])),
                                                 Math.Max(Math.Abs(p[3] - r.Rs[i]), Math.Abs(p[4] - r.Ts[i]))));
            }
            Check(worst < 1e-12, $"{r.File}: read here, R and T for s and p agree with ThinFilmDesigner's engine (worst {worst:G3})");

            /* Written back and read again: the same materials and layers. */
            var again = TfdFile.Read(TfdFile.Write(model));
            bool same = again.Layers.Count == model.Layers.Count
                && again.Layers.Zip(model.Layers).All(t => t.First.Material == t.Second.Material && t.First.Thickness == t.Second.Thickness)
                && model.Materials.Where(m => model.Uses(m.Name).Count > 0 || m.Name == model.Incident.Material || m.Name == model.Exit.Material)
                       .All(m => again.Material(m.Name) is { } a && Strip(a.Spec) == Strip(m.Spec));
            Check(same, $"{r.File}: written as a .tfd and read again, the same materials and layers");
        }

        var narrow = TfdFile.Read(Fixture("narrow-bandpass-550.tfd"));
        Check(narrow.Layers.Count == 52 && narrow.Material("SCHOTT N-BK7")?.Spec["glass"] is not null && narrow.Material("SiO2")?.Spec["sellmeier"]?["range"] is not null,
              "the catalogue glass is named and the Sellmeier keeps its range");
        Check(narrow.Notes.Contains("merit function"), "the notes say the merit function is not carried over");

        /* A stack of every exportable kind (Sellmeier, Cauchy in nm, an ε table, a catalogue glass),
           written as a .tfd and read back. ThinFilmDesigner itself gave the same R to 7e-8 on this
           file (2026-11-01); here the round trip must keep R and T to 1e-6, the ε table having been
           resampled for n, k interpolation. */
        var mixed = StackModel.FromJson(Fixture("export-check.json"));
        var before = Evaluator.Evaluate(mixed.ToDocument());
        var back = TfdFile.Read(TfdFile.Write(mixed));
        back.Spectral = mixed.Spectral; back.SpectralUnit = mixed.SpectralUnit;
        var after = Evaluator.Evaluate(back.ToDocument());
        double drift = before.Points.Zip(after.Points).Max(t => t.First.Power.Zip(t.Second.Power).Max(q => Math.Abs(q.First - q.Second)));
        Check(drift < 1e-6, $"a stack of Sellmeier, Cauchy, an ε table and a glass survives the round trip ({drift:G3})");

        bool refused = false;
        try { TfdFile.Write(StackModel.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "literature", "schubert-2016", "ga2o3-010.berr")))); }
        catch (StackFormatException e) { refused = e.Message.Contains("Ga2O3"); }
        Check(refused, "an anisotropic stack is refused, the material named");
    }

    /// <summary>A spec without its notes: the notes of a material are not part of what it is.</summary>
    private static string Strip(System.Text.Json.Nodes.JsonObject spec)
    {
        var c = (System.Text.Json.Nodes.JsonObject)spec.DeepClone();
        c.Remove("notes");
        return c.ToJsonString();
    }
}
