// Dialogs.cs — the small dialogs, built in code: a question with buttons, the layer properties, the
// library, the samples and coating import. Each returns what it decided and changes nothing itself;
// the window applies the result through the workspace, so it is one undoable edit.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Gui;

internal static class Dialogs
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A question; returns the index of the button pressed, or the last one when the window is closed.</summary>
    public static async Task<int> Ask(Window owner, string title, string message, params string[] buttons)
    {
        var w = new Window
        {
            Title = title, Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Length; ++i)
        {
            int k = i;
            var b = new Button { Name = $"Answer{i}", Content = buttons[i], IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            b.Click += (_, _) => w.Close(k);
            row.Children.Add(b);
        }
        w.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, row },
        };
        var result = await w.ShowDialog<int?>(owner);
        return result ?? buttons.Length - 1;
    }

    public static TextBlock Label(string text, double width = 0) => new()
    {
        Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        Width = width > 0 ? width : double.NaN,
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#666")),
    };

    public static TextBox Number(double value, double width = 80) => new()
    {
        Text = value.ToString("0.######", Inv), Width = width, TextAlignment = TextAlignment.Right,
    };

    public static double Read(TextBox box, string what) =>
        double.TryParse((box.Text ?? "").Trim().Replace("°", ""), NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v
            : throw new StackFormatException($"{what}: '{box.Text}' is not a number");

    public static StackPanel Row(params Control[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var c in items) p.Children.Add(c);
        return p;
    }

    public static StackPanel Buttons(Window w, Func<bool> ok)
    {
        var okButton = new Button { Name = "OkButton", Content = "OK", IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        okButton.Click += (_, _) => ok();
        cancel.Click += (_, _) => w.Close(null);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { okButton, cancel } };
    }
}

/// <summary>
/// One layer: its material and thickness, its ORIENTATION, and its twist. The orientation is the
/// Euler angles of contract Section 1 (R = Rz(φ) Rx(θ) Rz(ψ)); for a uniaxial material it can also
/// be set by where its optic axis points, which fills in the angles.
/// </summary>
public sealed class LayerPropertiesDialog : Window
{
    public LayerPropertiesDialog(StackModel model, int row)
    {
        var layer = model.Layers[row].Clone();
        string lu = model.LengthUnit == "um" ? "µm" : model.LengthUnit;
        Title = $"Layer {row + 1}";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var material = new ComboBox { Name = "MaterialBox", ItemsSource = model.Materials.Select(m => m.Name).ToList(), SelectedItem = layer.Material, MinWidth = 200 };
        var thickness = Dialogs.Number(layer.Thickness, 110); thickness.Name = "ThicknessBox";
        var phi = Dialogs.Number(layer.EulerDeg[0], 70); phi.Name = "PhiBox";
        var theta = Dialogs.Number(layer.EulerDeg[1], 70); theta.Name = "ThetaBox";
        var psi = Dialogs.Number(layer.EulerDeg[2], 70);
        var (t0, a0) = MaterialMatrix.AxisDirection(layer.EulerDeg[0], layer.EulerDeg[1], layer.EulerDeg[2]);
        var tilt = Dialogs.Number(t0, 70); tilt.Name = "TiltBox";
        var azimuth = Dialogs.Number(a0, 70); azimuth.Name = "AzimuthBox";
        var setAxis = new Button { Name = "SetAxisButton", Content = "Set the angles" };
        var axisNote = Dialogs.Hint("");

        var twisted = new CheckBox { Name = "TwistedBox", Content = "Twisted about z through the layer (a cholesteric, or any helix)", IsChecked = layer.Twist is not null };
        var tw = layer.Twist ?? new TwistModel { Pitch = Math.Max(layer.Thickness, 1e-9), Handedness = "right" };
        var pitch = Dialogs.Number(tw.Pitch, 110); pitch.Name = "PitchBox";
        var hand = new ComboBox { ItemsSource = new[] { "right", "left" }, SelectedItem = tw.Handedness, Width = 90 };
        var start = Dialogs.Number(tw.StartDeg, 70);
        var countMode = new ComboBox { ItemsSource = new[] { "sublayers per pitch", "sublayers in all" }, SelectedIndex = tw.Sublayers is null ? 0 : 1, Width = 170 };
        var count = Dialogs.Number(tw.Sublayers ?? tw.SublayersPerPitch ?? 64, 80);
        var twistPanel = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(20, 0, 0, 0),
            Children =
            {
                Dialogs.Row(Dialogs.Label("Pitch (one full turn)", 150), pitch, Dialogs.Label(lu), Dialogs.Label("  handedness"), hand),
                Dialogs.Row(Dialogs.Label("Starting angle", 150), start, Dialogs.Label("°, added to φ at the layer's incident side")),
                Dialogs.Row(Dialogs.Label("Evaluated as", 150), count, countMode),
                Dialogs.Hint("Each sublayer is uniform, at the angle of its midpoint; the status line reports how much doubling them changes R, T and A."),
            },
        };
        twistPanel.IsEnabled = twisted.IsChecked == true;
        twisted.IsCheckedChanged += (_, _) => twistPanel.IsEnabled = twisted.IsChecked == true;

