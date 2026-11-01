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
        ["quantities"] => ListQuantities(),
        ["convert", var from, var to] => Convert(from, to),
        ["report", var path, .. var rest] => Report(path, rest),
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
              --mueller        add the Mueller matrix in reflection, Mr12 ... Mr44 (each / M11)
              --quantities <a,b,...>  add these quantities by name (berreman quantities lists them)
              --csv <file>     write the table to a CSV file instead of the console
          quantities                        every quantity --quantities can name, with what it is
          report <stack.json> [--from a --to b --step s] [--out file]
                                            the layers and every material as tables (docs/user-guide.md)
          convert <in> <out>                a ThinFilmDesigner design (.tfd) to a stack file (.json),
                                            or an isotropic stack to a .tfd (docs/user-guide.md)
          status                            whether the native engine loads, and its ABI version
          help                              this text

        Stack files are described in docs/stack-format.md.
        """);
    return 0;
}

static int Report(string path, string[] options)
{
    var model = StackModel.FromJson(File.ReadAllText(path));
    var (from, to, step) = DesignReport.DefaultRange(model);
    string? output = null;
    for (int i = 0; i < options.Length; ++i)
    {
        double Next() => i + 1 < options.Length && double.TryParse(options[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : throw new StackFormatException($"{options[i]} needs a number");
        switch (options[i])
        {
            case "--from": from = Next(); break;
            case "--to": to = Next(); break;
            case "--step": step = Next(); break;
            case "--out" when i + 1 < options.Length: output = options[++i]; break;
            default:
                Console.Error.WriteLine($"berreman: unknown option '{options[i]}'. Run 'berreman help'.");
                return 2;
        }
    }
    var text = DesignReport.Build(model, from, to, step);
    if (output is null) Console.Write(text);
    else { File.WriteAllText(output, text, new UTF8Encoding(false)); Console.WriteLine($"report written to {output}"); }
    return 0;
}

static int Convert(string from, string to)
{
    bool tfdIn = from.EndsWith(".tfd", StringComparison.OrdinalIgnoreCase), tfdOut = to.EndsWith(".tfd", StringComparison.OrdinalIgnoreCase);
    if (tfdIn == tfdOut)
    {
        Console.Error.WriteLine("berreman: convert reads a .tfd and writes a .json, or reads a .json and writes a .tfd");
        return 2;
    }
    var text = File.ReadAllText(from);
    var output = tfdIn ? TfdFile.Read(text).ToJson() : TfdFile.Write(StackModel.FromJson(text));
    File.WriteAllText(to, output, new UTF8Encoding(false));
    Console.WriteLine($"{from} written as {to}");
    return 0;
}

static int ListQuantities()
{
    foreach (var q in Quantities.All()) Console.WriteLine($"{q.Name,-12} {q.Group,-11} {q.Description}");
    Console.WriteLine("With anisotropic outer media the waves are modes 0 and 1: R_01 for R_ps, Psi_01 for Psi_ps; either name is accepted.");
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
    var quantities = new List<string>();
    string? csv = null;
    for (int i = 0; i < options.Length; ++i)
    {
        switch (options[i])
        {
            case "--jones": jones = true; break;
            case "--ellipsometry": ellipsometry = true; break;
            case "--absorption": absorption = true; break;
            case "--mueller": quantities.AddRange(Quantities.All().Where(q => q.Name.StartsWith("Mr")).Select(q => q.Name)); break;
            case "--quantities" when i + 1 < options.Length: quantities.AddRange(options[++i].Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
            case "--csv" when i + 1 < options.Length: csv = options[++i]; break;
            default:
                Console.Error.WriteLine($"berreman: unknown option '{options[i]}'. Run 'berreman help'.");
                return 2;
        }
    }

    var doc = StackDocument.Load(path);
    var result = Evaluator.Evaluate(doc);
    ResultTable table;
    try { table = ResultTable.Build(result, jones, ellipsometry, absorption, quantities); }
    catch (StackFormatException e) { Console.Error.WriteLine($"berreman: {e.Message}"); return 2; }

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
