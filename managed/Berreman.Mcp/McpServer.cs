// McpServer.cs — the Model Context Protocol over stdio, by hand.
//
// JSON-RPC 2.0, one JSON object per line in and out; no SDK. The same framing as ThinFilmDesigner's
// server.
//
// TWO KINDS OF FAILURE. A protocol error (unparseable JSON, an unknown method) is a JSON-RPC
// `error`: the client is broken. A tool that cannot do what was asked returns a successful result
// with isError: true, because the model is meant to read that answer and try something else.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Berreman.Api;

namespace Berreman.Mcp;

public sealed class McpServer
{
    public const string ProtocolVersion = "2024-11-05";

    /// <summary>Relaxed escaping, because a model reads the text; the resolver is required by
    /// JsonNode.ToJsonString as soon as any options are given.</summary>
    public static readonly JsonSerializerOptions Wire = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = false,
    };

    /// <summary>Takes its reader and writer so the tests can drive it in process.</summary>
    public void Serve(TextReader input, TextWriter output)
    {
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;

            JsonNode? request;
            try { request = JsonNode.Parse(line); }
            catch (JsonException e)
            {
                Write(output, Error(null, -32700, $"parse error: {e.Message}"));
                continue;
            }

            if (request is not JsonObject obj)
            {
                Write(output, Error(null, -32600, "a request must be a JSON object"));
                continue;
            }

            var response = Handle(obj);
            if (response is not null) Write(output, response);
        }
    }

    /// <summary>Null when the message was a notification, which gets no reply.</summary>
    public JsonObject? Handle(JsonObject request)
    {
        var id = request["id"];
        var method = request["method"]?.GetValue<string>();
        if (method is null) return Error(id, -32600, "a request needs a method");

        bool isNotification = !request.ContainsKey("id");

        try
        {
            switch (method)
            {
                case "initialize":
                    return Result(id, Initialize());

                case "notifications/initialized":
                case "initialized":
                    return null;

                case "ping":
                    return isNotification ? null : Result(id, new JsonObject());

                case "tools/list":
                    return Result(id, new JsonObject { ["tools"] = DescribeTools() });

                case "tools/call":
                {
                    var name = (request["params"] as JsonObject)?["name"]?.GetValue<string>();
                    if (name is null) return Error(id, -32602, "tools/call needs params.name");

                    var (text, failed) = Call(name);
                    return Result(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                        ["isError"] = failed,
                    });
                }

                default:
                    return isNotification ? null
                         : Error(id, -32601, $"'{method}' is not a method this server implements");
            }
        }
        catch (Exception e)
        {
            return isNotification ? null : Error(id, -32603, $"internal error: {e.Message}");
        }
    }

    private static JsonObject Initialize() => new()
    {
        ["protocolVersion"] = ProtocolVersion,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = "berreman", ["version"] = "0.0.0" },
        ["instructions"] =
            "Berreman 4x4 evaluator for stratified, possibly anisotropic, media. It evaluates "
            + "stacks and never optimises them. Only engine_status exists so far.",
    };

    // ---- tools -----------------------------------------------------------------------------

    private static JsonArray DescribeTools() => new(
        new JsonObject
        {
            ["name"] = "engine_status",
            ["description"] = "Whether the native engine loads, its ABI version, and what it can do.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
        });

    private static (string Text, bool Failed) Call(string name)
    {
        switch (name)
        {
            case "engine_status":
            {
                var r = EngineStatus.Probe();
                var o = new JsonObject
                {
                    ["loaded"] = r.Loaded,
                    ["managed_abi"] = r.ManagedAbi,
                    ["native_abi"] = r.NativeAbi,
                    ["plan"] = r.PlanStatus?.ToString(),
                    ["evaluate"] = r.EvaluateStatus?.ToString(),
                    ["summary"] = r.Summary,
                };
                return (o.ToJsonString(Wire), !r.Loaded || r.NativeAbi != r.ManagedAbi);
            }
            default:
                return ($"'{name}' is not a tool. tools/list gives the ones that exist.", true);
        }
    }

    // ---- framing ---------------------------------------------------------------------------

    private static void Write(TextWriter output, JsonObject message)
    {
        output.WriteLine(message.ToJsonString(Wire));
        output.Flush();
    }

    private static JsonObject Result(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