        void ShowAxis()
        {
            try
            {
                var (t, a) = MaterialMatrix.AxisDirection(Dialogs.Read(phi, "φ"), Dialogs.Read(theta, "θ"), Dialogs.Read(psi, "ψ"));
                axisNote.Text = $"The material's z axis (a uniaxial optic axis) points {t.ToString("0.###", CultureInfo.InvariantCulture)}° from the normal, at azimuth {a.ToString("0.###", CultureInfo.InvariantCulture)}° from x.";
            }
            catch (StackFormatException e) { axisNote.Text = e.Message; }
        }
        foreach (var b in new[] { phi, theta, psi }) b.TextChanged += (_, _) => ShowAxis();
        setAxis.Click += (_, _) =>
        {
            try
            {
                var e = MaterialMatrix.EulerForAxis(Dialogs.Read(tilt, "tilt"), Dialogs.Read(azimuth, "azimuth"));
                phi.Text = e[0].ToString("0.######", CultureInfo.InvariantCulture);
                theta.Text = e[1].ToString("0.######", CultureInfo.InvariantCulture);
                psi.Text = "0";
            }
            catch (StackFormatException ex) { axisNote.Text = ex.Message; }
        };
        ShowAxis();

        var error = new TextBlock { Name = "ErrorText", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                Dialogs.Row(Dialogs.Label("Material", 110), material),
                Dialogs.Row(Dialogs.Label("Thickness", 110), thickness, Dialogs.Label(lu)),
                new Separator { Margin = new Thickness(0, 4) },
                new TextBlock { Text = "Orientation", FontWeight = FontWeight.SemiBold },
                Dialogs.Row(Dialogs.Label("Euler angles", 110), Dialogs.Label("φ"), phi, Dialogs.Label(" θ"), theta, Dialogs.Label(" ψ"), psi, Dialogs.Label("°")),
                Dialogs.Hint("R = Rz(φ) Rx(θ) Rz(ψ) turns the material from its own axes into the stack's (z the normal, into the stack; x in the plane of incidence at azimuth 0)."),
                Dialogs.Row(Dialogs.Label("Or point the axis", 110), Dialogs.Label("tilt"), tilt, Dialogs.Label("° azimuth"), azimuth, Dialogs.Label("°"), setAxis),
                axisNote,
                new Separator { Margin = new Thickness(0, 4) },
                twisted,
                twistPanel,
                error,
                Dialogs.Buttons(this, Ok),
            },
        };

        bool Ok()
        {
            try
            {
                layer.Material = material.SelectedItem as string ?? throw new StackFormatException("choose a material");
                layer.Thickness = Dialogs.Read(thickness, "thickness");
                if (!(layer.Thickness >= 0)) throw new StackFormatException("a thickness must be zero or positive");
                layer.EulerDeg = [Dialogs.Read(phi, "φ"), Dialogs.Read(theta, "θ"), Dialogs.Read(psi, "ψ")];
                if (twisted.IsChecked == true)
                {
                    var p = Dialogs.Read(pitch, "pitch");
                    if (!(p > 0)) throw new StackFormatException("the pitch must be positive");
                    if (!(layer.Thickness > 0)) throw new StackFormatException("a twisted layer needs a positive thickness");
                    var n = Dialogs.Read(count, "sublayers");
                    if (!(n >= 1)) throw new StackFormatException("at least one sublayer");
                    layer.Twist = new TwistModel
                    {
                        Pitch = p, Handedness = hand.SelectedItem as string ?? "right", StartDeg = Dialogs.Read(start, "starting angle"),
                        Sublayers = countMode.SelectedIndex == 1 ? (int)Math.Round(n) : null,
                        SublayersPerPitch = countMode.SelectedIndex == 0 ? n : null,
                    };
                }
                else layer.Twist = null;
                Close(layer);
                return true;
            }
            catch (StackFormatException e) { error.Text = e.Message; return false; }
        }
    }
}

