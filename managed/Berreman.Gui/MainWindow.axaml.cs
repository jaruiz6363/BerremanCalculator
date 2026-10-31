using Avalonia.Controls;
using Berreman.Api;

namespace Berreman.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var r = EngineStatus.Probe();
        EngineText.Text =
            $"engine: {(r.Loaded ? "loaded" : "not loaded")}\n"
            + $"ABI: native {r.NativeAbi?.ToString() ?? "-"}, expected {r.ManagedAbi}\n"
            + r.Summary;
    }
}
