// MainWindow.Materials.cs — the Materials tab's four views of the selected material
// (docs/material-model.md; the arithmetic is Gui.Core/MaterialEditing.cs, which is tested):
//   Simple    a constant n and k per principal axis;
//   Terms     each law and its 6×6 shape, typed into fields; any material can be shown this way,
//             and applying a change writes it as terms;
//   Response  what the material does across a range: n and k, any element of M, the directions
//             of its principal axes, and whether it is passive and reciprocal;
//   Text      the material as the file writes it.
// Every change goes through the workspace like any other edit, so it is checked, can be undone,
// and redraws everything.

using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Gui;

public partial class MainWindow
{
    private JsonArray? _terms;                 // the selected material written as terms
    private JsonObject? _lattice;              // its lattice, for directions in crystal axes
    private int _termIndex;
    private string? _shownMaterial;            // the material the views were last filled for
    private string? _shownSpec;                // and its definition then
    private bool _showingMaterial;             // true while the views are being filled, not edited
    private readonly TextBox[] _shapeCells = new TextBox[36];
    private readonly List<(LawField Field, TextBox Box)> _lawBoxes = [];

    private static readonly string[] FieldNames = ["Ex", "Ey", "Ez", "H̃x", "H̃y", "H̃z"];
    private static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace");

    private void InitMaterialEditor()
    {
        // ---- libraries ---------------------------------------------------------------------------
        LibraryButton.Click += async (_, _) => await AddFromLibrary();
        SaveToLibraryButton.Click += (_, _) =>
        {
            if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat) return;
            Try(() => MaterialLibrary.SaveToUserFile(name, mat.Spec),
                $"'{name}' saved to your materials ({MaterialLibrary.UserFile}); From library… offers it in any stack.");
        };

        // ---- simple ------------------------------------------------------------------------------
        SimpleKindBox.SelectionChanged += (_, _) => { if (!_showingMaterial) SimpleRows(); };
        ApplySimpleButton.Click += (_, _) => ApplySimple();
        RevertSimpleButton.Click += (_, _) => ShowMaterialViews(force: true);
        MakeSimpleButton.Click += (_, _) => MakeSimple();

        // ---- terms -------------------------------------------------------------------------------
        TermLawBox.ItemsSource = MaterialEditing.LawChoices;
        TermLawBox.SelectionChanged += (_, _) =>
        {
            if (_showingMaterial || TermLawBox.SelectedIndex < 0) return;
            var law = TermLawBox.SelectedIndex == 0 ? null : MaterialEditing.LawChoices[TermLawBox.SelectedIndex];
            LawFields(law is null ? null : MaterialEditing.DefaultLaw(law));
            TermLawFormula.Text = MaterialEditing.LawFormula(law);
        };
        ShapeModeBox.SelectionChanged += (_, _) => { if (!_showingMaterial) ShapePanels(fromDirection: true); };
        BuildShapeGrid();
        TermList.SelectionChanged += (_, _) =>
        {
            if (_showingMaterial || TermList.SelectedIndex < 0) return;
            _termIndex = TermList.SelectedIndex;
            ShowTerm();
        };
        AddTermButton.Click += (_, _) => EditTerms(t =>
        {
            int at = Math.Min(_termIndex + 1, t.Count);
            t.Insert(at, new JsonObject { ["label"] = "new term", ["lorentz"] = MaterialEditing.DefaultLaw("lorentz"), ["axis_deg"] = 0 });
            return at;
        }, "A Lorentz oscillator along x is added; give its parameters and Apply term.");
        DuplicateTermButton.Click += (_, _) => EditTerms(t =>
        {
            t.Insert(_termIndex + 1, t[_termIndex]!.DeepClone());
            return _termIndex + 1;
        }, "The term is copied.");
        DeleteTermButton.Click += (_, _) => EditTerms(t =>
        {
            if (t.Count == 1) throw new FormatException("a material needs at least one term");
            t.RemoveAt(_termIndex);
            return Math.Min(_termIndex, t.Count - 1);
        }, "The term is deleted.");
        TermUpButton.Click += (_, _) => EditTerms(t => Move(t, _termIndex, -1), null);
        TermDownButton.Click += (_, _) => EditTerms(t => Move(t, _termIndex, +1), null);
        ApplyTermButton.Click += (_, _) => EditTerms(t =>
        {
            t[_termIndex] = BuildTerm();
            return _termIndex;
        }, "The term is applied.");
        RevertTermButton.Click += (_, _) => ShowTerm();

