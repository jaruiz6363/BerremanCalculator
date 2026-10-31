// Evaluator.cs — a stack document in, results out, through the contract's call sequence
// (contract, Section 6): sizes from counts, allocation in C#, bc_plan, scratch per worker,
// bc_evaluate over disjoint point ranges.

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
    double[] LayerAbsorption,   // [incident wave][layer]
    BcPointDiagnostics Diagnostics)
{
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
}

public static class Evaluator
{
    public static EvaluationResult Evaluate(StackDocument doc, int? workers = null)
    {
        int nLayers = doc.Layers.Count, nMedia = nLayers + 2;
        int nSpectral = doc.Spectral.Length * doc.AzimuthsDeg.Length;

        // ---- M per (wavelength, azimuth), media in order: incident, layers, exit ---------------
        var m = new PinnedBuffer<Complex>((long)nSpectral * nMedia * 36);
        var wavelength = new PinnedBuffer<double>(nSpectral);
        // Media in order, each with the thickness its model may need (0 for the outer media).
        var media = new List<(Material Mat, double[] Euler, double Thickness)>
            { (doc.Materials[doc.Incident.Material], doc.Incident.EulerDeg, 0) };
        media.AddRange(doc.Layers.Select(l => (doc.Materials[l.Material], l.EulerDeg, l.ThicknessNm)));
        media.Add((doc.Materials[doc.Exit.Material], doc.Exit.EulerDeg, 0));

        // The incident index per wavelength, for angles: ξ = n sin θ.
        var nIncident = new double[doc.Spectral.Length];

        for (int w = 0; w < doc.Spectral.Length; ++w)
        {
            double lambda = Units.ToWavelengthNm(doc.Spectral[w], doc.SpectralUnit);
            var atLambda = media.Select(x => x.Mat.At(lambda, x.Thickness)).ToArray();

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
                var sample = MaterialMatrix.EulerRotation(doc.AzimuthsDeg[a], 0, 0);
                for (int k = 0; k < nMedia; ++k)
                {
                    var e = media[k].Euler;
                    var placed = (e[0] == 0 && e[1] == 0 && e[2] == 0 && doc.AzimuthsDeg[a] == 0)
                        ? atLambda[k]
                        : atLambda[k].Rotated(MaterialMatrix.Mul(sample, MaterialMatrix.EulerRotation(e[0], e[1], e[2])));
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
        for (int k = 0; k < nLayers; ++k) thickness[k] = doc.Layers[k].ThicknessNm;

        var jones = new PinnedBuffer<Complex>((long)nPoints * 8);
        var power = new PinnedBuffer<double>((long)nPoints * 6);
        var absorption = new PinnedBuffer<double>((long)nPoints * 2 * nLayers);
        var diagnostics = new PinnedBuffer<BcPointDiagnostics>(nPoints);

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
                NLayers = nLayers, NSpectral = nSpectral, NPoints = nPoints, NDepths = 0,
                M = m.Pointer, Wavelength = wavelength.Pointer, Thickness = thickness.Pointer,
                PointSpectral = pointSpectral.Pointer, PointXi = pointXi.Pointer, FieldDepths = null,
            };
            var (plan, scratchBytes) = Plan(problem);
            if (plan != BcStatus.Ok) throw new StackFormatException($"the engine refused the stack at bc_plan: {plan}");

            var output = new BcOutput
            {
                Jones = jones.Pointer, Power = power.Pointer,
                LayerAbsorption = nLayers > 0 ? absorption.Pointer : null,
                Fields = null, Diagnostics = diagnostics.Pointer,
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
        GC.KeepAlive(diagnostics);

        var points = new PointResult[nPoints];
        for (int p = 0; p < nPoints; ++p)
        {
            int s = pointSpectral[p], w = s / doc.AzimuthsDeg.Length, a = s % doc.AzimuthsDeg.Length;
            int i = p % incidence.Length;
            points[p] = new PointResult(
                doc.Spectral[w], wavelength[s], doc.AzimuthsDeg[a],
                doc.AnglesDeg?[i], pointXi[p],
                jones.Span.Slice(p * 8, 8).ToArray(),
                power.Span.Slice(p * 6, 6).ToArray(),
                nLayers > 0 ? absorption.Span.Slice(p * 2 * nLayers, 2 * nLayers).ToArray() : [],
                diagnostics[p]);
        }
        return new EvaluationResult { Document = doc, Points = points, Status = status };
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