/// <summary>
/// Materials from the shipped databases, the glass catalogues and the user's own file
/// (materials/README.md). Returns the entries chosen; the window adds them to the stack.
/// </summary>
public sealed class LibraryDialog : Window
{
    public LibraryDialog(double referenceNm, string referenceText)
    {
        Title = "Add materials from a library";
        Width = 760;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var collections = MaterialLibrary.Collections();
        var collection = new ComboBox { Name = "CollectionBox", ItemsSource = collections, SelectedIndex = 0, Width = 260 };
        var search = new TextBox { Name = "SearchBox", Watermark = "search names and sources", Width = 300 };
        var list = new ListBox { Name = "EntryList", SelectionMode = SelectionMode.Multiple };
        var detail = new SelectableTextBlock { Name = "EntryDetail", TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { Name = "ErrorText", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        IReadOnlyList<LibraryEntry> entries = [];

        void Load()
        {
            try { entries = MaterialLibrary.Entries(collection.SelectedItem as string ?? collections[0]); error.Text = ""; }
            catch (Exception e) when (e is FormatException or IOException) { entries = []; error.Text = e.Message; }
            Filter();
        }
        void Filter()
        {
            var q = (search.Text ?? "").Trim();
            list.ItemsSource = entries
                .Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Source.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(e => new Item(e)).ToList();
        }
        collection.SelectionChanged += (_, _) => Load();
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItems?.OfType<Item>().LastOrDefault() is not { } it) { detail.Text = ""; return; }
            string value;
            try
            {
                var m = MaterialParser.Parse(it.Entry.Spec, it.Entry.Name).At(referenceNm, 0);
                System.Numerics.Complex n0 = System.Numerics.Complex.Sqrt(m[0, 0]), n2 = System.Numerics.Complex.Sqrt(m[2, 2]);
                value = n0 == n2 ? $"n + ik = {n0.Real:G6} + {Math.Abs(n0.Imaginary):G4}i" : $"nₒ = {n0.Real:G6}, nₑ = {n2.Real:G6}";
                value += $" at {referenceText}.";
            }
            catch (StackFormatException e) { value = $"At {referenceText}: {e.Message}"; }
            detail.Text = $"{it.Entry.Name}\n{value}\n{it.Entry.Source}";
        };
        Load();

        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                Top(Dialogs.Row(Dialogs.Label("Library", 60), collection, Dialogs.Label("", 12), search)),
                Top(Dialogs.Hint("A database material is copied into the stack, which then owns it. A catalogue glass is named and always read from the catalogue. Select several with Ctrl or Shift.")),
                Bottom(Dialogs.Buttons(this, Ok)),
                Bottom(error),
                Bottom(new Border { Child = detail, Padding = new Thickness(0, 8), MinHeight = 80 }),
                list,
            },
        };

        bool Ok()
        {
            var chosen = list.SelectedItems?.OfType<Item>().Select(i => i.Entry).ToList() ?? [];
            if (chosen.Count == 0) { error.Text = "Select a material first."; return false; }
            Close(chosen);
            return true;
        }
    }

    private static Control Top(Control c) { DockPanel.SetDock(c, Dock.Top); c.Margin = new Thickness(0, 0, 0, 8); return c; }
    private static Control Bottom(Control c) { DockPanel.SetDock(c, Dock.Bottom); return c; }

    private sealed record Item(LibraryEntry Entry)
    {
        public override string ToString() =>
            Entry.Source.Length == 0 ? Entry.Name : $"{Entry.Name}   —   {(Entry.Source.Length > 90 ? Entry.Source[..90] + "…" : Entry.Source)}";
    }
}

