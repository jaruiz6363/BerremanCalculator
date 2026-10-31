// Program.cs — the managed suite.
//
// The checks that need both sides: that the C# structs have the native sizes, that the library
// loads with the ABI this binding expects, that the contract's call sequence works from C#, and
// that the MCP server speaks the protocol. Failures print what was expected and what came out.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Berreman.Api;
using Berreman.Mcp;
using Berreman.Native;

int checks = 0, failures = 0;

void Check(bool ok, string what)
{
    ++checks;
    if (!ok) { ++failures; Console.WriteLine($"  FAIL  {what}"); }
}

void Equal<T>(T got, T expected, string what)
{
    ++checks;
    if (!EqualityComparer<T>.Default.Equals(got, expected))
    {
        ++failures;
        Console.WriteLine($"  FAIL  {what}: got {got}, expected {expected}");
    }
}

// ---- layout: the same numbers test_skeleton.cpp checks on the native side -------------------

unsafe
{
    Equal(sizeof(Complex), 16, "sizeof(Complex) == sizeof(bc_complex)");
    Equal(sizeof(BcPointDiagnostics), 40, "sizeof(BcPointDiagnostics)");
    Equal(sizeof(BcProblem), 16 + 6 * sizeof(void*), "sizeof(BcProblem)");
    Equal(sizeof(BcOutput), 5 * sizeof(void*), "sizeof(BcOutput)");
}

// ---- the engine loads and the call sequence works -------------------------------------------

var report = EngineStatus.Probe();
Check(report.Loaded, $"the native engine loads ({report.Summary})");
Equal(report.NativeAbi, (int?)NativeMethods.AbiVersion, "native ABI matches the binding");
Equal(report.PlanStatus, (BcStatus?)BcStatus.Ok, "bc_plan accepts a trivial problem");
Equal(report.EvaluateStatus, (BcStatus?)BcStatus.Ok, "bc_evaluate evaluates a vacuum stack");

EvaluateLeavesOutputsUntouched();

// ---- the MCP server ------------------------------------------------------------------------

var server = new McpServer();

var init = server.Handle(Request(1, "initialize"));
Equal(init?["result"]?["protocolVersion"]?.GetValue<string>(), McpServer.ProtocolVersion, "initialize answers");
Check(server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }) is null,
      "a notification gets no reply");

var tools = server.Handle(Request(2, "tools/list"))?["result"]?["tools"]?.AsArray();
Check(tools?.Any(t => t?["name"]?.GetValue<string>() == "engine_status") == true, "engine_status is listed");

var call = server.Handle(Request(3, "tools/call", new JsonObject { ["name"] = "engine_status" }));
Equal(call?["result"]?["isError"]?.GetValue<bool>(), false, "engine_status succeeds");

var unknown = server.Handle(Request(4, "tools/call", new JsonObject { ["name"] = "no_such_tool" }));
Equal(unknown?["result"]?["isError"]?.GetValue<bool>(), true, "an unknown tool is a tool error, not a protocol error");

var badMethod = server.Handle(Request(5, "no/such/method"));
Equal(badMethod?["error"]?["code"]?.GetValue<int>(), -32601, "an unknown method is a protocol error");

var output = new StringWriter();
server.Serve(new StringReader("not json\n"), output);
Check(output.ToString().Contains("-32700"), "unparseable input is a parse error");

// ---- the API and the first literature example ------------------------------------------------

Berreman.Native.Tests.ApiTests.Run();
Berreman.Native.Tests.ClosedFormTests.Run();
Berreman.Native.Tests.PasslerTests.Run();
Berreman.Native.Tests.CholestericTests.Run();
checks += Berreman.Native.Tests.ApiTests.Checks;
failures += Berreman.Native.Tests.ApiTests.Failures;
checks += Berreman.Native.Tests.ClosedFormTests.Checks;
failures += Berreman.Native.Tests.ClosedFormTests.Failures;
checks += Berreman.Native.Tests.PasslerTests.Checks;
failures += Berreman.Native.Tests.PasslerTests.Failures;
checks += Berreman.Native.Tests.CholestericTests.Checks;
failures += Berreman.Native.Tests.CholestericTests.Failures;

