// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// TestApp.cs — the app builder the headless session uses: Skia behind a headless window, so a
// frame really is rendered, with no display attached.

using Avalonia;
using Avalonia.Headless;
using Berreman.Gui;
using Berreman.Gui.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Berreman.Gui.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UseSkia()
                  .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
