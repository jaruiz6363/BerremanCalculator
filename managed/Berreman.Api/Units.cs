// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// Units.cs — the units a person types and reads (contract, Section 1a).
//
// The engine is scale-free; C# works in nanometres throughout and converts at the edges. A spectral
// value of any kind becomes a vacuum wavelength in nm, and back.

namespace Berreman.Api;

public static class Units
{
    /// <summary>Speed of light in vacuum, m/s (exact, SI).</summary>
    public const double C = 299_792_458.0;

    /// <summary>hc/e in eV·nm, from the exact SI values of h, c and e.</summary>
    public static readonly double HcEvNm = 6.62607015e-34 * C / 1.602176634e-19 * 1e9;

    /// <summary>Nanometres per unit of length.</summary>
    public static double NmPerLength(string unit) => unit switch
    {
        "nm" => 1.0,
        "um" or "µm" => 1e3,
        "mm" => 1e6,
        "cm" => 1e7,
        "m" => 1e9,
        _ => throw new StackFormatException($"'{unit}' is not a length unit (nm, um, mm, cm, m)"),
    };

    public static bool IsSpectralUnit(string unit) =>
        unit is "nm" or "um" or "µm" or "mm" or "cm" or "m" or "GHz" or "THz" or "cm-1" or "eV";

    /// <summary>A spectral value in the given unit, as a vacuum wavelength in nm.</summary>
    public static double ToWavelengthNm(double value, string unit)
    {
        if (!(value > 0) || double.IsInfinity(value))
            throw new StackFormatException($"a spectral value must be positive and finite, not {value}");
        return unit switch
        {
            "nm" or "um" or "µm" or "mm" or "cm" or "m" => value * NmPerLength(unit),
            "GHz" => C / (value * 1e9) * 1e9,
            "THz" => C / (value * 1e12) * 1e9,
            "cm-1" => 1e7 / value,
            "eV" => HcEvNm / value,
            _ => throw new StackFormatException(
                $"'{unit}' is not a spectral unit (nm, um, mm, cm, m, GHz, THz, cm-1, eV)"),
        };
    }

    /// <summary>The inverse of <see cref="ToWavelengthNm"/>.</summary>
    public static double FromWavelengthNm(double nm, string unit) => unit switch
    {
        "nm" or "um" or "µm" or "mm" or "cm" or "m" => nm / NmPerLength(unit),
        "GHz" => C / (nm * 1e-9) / 1e9,
        "THz" => C / (nm * 1e-9) / 1e12,
        "cm-1" => 1e7 / nm,
        "eV" => HcEvNm / nm,
        _ => throw new StackFormatException($"'{unit}' is not a spectral unit"),
    };
}

/// <summary>A stack file or request that cannot be used, with a message that says why.</summary>
public sealed class StackFormatException(string message) : Exception(message);
