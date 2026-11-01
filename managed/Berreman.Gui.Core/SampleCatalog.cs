// SampleCatalog.cs — the literature samples installed beside the program, for the GUI's list.

using Berreman.Api;

namespace Berreman.Gui.Core;

/// <summary>One sample: its path, and a name for a list ("passler-2017 / sic-otto").</summary>
public sealed record SampleEntry(string Path, string Name);

public static class SampleCatalog
{
    /// <summary>The samples folder beside the program: samples/literature/&lt;paper&gt;/&lt;stack&gt;.berr.</summary>
    public static string DefaultRoot => System.IO.Path.Combine(AppContext.BaseDirectory, "samples");

    public static IReadOnlyList<SampleEntry> List(string root)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.berr", SearchOption.AllDirectories)
            .Select(p =>
            {
                var rel = System.IO.Path.GetRelativePath(root, p).Replace('\\', '/');
                if (rel.StartsWith("literature/", StringComparison.Ordinal)) rel = rel["literature/".Length..];
                return new SampleEntry(p, rel[..^".berr".Length].Replace("/", " / "));
            })
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A sample described for the browser: its title, source and notes, and what it holds.</summary>
    public static SampleInfo Describe(SampleEntry entry)
    {
        var m = StackModel.FromJson(File.ReadAllText(entry.Path));
        string lu = m.LengthUnit == "um" ? "µm" : m.LengthUnit;
        var layers = m.Layers.Count == 0 ? "no layers (a single interface)"
            : m.Layers.Count == 1 ? "1 layer" : $"{m.Layers.Count} layers";
        var sweep = $"{m.Spectral} {m.SpectralUnit}; {(m.AnglesAreXi ? "ξ" : "angle")} {m.Angles}{(m.AnglesAreXi ? "" : "°")}"
                  + (m.Azimuths.Trim().Length > 0 ? $"; azimuth {m.Azimuths}°" : "");
        var contents = $"{m.Incident.Material} | {layers} | {m.Exit.Material}. Materials: {string.Join(", ", m.Materials.Select(x => x.Name))}. "
                     + $"Lengths in {lu}. Sweep: {sweep}.";
        return new SampleInfo(entry, m.Title, m.Source, m.Notes, contents);
    }

    /// <summary>The samples whose name, title, source or notes contain every word of the query.</summary>
    public static IReadOnlyList<SampleInfo> Search(IEnumerable<SampleInfo> samples, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return samples.Where(s => words.All(w =>
            s.Entry.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || s.Title.Contains(w, StringComparison.OrdinalIgnoreCase)
            || s.Source.Contains(w, StringComparison.OrdinalIgnoreCase) || s.Notes.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

/// <summary>One sample, described: what the browser lists and shows.</summary>
public sealed record SampleInfo(SampleEntry Entry, string Title, string Source, string Notes, string Contents);
