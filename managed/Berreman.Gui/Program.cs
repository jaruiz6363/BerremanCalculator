// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// Program.cs — the Avalonia entry point.

using Avalonia;

namespace Berreman.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .LogToTrace();
}
