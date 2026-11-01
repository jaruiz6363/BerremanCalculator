// MaterialLibraryTests.cs — the shipped databases and glass catalogues (Api/MaterialLibrary.cs).
//
// THE CHECK THAT MATTERS for the catalogues, as in ThinFilmDesigner: every NM line carries the
// vendor's own nd, so every glass is evaluated at the helium d line and compared with it. A
// transposed coefficient or a misremembered formula fails hundreds of glasses at once.
// For the databases: every entry becomes a stack material that evaluates inside its own range.

using System.Text.Json.Nodes;
using Berreman.Api;

namespace Berreman.Native.Tests;

internal static class MaterialLibraryTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    public static void Run()
    {
        EveryGlassAgreesWithItsOwnNd();
        AGlassIsNamedNotCopied();
        EveryDatabaseEntryEvaluates();
        CrystalsPairIntoUniaxialMaterials();
        TheUserFileRoundTrips();
    }

    private static void EveryGlassAgreesWithItsOwnNd()
    {
        var all = GlassCatalogs.All.SelectMany(c => c.Glasses).ToList();
        Check(GlassCatalogs.All.Count == 5 && GlassCatalogs.Problems.Count == 0, $"five catalogues read, none refused ({string.Join("; ", GlassCatalogs.Problems)})");
        Check(all.Count >= 1799, $"the catalogues hold 1799 glasses or more ({all.Count})");
        Check(all.All(g => g.Formula is 1 or 2 or 3), "every glass uses formula 1, 2 or 3");
        int compared = 0;
        var disagree = new List<string>();
        foreach (var g in all)
        {
            if (g.Nd <= 1 || 0.5875618 < g.MinMicrons || 0.5875618 > g.MaxMicrons) continue;
            ++compared;
            if (Math.Abs(g.N(0.5875618) - g.Nd) > 2e-4) disagree.Add(g.FullName);
        }
        Check(compared > 1700, $"glasses compared at the d line ({compared})");
        // ThinFilmDesigner found three glasses whose formula and own nd disagree (CDGM H-TK9 by 0.034).
        Check(disagree.Count <= 3, $"at most three glasses disagree with their own nd by 2e-4 ({disagree.Count}: {string.Join(", ", disagree)})");
        Check(all.Where(g => disagree.Contains(g.FullName)).All(g => g.Caution is not null), "and each of those says so");
    }

    private static void AGlassIsNamedNotCopied()
    {
        var m = MaterialParser.Parse(JsonNode.Parse("""{ "glass": { "catalog": "SCHOTT", "name": "N-BK7" } }"""), "bk7");
        var n = System.Numerics.Complex.Sqrt(m.At(587.5618, 0)[0, 0]).Real;
        Check(Math.Abs(n - 1.5168) < 1e-4, $"SCHOTT N-BK7 at the d line is 1.5168 ({n:F5})");
        bool refused = false;
        try { MaterialParser.Parse(JsonNode.Parse("""{ "glass": { "catalog": "SCHOTT", "name": "NO-SUCH" } }"""), "x"); }
        catch (StackFormatException e) { refused = e.Message.Contains("NO-SUCH"); }
        Check(refused, "a glass not in the catalogues is refused, by name");
        refused = false;
        try { m.At(200, 0); } catch (StackFormatException e) { refused = e.Message.Contains("range"); }
        Check(refused, "a glass is refused outside its catalogue range");
        var entry = MaterialLibrary.Entries("Glass: SCHOTT").First(e => e.Name == "N-BK7");
        Check(entry.Spec.ToJsonString() == """{"glass":{"catalog":"SCHOTT","name":"N-BK7"}}""", "the library offers a glass as its name");
    }

    /// <summary>A wavelength inside an entry's own range, in nm.</summary>
    private static double Inside(JsonObject spec)
    {
        var law = MaterialParser.DispersionKeys.FirstOrDefault(spec.ContainsKey);
        if (law == "table") { var rows = spec["table"]!["data"]!.AsArray(); return rows[rows.Count / 2]![0]!.GetValue<double>() * 1000; }
        if (law is not null && spec[law]!["range"] is JsonArray r) return (r[0]!.GetValue<double>() + r[1]!.GetValue<double>()) / 2 * 1000;
        if (spec["uniaxial"] is JsonObject u) return Math.Max(Inside(u["ordinary"]!.AsObject()), Inside(u["extraordinary"]!.AsObject()));
        return 550;
    }

    private static void EveryDatabaseEntryEvaluates()
    {
        foreach (var collection in new[] { "Thin-film materials", "Literature materials" })
        {
            var entries = MaterialLibrary.Entries(collection);
            Check(entries.Count >= (collection.StartsWith("Thin") ? 33 : 397), $"{collection}: {entries.Count} entries");
            var bad = new List<string>();
            foreach (var e in entries)
            {
                try
                {
                    var m = MaterialParser.Parse(e.Spec, e.Name);
                    var eps = m.At(Inside(e.Spec), 0)[0, 0];
                    if (!double.IsFinite(eps.Real) || eps.Imaginary < 0) bad.Add($"{e.Name}: ε = {eps}");
                }
                catch (StackFormatException x) { bad.Add($"{e.Name}: {x.Message}"); }
            }
            Check(bad.Count == 0, $"{collection}: every entry evaluates inside its range ({bad.Count} do not: {string.Join("; ", bad.Take(5))})");
        }
        var sio2 = MaterialLibrary.Entries("Thin-film materials").First(e => e.Name == "SiO2");
        var n = System.Numerics.Complex.Sqrt(MaterialParser.Parse(sio2.Spec, "SiO2").At(587.5618, 0)[0, 0]).Real;
        Check(Math.Abs(n - 1.4585) < 2e-4, $"fused silica (Malitson) at the d line is 1.4585 ({n:F5})");
        Check(sio2.Spec["source"]?.GetValue<string>().Contains("Malitson") == true, "and it keeps its SOURCE");
    }

    private static void CrystalsPairIntoUniaxialMaterials()
    {
        var entries = MaterialLibrary.Entries("Thin-film materials");
        foreach (var name in new[] { "MgF2", "LiNbO3", "CdSe" })
        {
            var u = entries.FirstOrDefault(e => e.Name == $"{name} (uniaxial)");
            var o = entries.First(e => e.Name == $"{name} (o)");
            var x = entries.First(e => e.Name == $"{name} (e)");
            Check(u is not null, $"{name} (o) and (e) are offered as {name} (uniaxial)");
            if (u is null) continue;
            double l = Math.Max(Inside(o.Spec), Inside(x.Spec));
            var mu = MaterialParser.Parse(u.Spec, name).At(l, 0);
            Check(mu[0, 0] == MaterialParser.Parse(o.Spec, "o").At(l, 0)[0, 0] && mu[2, 2] == MaterialParser.Parse(x.Spec, "e").At(l, 0)[2, 2],
                  $"{name} (uniaxial): ordinary along x and y, extraordinary along z");
        }
    }

    private static void TheUserFileRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"berreman-user-{Guid.NewGuid():N}.json");
        try
        {
            var ga = ApiTests.Sample("schubert-2016/ga2o3-010.json");
            var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "literature", "schubert-2016", "ga2o3-010.json")))!["materials"]!["Ga2O3"]!.AsObject();
            MaterialLibrary.SaveToUserFile("Ga2O3 (Schubert 2016)", spec, path);
            MaterialLibrary.SaveToUserFile("glass", JsonNode.Parse("""{ "n": 1.5 }""")!.AsObject(), path);
            MaterialLibrary.SaveToUserFile("glass", JsonNode.Parse("""{ "n": 1.52 }""")!.AsObject(), path);
            var back = MaterialLibrary.ReadUserFile(path);
            Check(back.Count == 2, "two materials saved, the second replaced once");
            var m = MaterialParser.Parse(back.First(e => e.Name.StartsWith("Ga2O3")).Spec, "g");
            var a = m.At(1e7 / 600, 0); var b = ga.Materials["Ga2O3"].At(1e7 / 600, 0);
            Check(Enumerable.Range(0, 36).All(k => a.M[k] == b.M[k]), "a material of terms comes back exactly");
            bool refused = false;
            try { MaterialLibrary.SaveToUserFile("bad", JsonNode.Parse("""{ "n": 1, "eps": 2 }""")!.AsObject(), path); }
            catch (StackFormatException) { refused = true; }
            Check(refused && MaterialLibrary.ReadUserFile(path).Count == 2, "one that would not evaluate is refused, and the file is unchanged");
        }
        finally { File.Delete(path); }
    }
}
