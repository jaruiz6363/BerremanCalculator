// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// Evaluator.cs — a stack document in, results out, through the contract's call sequence
// (contract, Section 6): sizes from counts, allocation in C#, bc_plan, scratch per worker,
// bc_evaluate over disjoint point ranges.
//
// THE ENGINE'S LAYERS ARE NOT ALWAYS THE DOCUMENT'S. A twisted (cholesteric) layer becomes many
// uniform sublayers (contract, Section 2). Results are reported per DOCUMENT layer: the absorption
// of a twisted layer is the sum over its sublayers. When any layer is twisted, the stack is
// evaluated a second time with twice the sublayers, and the largest change in R, T or A is
// reported, so the discretisation error is visible rather than assumed.

using System.Numerics;
using Berreman.Native;

namespace Berreman.Api;

/// <summary>One evaluation point's results.</summary>
public sealed record PointResult(
    double Spectral,            // in the document's spectral unit
    double WavelengthNm,
    double AzimuthDeg,
    double? AngleDeg,
    double Xi,
    Complex[] Jones,            // r_pp r_ps r_sp r_ss t_pp t_ps t_sp t_ss (first subscript incident)
    double[] Power,             // R_p T_p A_p R_s T_s A_s; for anisotropic outer media, modes 0 and 1
    double[] LayerAbsorption,   // [incident wave][document layer]
    BcPointDiagnostics Diagnostics)
{
    /// <summary>
    /// The fields at the evaluation's depths, when it was asked for them: [incident wave][depth][6],
    /// (E_x, E_y, E_z, H̃_x, H̃_y, H̃_z) per unit incident amplitude (contract, Section 5).
    /// </summary>
    public Complex[]? Fields { get; init; }

    /// <summary>One field component: wave 0 or 1, depth index, component 0–5.</summary>
    public Complex Field(int wave, int depth, int component, int nDepths) => Fields![(wave * nDepths + depth) * 6 + component];

    public BcStatus Status => (BcStatus)Diagnostics.Status;
    public Complex Rpp => Jones[0];
    public Complex Rss => Jones[3];

    /// <summary>
    /// Ψ and Δ in degrees, from ρ = r_pp / r_ss = tan Ψ e^{iΔ} (Schubert 1996, Eq. 4; contract,
    /// Section 1). Δ in (−180, 180].
    /// </summary>
    public (double Psi, double Delta) Ellipsometry()
    {
        var rho = Jones[0] / Jones[3];
        return (Math.Atan(rho.Magnitude) * 180 / Math.PI, rho.Phase * 180 / Math.PI);
    }
}

public sealed class EvaluationResult
{
    public required StackDocument Document { get; init; }
    public required IReadOnlyList<PointResult> Points { get; init; }
    public required BcStatus Status { get; init; }

    /// <summary>The depths the fields were computed at, in nm from the first interface; empty when none.</summary>
    public double[] DepthsNm { get; init; } = [];

    /// <summary>
    /// When the stack has twisted layers: the largest change in any R, T or A when the number of
    /// sublayers is doubled. Null when nothing was discretised.
    /// </summary>
    public double? SublayerChange { get; init; }
}

public static class Evaluator
{
    /// <param name="depthsNm">Depths for the fields, in nm from the first interface (negative in the
    /// incident medium, past the last interface in the exit medium); null for none.</param>
    public static EvaluationResult Evaluate(StackDocument doc, int? workers = null, double[]? depthsNm = null)
    {
        var result = Core(doc, workers, 1, depthsNm ?? []);
        if (!doc.Layers.Any(l => l.Twist is not null)) return result;

        var finer = Core(doc, workers, 2, []);
        double change = 0;
        for (int p = 0; p < result.Points.Count; ++p)
            for (int k = 0; k < 6; ++k)
            {
                double a = result.Points[p].Power[k], b = finer.Points[p].Power[k];
                if (double.IsFinite(a) && double.IsFinite(b)) change = Math.Max(change, Math.Abs(a - b));
            }
        return new EvaluationResult { Document = doc, Points = result.Points, Status = result.Status, SublayerChange = change, DepthsNm = result.DepthsNm };
    }

    /// <summary>One medium as the engine sees it: a material, its rotation (null for none), its thickness, and the document layer it came from (−1 for the outer media).</summary>
    private readonly record struct EngineMedium(Material Mat, double[,]? Rotation, double ThicknessNm, int Source);

    private static double[,]? Euler(double[] e) =>
        e[0] == 0 && e[1] == 0 && e[2] == 0 ? null : MaterialMatrix.EulerRotation(e[0], e[1], e[2]);

