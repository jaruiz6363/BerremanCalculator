// Dialogs.cs — the small dialogs, built in code: a question with buttons, the layer properties and
// the design string. Each returns what it decided and changes nothing itself; the window applies
// the result through the workspace, so it is one undoable edit.

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

public sealed record DesignStringResult(List<LayerModel> Layers, bool Replace);

/// <summary>
/// Input design string: "(HL)8 H" with a material and a physical thickness for each letter
/// (ThinFilmDesigner's notation, with thicknesses instead of quarter waves).
/// </summary>
public sealed class DesignStringDialog : Window
{
    private sealed record Letter(ComboBox Material, TextBox Thickness, StackPanel Row);

    public DesignStringDialog(StackModel model, int selected)
    {
        Title = "Input design string";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        string lu = model.LengthUnit == "um" ? "µm" : model.LengthUnit;
        var names = model.Materials.Select(m => m.Name).ToList();
        var index = names.Select(model.RankingIndex).ToList();
        var text = new TextBox { Name = "StringBox", Text = "(HL)4 H", Watermark = "e.g. (HL)8 H, or 0.5L (HL)6 H 0.5L" };
        var end = new ComboBox { ItemsSource = new[] { "the incident medium", "the exit medium" }, SelectedIndex = 0, Width = 180 };
        var replace = new ComboBox
        {
            Name = "ReplaceBox",
            ItemsSource = new[] { "replace every layer", selected >= 0 ? $"insert after layer {selected + 1}" : "add after the last layer" },
            SelectedIndex = model.Layers.Count == 0 ? 0 : 1, Width = 200,
        };
        var letters = new StackPanel { Spacing = 4 };
        var rows = new Dictionary<char, Letter>();
        var preview = Dialogs.Hint("");
        var error = new TextBlock { Name = "ErrorText", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        void Update()
        {
            var used = DesignString.Letters(text.Text);
            foreach (var c in used.Where(c => !rows.ContainsKey(c)))
            {
                var mat = new ComboBox { Name = $"Material{c}", ItemsSource = names, SelectedItem = DesignString.Guess(names, index, c), MinWidth = 200 };
                var t = Dialogs.Number(100, 90);
                t.Name = $"Thickness{c}";
                rows[c] = new Letter(mat, t, Dialogs.Row(Dialogs.Label($"{c} is", 40), mat, t, Dialogs.Label(lu)));
            }
            letters.Children.Clear();
            foreach (var c in used) letters.Children.Add(rows[c].Row);
            try
            {
                preview.Text = DesignString.Describe(DesignString.Expand(text.Text));
                error.Text = "";
            }
            catch (FormatException e) { preview.Text = ""; error.Text = e.Message; }
        }
        text.TextChanged += (_, _) => Update();
        Update();

        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                Dialogs.Row(Dialogs.Label("Design string", 110), new Border { Width = 420, Child = text }),
                preview,
                Dialogs.Row(Dialogs.Label("The leftmost layer is next to", 200), end),
                Dialogs.Row(Dialogs.Label("The layers", 110), replace),
                new TextBlock { Text = "Each letter", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                letters,
                Dialogs.Hint("A letter is one layer of its material and thickness; a number before it multiplies the thickness, so 0.5H is half of H. H is guessed as the highest-index material at the first wavelength of the sweep, L the lowest and M the middle one; any other letter as the first material whose name starts with it."),
                error,
                Dialogs.Buttons(this, Ok),
            },
        };

        bool Ok()
        {
            try
            {
                var map = new Dictionary<char, (string, double)>();
                foreach (var c in DesignString.Letters(text.Text))
                    map[c] = (rows[c].Material.SelectedItem as string ?? throw new StackFormatException($"choose a material for {c}"),
                              Dialogs.Read(rows[c].Thickness, $"the thickness of {c}"));
                var layers = model.LayersFromString(text.Text ?? "", map, firstNextToExit: end.SelectedIndex == 1);
                Close(new DesignStringResult(layers, replace.SelectedIndex == 0));
                return true;
            }
            catch (Exception e) when (e is StackFormatException or FormatException) { error.Text = e.Message; return false; }
        }
    }
}
