// StackDocument.cs — the stack file: materials, layers, units and the sweep to evaluate.
//
// JSON, read by hand from JsonNode so that every error names the place in the file it came from.
// The format is in docs/stack-format.md. Version 1.
//
// MATERIALS ARE CONSTANTS in version 1 (the same at every wavelength). That covers the closed-form
// examples, Mansuripur (1990) and the microwave stacks. Dispersive materials come with the
// examples that need them, and a file that asks for one is refused, not approximated.

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Berreman.Api;

public sealed class StackLayer
{
    public required string Material { get; init; }
    public required double ThicknessNm { get; init; }
    public double[] EulerDeg { get; init; } = [0, 0, 0];
    /// <summary>A helical twist about z through the layer (a cholesteric), or null.</summary>
    public LayerTwist? Twist { get; init; }
}

/// <summary>
/// A twist about z: at depth z into the layer the material, already oriented by its Euler angles,
/// is turned further about z by φ(z) = φ0 + s·360°·z/P, with s = +1 for a right-handed helix
/// (φ increasing with z, as Berreman's Eq. 61) and −1 for a left-handed one. P is the pitch: one
/// full turn. The layer is evaluated as <see cref="Sublayers"/> uniform sublayers, each at the
/// angle of its midpoint (contract, Section 2).
/// </summary>
public sealed record LayerTwist(double PitchNm, int Sense, double StartDeg, int Sublayers)
{
    public double AngleDeg(double zNm) => StartDeg + Sense * 360.0 * zNm / PitchNm;
}

public sealed class StackOuter
{
    public required string Material { get; init; }
    public double[] EulerDeg { get; init; } = [0, 0, 0];
}

public sealed class StackDocument
{
    public string Title { get; init; } = "";
    public string Source { get; init; } = "";
    public string Notes { get; init; } = "";
    public string LengthUnit { get; init; } = "nm";
    public string SpectralUnit { get; init; } = "nm";

    public required Dictionary<string, Material> Materials { get; init; }
    public required StackOuter Incident { get; init; }
    public required StackOuter Exit { get; init; }
    public required List<StackLayer> Layers { get; init; }

    /// <summary>Spectral values in <see cref="SpectralUnit"/>.</summary>
    public required double[] Spectral { get; init; }
    public double[] AzimuthsDeg { get; init; } = [0];
    /// <summary>Angles of incidence in the incident medium; null when ξ is given instead.</summary>
    public double[]? AnglesDeg { get; init; }
    public double[]? Xi { get; init; }

    public static StackDocument Load(string path)
    {
        try { return Parse(File.ReadAllText(path)); }
        catch (FileNotFoundException) { throw new StackFormatException($"no file at {path}"); }
    }

