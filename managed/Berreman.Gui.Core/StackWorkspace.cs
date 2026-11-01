// StackWorkspace.cs — the stack being edited: the model, its file, undo and redo, and the rows the
// layer table shows. No Avalonia: every edit the window makes is a call here, and tested.
//
// EVERY EDIT IS ATOMIC. Edit() takes a snapshot (the file text), applies the change, and checks
// that the result still writes as a stack file; if the change throws, or leaves something that
// cannot be written, the snapshot comes back and the message goes to the caller. So the table can
// never hold a stack the file format cannot say, and undo is the snapshots.
//
// Whether the stack EVALUATES (materials valid, angles in range, ...) is a separate question,
// asked by Document(): a half-built stack is allowed while it is being built.

using System.Globalization;
using System.Numerics;
using Berreman.Api;

namespace Berreman.Gui.Core;

public sealed class StackWorkspace
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const int MaxUndo = 200;

    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string _savedJson;

    public StackModel Model { get; private set; }
    /// <summary>The file the stack came from or was saved to; null for a new stack or a sample.</summary>
    public string? Path { get; private set; }
    /// <summary>A name for the title bar: the file's, the sample's, or "untitled".</summary>
    public string Name { get; private set; } = "untitled";

    /// <summary>After every change of the stack, including undo, redo and loading.</summary>
    public event Action? Changed;

    public StackWorkspace()
    {
        Model = StackModel.NewDefault();
        _savedJson = Model.ToJson();
    }

    public bool IsDirty => Model.ToJson() != _savedJson;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // ---- loading and saving --------------------------------------------------------------------

    /// <summary>The starting stack; it counts as unchanged until it is edited, so closing an
    /// untouched new stack does not ask to save it.</summary>
    public void New() => Replace(StackModel.NewDefault(), null, "untitled", markSaved: true);

    /// <summary>Loads a stack file's text; a null path is a sample, saved only by Save as.</summary>
    public void Load(string json, string? path, string? name = null)
    {
        var model = StackModel.FromJson(json);
        Replace(model, path, name ?? (path is null ? "untitled" : System.IO.Path.GetFileName(path)), markSaved: true);
    }

    /// <summary>The file text to write; call <see cref="MarkSaved"/> once it is written.</summary>
    public string Text => Model.ToJson();

    public void MarkSaved(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        _savedJson = Model.ToJson();
        Changed?.Invoke();
    }

    private void Replace(StackModel model, string? path, string name, bool markSaved)
    {
        Model = model;
        Path = path;
        Name = name;
        _undo.Clear();
        _redo.Clear();
        if (markSaved) _savedJson = model.ToJson();
        Changed?.Invoke();
    }

    // ---- editing -------------------------------------------------------------------------------

    /// <summary>Applies a change, or undoes it and throws StackFormatException with what is wrong.</summary>
    public void Edit(Action<StackModel> change)
    {
        var before = Model.ToJson();
        try
        {
            change(Model);
            if (Model.ToJson() == before) return;
        }
        catch (Exception e) when (e is StackFormatException or FormatException or ArgumentException)
        {
            Model = StackModel.FromJson(before);
            Changed?.Invoke();
            throw new StackFormatException(e.Message);
        }
        _undo.Add(before);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>Replaces the whole stack with a file's text, as one undoable edit (the file tab).</summary>
    public void EditText(string json)
    {
        var model = StackModel.FromJson(json);
        Edit(m =>
        {
            var copy = model.Clone();
            m.Title = copy.Title; m.Source = copy.Source; m.Notes = copy.Notes;
            m.LengthUnit = copy.LengthUnit; m.SpectralUnit = copy.SpectralUnit;
            m.Materials = copy.Materials; m.Incident = copy.Incident; m.Exit = copy.Exit; m.Layers = copy.Layers;
            m.Spectral = copy.Spectral; m.AnglesAreXi = copy.AnglesAreXi; m.Angles = copy.Angles; m.Azimuths = copy.Azimuths;
            m.Extra = copy.Extra; m.UnitsExtra = copy.UnitsExtra; m.SweepExtra = copy.SweepExtra;
        });
    }

    public void Undo() => Step(_undo, _redo);
    public void Redo() => Step(_redo, _undo);

    private void Step(List<string> from, List<string> to)
    {
        if (from.Count == 0) return;
        to.Add(Model.ToJson());
        Model = StackModel.FromJson(from[^1]);
        from.RemoveAt(from.Count - 1);
        Changed?.Invoke();
    }

    /// <summary>The stack as the evaluator reads it, or a StackFormatException saying why it cannot be evaluated yet.</summary>
    public StackDocument Document() => Model.ToDocument();

    // ---- layer operations, as the table's buttons call them ------------------------------------

    /// <summary>Inserts a copy of the layer at <paramref name="after"/> after it, or, with no layer
    /// selected, 100 nm of the first material that is not an outer medium at the end (or at the
    /// top when <paramref name="after"/> is -1); returns the new layer's index.</summary>
    public int InsertLayer(int after)
    {
        int at = 0;
        Edit(m =>
        {
            LayerModel layer;
            if (after >= 0 && after < m.Layers.Count) { layer = m.Layers[after].Clone(); at = after + 1; }
            else
            {
                var mat = m.Materials.FirstOrDefault(x => x.Name != m.Incident.Material && x.Name != m.Exit.Material)
                          ?? m.Materials.FirstOrDefault()
                          ?? throw new StackFormatException("add a material first");
                layer = new LayerModel { Material = mat.Name, Thickness = 100 / Units.NmPerLength(m.LengthUnit) };
                at = after < 0 ? 0 : m.Layers.Count;
            }
            m.Layers.Insert(at, layer);
        });
        return at;
    }

    public void DeleteLayers(IReadOnlyCollection<int> rows) =>
        Edit(m => { foreach (var i in rows.OrderByDescending(i => i)) if (i >= 0 && i < m.Layers.Count) m.Layers.RemoveAt(i); });

    /// <summary>Moves a layer one place towards the incident medium (-1) or the exit medium (+1); returns where it went.</summary>
    public int MoveLayer(int row, int direction)
    {
        int to = row + direction;
        if (row < 0 || row >= Model.Layers.Count || to < 0 || to >= Model.Layers.Count) return row;
        Edit(m => m.MoveLayer(row, to));
        return to;
    }

    // ---- unit changes that keep the stack physically the same ------------------------------------

    /// <summary>Changes the length unit, converting every thickness and pitch so nothing physical changes.</summary>
    public void SetLengthUnit(string unit) => Edit(m =>
    {
        double f = Units.NmPerLength(m.LengthUnit) / Units.NmPerLength(unit);
        foreach (var l in m.Layers)
        {
            l.Thickness *= f;
            if (l.Twist is { } t) t.Pitch *= f;
        }
        m.LengthUnit = unit;
    });

    /// <summary>
    /// Changes the spectral unit, converting the sweep so the same wavelengths
    /// are evaluated. A list is converted value by value; a range keeps its end points and its
    /// number of points (it is evenly spaced in the new unit, which is what a range in that unit
    /// means). Material tables carry their own units and are untouched.
    /// </summary>
    public void SetSpectralUnit(string unit) => Edit(m =>
    {
        if (!Units.IsSpectralUnit(unit)) throw new StackFormatException($"'{unit}' is not a spectral unit");
        string old = m.SpectralUnit;
        double C(double v) => Units.FromWavelengthNm(Units.ToWavelengthNm(v, old), unit);
        var node = SweepText.Parse(m.Spectral, "sweep.spectral");
        var values = StackDocument.Values(node, "sweep.spectral");
        if (node is System.Text.Json.Nodes.JsonObject && values.Length > 1)
            m.Spectral = $"{Num(Round(C(values[0])))} to {Num(Round(C(values[^1])))} count {values.Length}";
        else
            m.Spectral = string.Join(", ", values.Select(v => Num(Round(C(v)))));
        m.SpectralUnit = unit;
    });

    /// <summary>
    /// The spectral sweep as the window's three boxes show it, in the spectral unit. A single value
    /// is a start with no end or step. A range given by its count shows the step that count makes.
    /// A list (only a file can give one) shows its first and last values, and ListCount says how
    /// many it holds, so the window can say that typing replaces it.
    /// </summary>
    public (string Start, string End, string Step, int ListCount) SpectralRange()
    {
        static string N(double v) => v.ToString("G10", Inv);
        static double D(System.Text.Json.Nodes.JsonNode? n) => double.Parse(n?.ToJsonString() ?? "0", Inv);
        System.Text.Json.Nodes.JsonNode node;
        try { node = SweepText.Parse(Model.Spectral, "spectral"); }
        catch (StackFormatException) { return ("", "", "", 0); }
        switch (node)
        {
            case System.Text.Json.Nodes.JsonArray a:
                var v = StackDocument.Values(a, "spectral");
                return (N(v[0]), N(v[^1]), "", v.Length);
            case System.Text.Json.Nodes.JsonObject r:
                double from = D(r["from"]), to = D(r["to"]);
                double step = r["count"] is { } c
                    ? (D(c) is var n && n > 1 ? Math.Abs(to - from) / (n - 1) : 0)
                    : D(r["step"]);
                return (N(from), N(to), step > 0 ? N(Round(step)) : "", 0);
            default:
                return (N(D(node)), "", "", 0);
        }
    }

    /// <summary>The smallest of 1, 2, 5 times a power of ten that is at least x.</summary>
    internal static double RoundStep(double x)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(x)));
        foreach (var m in new[] { 1.0, 2, 5, 10 })
            if (m * p >= x * (1 - 1e-12)) return Round(m * p);
        return Round(10 * p);
    }

    /// <summary>How many spectral points the sweep has; null when it does not parse.</summary>
    public int? SpectralPointCount()
    {
        try { return StackDocument.Values(SweepText.Parse(Model.Spectral, "spectral"), "spectral").Length; }
        catch (StackFormatException) { return null; }
    }

    /// <summary>
    /// Sets the spectral sweep from the three boxes: a start alone (or an end equal to it) is one
    /// value; otherwise start, end and a positive step are a range. With no step, a round one is
    /// chosen (1, 2 or 5 times a power of ten) giving about a hundred intervals, so typing an end
    /// alone makes a usable range instead of an error. Undoable, as every edit.
    /// </summary>
    public void SetSpectralRange(string start, string end, string step) => Edit(m =>
    {
        double s = Parse(start, "the spectral start");
        if (string.IsNullOrWhiteSpace(end) || Parse(end, "the spectral end") == s)
        {
            m.Spectral = Num(s);
            return;
        }
        double e = Parse(end, "the spectral end");
        double d = string.IsNullOrWhiteSpace(step) ? RoundStep(Math.Abs(e - s) / 100) : Parse(step, "the spectral step");
        if (!(d > 0)) throw new StackFormatException("the spectral step must be positive");
        if (Math.Abs(e - s) / d > 1_000_000) throw new StackFormatException($"that is {Math.Abs(e - s) / d + 1:N0} points; make the step larger");
        m.Spectral = $"{Num(s)} to {Num(e)} step {Num(d)}";
    });

    // Conversions such as nm → GHz give 15 digits of noise; 12 significant figures is plenty.
    private static double Round(double v) => double.Parse(v.ToString("G12", Inv), Inv);
    private static string Num(double v) => v.ToString("R", Inv);

    // ---- what the table shows ------------------------------------------------------------------

    public IReadOnlyList<string> MaterialNames => Model.Materials.Select(m => m.Name).ToList();

    public List<LayerRow> Rows() => Enumerable.Range(0, Model.Layers.Count).Select(i => new LayerRow(this, i)).ToList();

    /// <summary>
    /// A material in a sentence, for lists and tooltips: its type, its principal indices at the
    /// first wavelength of the sweep, and the law it disperses by. "uniaxial, nₒ = 1.54, nₑ = 1.55
    /// at 633 nm; Sellmeier".
    /// </summary>
    public string Summary(string material)
    {
        if (Model.Material(material) is not { } m) return "missing";
        var c = Classify(m);
        if (c.Invalid is { } why) return $"invalid: {why}";
        if (c.Detail == "" || c.Law == "constant") c = c with { At = "" };
        return c.Kind + (c.Detail == "" ? "" : $", {c.Detail}") + c.At + (c.Law == "constant" ? "" : $"; {c.Law}");
    }

    /// <summary>A material's type in a few words, for the layer table: "uniaxial · Sellmeier".</summary>
    public string Kind(string material)
    {
        if (Model.Material(material) is not { } m) return "missing";
        var c = Classify(m);
        return c.Invalid is not null ? "invalid" : $"{c.Kind} · {c.ShortLaw}";
    }

    /// <summary>
    /// Whether turning the material can change anything: false only for an isotropic one, so the
    /// window hides the orientation boxes of an isotropic medium. A missing or invalid material
    /// keeps them, since what it will be is not known.
    /// </summary>
    public bool NeedsOrientation(string material) => !Kind(material).StartsWith("isotropic ·");

    private sealed record MaterialClass(string Kind, string Detail, string At, string Law, string ShortLaw, string? Invalid = null);

    /// <summary>
    /// THE TYPE IS READ FROM THE MATERIAL MATRIX, whatever form the material was written in (a
    /// rotated uniaxial written as terms is still uniaxial), at the first wavelength of the sweep, in
    /// the material's own axes. In order: magnetoelectric blocks (bianisotropic); an antisymmetric
    /// part of μ (gyromagnetic) or of ε (magneto-optic); then the symmetric ε by how many of its
    /// eigenvalues coincide: isotropic, uniaxial, biaxial. A material that gives a crystal lattice
    /// with oblique angles is monoclinic (one) or triclinic (more) instead of biaxial.
    /// </summary>
    private MaterialClass Classify(NamedMaterial m)
    {
        var (law, shortLaw) = LawOf(m.Spec);
        if (m.Spec["model"]?.ToString() == "resistive_sheet")   /* its ε depends on the layer holding it */
            return new MaterialClass("isotropic", "", "", law, shortLaw);
        try
        {
            var mat = MaterialParser.Parse(m.Spec, $"materials.{m.Name}");
            double lambda = FirstWavelength();
            var M = mat.At(lambda, 0);
            string at = mat.IsConstant ? "" : $" at {Units.FromWavelengthNm(lambda, Model.SpectralUnit):G5} {Model.SpectralUnit}";
            var (kind, detail) = Classify(M, m.Spec["lattice"]);
            return new MaterialClass(kind, detail, at, law, shortLaw);
        }
        catch (StackFormatException e) { return new MaterialClass("", "", "", law, shortLaw, e.Message); }
    }

    private static (string Kind, string Detail) Classify(MaterialMatrix M, System.Text.Json.Nodes.JsonNode? lattice)
    {
        Complex E(int i, int j) => M[i, j];
        Complex Mu(int i, int j) => M[3 + i, 3 + j];
        double scale = 0;
        for (int i = 0; i < 3; ++i) scale = Math.Max(scale, Math.Max(E(i, i).Magnitude, Mu(i, i).Magnitude));
        double tol = 1e-12 * Math.Max(scale, 1e-300);
        bool Zero(Complex z) => z.Magnitude <= tol;

        bool rho = false, muAntisym = false, epsAntisym = false, muScalar = true;
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
            {
                rho |= !Zero(M[i, 3 + j]) || !Zero(M[3 + i, j]);
                muAntisym |= !Zero(Mu(i, j) - Mu(j, i));
                epsAntisym |= !Zero(E(i, j) - E(j, i));
                muScalar &= i == j ? Zero(Mu(i, i) - Mu(0, 0)) : Zero(Mu(i, j));
            }
        if (rho) return ("bianisotropic", "");
        if (muAntisym) return ("gyromagnetic", "");
        if (epsAntisym)
        {
            /* The diagonal indices, and the off-diagonal elements that carry the gyration. */
            var off = new[] { (0, 1, "ε_xy"), (0, 2, "ε_xz"), (1, 2, "ε_yz") }.Where(t => !Zero(E(t.Item1, t.Item2)))
                                                                         .Select(t => $"{t.Item3} = {C(E(t.Item1, t.Item2))}");
            return ("magneto-optic", $"n = {Index(Complex.Sqrt(E(0, 0)))}, {Index(Complex.Sqrt(E(1, 1)))}, {Index(Complex.Sqrt(E(2, 2)))}; {string.Join(", ", off)}");
        }
        string mu = !muScalar ? ", μ anisotropic" : Zero(Mu(0, 0) - 1) ? "" : $", μ = {C(Mu(0, 0))}";

        /* ε symmetric. Its eigenvalues are p + x, with x the roots of x³ − 3qx − 2r = 0 for
           B = ε − pI, p = tr ε / 3, q = tr B² / 6, r = det B / 2. Two coincide when r² = q³: then
           the double root is −r/q (ordinary) and the single one 2r/q (extraordinary). */
        Complex p = (E(0, 0) + E(1, 1) + E(2, 2)) / 3;
        var B = new Complex[3, 3];
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) B[i, j] = E(i, j) - (i == j ? p : 0);
        bool bZero = true;
        foreach (var b in B) bZero &= Zero(b);
        if (bZero) return ("isotropic", muScalar && Zero(Mu(0, 0) - 1) ? $"n = {Index(Complex.Sqrt(p))}" : $"ε = {C(p)}{mu}");
        Complex q = 0;
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) q += B[i, j] * B[j, i];
        q /= 6;
        Complex r = (B[0, 0] * (B[1, 1] * B[2, 2] - B[1, 2] * B[2, 1]) - B[0, 1] * (B[1, 0] * B[2, 2] - B[1, 2] * B[2, 0])
                   + B[0, 2] * (B[1, 0] * B[2, 1] - B[1, 1] * B[2, 0])) / 2;
        Complex q3 = q * q * q;
        int oblique = 0;
        if (lattice is System.Text.Json.Nodes.JsonObject lat)
            foreach (var a in new[] { "alpha", "beta", "gamma" })
                if (lat[a] is { } v && Math.Abs(v.GetValue<double>() - 90) > 1e-9) ++oblique;
        if (oblique == 0 && q.Magnitude > 0 && (r * r - q3).Magnitude <= 1e-9 * q3.Magnitude)
        {
            Complex eo = p - r / q, ee = p + 2 * r / q;
            return ("uniaxial", $"nₒ = {Index(Complex.Sqrt(eo))}, nₑ = {Index(Complex.Sqrt(ee))}{mu}");
        }
        string kind = oblique == 0 ? "biaxial" : oblique == 1 ? "monoclinic" : "triclinic";
        /* Without a lattice: when the real and imaginary parts of ε do not share principal axes,
           the index and absorption axes differ, which only a monoclinic or triclinic crystal
           allows. Monoclinic when one coordinate axis is still a principal axis of both. */
        if (oblique == 0)
        {
            double comm = 0, re = 0, im = 0;
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                {
                    double c = 0;
                    for (int k = 0; k < 3; ++k) c += E(i, k).Real * E(k, j).Imaginary - E(i, k).Imaginary * E(k, j).Real;
                    comm = Math.Max(comm, Math.Abs(c));
                    re = Math.Max(re, Math.Abs(E(i, j).Real)); im = Math.Max(im, Math.Abs(E(i, j).Imaginary));
                }
            if (comm > 1e-9 * re * im)
            {
                bool axisKept = Enumerable.Range(0, 3).Any(k => Enumerable.Range(0, 3).All(j => j == k || Zero(E(k, j))));
                kind = axisKept ? "monoclinic" : "monoclinic or triclinic";
            }
        }
        bool diagonal = Zero(E(0, 1)) && Zero(E(0, 2)) && Zero(E(1, 2));
        if (diagonal) return (kind, $"n = {Index(Complex.Sqrt(E(0, 0)))}, {Index(Complex.Sqrt(E(1, 1)))}, {Index(Complex.Sqrt(E(2, 2)))}{mu}");
        /* The three roots by Cardano: x = u + q/u for each cube root u of r + √(r² − q³). */
        Complex d = Complex.Sqrt(r * r - q3), s = r + d;
        if (s.Magnitude < (r - d).Magnitude) s = r - d;
        Complex u = Complex.Pow(s, 1.0 / 3), w = new Complex(-0.5, Math.Sqrt(3) / 2);
        var n = new[] { u, u * w, u * w * w }.Select(x => Complex.Sqrt(p + x + (x.Magnitude == 0 ? 0 : q / x)))
                                             .OrderBy(z => z.Real).Select(Index);
        return (kind, $"principal n = {string.Join(", ", n)}{mu}");
    }

    private static readonly Dictionary<string, string> LawNames = new()
    {
        ["sellmeier"] = "Sellmeier", ["cauchy"] = "Cauchy", ["lorentz"] = "Lorentz", ["drude"] = "Drude",
        ["tauc_lorentz"] = "Tauc–Lorentz", ["phonon"] = "phonon (TO–LO)", ["phonon_additive"] = "phonon (additive)",
        ["table"] = "table", ["conductivity"] = "conductivity", ["polder"] = "Polder",
    };

    /// <summary>How the material disperses, in full for the summary and short for the table.</summary>
    private static (string Law, string Short) LawOf(System.Text.Json.Nodes.JsonObject spec)
    {
        /* A glass is named in full in the summary ("SCHOTT N-BK7") and by its catalogue in the table. */
        static IEnumerable<(string Long, string Short)> Laws(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is not System.Text.Json.Nodes.JsonObject o) yield break;
            foreach (var (key, value) in o)
            {
                if (key == "glass" && value is System.Text.Json.Nodes.JsonObject g) yield return ($"{g["catalog"]} {g["name"]}".Trim(), $"{g["catalog"]} glass".Trim());
                else if (LawNames.TryGetValue(key, out var name)) yield return (name, name);
            }
        }
        if (spec["model"] is { } model)
        {
            var name = model.ToString() switch { "ferrite" => "ferrite model", "conductor" => "conductor model", "resistive_sheet" => "resistive sheet", var x => $"model {x}" };
            return (name, name);
        }
        if (spec["terms"] is System.Text.Json.Nodes.JsonArray terms)
        {
            var laws = terms.SelectMany(Laws).Select(l => l.Long).Distinct().ToList();
            string count = $"{terms.Count} term{(terms.Count == 1 ? "" : "s")}";
            return (laws.Count == 0 ? $"{count}, constant" : $"{count}: {string.Join(", ", laws)}", count);
        }
        var all = Laws(spec).Concat(Laws(spec["uniaxial"]?["ordinary"])).Concat(Laws(spec["uniaxial"]?["extraordinary"]))
                            .Concat(new[] { "x", "y", "z" }.SelectMany(a => Laws(spec["biaxial"]?[a]))).Distinct().ToList();
        if (all.Count == 0) return ("constant", "constant");
        return (string.Join(", ", all.Select(l => l.Long)), string.Join(", ", all.Select(l => l.Short).Distinct()));
    }

    private double FirstWavelength()
    {
        try { return Units.ToWavelengthNm(StackDocument.Values(SweepText.Parse(Model.Spectral, "spectral"), "spectral")[0], Model.SpectralUnit); }
        catch (StackFormatException) { return 550; }
    }

    /* An imaginary part below 10⁻¹² of the whole is round-off (from the eigenvalues) and dropped. */
    private static string Index(Complex n) => C(n);
    private static string C(Complex z)
    {
        double re = Math.Abs(z.Real) <= 1e-12 * z.Magnitude ? 0 : z.Real, im = Math.Abs(z.Imaginary) <= 1e-12 * z.Magnitude ? 0 : z.Imaginary;
        if (im == 0) return re.ToString("G6", Inv);
        string i = $"{Math.Abs(im).ToString("G4", Inv)}i";
        return re == 0 ? (im < 0 ? $"−{i}" : i) : $"{re.ToString("G5", Inv)} {(im < 0 ? "−" : "+")} {i}";
    }

    /// <summary>The total physical thickness, in the length unit.</summary>
    public double TotalThickness => Model.Layers.Sum(l => l.Thickness);

    internal static string Number(double v, string format = "0.####") => double.IsNaN(v) ? "—" : v.ToString(format, Inv);

    internal static double Parse(string? text, string what)
    {
        var t = (text ?? "").Trim().Replace("°", "");
        return double.TryParse(t, NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v
            : throw new StackFormatException($"{what}: '{text}' is not a number");
    }
}

