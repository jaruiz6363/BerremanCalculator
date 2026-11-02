// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// PinnedBuffer.cs — the allocation primitive (rule 1: C# allocates everything).
//
// The native side holds pointers into these across a call, so they must never move. They are
// allocated on the pinned object heap: never compacted, no handle to leak, and pinned by virtue of
// existing, so there is no way to make one and forget to pin it. (The same design as
// ThinFilmDesigner's, where the reasons against `fixed` and GCHandle are written out.)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Berreman.Native;

/// <summary>A managed array that never moves, plus the pointer the native side is given.</summary>
public sealed unsafe class PinnedBuffer<T> where T : unmanaged
{
    private readonly T[] _array;

    /// <param name="count">Element count. Zero is legal and gives a null pointer, the ABI's
    /// convention for a buffer that is not supplied.</param>
    public PinnedBuffer(long count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "a buffer count cannot be negative");
        if (count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(count), count,
                "a single buffer above int.MaxValue elements is beyond what one array can hold");

        Count = count;
        _array = count == 0 ? System.Array.Empty<T>() : GC.AllocateArray<T>((int)count, pinned: true);
    }

    public long Count { get; }

    public Span<T> Span => _array;

    /// <summary>The address the native side is given; null when the buffer is empty.</summary>
    public T* Pointer
    {
        get
        {
            if (Count == 0) return null;
            return (T*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_array));
        }
    }

    public ref T this[long i] => ref _array[i];

    public static PinnedBuffer<T> From(ReadOnlySpan<T> source)
    {
        var b = new PinnedBuffer<T>(source.Length);
        source.CopyTo(b.Span);
        return b;
    }

    public static PinnedBuffer<T> From(params T[] source) => From((ReadOnlySpan<T>)source);
}