    public static StackDocument Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new StackFormatException($"not valid JSON: {e.Message}"); }
        if (root is not JsonObject o) throw new StackFormatException("the file must be a JSON object");

        var format = Str(o, "format", "");
        if (format != "berreman-stack") throw new StackFormatException("\"format\" must be \"berreman-stack\"");
        var version = (int)Num(o["version"], "version");
        if (version != 1) throw new StackFormatException($"version {version} is not supported; this program reads version 1");

        var units = o["units"] as JsonObject ?? new JsonObject();
        var lengthUnit = Str(units, "length", "nm");
        var spectralUnit = Str(units, "spectral", "nm");
        double nm = Units.NmPerLength(lengthUnit);
        if (!Units.IsSpectralUnit(spectralUnit)) throw new StackFormatException($"units.spectral: '{spectralUnit}' is not a spectral unit");

        var mats = o["materials"] as JsonObject ?? throw new StackFormatException("\"materials\" is required");
        var materials = new Dictionary<string, Material>();
        foreach (var (name, node) in mats)
            materials[name] = MaterialParser.Parse(node, $"materials.{name}");

        StackOuter Outer(string key)
        {
            var n = o[key] as JsonObject ?? throw new StackFormatException($"\"{key}\" is required");
            var m = Str(n, "material", null, key);
            if (!materials.ContainsKey(m)) throw new StackFormatException($"{key}.material: no material named '{m}'");
            return new StackOuter { Material = m, EulerDeg = Euler(n, key) };
        }

        var layers = new List<StackLayer>();
        if (o["layers"] is JsonArray la)
            for (int i = 0; i < la.Count; ++i)
            {
                var where = $"layers[{i + 1}]";
                var n = la[i] as JsonObject ?? throw new StackFormatException($"{where} must be an object");
                var m = Str(n, "material", null, where);
                if (!materials.ContainsKey(m)) throw new StackFormatException($"{where}.material: no material named '{m}'");
                var t = Num(n["thickness"], $"{where}.thickness");
                if (!(t >= 0) || double.IsInfinity(t)) throw new StackFormatException($"{where}.thickness must be zero or positive");
                if (n["coherent"] is JsonNode c && c.GetValue<bool>() == false)
                    throw new StackFormatException($"{where}: incoherent layers are not implemented yet");
                LayerTwist? twist = null;
                if (n["twist"] is JsonObject tw)
                {
                    var tws = $"{where}.twist";
                    double pitch = Num(tw["pitch"], $"{tws}.pitch") * nm;
                    if (!(pitch > 0)) throw new StackFormatException($"{tws}.pitch must be positive");
                    if (!(t > 0)) throw new StackFormatException($"{where}: a twisted layer needs a positive thickness");
                    var hand = Str(tw, "handedness", null, tws);
                    int sense = hand switch
                    {
                        "right" => 1,
                        "left" => -1,
                        _ => throw new StackFormatException($"{tws}.handedness must be \"right\" or \"left\""),
                    };
                    double start = tw.ContainsKey("start_deg") ? Num(tw["start_deg"], $"{tws}.start_deg") : 0;
                    if (tw.ContainsKey("sublayers") && tw.ContainsKey("sublayers_per_pitch"))
                        throw new StackFormatException($"{tws}: give sublayers or sublayers_per_pitch, not both");
                    int count = tw.ContainsKey("sublayers")
                        ? (int)Num(tw["sublayers"], $"{tws}.sublayers")
                        : (int)Math.Ceiling(t * nm / pitch * (tw.ContainsKey("sublayers_per_pitch") ? Num(tw["sublayers_per_pitch"], $"{tws}.sublayers_per_pitch") : 64));
                    if (count < 1 || count > 1_000_000) throw new StackFormatException($"{tws}: {count} sublayers is not a usable count");
                    twist = new LayerTwist(pitch, sense, start, count);
                }
                layers.Add(new StackLayer { Material = m, ThicknessNm = t * nm, EulerDeg = Euler(n, where), Twist = twist });
            }

        var sweep = o["sweep"] as JsonObject ?? throw new StackFormatException("\"sweep\" is required");
        var spectral = Values(sweep["spectral"], "sweep.spectral");
        foreach (var v in spectral) Units.ToWavelengthNm(v, spectralUnit);
        var azimuths = sweep["azimuth_deg"] is null ? new double[] { 0 } : Values(sweep["azimuth_deg"], "sweep.azimuth_deg");
        double[]? angles = sweep["angle_deg"] is null ? null : Values(sweep["angle_deg"], "sweep.angle_deg");
        double[]? xi = sweep["xi"] is null ? null : Values(sweep["xi"], "sweep.xi");
        if ((angles is null) == (xi is null)) throw new StackFormatException("sweep: give exactly one of \"angle_deg\" and \"xi\"");
        if (angles is not null && angles.Any(a => !(a >= 0 && a < 90)))
            throw new StackFormatException("sweep.angle_deg: angles must be in [0, 90)");

        return new StackDocument
        {
            Title = Str(o, "title", ""),
            Source = Str(o, "source", ""),
            Notes = Str(o, "notes", ""),
            LengthUnit = lengthUnit,
            SpectralUnit = spectralUnit,
            Materials = materials,
            Incident = Outer("incident"),
            Exit = Outer("exit"),
            Layers = layers,
            Spectral = spectral,
            AzimuthsDeg = azimuths,
            AnglesDeg = angles,
            Xi = xi,
        };
    }

    // ---- small readers ------------------------------------------------------------------------

    internal static string Str(JsonObject o, string key, string? fallback, string where = "")
    {
        var n = o[key];
        if (n is null)
            return fallback ?? throw new StackFormatException($"{(where == "" ? "" : where + ".")}{key} is required");
        try { return n.GetValue<string>(); }
        catch (Exception) { throw new StackFormatException($"{(where == "" ? "" : where + ".")}{key} must be a string"); }
    }

    internal static double Num(JsonNode? n, string where)
    {
        if (n is null) throw new StackFormatException($"{where} is required");
        // By its text: a node built in code (StackModel) may hold an int, which GetValue<double>
        // refuses; one read from a file holds a JsonElement. The number is the same.
        if (n.GetValueKind() != JsonValueKind.Number) throw new StackFormatException($"{where} must be a number");
        return double.Parse(n.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double[] Euler(JsonObject n, string where)
    {
        if (n["euler_deg"] is not JsonArray a) return [0, 0, 0];
        if (a.Count != 3) throw new StackFormatException($"{where}.euler_deg must be [phi, theta, psi]");
        return [Num(a[0], $"{where}.euler_deg[1]"), Num(a[1], $"{where}.euler_deg[2]"), Num(a[2], $"{where}.euler_deg[3]")];
    }

    /// <summary>A number, an array of numbers, or {"from", "to", "step"} / {"from", "to", "count"}.</summary>
    public static double[] Values(JsonNode? n, string where)
    {
        switch (n)
        {
            case null: throw new StackFormatException($"{where} is required");
            case JsonValue: return [Num(n, where)];
            case JsonArray a: return a.Select((x, i) => Num(x, $"{where}[{i + 1}]")).ToArray();
            case JsonObject r:
            {
                double from = Num(r["from"], $"{where}.from"), to = Num(r["to"], $"{where}.to");
                int count;
                if (r["count"] is JsonNode c) count = (int)Num(c, $"{where}.count");
                else
                {
                    double step = Num(r["step"], $"{where}.step");
                    if (!(step > 0)) throw new StackFormatException($"{where}.step must be positive");
                    count = (int)Math.Floor(Math.Abs(to - from) / step + 1e-9) + 1;
                }
                if (count < 1 || count > 10_000_000) throw new StackFormatException($"{where}: {count} values is not a usable count");
                if (count == 1) return [from];
                return Enumerable.Range(0, count).Select(i => from + (to - from) * i / (count - 1.0)).ToArray();
            }
            default: throw new StackFormatException($"{where} must be a number, an array or a range");
        }
    }
}

/// <summary>Material definitions in the stack file (docs/stack-format.md, "Materials").</summary>
public static class MaterialParser
{
    public static Material Parse(JsonNode? node, string where)
    {
        if (node is not JsonObject o) throw new StackFormatException($"{where} must be an object");

        /* The general form: a list of terms, each a law times a 6×6 shape. */
        if (o.ContainsKey("terms"))
        {
            var others = o.Select(p => p.Key).Where(k => k != "terms" && k != "lattice" && k != "source" && k != "notes").ToList();
            if (others.Count > 0)
                throw new StackFormatException($"{where}: a material given by \"terms\" takes nothing else beside them ({string.Join(", ", others)}); write a constant as a term with no law");
            if (o["terms"] is not JsonArray list || list.Count == 0)
                throw new StackFormatException($"{where}.terms must be a non-empty array");
            var terms = new List<MaterialTerm>();
            bool hasMu = false, hasEps = false;
            var lattice = o.ContainsKey("lattice") ? Lattice.Parse(o["lattice"], $"{where}.lattice") : null;
            for (int i = 0; i < list.Count; ++i)
            {
                var (term, givesEps, givesMu) = ParseTerm(list[i], $"{where}.terms[{i + 1}]", lattice);
                terms.Add(term);
                hasEps |= givesEps; hasMu |= givesMu;
            }
            if (!hasEps) throw new StackFormatException($"{where}: no term gives ε");
            if (!hasMu) terms.Add(new MaterialTerm(null, MaterialMatrix.FromBlocks(new Complex[3, 3], MaterialMatrix.Scalar(1))));
            return new TermMaterial(terms);
        }

        /* Dispersive: one law for all three principal values, or one per principal axis. */
        if (DispersionKeys.Any(o.ContainsKey) || o.ContainsKey("uniaxial") || o.ContainsKey("biaxial"))
        {
            if (o.ContainsKey("convention"))
                throw new StackFormatException($"{where}: dispersion laws are in exp(-iwt) with positive losses; remove \"convention\"");
            var muD = o.ContainsKey("mu") ? MaterialMatrix.Scalar(Cplx(o["mu"], $"{where}.mu")) : MaterialMatrix.Scalar(1);
            if (o["uniaxial"] is JsonObject u)
            {
                var ord = ParseDispersion(u["ordinary"], $"{where}.uniaxial.ordinary");
                return new DispersiveMaterial(ord, ord, ParseDispersion(u["extraordinary"], $"{where}.uniaxial.extraordinary"), muD);
            }
            if (o["biaxial"] is JsonObject b)
                return new DispersiveMaterial(ParseDispersion(b["x"], $"{where}.biaxial.x"),
                                              ParseDispersion(b["y"], $"{where}.biaxial.y"),
                                              ParseDispersion(b["z"], $"{where}.biaxial.z"), muD);
            var d = ParseDispersion(o, where);
            return new DispersiveMaterial(d, d, d, muD);
        }

        if (o["model"] is not null)
        {
            if (o.ContainsKey("convention"))
                throw new StackFormatException($"{where}: a model's parameters are physical and have no time convention; remove \"convention\"");
            var kind = StackDocument.Str(o, "model", null, where);
            switch (kind)
            {
                case "ferrite":
                {
                    if (o.ContainsKey("mu") || o.ContainsKey("mu_r")) throw new StackFormatException($"{where}: a ferrite's μ comes from the model");
                    var e = Tensor(o, where, "eps", "n", "eps_r", "tan_delta", "no", "ne", "nx", "ny", "nz") ?? MaterialMatrix.Scalar(1);
                    double Req(string k) => StackDocument.Num(o[k], $"{where}.{k}");
                    var gamma = o.ContainsKey("gamma_mhz_per_oe") ? Req("gamma_mhz_per_oe") : 2.8;
                    var alpha = o.ContainsKey("alpha") ? Req("alpha") : 0.0;
                    if (alpha < 0) throw new StackFormatException($"{where}.alpha must not be negative");
                    return new FerriteMaterial(Req("four_pi_ms_gauss"), Req("h0_oe"), alpha, gamma, e);
                }
                case "conductor":
                {
                    var sigma = StackDocument.Num(o["sigma_s_per_m"], $"{where}.sigma_s_per_m");
                    if (!(sigma > 0)) throw new StackFormatException($"{where}.sigma_s_per_m must be positive");
                    var bg = o.ContainsKey("eps_background") ? Cplx(o["eps_background"], $"{where}.eps_background") : Complex.One;
                    return new ConductorMaterial(sigma, bg);
                }
                case "resistive_sheet":
                {
                    var rs = StackDocument.Num(o["ohm_per_square"], $"{where}.ohm_per_square");
                    if (!(rs > 0)) throw new StackFormatException($"{where}.ohm_per_square must be positive");
                    return new ResistiveSheetMaterial(rs);
                }
                default:
                    throw new StackFormatException($"{where}.model: '{kind}' is not a model (ferrite, conductor, resistive_sheet)");
            }
        }

        var eps = Tensor(o, where, "eps", "n", "eps_r", "tan_delta", "no", "ne", "nx", "ny", "nz") ?? MaterialMatrix.Scalar(1);
        var mu = Tensor(o, where, "mu", null, "mu_r", "tan_delta_mu", null, null, null, null, null) ?? MaterialMatrix.Scalar(1);
        var rho = o.ContainsKey("rho") ? Matrix3(o["rho"], $"{where}.rho") : null;
        var rhoPrime = o.ContainsKey("rho_prime") ? Matrix3(o["rho_prime"], $"{where}.rho_prime") : null;

        var m = MaterialMatrix.FromBlocks(eps, mu, rho, rhoPrime);
        var convention = o["convention"] is null ? "exp(-iwt)" : StackDocument.Str(o, "convention", null, where);
        return convention switch
        {
            "exp(-iwt)" => new ConstantMaterial(m),
            "exp(+jwt)" => new ConstantMaterial(m.Conjugated()),
            _ => throw new StackFormatException($"{where}.convention must be \"exp(-iwt)\" or \"exp(+jwt)\""),
        };
    }

    /// <summary>
    /// One of the ways to give ε (or μ). Exactly one may be used:
    ///   eps: complex scalar or 3×3     n: complex index, ε = n²
    ///   eps_r with optional tan_delta: ε = ε′ (1 + i tan δ) in exp(−iωt)
    ///   no, ne: uniaxial indices, axis along z      nx, ny, nz: biaxial indices
    /// </summary>
    private static Complex[,]? Tensor(JsonObject o, string where, string direct, string? index, string real, string tan,
                                      string? no, string? ne, string? nx, string? ny, string? nz)
    {
        var given = new List<string>();
        if (o.ContainsKey(direct)) given.Add(direct);
        if (index is not null && o.ContainsKey(index)) given.Add(index);
        if (o.ContainsKey(real)) given.Add(real);
        if (no is not null && (o.ContainsKey(no) || o.ContainsKey(ne!))) given.Add($"{no}/{ne}");
        if (nx is not null && (o.ContainsKey(nx) || o.ContainsKey(ny!) || o.ContainsKey(nz!))) given.Add($"{nx}/{ny}/{nz}");
        if (given.Count > 1) throw new StackFormatException($"{where}: give only one of {string.Join(", ", given)}");
        if (given.Count == 0)
        {
            if (o.ContainsKey(tan)) throw new StackFormatException($"{where}.{tan} needs {real}");
            return null;
        }

        if (o.ContainsKey(direct))
            return o[direct] is JsonArray a && a.Count == 3 && a[0] is JsonArray r && r.Count == 3
                ? Matrix3(o[direct], $"{where}.{direct}")
                : MaterialMatrix.Scalar(Cplx(o[direct], $"{where}.{direct}"));
        if (index is not null && o.ContainsKey(index))
        {
            var n = Cplx(o[index], $"{where}.{index}");
            return MaterialMatrix.Scalar(n * n);
        }
        if (o.ContainsKey(real))
        {
            var e = StackDocument.Num(o[real], $"{where}.{real}");
            var t = o.ContainsKey(tan) ? StackDocument.Num(o[tan], $"{where}.{tan}") : 0.0;
            return MaterialMatrix.Scalar(new Complex(e, e * t));
        }
        if (no is not null && (o.ContainsKey(no) || o.ContainsKey(ne!)))
        {
            var a = Cplx(o[no], $"{where}.{no}");
            var b = Cplx(o[ne!], $"{where}.{ne}");
            return MaterialMatrix.Diagonal(a * a, a * a, b * b);
        }
        var x = Cplx(o[nx!], $"{where}.{nx}");
        var y = Cplx(o[ny!], $"{where}.{ny}");
        var z = Cplx(o[nz!], $"{where}.{nz}");
        return MaterialMatrix.Diagonal(x * x, y * y, z * z);
    }

    /// <summary>The scalar laws, each a key whose value is the law's parameters.</summary>
    public static readonly string[] DispersionKeys =
        ["sellmeier", "cauchy", "lorentz", "drude", "phonon", "phonon_additive", "table", "glass", "conductivity", "polder"];

    /// <summary>The keys that give a term's shape (docs/stack-format.md, "General materials").</summary>
    public static readonly string[] ShapeKeys = ["eps", "mu", "rho", "rho_prime", "matrix", "axis", "axis_deg", "axis_uvw"];

    /// <summary>One scalar dispersion law: a constant (n or eps), or one of DispersionKeys.</summary>
    private static Dispersion ParseDispersion(JsonNode? node, string where)
    {
        if (node is not JsonObject o) throw new StackFormatException($"{where} must be an object");
        var given = DispersionKeys.Where(o.ContainsKey).Concat(new[] { "n", "eps" }.Where(o.ContainsKey)).ToList();
        if (given.Count != 1) throw new StackFormatException($"{where}: give exactly one of n, eps, {string.Join(", ", DispersionKeys)}");
        var key = given[0];
        var w = $"{where}.{key}";
        if (key == "n") { var n = Cplx(o["n"], w); return new ConstantDispersion(n * n); }
        if (key == "eps") return new ConstantDispersion(Cplx(o["eps"], w));
        return ParseLaw(key, o[key], w);
    }

    /// <summary>
    /// One term of a general material: an optional law (none means the constant 1) and its shape.
    /// Also says whether the shape gives ε and μ, so the material can default an absent μ to 1.
    /// </summary>
    private static (MaterialTerm Term, bool Eps, bool Mu) ParseTerm(JsonNode? node, string where, Lattice? lattice)
    {
        if (node is not JsonObject o) throw new StackFormatException($"{where} must be an object");
        var unknown = o.Select(p => p.Key).Where(k => !DispersionKeys.Contains(k) && !ShapeKeys.Contains(k) && k != "label").ToList();
        if (unknown.Count > 0)
            throw new StackFormatException($"{where}: '{unknown[0]}' is not a law ({string.Join(", ", DispersionKeys)}) or a shape ({string.Join(", ", ShapeKeys)})");
        var laws = DispersionKeys.Where(o.ContainsKey).ToList();
        if (laws.Count > 1) throw new StackFormatException($"{where}: a term has one law, not {string.Join(" and ", laws)}");
        var law = laws.Count == 1 ? ParseLaw(laws[0], o[laws[0]], $"{where}.{laws[0]}") : null;

        var shapes = ShapeKeys.Where(o.ContainsKey).ToList();
        if (shapes.Count == 0) throw new StackFormatException($"{where}: say where the term goes, with one of {string.Join(", ", ShapeKeys)}");
        var axisKeys = new[] { "axis", "axis_deg", "axis_uvw" }.Where(o.ContainsKey).ToList();
        bool axis = axisKeys.Count > 0;
        if (axisKeys.Count > 1) throw new StackFormatException($"{where}: give one of {string.Join(", ", axisKeys)}, not several");
        if (axis && (o.ContainsKey("eps") || o.ContainsKey("matrix")))
            throw new StackFormatException($"{where}: an axis is a shape in the ε block; give it or eps, not both");
        if (o.ContainsKey("matrix") && shapes.Count > 1)
            throw new StackFormatException($"{where}: a 6×6 matrix is the whole shape; give no other shape beside it");

        MaterialMatrix shape;
        if (o.ContainsKey("matrix")) shape = Matrix6(o["matrix"], $"{where}.matrix");
        else
        {
            Complex[,] eps;
            if (axis)
            {
                var e = o.ContainsKey("axis") ? AxisVector(o["axis"], $"{where}.axis")
                      : o.ContainsKey("axis_uvw") ? (lattice ?? throw new StackFormatException($"{where}.axis_uvw: a direction in crystal axes needs the material's \"lattice\""))
                                                    .Direction(AxisVector(o["axis_uvw"], $"{where}.axis_uvw", normalise: false), $"{where}.axis_uvw")
                      : AxisFromDegrees(o["axis_deg"], $"{where}.axis_deg");
                eps = new Complex[3, 3];
                for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) eps[i, j] = e[i] * e[j];
            }
            else eps = o.ContainsKey("eps") ? Block(o["eps"], $"{where}.eps") : new Complex[3, 3];
            shape = MaterialMatrix.FromBlocks(eps,
                o.ContainsKey("mu") ? Block(o["mu"], $"{where}.mu") : new Complex[3, 3],
                o.ContainsKey("rho") ? Block(o["rho"], $"{where}.rho") : null,
                o.ContainsKey("rho_prime") ? Block(o["rho_prime"], $"{where}.rho_prime") : null);
        }
        bool givesEps = axis || o.ContainsKey("eps") || o.ContainsKey("matrix");
        bool givesMu = o.ContainsKey("mu") || o.ContainsKey("matrix");
        return (new MaterialTerm(law, shape), givesEps, givesMu);
    }

    /// <summary>A unit direction from [x, y, z] (normalised here unless asked not to).</summary>
    private static double[] AxisVector(JsonNode? n, string where, bool normalise = true)
    {
        if (n is not JsonArray a || a.Count != 3) throw new StackFormatException($"{where} must be [x, y, z]");
        var v = new[] { StackDocument.Num(a[0], where + "[1]"), StackDocument.Num(a[1], where + "[2]"), StackDocument.Num(a[2], where + "[3]") };
        double len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (!(len > 0)) throw new StackFormatException($"{where} must not be zero");
        return normalise ? [v[0] / len, v[1] / len, v[2] / len] : v;
    }

    /// <summary>
    /// A direction from angles in degrees: a number α is (cos α, sin α, 0), in the x–y plane from x
    /// towards y; [α, β] is lifted by β out of that plane towards z.
    /// </summary>
    private static double[] AxisFromDegrees(JsonNode? n, string where)
    {
        double az, el = 0;
        if (n is JsonArray a)
        {
            if (a.Count != 2) throw new StackFormatException($"{where} must be an angle or [angle, elevation]");
            az = StackDocument.Num(a[0], where + "[1]");
            el = StackDocument.Num(a[1], where + "[2]");
        }
        else az = StackDocument.Num(n, where);
        const double d = Math.PI / 180;
        return [Math.Cos(el * d) * Math.Cos(az * d), Math.Cos(el * d) * Math.Sin(az * d), Math.Sin(el * d)];
    }

    /// <summary>A complex scalar (times the identity) or a 3×3 array.</summary>
    private static Complex[,] Block(JsonNode? n, string where) =>
        n is JsonArray a && a.Count == 3 && a[0] is JsonArray ? Matrix3(n, where) : MaterialMatrix.Scalar(Cplx(n, where));

    private static MaterialMatrix Matrix6(JsonNode? n, string where)
    {
        if (n is not JsonArray rows || rows.Count != 6) throw new StackFormatException($"{where} must be a 6×6 array");
        var m = new MaterialMatrix();
        for (int i = 0; i < 6; ++i)
        {
            if (rows[i] is not JsonArray row || row.Count != 6) throw new StackFormatException($"{where}[{i + 1}] must have 6 entries");
            for (int j = 0; j < 6; ++j) m[i, j] = Cplx(row[j], $"{where}[{i + 1}][{j + 1}]");
        }
        return m;
    }

    private static bool IsFrequencyUnit(string unit) => unit is "GHz" or "THz" or "cm-1" or "eV";

    /// <summary>The parameters of one law, the value of its key.</summary>
    private static Dispersion ParseLaw(string key, JsonNode? node, string w)
    {
        var s = node as JsonObject ?? throw new StackFormatException($"{w} must be an object");
        double N(JsonNode? x, string at) => StackDocument.Num(x, at);
        double Opt(string k, double fallback) => s.ContainsKey(k) ? N(s[k], $"{w}.{k}") : fallback;
        JsonArray Arr(string k) => s[k] as JsonArray ?? throw new StackFormatException($"{w}.{k} must be an array");

        /* The laws that are not in a spectral unit. */
        if (key == "glass")
            return new GlassDispersion(GlassCatalogs.Find(StackDocument.Str(s, "catalog", null, w), StackDocument.Str(s, "name", null, w)));
        if (key == "conductivity")
        {
            var sigma = N(s["sigma_s_per_m"], $"{w}.sigma_s_per_m");
            if (!(sigma >= 0)) throw new StackFormatException($"{w}.sigma_s_per_m must not be negative");
            return new ConductivityDispersion(sigma);
        }
        if (key == "polder")
        {
            var part = StackDocument.Str(s, "part", null, w);
            if (part is not ("mu" or "kappa")) throw new StackFormatException($"{w}.part must be \"mu\" or \"kappa\"");
            var alpha = Opt("alpha", 0);
            if (alpha < 0) throw new StackFormatException($"{w}.alpha must not be negative");
            return new PolderDispersion(N(s["four_pi_ms_gauss"], $"{w}.four_pi_ms_gauss"), N(s["h0_oe"], $"{w}.h0_oe"),
                                        alpha, Opt("gamma_mhz_per_oe", 2.8), part == "kappa");
        }

        var unit = StackDocument.Str(s, "unit", null, w);
        if (!Units.IsSpectralUnit(unit)) throw new StackFormatException($"{w}.unit: '{unit}' is not a spectral unit");

        switch (key)
        {
            case "lorentz":
            case "drude":
            {
                if (!IsFrequencyUnit(unit))
                    throw new StackFormatException($"{w}.unit: a {key} law is written in frequency (cm-1, eV, THz, GHz), not '{unit}'");
                double gamma = Opt("gamma", 0);
                if (gamma < 0) throw new StackFormatException($"{w}.gamma must not be negative");
                if (key == "drude") return new DrudeDispersion(unit, N(s["plasma"], $"{w}.plasma"), gamma);
                double center = N(s["center"], $"{w}.center");
                if (!(center > 0)) throw new StackFormatException($"{w}.center must be positive");
                if (s.ContainsKey("strength") == s.ContainsKey("amplitude"))
                    throw new StackFormatException($"{w}: give one of strength (S) or amplitude (√S)");
                double strength = s.ContainsKey("strength") ? N(s["strength"], $"{w}.strength") : Math.Pow(N(s["amplitude"], $"{w}.amplitude"), 2);
                return new LorentzDispersion(unit, strength, center, gamma);
            }
            case "cauchy":
            {
                if (IsFrequencyUnit(unit))
                    throw new StackFormatException($"{w}.unit: a Cauchy formula is written in wavelength (nm, um, ...), not '{unit}'");
                double[]? range = s["range"] is JsonArray r ? [N(r[0], $"{w}.range[1]"), N(r[1], $"{w}.range[2]")] : null;
                return new CauchyDispersion(unit, N(s["a"], $"{w}.a"), Opt("b", 0), Opt("c", 0), range);
            }
            case "sellmeier":
            {
                var terms = Arr("terms").Select((t, i) =>
                {
                    var to = t as JsonObject ?? throw new StackFormatException($"{w}.terms[{i + 1}] must be an object");
                    double b = N(to["b"], $"{w}.terms[{i + 1}].b");
                    if (to.ContainsKey("c2") == to.ContainsKey("c"))
                        throw new StackFormatException($"{w}.terms[{i + 1}]: give one of c (a resonance) or c2 (its square)");
                    double c2 = to.ContainsKey("c2") ? N(to["c2"], $"{w}.terms[{i + 1}].c2") : Math.Pow(N(to["c"], $"{w}.terms[{i + 1}].c"), 2);
                    return (b, c2);
                }).ToArray();
                double[]? range = s["range"] is JsonArray r ? [N(r[0], $"{w}.range[1]"), N(r[1], $"{w}.range[2]")] : null;
                return new SellmeierDispersion(unit, s.ContainsKey("a") ? N(s["a"], $"{w}.a") : 0, terms, range);
            }
            case "phonon":
            {
                var modes = Arr("modes").Select((m, i) =>
                {
                    var mo = m as JsonObject ?? throw new StackFormatException($"{w}.modes[{i + 1}] must be an object");
                    var at = $"{w}.modes[{i + 1}]";
                    return (N(mo["to"], at + ".to"), N(mo["lo"], at + ".lo"), N(mo["gamma_to"], at + ".gamma_to"), N(mo["gamma_lo"], at + ".gamma_lo"));
                }).ToArray();
                return new FactorisedPhononDispersion(unit, N(s["eps_inf"], $"{w}.eps_inf"), modes);
            }
            case "phonon_additive":
            {
                var modes = Arr("modes").Select((m, i) =>
                {
                    var mo = m as JsonObject ?? throw new StackFormatException($"{w}.modes[{i + 1}] must be an object");
                    var at = $"{w}.modes[{i + 1}]";
                    return (N(mo["to"], at + ".to"), N(mo["lo"], at + ".lo"), N(mo["gamma"], at + ".gamma"));
                }).ToArray();
                return new AdditivePhononDispersion(unit, N(s["eps_inf"], $"{w}.eps_inf"), modes);
            }
            default:   /* table */
            {
                var columns = Arr("columns").Select(c => c!.GetValue<string>()).ToArray();
                bool index = columns.SequenceEqual(new[] { "x", "n", "k" });
                if (!index && !columns.SequenceEqual(new[] { "x", "eps_re", "eps_im" }))
                    throw new StackFormatException($"{w}.columns must be [\"x\", \"n\", \"k\"] or [\"x\", \"eps_re\", \"eps_im\"]");
                var rows = Arr("data");
                if (rows.Count < 2) throw new StackFormatException($"{w}.data needs at least two rows");
                var x = new double[rows.Count];
                var v = new Complex[rows.Count];
                for (int i = 0; i < rows.Count; ++i)
                {
                    if (rows[i] is not JsonArray row || row.Count != 3) throw new StackFormatException($"{w}.data[{i + 1}] must have 3 entries");
                    x[i] = N(row[0], $"{w}.data[{i + 1}][1]");
                    v[i] = new Complex(N(row[1], $"{w}.data[{i + 1}][2]"), N(row[2], $"{w}.data[{i + 1}][3]"));
                    if (i > 1 && Math.Sign(x[i] - x[i - 1]) != Math.Sign(x[1] - x[0]))
                        throw new StackFormatException($"{w}.data must be strictly monotonic in x");
                }
                if (x[1] == x[0]) throw new StackFormatException($"{w}.data must be strictly monotonic in x");
                return new TableDispersion(unit, x, v, index);
            }
        }
    }

    /// <summary>A real number, or [re, im].</summary>
    public static Complex Cplx(JsonNode? n, string where)
    {
        if (n is JsonArray a)
        {
            if (a.Count != 2) throw new StackFormatException($"{where} must be a number or [re, im]");
            return new Complex(StackDocument.Num(a[0], where + "[1]"), StackDocument.Num(a[1], where + "[2]"));
        }
        return StackDocument.Num(n, where);
    }

    private static Complex[,] Matrix3(JsonNode? n, string where)
    {
        if (n is not JsonArray rows || rows.Count != 3) throw new StackFormatException($"{where} must be a 3×3 array");
        var m = new Complex[3, 3];
        for (int i = 0; i < 3; ++i)
        {
            if (rows[i] is not JsonArray row || row.Count != 3) throw new StackFormatException($"{where}[{i + 1}] must have 3 entries");
            for (int j = 0; j < 3; ++j) m[i, j] = Cplx(row[j], $"{where}[{i + 1}][{j + 1}]");
        }
        return m;
    }
}