/// <summary>
/// One row of the layer table. Reading a property shows the stack; setting one edits it through
/// the workspace (and so is undoable), and a refused edit is reported by <see cref="Error"/>.
/// </summary>
public sealed class LayerRow(StackWorkspace ws, int index)
{
    /// <summary>Reports an edit that was refused, with why; the window shows it in the status line.</summary>
    public static event Action<string>? Error;

    private LayerModel L => ws.Model.Layers[index];
    public int Index => index;
    public int Number => index + 1;

    public IReadOnlyList<string> MaterialOptions => ws.MaterialNames;
    public string Material
    {
        get => L.Material;
        set { if (value is not null && value != L.Material) Try(m => m.Layers[index].Material = value); }
    }

    /// <summary>The material's type and dispersion, "uniaxial · Sellmeier"; the full summary is the tooltip.</summary>
    public string TypeText => ws.Kind(L.Material);
    public string MaterialSummary => ws.Summary(L.Material);

    /// <summary>The physical thickness, in the stack's length unit.</summary>
    public string ThicknessText
    {
        get => StackWorkspace.Number(L.Thickness, "0.#####");
        set => Try(m =>
        {
            var t = StackWorkspace.Parse(value, $"layer {Number} thickness");
            if (!(t >= 0)) throw new StackFormatException($"layer {Number}: a thickness must be zero or positive");
            m.Layers[index].Thickness = t;
        });
    }