/// <summary>
/// File > Browse samples: every literature sample with its title, source and notes, searchable.
/// Returns the sample chosen; the window opens it as a copy.
/// </summary>
public sealed class SampleBrowser : Window
{
    public SampleBrowser(IReadOnlyList<SampleInfo> samples)
    {
        Title = "Samples from the literature";
        Width = 900;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var search = new TextBox { Name = "SampleSearchBox", Watermark = "search titles, sources and notes (e.g. Passler, cholesteric, ferrite)" };
        var list = new ListBox { Name = "SampleList", Width = 330 };
        var title = new TextBlock { Name = "SampleTitle", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        var source = new SelectableTextBlock { Name = "SampleSource", TextWrapping = TextWrapping.Wrap };
        var contents = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#444")) };
        var notes = new SelectableTextBlock { Name = "SampleNotes", TextWrapping = TextWrapping.Wrap };
        void Filter()
        {
            list.ItemsSource = SampleCatalog.Search(samples, search.Text ?? "").Select(s => new Item(s)).ToList();
            if (list.ItemCount > 0) list.SelectedIndex = 0;
        }
        list.SelectionChanged += (_, _) =>
        {
            var s = (list.SelectedItem as Item)?.Info;
            title.Text = s?.Title ?? "";
            source.Text = s is null ? "" : "Source: " + s.Source;
            contents.Text = s?.Contents ?? "";
            notes.Text = s?.Notes ?? "";
        };
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is Item it) Close(it.Info); };
        search.TextChanged += (_, _) => Filter();
        Filter();

        var detail = new ScrollViewer
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = new StackPanel { Spacing = 8, Children = { title, source, contents, new TextBlock { Text = "Notes", FontWeight = FontWeight.SemiBold }, notes } },
        };
        var body = new DockPanel();
        DockPanel.SetDock(list, Avalonia.Controls.Dock.Left);
        body.Children.Add(list);
        body.Children.Add(detail);

        var top = new StackPanel { Spacing = 6, Children = { search, Dialogs.Hint("samples/README.md says how closely each one is reproduced. A sample opens as a copy; Save as… keeps your changes.") } };
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        var buttons = Dialogs.Buttons(this, () => { if (list.SelectedItem is Item it) { Close(it.Info); return true; } return false; });
        ((Button)buttons.Children[0]).Content = "Open";
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        top.Margin = new Thickness(0, 0, 0, 8);
        Content = new DockPanel { Margin = new Thickness(16), Children = { top, buttons, body } };
    }

    private sealed record Item(SampleInfo Info)
    {
        public override string ToString() => Info.Entry.Name;
    }
}

/// <summary>What the coating dialog decided: the coating read, and whether to take its media.</summary>
public sealed record CoatingChoice(ImportedCoating Coating, bool TakeMedia);