Console.WriteLine($"{checks} checks, {failures} failures");
return failures == 0 ? 0 : 1;

// ---- helpers --------------------------------------------------------------------------------

static JsonObject Request(int id, string method, JsonObject? parameters = null)
{
    var r = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
    if (parameters is not null) r["params"] = parameters;
    return r;
}

// Rule 5 from C#: fields against depth are not built yet. Every output is filled with a sentinel,
// and after the NOT_IMPLEMENTED call every element must still be the sentinel.
unsafe void EvaluateLeavesOutputsUntouched()
{
    var m = new PinnedBuffer<Complex>(3 * 36);
    for (int k = 0; k < 3; ++k) for (int i = 0; i < 6; ++i) m[k * 36 + 7 * i] = Complex.One;
    var wavelength = PinnedBuffer<double>.From(633.0);
    var thickness = PinnedBuffer<double>.From(50.0);
    var spectral = PinnedBuffer<int>.From(0, 0);
    var xi = PinnedBuffer<double>.From(0.0, 0.7);
    var depths = PinnedBuffer<double>.From(0.0, 25.0);

    var p = new BcProblem
    {
        NLayers = 1, NSpectral = 1, NPoints = 2, NDepths = 2,
        M = m.Pointer, Wavelength = wavelength.Pointer, Thickness = thickness.Pointer,
        PointSpectral = spectral.Pointer, PointXi = xi.Pointer, FieldDepths = depths.Pointer,
    };

    long bytes;
    Equal(NativeMethods.bc_plan(&p, &bytes), BcStatus.Ok, "plan with fields requested");

    var scratch = new PinnedBuffer<byte>(bytes);
    var jones = new PinnedBuffer<Complex>(2 * 8);
    var power = new PinnedBuffer<double>(2 * 6);
    var absorption = new PinnedBuffer<double>(2 * 2 * 1);
    var fields = new PinnedBuffer<Complex>(2 * 2 * 2 * 6);
    var diagnostics = new PinnedBuffer<BcPointDiagnostics>(2);

    var sentinelC = new Complex(-7.25, 3.5);
    var sentinelD = new BcPointDiagnostics { Status = -99, MaxSteps = -99, FrontCondition = -7.25 };
    jones.Span.Fill(sentinelC); fields.Span.Fill(sentinelC);
    power.Span.Fill(-7.25); absorption.Span.Fill(-7.25);
    diagnostics.Span.Fill(sentinelD);

    var o = new BcOutput
    {
        Jones = jones.Pointer, Power = power.Pointer, LayerAbsorption = absorption.Pointer,
        Fields = fields.Pointer, Diagnostics = diagnostics.Pointer,
    };
    Equal(NativeMethods.bc_evaluate(&p, 0, 2, scratch.Pointer, bytes, &o), BcStatus.NotImplemented,
          "evaluate with every output requested");

    Check(jones.Span.ToArray().All(z => z == sentinelC), "jones untouched");
    Check(fields.Span.ToArray().All(z => z == sentinelC), "fields untouched");
    Check(power.Span.ToArray().All(x => x == -7.25), "power untouched");
    Check(absorption.Span.ToArray().All(x => x == -7.25), "layer_absorption untouched");
    Check(diagnostics.Span.ToArray().All(d => d.Equals(sentinelD)), "diagnostics untouched");

    GC.KeepAlive(m); GC.KeepAlive(wavelength); GC.KeepAlive(thickness); GC.KeepAlive(spectral);
    GC.KeepAlive(xi); GC.KeepAlive(depths); GC.KeepAlive(scratch); GC.KeepAlive(jones);
    GC.KeepAlive(power); GC.KeepAlive(absorption); GC.KeepAlive(fields); GC.KeepAlive(diagnostics);
}