    public string Phi { get => Angle(0); set => SetAngle(0, value, "φ"); }
    public string Theta { get => Angle(1); set => SetAngle(1, value, "θ"); }
    public string Psi { get => Angle(2); set => SetAngle(2, value, "ψ"); }

    /// <summary>Whether the layer's orientation matters: false for an isotropic material, whose φ, θ, ψ
    /// cells and optic axis the table leaves blank (the angles are kept).</summary>
    public bool Oriented => ws.NeedsOrientation(L.Material);

    /// <summary>Where the material's z axis (a uniaxial optic axis) points: "tilt 30°, az 45°", or "z" when unrotated.</summary>
    public string AxisText
    {
        get
        {
            if (!Oriented) return "";
            var e = L.EulerDeg;
            if (e[1] == 0) return e[0] + e[2] == 0 ? "along z" : "along z (turned)";
            var (tilt, az) = MaterialMatrix.AxisDirection(e[0], e[1], e[2]);
            return $"tilt {StackWorkspace.Number(tilt, "0.##")}°, az {StackWorkspace.Number(az, "0.##")}°";
        }
    }

    public string TwistText => L.Twist is { } t
        ? $"{StackWorkspace.Number(t.Pitch, "0.###")} {(ws.Model.LengthUnit == "um" ? "µm" : ws.Model.LengthUnit)} {t.Handedness}"
        : "";

    private string Angle(int k) => StackWorkspace.Number(L.EulerDeg[k], "0.####");

    private void SetAngle(int k, string? text, string name) =>
        Try(m => m.Layers[index].EulerDeg[k] = StackWorkspace.Parse(text, $"layer {Number} {name}"));

    private void Try(Action<StackModel> change)
    {
        try { ws.Edit(change); }
        catch (StackFormatException e) { Error?.Invoke(e.Message); }
    }
}
