// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// EngineStatus.cs — whether the native engine loads, which ABI it has, and what it can do.
//
// The first thing every front end asks, and the only question the skeleton can answer. It runs the
// full call sequence of contract Section 6 on a trivial problem (one vacuum layer between vacuum
// half-spaces, at normal incidence): plan, allocate scratch from the plan, evaluate. So it proves
// the library loads, the structs pass, and the ownership model works, before any physics exists.

using System.Numerics;
using Berreman.Native;

namespace Berreman.Api;

public sealed record EngineReport(
    bool Loaded,
    int ManagedAbi,
    int? NativeAbi,
    BcStatus? PlanStatus,
    BcStatus? EvaluateStatus,
    string Summary);

public static class EngineStatus
{
    public static unsafe EngineReport Probe()
    {
        int nativeAbi;
        try
        {
            nativeAbi = NativeMethods.bc_abi_version();
        }
        catch (DllNotFoundException e)
        {
            return new EngineReport(false, NativeMethods.AbiVersion, null, null, null,
                $"the native engine ({NativeMethods.Library}) did not load: {e.Message}");
        }

        if (nativeAbi != NativeMethods.AbiVersion)
            return new EngineReport(true, NativeMethods.AbiVersion, nativeAbi, null, null,
                $"ABI mismatch: the native engine is version {nativeAbi}, this program expects {NativeMethods.AbiVersion}");

        const int nLayers = 1, nMedia = nLayers + 2;
        var m = new PinnedBuffer<Complex>(nMedia * 36);
        for (int k = 0; k < nMedia; ++k)
            for (int i = 0; i < 6; ++i)
                m[k * 36 + 7 * i] = Complex.One;            // vacuum: eps = mu = I

        var wavelength = PinnedBuffer<double>.From(550.0);
        var thickness = PinnedBuffer<double>.From(100.0);
        var pointSpectral = PinnedBuffer<int>.From(0);
        var pointXi = PinnedBuffer<double>.From(0.0);

        var problem = new BcProblem
        {
            NLayers = nLayers, NSpectral = 1, NPoints = 1, NDepths = 0,
            M = m.Pointer, Wavelength = wavelength.Pointer, Thickness = thickness.Pointer,
            PointSpectral = pointSpectral.Pointer, PointXi = pointXi.Pointer, FieldDepths = null,
        };

        long scratchBytes;
        var plan = NativeMethods.bc_plan(&problem, &scratchBytes);
        if (plan != BcStatus.Ok)
            return new EngineReport(true, NativeMethods.AbiVersion, nativeAbi, plan, null,
                $"the engine refused a trivial problem at bc_plan: {plan}");

        var scratch = new PinnedBuffer<byte>(scratchBytes);
        var jones = new PinnedBuffer<Complex>(8);
        var diagnostics = new PinnedBuffer<BcPointDiagnostics>(1);
        var output = new BcOutput { Jones = jones.Pointer, Diagnostics = diagnostics.Pointer };

        var evaluate = NativeMethods.bc_evaluate(&problem, 0, 1, scratch.Pointer, scratchBytes, &output);

        // The structs hold raw pointers, which the collector does not see: without these, a buffer
        // whose last managed use was reading .Pointer could be collected during the native call.
        GC.KeepAlive(m); GC.KeepAlive(wavelength); GC.KeepAlive(thickness);
        GC.KeepAlive(pointSpectral); GC.KeepAlive(pointXi);
        GC.KeepAlive(scratch); GC.KeepAlive(jones); GC.KeepAlive(diagnostics);

        string summary = evaluate switch
        {
            BcStatus.Ok => "the engine loads and evaluates",
            BcStatus.NotImplemented => "the engine loads and plans; evaluation is not implemented yet",
            _ => $"the engine loads but bc_evaluate returned {evaluate} on a trivial problem",
        };
        return new EngineReport(true, NativeMethods.AbiVersion, nativeAbi, plan, evaluate, summary);
    }
}
