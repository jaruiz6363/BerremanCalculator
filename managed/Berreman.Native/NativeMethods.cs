// NativeMethods.cs — the exported calls, declared and not wrapped.
//
// Every signature takes pointers to structs and buffers the caller already owns, and returns a
// status. [LibraryImport] rather than [DllImport]: the source generator emits a direct call for an
// all-blittable signature and refuses to compile one that is not.

using System.Runtime.InteropServices;

namespace Berreman.Native;

public static unsafe partial class NativeMethods
{
    /// <summary>
    /// The name P/Invoke resolves: berreman_native.dll, libberreman_native.so or .dylib. Not
    /// "berreman", which is the CLI assembly's name.
    /// </summary>
    public const string Library = "berreman_native";

    /// <summary>The ABI version this binding was written against: berreman.h's BC_ABI_VERSION.</summary>
    public const int AbiVersion = 0;

    [LibraryImport(Library)]
    public static partial int bc_abi_version();

    /// <summary>Checks the problem and returns the scratch one worker needs, in bytes.</summary>
    [LibraryImport(Library)]
    public static partial BcStatus bc_plan(BcProblem* problem, long* scratchBytes);

    /// <summary>Evaluates points [firstPoint, firstPoint + nPoints) with one worker's scratch.</summary>
    [LibraryImport(Library)]
    public static partial BcStatus bc_evaluate(
        BcProblem* problem, int firstPoint, int nPoints,
        void* scratch, long scratchBytes, BcOutput* output);
}