        // ---- response ----------------------------------------------------------------------------
        ResponseQuantityBox.SelectedIndex = 0;
        ResponseRowBox.ItemsSource = FieldNames;
        ResponseColBox.ItemsSource = FieldNames;
        ResponseRowBox.SelectedIndex = 0;
        ResponseColBox.SelectedIndex = 0;
        ResponseQuantityBox.SelectionChanged += (_, _) => { if (!_showingMaterial) PlotResponse(); };
        ResponseRowBox.SelectionChanged += (_, _) => { if (!_showingMaterial) PlotResponse(); };
        ResponseColBox.SelectionChanged += (_, _) => { if (!_showingMaterial) PlotResponse(); };
        ResponsePlotButton.Click += (_, _) => PlotResponse();
        foreach (var box in new[] { ResponseFromBox, ResponseToBox, ResponsePointsBox, ResponseAtBox })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; PlotResponse(); } };
        ResponsePlot.EmptyText = "Choose a material and a range, then Plot.";
        MaterialTabs.SelectionChanged += (_, e) => { if (e.Source == MaterialTabs && MaterialTabs.SelectedIndex == 2 && !_showingMaterial) PlotResponse(); };
    }

    /// <summary>The library dialog; what it returns is added to the stack as one undoable edit.</summary>
    private async Task AddFromLibrary()
    {
        double nm;
        string at;
        try
        {
            var doc = _ws.Model.ToDocument();
            nm = Units.ToWavelengthNm(doc.Spectral[0], doc.SpectralUnit);
            at = $"{doc.Spectral[0].ToString("G6", Inv)} {doc.SpectralUnit}";
        }
        catch (StackFormatException) { nm = 550; at = "550 nm"; }
        var chosen = await new LibraryDialog(nm, at).ShowDialog<List<LibraryEntry>?>(this);
        if (chosen is null || chosen.Count == 0) return;
        var added = new List<string>();
        Try(() => _ws.Edit(m =>
        {
            foreach (var e in chosen)
            {
                var name = m.UniqueName(e.Name);
                m.Materials.Add(new NamedMaterial(name, (JsonObject)e.Spec.DeepClone()));
                added.Add(name);
            }
        }), null);
        if (added.Count == 0) return;
        Status(added.Count == 1 ? $"'{added[0]}' added from {chosen[0].Collection}." : $"{added.Count} materials added: {string.Join(", ", added)}.");
        _selectedMaterial = added[^1];
        ShowMaterials();
    }

    private JsonObject? _pendingLattice;       // a lattice typed with a term, written when it is applied

    private static int Move(JsonArray t, int at, int by)
    {
        int to = at + by;
        if (to < 0 || to >= t.Count) return at;
        var node = t[at];
        t.RemoveAt(at);
        t.Insert(to, node!.DeepClone());
        return to;
    }

    /// <summary>The views for the selected material; refilled when it changes or after an edit.</summary>
    private void ShowMaterialViews(bool force = false)
    {
        var mat = _selectedMaterial is null ? null : _ws.Model.Material(_selectedMaterial);
        bool changed = force || mat?.Name != _shownMaterial;
        /* The window redraws after every evaluation; the views are refilled only when the material
           itself changed, so what is being typed into them is not lost. */
        var specText = mat?.Spec.ToJsonString();
        if (!changed && specText == _shownSpec) return;
        _shownMaterial = mat?.Name;
        _shownSpec = specText;
        _showingMaterial = true;
        try
        {
            MaterialTabs.IsEnabled = mat is not null;
            if (mat is null) { _terms = null; return; }

            /* Simple. */
            var simple = MaterialEditing.AsSimple(mat.Spec);
            SimplePanel.IsVisible = simple is not null;
            MakeSimpleButton.IsVisible = simple is null;
            SimpleHint.Text = simple is not null
                ? "A constant n and k along each principal axis. A uniaxial material's optic axis is its z; a layer's Euler angles turn it."
                : "This material is not a constant n and k: it disperses, or is a tensor, a model or a list of terms. Edit it on Terms or Text. "
                  + "Or replace it with constants, its values at the stack's first spectral point.";
            if (simple is not null)
            {
                SimpleKindBox.SelectedIndex = (int)simple.Kind;
                var n = new[] { SimpleN0, SimpleN1, SimpleN2 };
                var k = new[] { SimpleK0, SimpleK1, SimpleK2 };
                for (int i = 0; i < 3; ++i) { n[i].Text = simple.N[i].ToString("R", Inv); k[i].Text = simple.K[i].ToString("R", Inv); }
                SimpleRows();
            }

            /* Terms. */
            _lattice = mat.Spec["lattice"] as JsonObject;
            try { _terms = (JsonArray)MaterialEditing.AsTerms(mat.Spec)["terms"]!; }
            catch (FormatException e) { _terms = null; TermsHint.Text = e.Message; }
            if (_terms is not null)
                TermsHint.Text = mat.Spec.ContainsKey("terms")
                    ? $"{_terms.Count} terms, added together: M = Σ f(λ) × shape."
                    : "This material is written in a short form, shown here as the terms it stands for. Applying a change here writes it as terms, with the same numbers.";
            TermList.ItemsSource = _terms?.Select(t => MaterialEditing.Describe(t!.AsObject())).ToList();
            if (changed) _termIndex = 0;
            if (_terms is not null) _termIndex = Math.Clamp(_termIndex, 0, _terms.Count - 1);
            TermList.SelectedIndex = _terms is null ? -1 : _termIndex;
            foreach (var c in new Control[] { AddTermButton, DuplicateTermButton, DeleteTermButton, TermUpButton, TermDownButton, TermEditor })
                c.IsEnabled = _terms is not null;

            if (changed)
            {
                MaterialTabs.SelectedIndex = simple is not null ? 0 : mat.Spec.ContainsKey("terms") ? 1 : MaterialTabs.SelectedIndex is 0 ? 1 : MaterialTabs.SelectedIndex;
                DefaultResponseRange();
            }
        }
        finally { _showingMaterial = false; }
        ShowTerm();
        if (MaterialTabs.SelectedIndex == 2) PlotResponse();
    }

    // ---- simple --------------------------------------------------------------------------------

    private void SimpleRows()
    {
        int kind = Math.Max(0, SimpleKindBox.SelectedIndex);
        SimpleLabel0.Text = kind switch { 0 => "all axes", 1 => "ordinary (x, y)", _ => "x" };
        SimpleLabel2.Text = kind == 1 ? "extraordinary (z)" : "z";
        foreach (var c in new Control[] { SimpleLabel1, SimpleN1, SimpleK1 }) c.IsVisible = kind == 2;
        foreach (var c in new Control[] { SimpleLabel2, SimpleN2, SimpleK2 }) c.IsVisible = kind >= 1;
    }

    private void ApplySimple()
    {
        if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat) return;
        Try(() =>
        {
            double V(TextBox b, string what) => double.TryParse((b.Text ?? "").Trim(), NumberStyles.Float, Inv, out var v) && double.IsFinite(v)
                ? v : throw new FormatException($"{what}: '{b.Text}' is not a number");
            var kind = (MaterialEditing.SimpleKind)Math.Max(0, SimpleKindBox.SelectedIndex);
            var n = new[] { V(SimpleN0, "n"), SimpleN1.IsVisible ? V(SimpleN1, "n (y)") : 0, SimpleN2.IsVisible ? V(SimpleN2, "n (z)") : 0 };
            var k = new[] { V(SimpleK0, "k"), SimpleK1.IsVisible ? V(SimpleK1, "k (y)") : 0, SimpleK2.IsVisible ? V(SimpleK2, "k (z)") : 0 };
            if (n.Any(x => x < 0) || k.Any(x => x < 0)) throw new FormatException("n and k must not be negative (k ≥ 0 is absorption in this program's convention)");
            var spec = MaterialEditing.FromSimple(new MaterialEditing.SimpleForm(kind, n, k), mat.Spec);
            SetSpec(name, spec);
        }, $"'{name}' updated.");
    }

    private void MakeSimple()
    {
        if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat) return;
        Try(() =>
        {
            var doc = _ws.Model.ToDocument();
            double lambda = Units.ToWavelengthNm(doc.Spectral[0], doc.SpectralUnit);
            var M = doc.Materials[name].At(lambda, 100);
            Complex Ix(int i) { var z = Complex.Sqrt(M[i, i]); return z.Imaginary < 0 ? -z : z; }
            var ix = new[] { Ix(0), Ix(1), Ix(2) };
            var kind = ix[0] == ix[1] && ix[1] == ix[2] ? MaterialEditing.SimpleKind.Isotropic
                     : ix[0] == ix[1] ? MaterialEditing.SimpleKind.Uniaxial : MaterialEditing.SimpleKind.Biaxial;
            var spec = MaterialEditing.FromSimple(new MaterialEditing.SimpleForm(kind, ix.Select(z => z.Real).ToArray(), ix.Select(z => z.Imaginary).ToArray()), mat.Spec);
            SetSpec(name, spec);
        }, $"'{name}' is now constant: the diagonal of ε at the first spectral point. Off-diagonal elements and μ are dropped; Undo brings it back.");
        ShowMaterialViews(force: true);
    }

    /// <summary>Checks a material and puts it in the stack, as one undoable edit.</summary>
    private void SetSpec(string name, JsonObject spec)
    {
        MaterialParser.Parse(spec, $"materials.{name}");   // refuse what would not evaluate
        _ws.Edit(m => m.Material(name)!.Spec = spec);
    }

    // ---- terms ---------------------------------------------------------------------------------

    /// <summary>One change to the term list, written back as the material's terms.</summary>
    private void EditTerms(Func<JsonArray, int> change, string? done)
    {
        if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat || _terms is null) return;
        int select = _termIndex;
        Try(() =>
        {
            var terms = (JsonArray)_terms.DeepClone();
            select = change(terms);
            var spec = new JsonObject { ["terms"] = terms };
            var lattice = _pendingLattice ?? _lattice;
            if (lattice is not null) spec["lattice"] = lattice.DeepClone();
            foreach (var k in new[] { "source", "notes" })
                if (mat.Spec[k] is { } v) spec[k] = v.DeepClone();
            SetSpec(name, spec);
        }, done);
        _termIndex = select;
        _pendingLattice = null;
        ShowMaterialViews();
    }

    private void ShowTerm()
    {
        _showingMaterial = true;
        try
        {
            var term = _terms is not null && _termIndex >= 0 && _termIndex < _terms.Count ? _terms[_termIndex]!.AsObject() : null;
            TermEditor.IsEnabled = term is not null;
            if (term is null) { LawFields(null); return; }
            TermLabelBox.Text = term["label"]?.GetValue<string>() ?? "";
            var law = MaterialParser.DispersionKeys.FirstOrDefault(term.ContainsKey);
            TermLawBox.SelectedIndex = law is null ? 0 : Array.IndexOf(MaterialEditing.LawChoices, law);
            TermLawFormula.Text = MaterialEditing.LawFormula(law);
            LawFields(law is null ? null : term[law] as JsonObject);

            ShapeModeBox.SelectedIndex = term.ContainsKey("axis_deg") ? 0 : term.ContainsKey("axis") ? 1 : term.ContainsKey("axis_uvw") ? 3 : 2;
            UvwUBox.Text = UvwVBox.Text = UvwWBox.Text = "0";
            if (term["axis_uvw"] is JsonArray uvw) { UvwUBox.Text = Txt(uvw[0]); UvwVBox.Text = Txt(uvw[1]); UvwWBox.Text = Txt(uvw[2]); }
            LatticeABox.Text = Txt(_lattice?["a"]); LatticeBBox.Text = Txt(_lattice?["b"]); LatticeCBox.Text = Txt(_lattice?["c"]);
            LatticeAlphaBox.Text = _lattice?["alpha"] is { } al ? Txt(al) : "90";
            LatticeBetaBox.Text = _lattice?["beta"] is { } be ? Txt(be) : "90";
            LatticeGammaBox.Text = _lattice?["gamma"] is { } ga ? Txt(ga) : "90";
            AxisAngleBox.Text = AxisElevationBox.Text = "0";
            AxisXBox.Text = "1"; AxisYBox.Text = AxisZBox.Text = "0";
            if (term["axis_deg"] is JsonArray ad) { AxisAngleBox.Text = Txt(ad[0]); AxisElevationBox.Text = Txt(ad[1]); }
            else if (term["axis_deg"] is { } a) AxisAngleBox.Text = Txt(a);
            if (term["axis"] is JsonArray v) { AxisXBox.Text = Txt(v[0]); AxisYBox.Text = Txt(v[1]); AxisZBox.Text = Txt(v[2]); }
            string[] cells;
            try { cells = MaterialEditing.ShapeCells(term, _lattice); }
            catch (StackFormatException) { cells = Enumerable.Repeat("", 36).ToArray(); }
            for (int k = 0; k < 36; ++k) _shapeCells[k].Text = cells[k] == "0" ? "" : cells[k];
            ShapePanels(fromDirection: false);
        }
        finally { _showingMaterial = false; }
    }

    private static string Txt(JsonNode? n) => MaterialEditing.AsNumber(n) is { } d ? d.ToString("R", CultureInfo.InvariantCulture) : n?.ToJsonString() ?? "";

    /// <summary>One field per parameter of the law: a box for a number or word, a block of lines for a list.</summary>
    private void LawFields(JsonObject? law)
    {
        LawFieldsPanel.Children.Clear();
        _lawBoxes.Clear();
        if (law is null) return;
        foreach (var f in MaterialEditing.Fields(law))
        {
            var box = new TextBox { Text = f.Text, Name = $"Law_{f.Key}", Width = f.IsRows ? 420 : 160, HorizontalAlignment = HorizontalAlignment.Left };
            if (f.IsRows)
            {
                box.AcceptsReturn = true;
                box.TextWrapping = TextWrapping.NoWrap;
                box.MinHeight = 60;
                box.MaxHeight = 240;
                box.FontFamily = Mono;
                box.FontSize = 12;
            }
            var label = new TextBlock { Text = f.Title, Width = f.IsRows ? double.NaN : 150, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Control row = f.IsRows
                ? new StackPanel { Spacing = 2, Children = { label, box } }
                : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, box } };
            if (f.Key == "unit")
                ((StackPanel)row).Children.Add(new TextBlock { Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center, Text = "nm, um, mm, cm, m, GHz, THz, cm-1, eV" });
            LawFieldsPanel.Children.Add(row);
            _lawBoxes.Add((f, box));
        }
    }

    private void BuildShapeGrid()
    {
        for (int i = 0; i < 7; ++i)
        {
            ShapeGrid.ColumnDefinitions.Add(new ColumnDefinition(i == 0 ? GridLength.Auto : new GridLength(76)));
            ShapeGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        for (int i = 0; i < 6; ++i)
        {
            var head = new TextBlock { Text = FieldNames[i], HorizontalAlignment = HorizontalAlignment.Center, Margin = new(2) };
            Grid.SetColumn(head, i + 1);
            ShapeGrid.Children.Add(head);
            var side = new TextBlock { Text = FieldNames[i], VerticalAlignment = VerticalAlignment.Center, Margin = new(2, 2, 6, 2) };
            Grid.SetRow(side, i + 1);
            ShapeGrid.Children.Add(side);
        }
        for (int k = 0; k < 36; ++k)
        {
            int r = k / 6, c = k % 6;
            var box = new TextBox
            {
                Name = $"Shape{r}{c}", FontFamily = Mono, FontSize = 12, Margin = new(1),
                /* The four 3×3 blocks shaded apart: ε, ρ, ρ′, μ. */
                Background = (r < 3) == (c < 3) ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF8)),
            };
            Grid.SetRow(box, r + 1);
            Grid.SetColumn(box, c + 1);
            ShapeGrid.Children.Add(box);
            _shapeCells[k] = box;
        }
    }

    /// <summary>Shows the panel for the shape mode; turning a direction into a matrix fills the cells from it.</summary>
    private void ShapePanels(bool fromDirection)
    {
        int mode = Math.Max(0, ShapeModeBox.SelectedIndex);
        DirectionAnglesPanel.IsVisible = mode == 0;
        DirectionVectorPanel.IsVisible = mode == 1;
        DirectionCrystalPanel.IsVisible = mode == 3;
        DirectionHint.IsVisible = mode != 2;
        ShapeMatrixPanel.IsVisible = mode == 2;
        if (fromDirection && mode == 2)
        {
            /* Whatever direction the boxes give, as cells: the user can then add to it. */
            try
            {
                var probe = new JsonObject();
                foreach (var (k, v) in DirectionShape()) probe[k] = v!.DeepClone();
                var cells = MaterialEditing.ShapeCells(probe, _pendingLattice ?? _lattice);
                for (int k = 0; k < 36; ++k) _shapeCells[k].Text = cells[k] == "0" ? "" : cells[k];
            }
            catch (Exception e) when (e is FormatException or StackFormatException) { }
        }
    }

    private JsonObject DirectionShape()
    {
        double V(TextBox b, string what) => double.TryParse((b.Text ?? "").Trim(), NumberStyles.Float, Inv, out var v) && double.IsFinite(v)
            ? v : throw new FormatException($"{what}: '{b.Text}' is not a number");
        if (ShapeModeBox.SelectedIndex == 3)
        {
            double u = V(UvwUBox, "u"), v = V(UvwVBox, "v"), w = V(UvwWBox, "w");
            if (u == 0 && v == 0 && w == 0) throw new FormatException("[u v w] must not be zero");
            /* The lattice typed beside it is the material's, written with the term. */
            var lat = new JsonObject
            {
                ["a"] = V(LatticeABox, "a"), ["b"] = V(LatticeBBox, "b"), ["c"] = V(LatticeCBox, "c"),
                ["alpha"] = V(LatticeAlphaBox, "α"), ["beta"] = V(LatticeBetaBox, "β"), ["gamma"] = V(LatticeGammaBox, "γ"),
            };
            Lattice.Parse(lat, "lattice");
            _pendingLattice = lat;
            return new JsonObject { ["axis_uvw"] = new JsonArray(u, v, w) };
        }
        if (ShapeModeBox.SelectedIndex == 1)
        {
            double x = V(AxisXBox, "x"), y = V(AxisYBox, "y"), z = V(AxisZBox, "z");
            if (x == 0 && y == 0 && z == 0) throw new FormatException("the direction must not be zero");
            return new JsonObject { ["axis"] = new JsonArray(x, y, z) };
        }
        double a = V(AxisAngleBox, "angle"), el = V(AxisElevationBox, "elevation");
        return new JsonObject { ["axis_deg"] = el == 0 ? JsonValue.Create(a) : new JsonArray(a, el) };
    }

    /// <summary>The term the editor shows, or a FormatException naming the field that is wrong.</summary>
    private JsonObject BuildTerm()
    {
        var t = new JsonObject();
        var label = (TermLabelBox.Text ?? "").Trim();
        if (label.Length > 0) t["label"] = label;
        if (TermLawBox.SelectedIndex > 0)
        {
            foreach (var (f, box) in _lawBoxes) f.Text = box.Text ?? "";
            t[MaterialEditing.LawChoices[TermLawBox.SelectedIndex]] = MaterialEditing.FromFields(_lawBoxes.Select(p => p.Field));
        }
        var shape = ShapeModeBox.SelectedIndex == 2
            ? MaterialEditing.ShapeFromCells(_shapeCells.Select(b => b.Text ?? "").ToArray())
            : DirectionShape();
        foreach (var (k, v) in shape) t[k] = v!.DeepClone();
        return t;
    }

    // ---- response ------------------------------------------------------------------------------

    private void DefaultResponseRange()
    {
        var unit = _ws.Model.SpectralUnit;
        ResponseUnit.Text = ResponseAtUnit.Text = unit;
        try
        {
            var v = StackDocument.Values(SweepText.Parse(_ws.Model.Spectral, "spectral"), "spectral");
            double lo = v.Min(), hi = v.Max();
            if (lo == hi) { lo *= 0.6; hi *= 1.4; }
            ResponseFromBox.Text = lo.ToString("G6", Inv);
            ResponseToBox.Text = hi.ToString("G6", Inv);
            ResponseAtBox.Text = v[0].ToString("G6", Inv);
        }
        catch (StackFormatException) { }
    }

    private void PlotResponse()
    {
        if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat) return;
        var unit = _ws.Model.SpectralUnit;
        ResponseUnit.Text = ResponseAtUnit.Text = unit;
        try
        {
            double V(TextBox b, string what) => double.TryParse((b.Text ?? "").Trim(), NumberStyles.Float, Inv, out var v) && v > 0 && double.IsFinite(v)
                ? v : throw new FormatException($"{what}: '{b.Text}' is not a positive number");
            double from = V(ResponseFromBox, "from"), to = V(ResponseToBox, "to");
            int points = (int)Math.Clamp(V(ResponsePointsBox, "points"), 2, 20000);
            var material = MaterialParser.Parse(mat.Spec, $"materials.{name}");
            var x = MaterialResponse.Grid(from, to, points);
            var q = (ResponseQuantity)Math.Max(0, ResponseQuantityBox.SelectedIndex);
            ResponseRowBox.IsVisible = ResponseColBox.IsVisible = q == ResponseQuantity.Element;
            ResponsePlot.XTitle = unit is "cm-1" ? "wavenumber (cm⁻¹)" : unit is "eV" ? "photon energy (eV)" : unit is "GHz" or "THz" ? $"frequency ({unit})" : $"wavelength ({unit})";
            ResponsePlot.YTitle = q switch { ResponseQuantity.PrincipalIndex => "n", ResponseQuantity.Element => "M element", _ => "angle (°)" };
            ResponsePlot.Series = MaterialResponse.Curves(material, x, unit, q, Math.Max(0, ResponseRowBox.SelectedIndex), Math.Max(0, ResponseColBox.SelectedIndex));
            ResponseChecks.Text = MaterialResponse.Checks(material, x, unit);
            ResponseChecks.Foreground = ResponseChecks.Text.Contains("NOT PASSIVE") ? Brushes.Firebrick : Brushes.Black;
            try { ResponseMatrix.Text = MaterialResponse.MatrixText(material, V(ResponseAtBox, "M at"), unit); }
            catch (StackFormatException e) { ResponseMatrix.Text = e.Message; }
        }
        catch (Exception e) when (e is FormatException or StackFormatException)
        {
            ResponseChecks.Text = e.Message;
            ResponseChecks.Foreground = Brushes.Firebrick;
            ResponsePlot.Series = null;
        }
    }
}
