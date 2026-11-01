// CoatingImportTests.cs — coatings imported from OpticStudio coating files and from Optiland
// (Berreman.Api/CoatingImport.cs), and merged into a stack (StackWorkspace.ImportCoating).
//
// The fixtures follow ThinFilmDesigner's (ZemaxTests.cs and OptilandTests.cs there): a cut-down
// COATING.DAT as OpticStudio writes it (UTF-16 with a BOM, CRLF, padded names, a coating in waves,
// one in µm, a loop, a taper, ideal and table coatings), and a ThinFilmCoating as Optiland's
// save_obj_to_json writes it, alone and on a surface of a lens file.
//
// THE INDEPENDENT CHECK is OpticStudio's classic quarter-half-quarter AR (its HEAR1), imported in
// waves and evaluated by the engine: with constant indices the half wave is absent at the design
// wavelength, so R = ((n0 n3² − ns n1²) / (n0 n3² + ns n1²))².

using System.Text;
using System.Text.Json.Nodes;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Native.Tests;

internal static class CoatingImportTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static readonly string[] Dat =
    [
        "! Sample coating.dat file supplied with ZEMAX",
        "",
        "MATE AIR",
        "0.55 1.0 0",
        "",
        "MATE MGF2",
        ".400 1.38 0",
        ".700 1.375 0",
        "",
        "MATE          SIO2_EM",
        " .40         1.4701      -4.8E-07",
        " .80         1.4533      -4.8E-07",
        "",
        "! Sample coating definitions",
        "COAT AR",
        "MGF2 .25",
        "",
        "COAT          AR_3L",
        "SIO2_EM           .087221100520488           1                           ",
        "MGF2              .1                         1   ",
        "                       ",
        "",
        "COAT ETALON",
        "MGF2 .25 0 4",
        "",
        "COAT AR_TAPR",
        "MGF2 .25 0 0 QUAD",
        "",
        "COAT MISSING",
        "TIO2 .25",
        "",
        "COAT I.95",
        "",
        "IDEAL SPLIT 0.50 0.50",
        "TABLE PASS45",
        "ANGL 0.0",
        "WAVE 0.45 1.0 1.0 0.0 0.0 0.0 0.0 0.0 0.0",
        "",
        "TAPR QUAD",
        "RT 2 .1",
        "",
        "ENCRYPTED ZEC_AR-16.ZEC",
    ];

    private static byte[] Utf16(IEnumerable<string> lines)
    {
        var e = new UnicodeEncoding(false, true);
        return [.. e.GetPreamble(), .. e.GetBytes(string.Join("\r\n", lines))];
    }

    private const string Coating = """
    {
        "type": "ThinFilmCoating",
        "material_pre": {"type": "IdealMaterial", "propagation_model": {"class": "HomogeneousPropagation"}, "index": 1.0, "absorp": 0.0},
        "material_post": {"type": "Material", "propagation_model": {"class": "HomogeneousPropagation"},
                          "filename": "C:\\nowhere\\N-BK7.yml", "bounds": "clamp", "name": "N-BK7", "reference": "SCHOTT"},
        "layers": [
            {"material": {"type": "DataMaterial", "name": "SiO2-Malitson",
                          "definition": {"dispersion": {"kind": "formula", "formula": "formula 2",
                                                        "coefficients": [0.0, 0.6961663, 0.0046791, 0.4079426, 0.0135121, 0.8974794, 97.934],
                                                        "wavelength_range_um": null},
                                         "extinction": null}},
             "thickness_nm": 94.0, "name": "L1"},
            {"material": {"type": "DataMaterial", "name": "TiO2-ebeam",
                          "definition": {"dispersion": {"kind": "tabulated", "wavelengths_um": [0.4, 0.5, 0.7], "indices": [2.45, 2.35, 2.28]},
                                         "extinction": {"kind": "tabulated_k", "wavelengths_um": [0.4, 0.7], "values": [0.002, 0.0]}}},
             "thickness_nm": 57.0, "name": "H1"},
            {"material": {"type": "IdealMaterial", "index": 1.38, "absorp": 0.0},
             "thickness_nm": 99.6, "name": "M"},
            {"material": {"type": "DataMaterial", "name": "SiO2-Malitson",
                          "definition": {"dispersion": {"kind": "formula", "formula": "formula 2",
                                                        "coefficients": [0.0, 0.6961663, 0.0046791, 0.4079426, 0.0135121, 0.8974794, 97.934],
                                                        "wavelength_range_um": null},
                                         "extinction": null}},
             "thickness_nm": 20.0, "name": "L2"}
        ]
    }
    """;

    private static string Lens() => """
    {"version": 1.0, "name": null,
     "surface_group": {"surfaces": [
        {"type": "ObjectSurface", "thickness": Infinity, "geometry": {"cs": {"z": -Infinity, "rx": NaN}},
         "comment": "", "interaction_model": {"type": "RefractiveReflectiveModel", "is_reflective": false, "coating": null}},
        {"type": "Surface", "thickness": 5.0, "comment": "front",
         "interaction_model": {"type": "RefractiveReflectiveModel", "is_reflective": false, "coating": COATING}},
        {"type": "Surface", "thickness": 40.0, "comment": "",
         "interaction_model": {"type": "RefractiveReflectiveModel", "is_reflective": false, "coating": {"type": "FresnelCoating"}}}
     ]}}
    """.Replace("COATING", Coating);

    public static void Run()
    {
        Zemax();
        TheClassicArIsTheClosedForm();
        Optiland();
        MergingIntoAStack();
    }

    private static void Zemax()
    {
        var bytes = Utf16(Dat);
        var list = ZemaxCoatingReader.List(bytes);
        Check(list.Select(c => c.Name).SequenceEqual(["AR", "AR_3L", "ETALON", "AR_TAPR", "MISSING"]),
              $"the layer-stack coatings are listed; an ideal I.95, IDEAL, TABLE and ENCRYPTED are not ({string.Join(", ", list.Select(c => c.Name))})");
        Check(list.First(c => c.Name == "ETALON").Problem!.Contains("loop") && list.First(c => c.Name == "MISSING").Problem!.Contains("TIO2"),
              "a loop and a missing MATE are said, not imported");
        Check(list.First(c => c.Name == "AR").Problem is null && list.First(c => c.Name == "AR_3L").Layers == 2, "the others import");

        bool refused = false;
        try { ZemaxCoatingReader.Read(bytes, "AR"); } catch (StackFormatException e) { refused = e.Message.Contains("waves"); }
        Check(refused, "a coating in waves needs the wavelength when the file does not give it");
        var ar = ZemaxCoatingReader.Read(bytes, "AR", 550);
        double n550 = 1.38 + (1.375 - 1.38) * (0.55 - 0.4) / 0.3;
        Check(Math.Abs(ar.Layers[0].ThicknessNm - 0.25 * 550 / n550) < 1e-9, $"a quarter wave is λ/4n at the wavelength given ({ar.Layers[0].ThicknessNm})");

        var three = ZemaxCoatingReader.Read(bytes, "ar_3l");
        Check(three.Layers.Select(l => l.Material).SequenceEqual(["SIO2_EM", "MGF2"]) && Math.Abs(three.Layers[0].ThicknessNm - 87.221100520488) < 1e-9,
              "a coating in µm, found in any case, padded names and all");
        var sio2 = three.Materials.First(m => m.Name == "SIO2_EM").Spec;
        Check(sio2["table"]?["data"]?[0]?[2]?.GetValue<double>() == 4.8e-7, "OpticStudio's negative k for an absorber is positive here");
        var doc = MaterialParser.Parse(sio2, "SIO2_EM").At(600, 0);
        Check(Math.Abs(Math.Sqrt(doc[0, 0].Real) - (1.4701 + 1.4533) / 2) < 1e-6, "the table interpolates as OpticStudio does");
        Check(ZemaxCoatingReader.Read(bytes, "AR_TAPR", 550).Notes.Any(n => n.Contains("tapered")), "a taper is noted, the layers taken at nominal thickness");
        refused = false;
        try { ZemaxCoatingReader.Read(bytes, "SPLIT"); } catch (StackFormatException e) { refused = e.Message.Contains("IDEAL"); }
        Check(refused, "an IDEAL coating is refused, with why");
    }

    private static void TheClassicArIsTheClosedForm()
    {
        var bytes = Encoding.ASCII.GetBytes(string.Join("\r\n",
            "MATE MGF2", "0.55 1.38 0", "", "MATE ZRO2", "0.55 2.05 0", "", "MATE CEF3", "0.55 1.63 0", "",
            "! designed at 0.55 um", "COAT HEAR_TEST", "MGF2 .25", "ZRO2 .50", "CEF3 .25", ""));
        var c = ZemaxCoatingReader.Read(bytes, "HEAR_TEST");
        Check(c.Notes.Any(n => n.Contains("550 nm") && n.Contains("designed at")), "the file's 'designed at 0.55 um' is the reference");
        var ws = new StackWorkspace();
        ws.Edit(m => { m.Spectral = "550"; m.Angles = "0"; m.Materials.Add(new NamedMaterial("sub", new JsonObject { ["n"] = 1.52 })); m.Exit.Material = "sub"; });
        ws.ImportCoating(c, takeMedia: false);
        Check(ws.Model.Layers.Select(l => l.Material).SequenceEqual(["MGF2", "ZRO2", "CEF3"]), "layer 1 is the first COAT line, next to the incident medium");
        var r = Evaluator.Evaluate(ws.Document()).Points[0].Power[0];
        double n0 = 1, n1 = 1.38, n3 = 1.63, ns = 1.52, x = (n0 * n3 * n3 - ns * n1 * n1) / (n0 * n3 * n3 + ns * n1 * n1);
        Check(Math.Abs(r - x * x) < 1e-12, $"HEAR1 imported in waves: R at 550 nm is the closed form ({r:G6} against {x * x:G6})");
    }

    private static void Optiland()
    {
        var grid = (0.4, 0.8);
        var list = OptilandCoatingReader.List(Coating, grid);
        Check(list.Count == 1 && list[0].Layers == 4 && list[0].Problem is null, "a coating file is one coating");
        var lens = OptilandCoatingReader.List(Lens(), grid);
        Check(lens.Count == 1 && lens[0].Name == "surface 1 (front)", $"a lens file's coated surface is found, Infinity and NaN and all ({string.Join(", ", lens.Select(l => l.Name))})");

        var c = OptilandCoatingReader.Read(Coating, 0, grid);
        Check(c.Layers.Select(l => l.ThicknessNm).SequenceEqual([94.0, 57, 99.6, 20]), "thicknesses in nm, incident side first");
        Check(c.Materials.Count == 3 && c.Layers[0].Material == c.Layers[3].Material, "the same material twice is one material");
        var sio2 = c.Materials.First(m => m.Name == "SiO2-Malitson").Spec;
        double l = 0.55, l2 = l * l;
        double malitson = Math.Sqrt(1 + 0.6961663 * l2 / (l2 - 0.0046791) + 0.4079426 * l2 / (l2 - 0.0135121) + 0.8974794 * l2 / (l2 - 97.934));
        double got = Math.Sqrt(MaterialParser.Parse(sio2, "s").At(550, 0)[0, 0].Real);
        Check(sio2["sellmeier"] is not null && Math.Abs(got - malitson) < 1e-12, $"formula 2 is our Sellmeier, exactly ({got:G10})");
        var tio2 = MaterialParser.Parse(c.Materials.First(m => m.Name == "TiO2-ebeam").Spec, "t").At(400, 0)[0, 0];
        Check(Math.Abs(System.Numerics.Complex.Sqrt(tio2).Imaginary - 0.002) < 1e-12, "a tabulated k beside the n table is merged into it");
        Check(c.Incident is { Name: "air" } && c.Exit is null && c.Notes.Any(n => n.Contains("N-BK7")),
              "the incident medium is read; a database material whose file is not here is said, not guessed");
        bool refused = false;
        try { OptilandCoatingReader.Read("""{"type": "ThinFilmCoating", "layers": [{"material": {"type": "AbbeMaterial", "index": 1.5, "abbe": 60}, "thickness_nm": 10}]}""", 0, grid); }
        catch (StackFormatException e) { refused = e.Message.Contains("two numbers"); }
        Check(refused, "an Abbe material is refused, with why");
    }

    private static void MergingIntoAStack()
    {
        var c = OptilandCoatingReader.Read(Coating, 0, (0.4, 0.8));
        var ws = new StackWorkspace();
        ws.Edit(m => m.Materials.Add(new NamedMaterial("SiO2-Malitson", new JsonObject { ["n"] = 1.46 })));
        int before = ws.Model.Materials.Count;
        var notes = ws.ImportCoating(c, takeMedia: true);
        Check(ws.Model.Layers.Count == 4 && ws.Model.Layers[0].Material == "SiO2-Malitson_2" && notes.Any(n => n.Contains("SiO2-Malitson_2")),
              $"a different material of the same name becomes NAME_2, and is said ({ws.Model.Layers[0].Material})");
        Check(ws.Model.Incident.Material == "air" && ws.Model.Materials.Count(m => m.Name.StartsWith("air")) == 1,
              "the file's air is the stack's own air, not a second one");
        Check(ws.Model.Materials.Count == before + 3, $"three materials are added ({ws.Model.Materials.Count - before})");
        Check(Math.Abs(ws.Model.Layers[2].Thickness - 99.6) < 1e-12, "in the stack's length unit");
        Check(notes.Any(n => n.StartsWith("TiO2-ebeam has data from 0.4 to 0.7 µm only")),
              "a table that stops short of the sweep (400 to 800 nm) is said at once, before an evaluation fails on it");
        ws.Undo();
        Check(ws.Model.Layers.Count == 1 && ws.Model.Materials.Count == before, "one Undo takes the whole import back");
        ws.SetLengthUnit("um");
        ws.ImportCoating(c, takeMedia: false);
        Check(Math.Abs(ws.Model.Layers[0].Thickness - 0.094) < 1e-12, "and in µm when the stack is in µm");
        ws.SetSpectralRange("400", "700", "10");
        Check(Evaluator.Evaluate(ws.Document()).Points.Count == 31, "and within the data's range the stack evaluates");
    }
}
