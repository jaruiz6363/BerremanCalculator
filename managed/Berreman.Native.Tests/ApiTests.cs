// ApiTests.cs — the C# side: units, rotation, the stack format, and the first literature example.

using System.Numerics;
using System.Reflection;
using Berreman.Api;
using Berreman.Native;

namespace Berreman.Native.Tests;

internal static class ApiTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static void Near(double got, double expected, double tol, string what)
    {
        ++Checks;
        if (!(Math.Abs(got - expected) <= tol))
        {
            ++Failures;
            Console.WriteLine($"  FAIL  {what}: got {got:G10}, expected {expected:G10} ± {tol:G3}");
        }
    }

    /// <summary>A sample from samples/literature, embedded in this assembly.</summary>
    public static StackDocument Sample(string relative)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.Replace('\\', '/').EndsWith("samples/" + relative, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"sample {relative} is not embedded");
        using var s = asm.GetManifestResourceStream(name)!;
        return StackDocument.Parse(new StreamReader(s).ReadToEnd());
    }

    public static void Run()
    {
        UnitsRoundTrip();
        RotationKeepsLosslessMediaExactlyHermitian();
        StackFormatRefusesWhatItCannotUse();
        DispersionLaws();
        FresnelThroughTheApi();
        MansuripurQuadrilayer();
    }

    private static void UnitsRoundTrip()
    {
        foreach (var unit in new[] { "nm", "um", "mm", "cm", "m", "GHz", "THz", "cm-1", "eV" })
        {
            double nm = Units.ToWavelengthNm(3.7, unit);
            Near(Units.FromWavelengthNm(nm, unit), 3.7, 1e-14, $"spectral unit {unit} round trip");
        }
        Near(Units.ToWavelengthNm(10, "GHz"), 29_979_245.8, 1e-6, "10 GHz is 29.98 mm");
        Near(Units.ToWavelengthNm(1000, "cm-1"), 10_000, 1e-9, "1000 cm-1 is 10 µm");
        Near(Units.ToWavelengthNm(1, "eV"), 1239.841984, 1e-6, "1 eV is 1239.84 nm");
    }

    private static void RotationKeepsLosslessMediaExactlyHermitian()
    {
        var m = MaterialMatrix.FromBlocks(MaterialMatrix.Diagonal(2.1, 2.5, 3.3), MaterialMatrix.Scalar(1));
        var r = m.Rotated(MaterialMatrix.EulerRotation(31, 47, 113));
        bool hermitian = true;
        for (int i = 0; i < 6; ++i)
            for (int j = 0; j < 6; ++j)
                hermitian &= r[i, j] == Complex.Conjugate(r[j, i]);
        Check(hermitian, "a rotated lossless tensor is exactly Hermitian");

        /* The trace is invariant, and Yeh's convention puts the principal x axis along (cos φ, sin φ, 0). */
        Near((r[0, 0] + r[1, 1] + r[2, 2]).Real, 2.1 + 2.5 + 3.3, 1e-14, "rotation keeps the trace");
        var R = MaterialMatrix.EulerRotation(30, 0, 0);
        Near(R[0, 0], Math.Cos(Math.PI / 6), 1e-15, "R x̂ = (cos φ, sin φ, 0): x");
        Near(R[1, 0], Math.Sin(Math.PI / 6), 1e-15, "R x̂ = (cos φ, sin φ, 0): y");
    }

    private static void StackFormatRefusesWhatItCannotUse()
    {
        static bool Refused(string json)
        {
            try { StackDocument.Parse(json); return false; }
            catch (StackFormatException) { return true; }
        }
        const string ok = """
            { "format": "berreman-stack", "version": 1,
              "materials": { "a": { "n": 1 }, "g": { "n": 1.5 } },
              "incident": { "material": "a" }, "exit": { "material": "g" },
              "layers": [], "sweep": { "spectral": 500, "angle_deg": 0 } }
            """;
        Check(!Refused(ok), "a minimal stack parses");
        Check(Refused(ok.Replace("\"version\": 1", "\"version\": 2")), "an unknown version is refused");
        Check(Refused(ok.Replace("\"g\": { \"n\": 1.5 }", "\"g\": { \"n\": 1.5, \"eps\": 2 }")), "two definitions of ε are refused");
        Check(Refused(ok.Replace("\"g\": { \"n\": 1.5 }", "\"g\": { \"sellmeier\": {} }")), "a dispersive material is refused, not approximated");
        Check(Refused(ok.Replace("\"angle_deg\": 0", "\"angle_deg\": 0, \"xi\": 0")), "both angle and xi are refused");
        Check(Refused(ok.Replace("\"material\": \"g\"", "\"material\": \"nope\"")), "an unknown material is refused");
        Check(Refused(ok.Replace("\"layers\": []", "\"layers\": [{ \"material\": \"g\", \"thickness\": 10, \"coherent\": false }]")),
              "an incoherent layer is refused until it is built");

        /* exp(+jωt) data is conjugated on entry. */
        var d = StackDocument.Parse(ok.Replace("\"g\": { \"n\": 1.5 }", "\"g\": { \"eps_r\": 4, \"tan_delta\": 0.01, \"convention\": \"exp(+jwt)\" }"));
        Near(d.Materials["g"].At(500, 0)[0, 0].Imaginary, -0.04, 1e-15, "exp(+jωt) data is conjugated");
        d = StackDocument.Parse(ok.Replace("\"g\": { \"n\": 1.5 }", "\"g\": { \"eps_r\": 4, \"tan_delta\": 0.01 }"));
        Near(d.Materials["g"].At(500, 0)[0, 0].Imaginary, 0.04, 1e-15, "ε′ with tan δ in exp(−iωt): ε″ = ε′ tan δ");
    }

    private static void DispersionLaws()
    {
        /* KRS5, Rodney and Malitson's Sellmeier formula: 2.37069 at 10 µm, the value in Passler's
         * table at 1000 cm⁻¹; and refused outside its stated range. */
        var krs5 = Sample("passler-2017/sic-otto.berr").Materials["KRS5"];
        Near(Complex.Sqrt(krs5.At(10_000, 0)[0, 0]).Real, 2.37069, 5e-6, "KRS5 Sellmeier at 10 µm");
        bool refused = false;
        try { krs5.At(100_000, 0); } catch (StackFormatException) { refused = true; }
        Check(refused, "a Sellmeier formula is refused outside its range");

        /* Phonon oscillators in exp(−iωt): Im ε > 0 (absorbing), and Re ε < 0 in the Reststrahlen
         * band. 6H-SiC ordinary: TO 794.78, LO 967.93 cm⁻¹. */
        var sic = Sample("passler-2017/sic-otto.berr").Materials["SiC"];
        foreach (var w in new[] { 600.0, 794.78, 850, 967.93, 1200 })
        {
            var e = sic.At(1e7 / w, 0)[0, 0];
            Check(e.Imaginary > 0, $"SiC ε″ > 0 at {w} cm⁻¹");
        }
        Check(sic.At(1e7 / 850, 0)[0, 0].Real < 0, "SiC ε′ < 0 inside its Reststrahlen band");
        Check(sic.At(1e7 / 1200, 0)[0, 0].Real > 0, "SiC ε′ > 0 above it");
        /* LO: ε′ crosses zero at ω_L in the lossless limit; with γ = 2.535 it is small there. */
        Near(sic.At(1e7 / 967.93, 0)[0, 0].Real, 0, 0.05, "SiC ε′ ≈ 0 at the LO frequency");

        /* A table: linear interpolation in its own variable, refused outside. */
        var t = StackDocument.Parse("""
            { "format": "berreman-stack", "version": 1,
              "materials": { "a": { "n": 1 },
                "t": { "table": { "unit": "um", "columns": ["x", "n", "k"], "data": [[1, 1.5, 0], [2, 1.7, 0.2]] } } },
              "incident": { "material": "a" }, "exit": { "material": "t" }, "layers": [],
              "sweep": { "spectral": 1500, "angle_deg": 0 } }
            """).Materials["t"];
        var n15 = Complex.Sqrt(t.At(1500, 0)[0, 0]);
        Near(n15.Real, 1.6, 1e-12, "table: n interpolated at 1.5 µm");
        Near(n15.Imaginary, 0.1, 1e-12, "table: k interpolated at 1.5 µm");
        refused = false;
        try { t.At(2500, 0); } catch (StackFormatException) { refused = true; }
        Check(refused, "a table is refused outside its range, never extrapolated");
    }

    private static void FresnelThroughTheApi()
    {
        var doc = StackDocument.Parse("""
            { "format": "berreman-stack", "version": 1,
              "materials": { "air": { "n": 1 }, "glass": { "n": 1.5 } },
              "incident": { "material": "air" }, "exit": { "material": "glass" },
              "layers": [], "sweep": { "spectral": 633, "angle_deg": [0, 30, 56.30993247402] } }
            """);
        var r = Evaluator.Evaluate(doc);
        Near(r.Points[0].Power[0], 0.04, 1e-15, "air/glass at normal incidence: R = 4%");
        Near(r.Points[0].Rpp.Real, 0.2, 1e-15, "r_pp = (n2 − n1)/(n1 + n2) = +0.2");
        Near(r.Points[0].Rss.Real, -0.2, 1e-15, "r_ss = −0.2: r_pp = −r_ss (contract)");
        Near(r.Points[2].Power[0], 0, 1e-20, "R_p = 0 at Brewster's angle");
        var (psi, _) = r.Points[1].Ellipsometry();
        Check(psi > 0 && psi < 45, "Ψ at 30° is between 0 and 45°");
    }

    /// <summary>
    /// Mansuripur (1990), every input printed. The paper's results are figures and rounded text, so
    /// the tolerances are those of reading them: Fig. 2(a) amplitude reflectivities, Fig. 2(b) Kerr
    /// rotation and ellipticity for P light (magnitudes: the paper does not state its sign
    /// convention, and ours comes out opposite), Fig. 3 power budget at normal incidence; and the
    /// exact statement that r_ps = r_sp.
    /// </summary>
    private static void MansuripurQuadrilayer()
    {
        var doc = Sample("mansuripur-1990/quadrilayer.berr");
        var r = Evaluator.Evaluate(doc);
        Check(r.Status == BcStatus.Ok, "Mansuripur quadrilayer evaluates");

        var p0 = r.Points[0];
        Near(p0.Power[0], 0.06, 0.008, "Fig. 3: about 6% reflected");
        Near(p0.LayerAbsorption[1], 0.91, 0.015, "Fig. 3: about 91% absorbed in the MO layer");
        Near(p0.LayerAbsorption[3], 0.03, 0.008, "Fig. 3: about 3% absorbed in the aluminium");
        Near(p0.Rpp.Magnitude, 0.235, 0.01, "Fig. 2(a): |r_pp| at normal incidence");
        Near(p0.Rss.Magnitude, 0.235, 0.01, "Fig. 2(a): |r_ss| at normal incidence");
        Near(100 * p0.Jones[1].Magnitude, 1.0, 0.05, "Fig. 2(a): 100 |r_ps| at normal incidence");

        var kerr = p0.Jones[1] / p0.Jones[0];
        Near(Math.Abs(kerr.Real) * 180 / Math.PI, 1.75, 0.1, "Fig. 2(b): |Kerr rotation| for P light at 0°");
        Near(Math.Abs(kerr.Imaginary) * 180 / Math.PI, 1.7, 0.1, "Fig. 2(b): |Kerr ellipticity| for P light at 0°");

        double worst = 0;
        foreach (var p in r.Points) worst = Math.Max(worst, Math.Abs(p.Jones[1].Magnitude - p.Jones[2].Magnitude));
        Check(worst < 1e-12, "text: |r_ps| = |r_sp| at every angle");

        var min = r.Points.MinBy(p => p.Rss.Magnitude)!;
        Near(min.AngleDeg!.Value, 60, 5, "Fig. 2(a): the r_ss dip is near 60°");
        Near(min.Rss.Magnitude, 0.17, 0.015, "Fig. 2(a): the r_ss dip is about 0.17");
    }
}