/// <summary>
/// A coating from an OpticStudio coating file (COATING.DAT and the like) or an Optiland file (a
/// coating alone, or a lens file with coated surfaces), chosen from the file's list. A ZEMAX
/// coating in waves asks for the wavelength they are counted at, unless the file says; an Optiland
/// coating that gives its media offers them. Returns the choice; the window applies it.
/// </summary>
public sealed class CoatingImportDialog : Window
{
    public CoatingImportDialog(byte[] bytes, string fileName, double? firstWavelengthNm, (double Lo, double Hi) gridUm)
    {
        Title = $"Import a coating from {fileName}";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        string text = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿', ' ', '\t', '\r', '\n');
        bool optiland = text.StartsWith('{');
        var list = new ListBox { Name = "CoatingList", Height = 200 };
        var waves = new TextBox
        {
            Name = "WavelengthBox", Width = 90, TextAlignment = TextAlignment.Right,
            Text = (firstWavelengthNm ?? 550).ToString("0.###", CultureInfo.InvariantCulture),
        };
        var wavesRow = Dialogs.Row(Dialogs.Label("Thicknesses are in waves, counted at"), waves, Dialogs.Label("nm"));
        var media = new CheckBox { Name = "MediaBox", IsChecked = true };
        var detail = Dialogs.Hint("");
        var error = new TextBlock { Name = "ErrorText", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        IReadOnlyList<CoatingInFile> found = [];
        try { found = optiland ? OptilandCoatingReader.List(text, gridUm) : ZemaxCoatingReader.List(bytes); }
        catch (StackFormatException e) { error.Text = e.Message; }
        list.ItemsSource = found.Select((c, i) => new Item(c, i)).ToList();
        if (found.Count == 0 && string.IsNullOrEmpty(error.Text)) error.Text = "The file holds no coating made of layers.";

        void Show()
        {
            error.Text = "";
            wavesRow.IsVisible = media.IsVisible = false;
            detail.Text = "";
            if (list.SelectedItem is not Item it) return;
            if (it.Coating.Problem is { } why) { error.Text = $"{it.Coating.Name} cannot be imported: it {why}."; return; }
            try
            {
                if (optiland)
                {
                    var c = OptilandCoatingReader.Read(text, it.Index, gridUm);
                    if (c.Incident is not null || c.Exit is not null)
                    {
                        media.Content = $"Also use the coating's media: {c.Incident?.Name ?? "(the stack's own)"} and {c.Exit?.Name ?? "(the stack's own)"}";
                        media.IsVisible = true;
                    }
                    detail.Text = string.Join(". ", c.Notes.Select(n => char.ToUpper(n[0]) + n[1..]));
                }
                else
                {
                    var (inWaves, stated) = ZemaxCoatingReader.Waves(bytes, it.Coating.Name);
                    wavesRow.IsVisible = inWaves && stated is null;
                    if (inWaves && stated is { } s)
                        detail.Text = $"Its thicknesses are in waves, counted at {s.ToString("0.###", CultureInfo.InvariantCulture)} nm, which the file says it was designed at.";
                }
            }
            catch (StackFormatException e) { error.Text = e.Message; }
        }
        list.SelectionChanged += (_, _) => Show();
        list.DoubleTapped += (_, _) => Ok();

        var buttons = Dialogs.Buttons(this, Ok);
        ((Button)buttons.Children[0]).Content = "Import";
        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                new TextBlock { Text = optiland ? "Coatings in this Optiland file" : "Coatings in this OpticStudio coating file", FontWeight = FontWeight.SemiBold },
                list, wavesRow, media, detail,
                Dialogs.Hint("The coating replaces the stack's layers. Its materials are added under their names in the file; the spectral sweep and the angles are kept. Undo takes the import back."),
                error, buttons,
            },
        };
        if (found.Count > 0) list.SelectedIndex = Math.Max(0, found.ToList().FindIndex(c => c.Problem is null));
        else Show();

        bool Ok()
        {
            if (list.SelectedItem is not Item it || it.Coating.Problem is not null) return false;
            try
            {
                var c = optiland
                    ? OptilandCoatingReader.Read(text, it.Index, gridUm)
                    : ZemaxCoatingReader.Read(bytes, it.Coating.Name, wavesRow.IsVisible ? Dialogs.Read(waves, "the wavelength") : null);
                Close(new CoatingChoice(c, media.IsVisible && media.IsChecked == true));
                return true;
            }
            catch (StackFormatException e) { error.Text = e.Message; return false; }
        }
    }

    private sealed record Item(CoatingInFile Coating, int Index)
    {
        public override string ToString() =>
            $"{Coating.Name}   ({Coating.Layers} layer{(Coating.Layers == 1 ? "" : "s")}{(Coating.Problem is null ? "" : "; cannot be imported")})";
    }
}
