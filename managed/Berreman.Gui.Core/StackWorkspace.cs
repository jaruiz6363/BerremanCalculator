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

    // Conversions such as nm → GHz give 15 digits of noise; 12 significant figures is plenty.
    private static double Round(double v) => double.Parse(v.ToString("G12", Inv), Inv);
    private static string Num(double v) => v.ToString("R", Inv);

    // ---- what the table shows ------------------------------------------------------------------

    public IReadOnlyList<string> MaterialNames => Model.Materials.Select(m => m.Name).ToList();

    public List<LayerRow> Rows() => Enumerable.Range(0, Model.Layers.Count).Select(i => new LayerRow(this, i)).ToList();

    /// <summary>A material in a few words, for lists: "n = 1.52", "uniaxial, nₒ = 1.54, nₑ = 1.55", "model: ferrite".</summary>
    public string Summary(string material)
    {
        if (Model.Material(material) is not { } m) return "missing";
        if (m.Spec["model"] is { } model) return $"model: {model}";
        string terms = m.Spec["terms"] is System.Text.Json.Nodes.JsonArray list ? $"{list.Count} terms; " : "";
        return terms + Describe(m);
    }

    private string Describe(NamedMaterial m)
    {
        try
        {
            var mat = MaterialParser.Parse(m.Spec, $"materials.{m.Name}");
            double lambda = FirstWavelength();
            var M = mat.At(lambda, 0);
            string at = mat.IsConstant ? "" : $" at {Units.FromWavelengthNm(lambda, Model.SpectralUnit):G5} {Model.SpectralUnit}";
            if (M.IsIsotropic(out var eps, out var mu))
                return mu == Complex.One ? $"n = {Index(Complex.Sqrt(eps))}{at}" : $"ε = {C(eps)}, μ = {C(mu)}{at}";
            bool diagonal = Enumerable.Range(0, 6).All(i => Enumerable.Range(0, 6).All(j => i == j || M[i, j] == Complex.Zero));
            if (diagonal && M[3, 3] == Complex.One && M[4, 4] == Complex.One && M[5, 5] == Complex.One)
            {
                Complex nx = Complex.Sqrt(M[0, 0]), ny = Complex.Sqrt(M[1, 1]), nz = Complex.Sqrt(M[2, 2]);
                if (nx == ny) return $"uniaxial, nₒ = {Index(nx)}, nₑ = {Index(nz)}{at}";
                return $"biaxial, n = {Index(nx)}, {Index(ny)}, {Index(nz)}{at}";
            }
            return $"anisotropic (6×6){at}";
        }
        catch (StackFormatException e) { return $"invalid: {e.Message}"; }
    }

    private double FirstWavelength()
    {
        try { return Units.ToWavelengthNm(StackDocument.Values(SweepText.Parse(Model.Spectral, "spectral"), "spectral")[0], Model.SpectralUnit); }
        catch (StackFormatException) { return 550; }
    }

    private static string Index(Complex n) => n.Imaginary == 0 ? n.Real.ToString("G6", Inv) : $"{n.Real.ToString("G5", Inv)} + {n.Imaginary.ToString("G4", Inv)}i";
    private static string C(Complex z) => z.Imaginary == 0 ? z.Real.ToString("G6", Inv) : $"{z.Real.ToString("G5", Inv)} + {z.Imaginary.ToString("G4", Inv)}i";

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

    /// <summary>Where the material's z axis (a uniaxial optic axis) points: "tilt 30°, az 45°", or "z" when unrotated.</summary>
    public string AxisText
    {
        get
        {
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
