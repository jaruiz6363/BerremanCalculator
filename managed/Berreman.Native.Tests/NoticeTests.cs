// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// NoticeTests.cs — every source file carries the copyright and licence lines.
//
// The files travel on their own: the installer puts the engine's sources in the OpticStudio
// example's engine folder, away from LICENSE, and a surface's source may be copied into another
// DLL. MIT asks that the notice go with the code, so each file opens with
//   Copyright (c) 2026 Javier Ruiz
//   SPDX-License-Identifier: MIT
// as a comment, before its own description. A new file without them fails here.

namespace Berreman.Native.Tests;

internal static class NoticeTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    public static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CMakeLists.txt"))) root = root.Parent;
        if (root is null) { Check(false, "the source repository (CMakeLists.txt) is found above the test"); return; }

        /* Sources only: build output and Visual Studio's own folders are not ours. */
        string[] skip = ["bin", "obj", "build", ".vs", "out"];
        var files = new[] { "native", "examples", "tests", "managed" }
            .Select(d => Path.Combine(root.FullName, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cpp") || f.EndsWith(".h") || f.EndsWith(".cs"))
            .Where(f => !Path.GetRelativePath(root.FullName, f).Split('\\', '/').Any(skip.Contains))
            .ToList();

        var missing = files.Where(f =>
        {
            var head = File.ReadLines(f).Take(3).ToList();
            return head.Count < 2 || !head[0].Contains("Copyright (c) 2026 Javier Ruiz") || !head[1].Contains("SPDX-License-Identifier: MIT");
        }).Select(f => Path.GetRelativePath(root.FullName, f)).ToList();

        Check(files.Count > 80, $"the sources are found ({files.Count})");
        Check(missing.Count == 0, $"every source file opens with the copyright and SPDX lines (missing: {string.Join(", ", missing)})");
    }
}
