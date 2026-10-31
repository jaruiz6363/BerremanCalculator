// Program.cs — the berreman command.
//
// Exit codes: 0 success, 1 the engine failed on some point, 2 a usage error, an unreadable stack,
// or a command not built yet.

using System.Globalization;
using System.Text;
using Berreman.Api;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

try
{
    return args switch
    {
        [] or ["help"] or ["--help"] or ["-h"] => Help(),
        ["status"] => Status(),
        ["evaluate", var path, .. var rest] => Evaluate(path, rest),
        [var command, ..] => NotYet(command),
    };
}
catch (StackFormatException e)
{
    Console.Error.WriteLine($"berreman: {e.Message}");
    return 2;
}

static int Help()
{
    Console.WriteLine("""
        berreman — a Berreman 4x4 evaluator for stratified media

        Commands:
          evaluate <stack.json> [options]   evaluate a stack file and print a table
              --jones          add the Jones coefficients (re, im)
              --ellipsometry   add Psi and Delta (degrees), from r_pp / r_ss
              --absorption     add the absorption in each layer
              --csv <file>     write the table to a CSV file instead of the console
          status                            whether the native engine loads, and its ABI version
          help                              this text

        Stack files are described in docs/stack-format.md.
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

static int Evaluate(string path, string[] options)
{
    bool jones = false, ellipsometry = false, absorption = false;
    string? csv = null;
    for (int i = 0; i < options.Length; ++i)
    {
        switch (options[i])
        {
            case "--jones": jones = true; break;
            case "--ellipsometry": ellipsometry = true; break;
            case "--absorption": absorption = true; break;
            case "--csv" when i + 1 < options.Length: csv = options[++i]; break;
            default:
                Console.Error.WriteLine($"berreman: unknown option '{options[i]}'. Run 'berreman help'.");
                return 2;
        }
    }

    var doc = StackDocument.Load(path);
    var result = Evaluator.Evaluate(doc);
    var table = ResultTable.Build(result, jones, ellipsometry, absorption);

    if (csv is not null)
    {
        File.WriteAllText(csv, table.ToCsv(), Encoding.UTF8);
        Console.WriteLine($"{result.Points.Count} points written to {csv}");
    }
    else
    {
        if (doc.Title != "") Console.WriteLine(doc.Title);
        if (doc.Source != "") Console.WriteLine($"Source: {doc.Source}");
        Console.WriteLine(table.ToText());
        if (result.SublayerChange is { } dc)
            Console.WriteLine($"Sublayers: doubling them changes R, T and A by at most {dc:G3}.");
    }

    int failed = result.Points.Count(p => p.Status != Berreman.Native.BcStatus.Ok);
    if (failed > 0)
    {
        Console.Error.WriteLine($"berreman: {failed} of {result.Points.Count} points failed; see the status column");
        return 1;
    }
    return 0;
}

static int NotYet(string command)
{
    Console.Error.WriteLine($"berreman: '{command}' is not a command. Run 'berreman help'.");
    return 2;
}