    private static List<EngineMedium> Expand(StackDocument doc, int refine)
    {
        var media = new List<EngineMedium> { new(doc.Materials[doc.Incident.Material], Euler(doc.Incident.EulerDeg), 0, -1) };
        for (int k = 0; k < doc.Layers.Count; ++k)
        {
            var l = doc.Layers[k];
            var mat = doc.Materials[l.Material];
            if (l.Twist is not { } tw)
            {
                media.Add(new(mat, Euler(l.EulerDeg), l.ThicknessNm, k));
                continue;
            }
            int n = tw.Sublayers * refine;
            double h = l.ThicknessNm / n;
            var e = MaterialMatrix.EulerRotation(l.EulerDeg[0], l.EulerDeg[1], l.EulerDeg[2]);
            for (int j = 0; j < n; ++j)
                media.Add(new(mat, MaterialMatrix.Mul(MaterialMatrix.EulerRotation(tw.AngleDeg((j + 0.5) * h), 0, 0), e), h, k));
        }
        media.Add(new(doc.Materials[doc.Exit.Material], Euler(doc.Exit.EulerDeg), 0, -1));
        return media;
    }

    private static EvaluationResult Core(StackDocument doc, int? workers, int refine, double[] depthsNm)
    {
        var media = Expand(doc, refine);
        int nMedia = media.Count, nLayers = nMedia - 2, nDocLayers = doc.Layers.Count;
        int nSpectral = doc.Spectral.Length * doc.AzimuthsDeg.Length;

        // ---- M per (wavelength, azimuth), media in order: incident, layers, exit ---------------
        var m = new PinnedBuffer<Complex>((long)nSpectral * nMedia * 36);
        var wavelength = new PinnedBuffer<double>(nSpectral);

        // The incident index per wavelength, for angles: ξ = n sin θ.
        var nIncident = new double[doc.Spectral.Length];

        for (int w = 0; w < doc.Spectral.Length; ++w)
        {
            double lambda = Units.ToWavelengthNm(doc.Spectral[w], doc.SpectralUnit);
            // A twisted layer's sublayers share one material: evaluate it once per wavelength.
            var cache = new Dictionary<(Material, double), MaterialMatrix>();
            var atLambda = media.Select(x =>
            {
                if (!cache.TryGetValue((x.Mat, x.ThicknessNm), out var v)) cache[(x.Mat, x.ThicknessNm)] = v = x.Mat.At(lambda, x.ThicknessNm);
                return v;
            }).ToArray();

            if (doc.AnglesDeg is not null)
            {
                if (!atLambda[0].IsIsotropic(out var eps, out var mu) || eps.Imaginary != 0 || mu.Imaginary != 0 || (eps * mu).Real <= 0)
                    throw new StackFormatException(
                        "sweep.angle_deg needs an isotropic, lossless incident medium; for any other, give sweep.xi");
                nIncident[w] = Math.Sqrt((eps * mu).Real);
            }

            for (int a = 0; a < doc.AzimuthsDeg.Length; ++a)
            {
                int s = w * doc.AzimuthsDeg.Length + a;
                wavelength[s] = lambda;
                double az = doc.AzimuthsDeg[a];
                var sample = MaterialMatrix.EulerRotation(az, 0, 0);
                for (int k = 0; k < nMedia; ++k)
                {
                    var r = media[k].Rotation;
                    var placed = r is null
                        ? (az == 0 ? atLambda[k] : atLambda[k].Rotated(sample))
                        : atLambda[k].Rotated(az == 0 ? r : MaterialMatrix.Mul(sample, r));
                    placed.M.CopyTo(m.Span.Slice((s * nMedia + k) * 36, 36));
                }
            }
        }

        // ---- points: wavelength, then azimuth, then angle ------------------------------------
        var incidence = doc.AnglesDeg ?? doc.Xi!;
        int nPoints = nSpectral * incidence.Length;
        var pointSpectral = new PinnedBuffer<int>(nPoints);
        var pointXi = new PinnedBuffer<double>(nPoints);
        for (int s = 0; s < nSpectral; ++s)
            for (int i = 0; i < incidence.Length; ++i)
            {
                int p = s * incidence.Length + i;
                pointSpectral[p] = s;
                pointXi[p] = doc.AnglesDeg is null
                    ? incidence[i]
                    : nIncident[s / doc.AzimuthsDeg.Length] * Math.Sin(incidence[i] * Math.PI / 180);
            }

        var thickness = new PinnedBuffer<double>(nLayers);
        for (int k = 0; k < nLayers; ++k) thickness[k] = media[k + 1].ThicknessNm;

        var jones = new PinnedBuffer<Complex>((long)nPoints * 8);
        var power = new PinnedBuffer<double>((long)nPoints * 6);
        var absorption = new PinnedBuffer<double>((long)nPoints * 2 * nLayers);
        var diagnostics = new PinnedBuffer<BcPointDiagnostics>(nPoints);
        int nDepths = depthsNm.Length;
        var depths = PinnedBuffer<double>.From(depthsNm);
        var fields = new PinnedBuffer<Complex>(Math.Max(1L, (long)nPoints * 2 * nDepths * 6));
        fields.Span.Fill(new Complex(double.NaN, double.NaN));

        // A point that fails leaves its outputs untouched (rule 5), so they start as NaN: a failed
        // point then reads as NaN everywhere, never as plausible zeros.
        jones.Span.Fill(new Complex(double.NaN, double.NaN));
        power.Span.Fill(double.NaN);
        absorption.Span.Fill(double.NaN);

        BcStatus status;
        unsafe
        {
            var problem = new BcProblem
            {
                NLayers = nLayers, NSpectral = nSpectral, NPoints = nPoints, NDepths = nDepths,
                M = m.Pointer, Wavelength = wavelength.Pointer, Thickness = thickness.Pointer,
                PointSpectral = pointSpectral.Pointer, PointXi = pointXi.Pointer, FieldDepths = nDepths > 0 ? depths.Pointer : null,
            };
            var (plan, scratchBytes) = Plan(problem);
            if (plan != BcStatus.Ok) throw new StackFormatException($"the engine refused the stack at bc_plan: {plan}");

            var output = new BcOutput
            {
                Jones = jones.Pointer, Power = power.Pointer,
                LayerAbsorption = nLayers > 0 ? absorption.Pointer : null,
                Fields = nDepths > 0 ? fields.Pointer : null, Diagnostics = diagnostics.Pointer,
            };

            int nw = Math.Max(1, Math.Min(workers ?? Environment.ProcessorCount, (nPoints + 63) / 64));
            var statuses = new BcStatus[nw];
            Parallel.For(0, nw, w =>
            {
                int first = (int)((long)nPoints * w / nw), last = (int)((long)nPoints * (w + 1) / nw);
                statuses[w] = Run(problem, output, first, last - first, scratchBytes);
            });
            status = statuses.FirstOrDefault(s => s != BcStatus.Ok);
        }
        GC.KeepAlive(m); GC.KeepAlive(wavelength); GC.KeepAlive(pointSpectral); GC.KeepAlive(pointXi);
        GC.KeepAlive(thickness); GC.KeepAlive(jones); GC.KeepAlive(power); GC.KeepAlive(absorption);
        GC.KeepAlive(diagnostics); GC.KeepAlive(depths); GC.KeepAlive(fields);

        var points = new PointResult[nPoints];
        for (int p = 0; p < nPoints; ++p)
        {
            int s = pointSpectral[p], w = s / doc.AzimuthsDeg.Length, a = s % doc.AzimuthsDeg.Length;
            int i = p % incidence.Length;

            /* Absorption per document layer: the sum over its engine layers. */
            var layerAbs = new double[2 * nDocLayers];
            for (int j = 0; j < 2; ++j)
                for (int k = 0; k < nLayers; ++k)
                    layerAbs[j * nDocLayers + media[k + 1].Source] += absorption[((long)p * 2 + j) * nLayers + k];

            points[p] = new PointResult(
                doc.Spectral[w], wavelength[s], doc.AzimuthsDeg[a],
                doc.AnglesDeg?[i], pointXi[p],
                jones.Span.Slice(p * 8, 8).ToArray(),
                power.Span.Slice(p * 6, 6).ToArray(),
                layerAbs,
                diagnostics[p])
            {
                Fields = nDepths > 0 ? fields.Span.Slice((int)((long)p * 2 * nDepths * 6), 2 * nDepths * 6).ToArray() : null,
            };
        }
        return new EvaluationResult { Document = doc, Points = points, Status = status, DepthsNm = depthsNm };
    }

    private static unsafe (BcStatus, long) Plan(BcProblem problem)
    {
        long bytes;
        var s = NativeMethods.bc_plan(&problem, &bytes);
        return (s, bytes);
    }

    /// <summary>One worker: its own scratch, a disjoint range of points.</summary>
    private static unsafe BcStatus Run(BcProblem problem, BcOutput output, int first, int count, long scratchBytes)
    {
        var scratch = new PinnedBuffer<byte>(scratchBytes);
        var s = NativeMethods.bc_evaluate(&problem, first, count, scratch.Pointer, scratchBytes, &output);
        GC.KeepAlive(scratch);
        return s;
    }
}
