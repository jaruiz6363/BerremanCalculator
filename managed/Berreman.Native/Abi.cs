// Abi.cs — the structs of berreman.h, field for field.
//
// Sequential layout, blittable fields only, so [LibraryImport] passes them with no marshalling and
// refuses to compile if one stops being blittable. The sizes are checked against the native side's
// in the test suite, which is the only check that cannot agree with itself by construction.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Berreman.Native;

/// <summary>berreman.h's bc_status. The values are the ABI; never renumber.</summary>
public enum BcStatus : int
{
    Ok = 0,
    InvalidArgument = 1,
    NotImplemented = 2,
    ScratchTooSmall = 3,
    IncidentAbsorbing = 4,
    GrazingOuter = 5,
    DeltaSingular = 6,
    OuterEigenFailed = 7,
    BoundarySingular = 8,
}

/// <summary>
/// berreman.h's bc_problem. Every pointer is to a <see cref="PinnedBuffer{T}"/> the caller owns.
/// Complex arrays are <see cref="Complex"/>, whose layout (re, im) is bc_complex's.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BcProblem
{
    public int NLayers;
    public int NSpectral;
    public int NPoints;
    public int NDepths;

    /// <summary>n_spectral × (n_layers + 2) × 36, row-major 6×6 per (spectral index, medium).</summary>
    public Complex* M;
    public double* Wavelength;
    public double* Thickness;
    public int* PointSpectral;
    public double* PointXi;
    public double* FieldDepths;
}

/// <summary>berreman.h's bc_point_diagnostics: 40 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BcPointDiagnostics
{
    public int Status;
    public int MaxSteps;
    public int Opaque;
    public int Reserved;
    public double FrontCondition;
    public double IncidentCondition;
    public double ExitCondition;
}

/// <summary>berreman.h's bc_output. A null pointer means that output is not computed.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BcOutput
{
    public Complex* Jones;
    public double* Power;
    public double* LayerAbsorption;
    public Complex* Fields;
    public BcPointDiagnostics* Diagnostics;
}
