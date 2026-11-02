// MainWindow.Materials.cs — the Materials tab (docs/material-model.md; the arithmetic is
// Gui.Core/MaterialEditing.cs, which is tested). Two pages:
//   Material  the material as people think of it: the same in every direction, ordinary and
//             extraordinary, or x, y and z, each a constant n, k or one dispersion formula; and
//             a plot of n and k over the stack's range. A material that is more than that says
//             what it is and is edited on Advanced.
//   Advanced  Terms     each law and its 6×6 shape, typed into fields; any material can be shown
//                       this way, and applying a change writes it as terms;
//             Response  n and k, any element of M, the directions of the principal axes, and
//                       whether it is passive and reciprocal;
//             Text      the material as the file writes it.
// A box takes effect when it is left or Enter is pressed in it, and a choice when it is made;
// there is no Apply. Every change goes through the workspace like any other edit, so it is
// checked (what is refused stays in its box, outlined in red), can be undone with Edit > Undo,
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
    private bool _keepPlainEditors;            // an edit made on the material page: its boxes stay
    private bool _keepTermEditor;              // an edit made in the term editor: its boxes stay
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

        // ---- the material page ---------------------------------------------------------------
        PlainAxesBox.SelectionChanged += (_, _) => { if (!_showingMaterial) { ChangePlainAxes(); ApplyPlain(); } };
        MakeSimpleButton.Click += (_, _) => MakeSimple();
        PlainPlot.EmptyText = "No material, or it cannot be evaluated in the stack's range.";
        PlainPlot.YTitle = "n";

        // ---- terms -------------------------------------------------------------------------------
        TermLawBox.ItemsSource = MaterialEditing.LawChoices;
        TermLawBox.SelectionChanged += (_, _) =>
        {
            if (_showingMaterial || TermLawBox.SelectedIndex < 0) return;
            var law = TermLawBox.SelectedIndex == 0 ? null : MaterialEditing.LawChoices[TermLawBox.SelectedIndex];
            LawFields(law is null ? null : MaterialEditing.DefaultLaw(law));
            TermLawFormula.Text = MaterialEditing.LawFormula(law);
            ApplyTerm();
        };
        ShapeModeBox.SelectionChanged += (_, _) =>
        {
            if (_showingMaterial) return;
            ShapePanels(fromDirection: true);
            ApplyTerm();
        };
        BuildShapeGrid();
        foreach (var box in new[] { TermLabelBox, UvwUBox, UvwVBox, UvwWBox, LatticeABox, LatticeBBox, LatticeCBox, LatticeAlphaBox,
                                    LatticeBetaBox, LatticeGammaBox, AxisAngleBox, AxisElevationBox, AxisXBox, AxisYBox, AxisZBox }.Concat(_shapeCells))
            Field(box, "term", ApplyTerm);
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
        }, "A Lorentz oscillator along x is added; give its parameters.");
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
        MaterialTabs.SelectionChanged += (_, e) => { if ((e.Source == MaterialTabs || e.Source == AdvancedTabs) && ShowingResponse && !_showingMaterial) PlotResponse(); };
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
           itself changed, so what is being typed into them is not lost. The sweep is part of what
           is shown, since the plot covers it. */
        var specText = mat is null ? null : mat.Spec.ToJsonString() + "|" + _ws.Model.Spectral + _ws.Model.SpectralUnit;
        if (!changed && specText == _shownSpec) return;
        _shownMaterial = mat?.Name;
        _shownSpec = specText;
        _showingMaterial = true;
        try
        {
            MaterialTabs.IsEnabled = mat is not null;
            if (mat is null) { _terms = null; PlainPlot.Series = null; PlainChecks.Text = ""; return; }

            /* The material page. */
            var plain = MaterialEditing.AsPlain(mat.Spec);
            PlainScroll.IsVisible = PlainPanel.IsVisible = plain is not null;
            NotPlainPanel.IsVisible = plain is null;
            if (plain is null) NotPlainText.Text = "This material is more than one index per direction. " + MaterialEditing.WhyNotPlain(mat.Spec);
            else if (!(_keepPlainEditors && PlainEditorsShow(plain)))
            {
                PlainAxesBox.SelectedIndex = (int)plain.Axes;
                PlainValues(plain);
                Accepted("plain");
            }

            /* Terms. */
            _lattice = mat.Spec["lattice"] as JsonObject;
            try { _terms = (JsonArray)MaterialEditing.AsTerms(mat.Spec)["terms"]!; }
            catch (FormatException e) { _terms = null; TermsHint.Text = e.Message; }
            if (_terms is not null)
                TermsHint.Text = mat.Spec.ContainsKey("terms")
                    ? $"{_terms.Count} terms, added together: M = Σ f(λ) × shape."
                    : "This material is written in a short form, shown here as the terms it stands for. A change made here writes it as terms, with the same numbers.";
            TermList.ItemsSource = _terms?.Select(t => MaterialEditing.Describe(t!.AsObject())).ToList();
            if (changed) _termIndex = 0;
            if (_terms is not null) _termIndex = Math.Clamp(_termIndex, 0, _terms.Count - 1);
            TermList.SelectedIndex = _terms is null ? -1 : _termIndex;
            foreach (var c in new Control[] { AddTermButton, DuplicateTermButton, DeleteTermButton, TermUpButton, TermDownButton, TermEditor })
                c.IsEnabled = _terms is not null;

            if (changed)
            {
                /* A material the page can show opens on it; another keeps the page the user was on. */
                if (plain is not null) MaterialTabs.SelectedIndex = 0;
                DefaultResponseRange();
            }
        }
        finally { _showingMaterial = false; }
        if (!_keepTermEditor) ShowTerm();
        PlotPlain();
        if (ShowingResponse) PlotResponse();
    }

    private bool ShowingResponse => MaterialTabs.SelectedIndex == 1 && AdvancedTabs.SelectedIndex == 1;

    // ---- the material page -----------------------------------------------------------------

    /// <summary>One principal value's editor: its choice of constant or formula, and the boxes for it.</summary>
    private sealed record PlainEditor(ComboBox Choice, TextBox? N, TextBox? K, List<(LawField Field, TextBox Box)> Fields);

    private readonly List<PlainEditor> _plainEditors = [];

    /// <summary>The editors for a plain form: for each principal value, a choice and its fields.</summary>
    private void PlainValues(MaterialEditing.PlainForm f)
    {
        PlainValuesPanel.Children.Clear();
        _plainEditors.Clear();
        var names = MaterialEditing.PlainAxisNames(f.Axes);
        for (int i = 0; i < f.Values.Length; ++i)
        {
            int at = i;
            var group = new StackPanel { Spacing = 4 };
            if (names[i] != "") group.Children.Add(new TextBlock { Text = names[i], FontWeight = FontWeight.SemiBold });
            var choice = new ComboBox
            {
                Name = $"PlainChoice{i}", Width = 300,
                ItemsSource = MaterialEditing.PlainChoices.Select(c => c.Title).ToList(),
                SelectedIndex = Array.FindIndex(MaterialEditing.PlainChoices, c => c.Law == f.Values[i].Law),
            };
            group.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new TextBlock { Text = "Defined by", Width = 150, VerticalAlignment = VerticalAlignment.Center }, choice },
            });
            var formula = new TextBlock { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap, Text = MaterialEditing.PlainFormula(f.Values[i].Law) };
            var fields = new StackPanel { Spacing = 4 };
            group.Children.Add(formula);
            group.Children.Add(fields);
            PlainValuesPanel.Children.Add(group);
            _plainEditors.Add(PlainFields(fields, choice, i, f.Values[i]));
            choice.SelectionChanged += (_, _) =>
            {
                if (_showingMaterial || choice.SelectedIndex < 0) return;
                var law = MaterialEditing.PlainChoices[choice.SelectedIndex].Law;
                formula.Text = MaterialEditing.PlainFormula(law);
                _plainEditors[at] = PlainFields(fields, choice, at, law is null ? ConstantNow(at) : new MaterialEditing.PlainValue(law, MaterialEditing.DefaultLaw(law)));
                ApplyPlain();
            };
        }
    }

    private PlainEditor PlainFields(StackPanel panel, ComboBox choice, int i, MaterialEditing.PlainValue v)
    {
        panel.Children.Clear();
        if (v.Law is not null)
        {
            var boxes = new List<(LawField, TextBox)>();
            LawFields(panel, boxes, v.Parameters, $"Plain{i}_");
            foreach (var (_, box) in boxes) Field(box, "plain", ApplyPlain, enterCommits: !box.AcceptsReturn);
            return new PlainEditor(choice, null, null, boxes);
        }
        var n = new TextBox { Name = $"PlainN{i}", Width = 110, Text = v.N.ToString("R", Inv) };
        var k = new TextBox { Name = $"PlainK{i}", Width = 110, Text = v.K.ToString("R", Inv) };
        Field(n, "plain", ApplyPlain);
        Field(k, "plain", ApplyPlain);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                new TextBlock { Text = "n", Width = 150, VerticalAlignment = VerticalAlignment.Center }, n,
                new TextBlock { Text = "k", Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, k,
            },
        });
        return new PlainEditor(choice, n, k, []);
    }

    /// <summary>
    /// A constant for principal value i: the material's own value there at the first spectral
    /// point, so switching from a formula to a constant starts close to where it was.
    /// </summary>
    private MaterialEditing.PlainValue ConstantNow(int i)
    {
        try
        {
            var doc = _ws.Model.ToDocument();
            var M = doc.Materials[_selectedMaterial!].At(Units.ToWavelengthNm(doc.Spectral[0], doc.SpectralUnit), 100);
            int d = _plainEditors.Count switch { 1 => 0, 2 => i == 0 ? 0 : 2, _ => i };
            var z = Complex.Sqrt(M[d, d]);
            if (z.Imaginary < 0) z = -z;
            return new MaterialEditing.PlainValue(null, null, double.Parse(z.Real.ToString("G6", Inv), Inv), double.Parse(z.Imaginary.ToString("G6", Inv), Inv));
        }
        catch (Exception e) when (e is StackFormatException or KeyNotFoundException) { return new MaterialEditing.PlainValue(null, null); }
    }

    /// <summary>The values the editors show, or a FormatException naming the box that is wrong.</summary>
    private MaterialEditing.PlainValue[] PlainValuesTyped()
    {
        var names = MaterialEditing.PlainAxisNames((MaterialEditing.PlainAxes)(_plainEditors.Count - 1));
        return _plainEditors.Select((e, i) =>
        {
            string where = names[i] == "" ? "" : $"{names[i]}: ";
            var law = MaterialEditing.PlainChoices[Math.Max(0, e.Choice.SelectedIndex)].Law;
            if (law is not null)
            {
                foreach (var (f, box) in e.Fields) f.Text = box.Text ?? "";
                try { return new MaterialEditing.PlainValue(law, MaterialEditing.FromFields(e.Fields.Select(p => p.Field))); }
                catch (FormatException x) { throw new FormatException(where + x.Message); }
            }
            double V(TextBox b, string what) => double.TryParse((b.Text ?? "").Trim(), NumberStyles.Float, Inv, out var v) && double.IsFinite(v) && v >= 0
                ? v : throw new FormatException($"{where}{what}: '{b.Text}' is not a number of 0 or more (k > 0 absorbs)");
            return new MaterialEditing.PlainValue(null, null, V(e.N!, "n"), V(e.K!, "k"));
        }).ToArray();
    }

    /// <summary>
    /// A new choice of directions: the values typed so far are kept where they mean the same
    /// thing (the isotropic value becomes every axis; ordinary becomes x and y).
    /// </summary>
    private void ChangePlainAxes()
    {
        var axes = (MaterialEditing.PlainAxes)Math.Max(0, PlainAxesBox.SelectedIndex);
        MaterialEditing.PlainValue[] now;
        try { now = PlainValuesTyped(); }
        catch (FormatException) { now = _plainEditors.Select(_ => new MaterialEditing.PlainValue(null, null)).ToArray(); }
        var xyz = now.Length switch { 1 => new[] { now[0], now[0], now[0] }, 2 => [now[0], now[0], now[1]], _ => now };
        var values = axes switch { MaterialEditing.PlainAxes.Same => [xyz[0]], MaterialEditing.PlainAxes.Uniaxial => [xyz[0], xyz[2]], _ => xyz };
        _showingMaterial = true;
        try { PlainValues(new MaterialEditing.PlainForm(axes, values)); }
        finally { _showingMaterial = false; }
    }

    /// <summary>The material page's boxes and choices, put in the stack; false if they are refused.</summary>
    private bool ApplyPlain()
    {
        if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat) return true;
        _keepPlainEditors = true;
        try
        {
            return Try(() =>
            {
                var axes = (MaterialEditing.PlainAxes)Math.Max(0, PlainAxesBox.SelectedIndex);
                SetSpec(name, MaterialEditing.FromPlain(new MaterialEditing.PlainForm(axes, PlainValuesTyped()), mat.Spec));
            }, $"'{name}' updated.");
        }
        finally { _keepPlainEditors = false; }
    }

    /// <summary>Whether the editors already show this form: the same directions, and the same choice for each.</summary>
    private bool PlainEditorsShow(MaterialEditing.PlainForm f) =>
        PlainAxesBox.SelectedIndex == (int)f.Axes && _plainEditors.Count == f.Values.Length
        && _plainEditors.Select((e, i) => e.Choice.SelectedIndex >= 0 && MaterialEditing.PlainChoices[e.Choice.SelectedIndex].Law == f.Values[i].Law).All(same => same);

    /// <summary>n and k over the stack's spectral range, with the passivity check under them.</summary>
    private void PlotPlain()
    {
        if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat) return;
        var unit = _ws.Model.SpectralUnit;
        try
        {
            var v = StackDocument.Values(SweepText.Parse(_ws.Model.Spectral, "spectral"), "spectral");
            double lo = v.Min(), hi = v.Max();
            if (lo == hi) { lo *= 0.6; hi *= 1.4; }
            var x = MaterialResponse.Grid(lo, hi, 201);
            var material = MaterialParser.Parse(mat.Spec, $"materials.{name}");
            PlainPlot.XTitle = unit is "cm-1" ? "wavenumber (cm⁻¹)" : unit is "eV" ? "photon energy (eV)" : unit is "GHz" or "THz" ? $"frequency ({unit})" : $"wavelength ({unit})";
            PlainPlot.Series = MaterialEditing.AsPlain(mat.Spec) is { } plain
                ? MaterialResponse.IndexCurves(material, x, unit, plain.Axes)
                : MaterialResponse.Curves(material, x, unit, ResponseQuantity.PrincipalIndex);
            PlainChecks.Text = MaterialResponse.Checks(material, x, unit);
            PlainChecks.Foreground = PlainChecks.Text.Contains("NOT PASSIVE") || PlainChecks.Text.Contains("cannot be evaluated") ? Brushes.Firebrick : Brushes.Black;
        }
        catch (StackFormatException e)
        {
            PlainPlot.Series = null;
            PlainChecks.Text = e.Message;
            PlainChecks.Foreground = Brushes.Firebrick;
        }
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

    /// <summary>The term editor's boxes and choices, put in the material; false if they are refused.</summary>
    private bool ApplyTerm()
    {
        if (_terms is null) return true;
        _keepTermEditor = true;
        try
        {
            return EditTerms(t =>
            {
                t[_termIndex] = BuildTerm();
                return _termIndex;
            }, _selectedMaterial is { } name ? $"'{name}' updated." : null);
        }
        finally { _keepTermEditor = false; }
    }

    /// <summary>One change to the term list, written back as the material's terms; false if it is refused.</summary>
    private bool EditTerms(Func<JsonArray, int> change, string? done)
    {
        if (_selectedMaterial is not { } name || _ws.Model.Material(name) is not { } mat || _terms is null) return true;
        int select = _termIndex;
        bool taken = Try(() =>
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
        return taken;
    }

    private void ShowTerm()
    {
        _showingMaterial = true;
        try
        {
            var term = _terms is not null && _termIndex >= 0 && _termIndex < _terms.Count ? _terms[_termIndex]!.AsObject() : null;
            TermEditor.IsEnabled = term is not null;
            Accepted("term");
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
        LawFields(LawFieldsPanel, _lawBoxes, law, "Law_");
        foreach (var (_, box) in _lawBoxes) Field(box, "term", ApplyTerm, enterCommits: !box.AcceptsReturn);
    }

    private static void LawFields(Panel panel, List<(LawField Field, TextBox Box)> boxes, JsonObject? law, string prefix)
    {
        panel.Children.Clear();
        boxes.Clear();
        if (law is null) return;
        foreach (var f in MaterialEditing.Fields(law))
        {
            var box = new TextBox { Text = f.Text, Name = $"{prefix}{f.Key}", Width = f.IsRows ? 420 : 160, HorizontalAlignment = HorizontalAlignment.Left };
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
            panel.Children.Add(row);
            boxes.Add((f, box));
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
