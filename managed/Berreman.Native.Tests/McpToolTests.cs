// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// McpToolTests.cs — the MCP tools beyond evaluate_stack (Mcp/McpTools.cs), driven as a client drives
// them: tools/call, and the text that comes back.

using System.Text.Json.Nodes;
using Berreman.Api;
using Berreman.Mcp;

namespace Berreman.Native.Tests;

internal static class McpToolTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static int _id = 100;

    private static (string Text, bool Error) Call(McpServer s, string tool, JsonObject args)
    {
        var r = s.Handle(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = args },
        });
        return (r?["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "", r?["result"]?["isError"]?.GetValue<bool>() ?? true);
    }

    public static void Run()
    {
        var s = new McpServer();
        var tools = s.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 99, ["method"] = "tools/list" })?["result"]?["tools"]?.AsArray()
                    .Select(t => t!["name"]!.GetValue<string>()).ToList() ?? [];
        foreach (var t in new[] { "list_samples", "read_sample", "describe_stack", "search_materials", "build_layers", "import_tfd", "export_tfd", "list_coatings", "import_coating", "field_profile", "conoscope_map", "material_response" })
            Check(tools.Contains(t), $"{t} is listed");

        var (list, e1) = Call(s, "list_samples", new JsonObject { ["filter"] = "cholesteric" });
        Check(!e1 && JsonNode.Parse(list)!.AsArray().Count == 1, "list_samples filters");
        var (stack, e2) = Call(s, "read_sample", new JsonObject { ["name"] = "ting-2006 / cholesteric-fig2" });
        Check(!e2 && StackDocument.Parse(stack).Layers.Count == 1, "read_sample returns the stack");

        var (desc, e3) = Call(s, "describe_stack", new JsonObject { ["stack"] = stack });
        Check(!e3 && desc.Contains("twisted with pitch") && desc.Contains("Passive"), $"describe_stack names the twist and the checks");

        var (cols, _) = Call(s, "search_materials", new JsonObject());
        Check(cols.Contains("Glass: SCHOTT"), "search_materials without a collection lists them");
        var (found, e4) = Call(s, "search_materials", new JsonObject { ["collection"] = "Thin-film materials", ["query"] = "SiO2" });
        Check(!e4 && JsonNode.Parse(found)!.AsArray().Any(x => x!["spec"]!["sellmeier"] is not null), "search_materials returns stack-file forms");

        var start = """
            { "format": "berreman-stack", "version": 1, "units": { "length": "nm", "spectral": "nm" },
              "materials": { "air": { "n": 1 }, "H": { "n": 2.35 }, "L": { "n": 1.46 }, "glass": { "n": 1.52 } },
              "incident": { "material": "air" }, "layers": [], "exit": { "material": "glass" },
              "sweep": { "spectral": 550, "angle_deg": 0 } }
            """;
        var (built, e5) = Call(s, "build_layers", new JsonObject
        {
            ["stack"] = start, ["design"] = "(HL)4 H",
            ["letters"] = new JsonObject { ["H"] = new JsonObject { ["material"] = "H", ["thickness"] = 550 / (4 * 2.35) }, ["L"] = new JsonObject { ["material"] = "L", ["thickness"] = 550 / (4 * 1.46) } },
        });
        var mirror = e5 ? null : StackDocument.Parse(built);
        Check(mirror?.Layers.Count == 9, "build_layers expands (HL)4 H into 9 layers");
        if (mirror is not null)
        {
            /* A quarter-wave mirror of 2p+1 layers: R = ((1 - Y)/(1 + Y))², Y = (nH/nL)^(2p) nH²/ns. */
            double Y = Math.Pow(2.35 / 1.46, 8) * 2.35 * 2.35 / 1.52, want = Math.Pow((1 - Y) / (1 + Y), 2);
            Check(Math.Abs(Evaluator.Evaluate(mirror).Points[0].Power[0] - want) < 1e-12, "and it is the textbook quarter-wave mirror");
        }

        var (profile, e6) = Call(s, "field_profile", new JsonObject { ["stack"] = built, ["depths"] = 11 });
        Check(!e6 && profile.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 13 && profile.Contains("E2_p"), "field_profile returns its interfaces and 11 depths");

        var (map, e7) = Call(s, "conoscope_map", new JsonObject { ["stack"] = start, ["quantity"] = "R_mean", ["angles"] = 4, ["azimuths"] = 8 });
        Check(!e7 && map.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 2 + 32, "conoscope_map returns 4 × 8 directions");
        var (bad, e8) = Call(s, "conoscope_map", new JsonObject { ["stack"] = start, ["quantity"] = "nonsense" });
        Check(e8 && bad.Contains("not a quantity"), "an unknown quantity is a tool error that says why");

        var (resp, e9) = Call(s, "material_response", new JsonObject { ["stack"] = start, ["material"] = "H", ["from"] = 400, ["to"] = 800, ["points"] = 5 });
        Check(!e9 && resp.StartsWith("Passive and lossless") && resp.Contains("n_axis1"), "material_response gives the checks and n, k");

        var tfd = Path.Combine(Path.GetTempPath(), $"berreman-mcp-{Guid.NewGuid():N}.tfd");
        try
        {
            var (w, e10) = Call(s, "export_tfd", new JsonObject { ["stack"] = built, ["out"] = tfd });
            var (back, e11) = Call(s, "import_tfd", new JsonObject { ["path"] = tfd });
            Check(!e10 && !e11 && StackDocument.Parse(back).Layers.Count == 9, "export_tfd and import_tfd round-trip the mirror");
        }
        finally { File.Delete(tfd); }

        /* Coatings from an OpticStudio coating file: listed, then imported in waves at a given wavelength. */
        var dat = Path.Combine(Path.GetTempPath(), $"berreman-mcp-{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllText(dat, "MATE MGF2\r\n0.4 1.38 0\r\n0.9 1.38 0\r\n\r\nCOAT AR\r\nMGF2 .25\r\n\r\nCOAT LOOPED\r\nMGF2 .25 0 2\r\n");
            var (listed, e12) = Call(s, "list_coatings", new JsonObject { ["file"] = dat });
            Check(!e12 && listed.Contains("\"AR\"") && listed.Contains("loop index"), $"list_coatings names them, and says why one cannot be imported ({listed})");
            var (_, e13) = Call(s, "import_coating", new JsonObject { ["file"] = dat, ["coating"] = "AR" });
            Check(e13, "a coating in waves with no wavelength is refused");
            var (imported, e14) = Call(s, "import_coating", new JsonObject { ["file"] = dat, ["coating"] = "1", ["wavelength_nm"] = 552, ["stack"] = start });
            var coated = StackDocument.Parse(imported[imported.IndexOf('{')..]);
            Check(!e14 && coated.Layers.Count == 1 && Math.Abs(coated.Layers[0].ThicknessNm - 552 / 4.0 / 1.38) < 1e-6,
                  $"import_coating by number replaces the given stack's layers with the quarter wave");
        }
        finally { File.Delete(dat); }
    }
}
