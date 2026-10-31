// SampleCatalog.cs — the literature samples installed beside the program, for the GUI's list.

namespace Berreman.Gui.Core;

/// <summary>One sample: its path, and a name for a list ("passler-2017 / sic-otto").</summary>
public sealed record SampleEntry(string Path, string Name);

public static class SampleCatalog
{
    /// <summary>The samples folder beside the program: samples/literature/&lt;paper&gt;/&lt;stack&gt;.json.</summary>
    public static string DefaultRoot => System.IO.Path.Combine(AppContext.BaseDirectory, "samples");

    public static IReadOnlyList<SampleEntry> List(string root)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(p =>
            {
                var rel = System.IO.Path.GetRelativePath(root, p).Replace('\\', '/');
                if (rel.StartsWith("literature/", StringComparison.Ordinal)) rel = rel["literature/".Length..];
                return new SampleEntry(p, rel[..^".json".Length].Replace("/", " / "));
            })
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }
}
