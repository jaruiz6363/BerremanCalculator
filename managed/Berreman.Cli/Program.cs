// Program.cs — the berreman command.
//
// Exit codes: 0 success, 1 the engine failed, 2 a usage error or a command not built yet.

using Berreman.Api;

return args switch
{
    [] or ["help"] or ["--help"] or ["-h"] => Help(),
    ["status"] => Status(),
    [var command, ..] => NotYet(command),
};

static int Help()
{
    Console.WriteLine("""
        berreman — a Berreman 4x4 evaluator for stratified media

        Commands:
          status    whether the native engine loads, its ABI version, and what it can do
          help      this text

        Nothing else is implemented yet.
        """);
    return 0;
}

static int Status()
{
    var r = EngineStatus.Probe();
    Console.WriteLine($"engine:      {(r.Loaded ? "loaded" : "not loaded")}");
    Console.WriteLine($"ABI:         native {r.NativeAbi?.ToString() ?? "-"}, expected {r.ManagedAbi}");
    if (r.PlanStatus is { } plan) Console.WriteLine($"bc_plan:     {plan}");
    if (r.EvaluateStatus is { } evaluate) Console.WriteLine($"bc_evaluate: {evaluate}");
    Console.WriteLine(r.Summary);
    return r.Loaded && r.NativeAbi == r.ManagedAbi ? 0 : 1;
}

static int NotYet(string command)
{
    Console.Error.WriteLine($"berreman: '{command}' is not a command yet. Run 'berreman help'.");
    return 2;
}
