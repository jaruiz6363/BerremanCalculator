// Material.cs — a material is a model evaluated at a wavelength (and, for a resistive sheet, at the
// thickness of the layer that holds it). Constants are the common case; the models below are the
// ones the microwave examples need. Every model returns M in the program's exp(−iωt) convention.

using System.Numerics;

namespace Berreman.Api;

public abstract class Material
{
    /// <summary>M at a vacuum wavelength, for a layer of the given thickness (both in nm).</summary>
    public abstract MaterialMatrix At(double wavelengthNm, double thicknessNm);

    /// <summary>True when M does not depend on wavelength or thickness.</summary>
    public virtual bool IsConstant => false;

    /// <summary>Free-space impedance μ0 c, ohms (μ0 from CODATA 2018).</summary>
    public static readonly double Z0 = 1.25663706212e-6 * Units.C;
}

public sealed class ConstantMaterial(MaterialMatrix m) : Material
{
    public MaterialMatrix M { get; } = m;
    public override MaterialMatrix At(double wavelengthNm, double thicknessNm) => M;
    public override bool IsConstant => true;
}

/// <summary>
/// A magnetised ferrite: the Polder permeability tensor with the bias along the principal z.
///
/// THE FORM. Kodera and Caloz, IEEE AWPL 17 (2018), Eq. 3, which is Pozar's: in exp(+jωt),
/// μ_P = [[μ, jκ, 0], [−jκ, μ, 0], [0, 0, 1]], μ = 1 + ω0 ωm / (ω0² − ω²), κ = ω ωm / (ω0² − ω²),
/// with loss by ω0 → ω0 + jαω. Converting to exp(−iωt) conjugates every element:
/// [[μ, −iκ, 0], [iκ, μ, 0], [0, 0, 1]] with ω0 → ω0 − iαω. Check: at ω = ω0 the susceptibility is
/// ≈ i ωm / (2α ω0), with a positive imaginary part, as a passive medium needs in exp(−iωt).
///
/// ω0 = γ H0 and ωm = γ 4πMs with γ/2π in MHz/Oe (2.8 by default, as in the sources), H0 the
/// INTERNAL static field in Oe and 4πMs in gauss. The sign of the off-diagonal fixes the sense of
/// Faraday rotation; papers differ in it (docs/literature.md, "Microwaves"), and the program follows
/// this one form throughout.
/// </summary>
public sealed class FerriteMaterial(double fourPiMsGauss, double h0Oe, double alpha, double gammaMHzPerOe, Complex[,] eps) : Material
{
    public override MaterialMatrix At(double wavelengthNm, double thicknessNm)
    {
        double f = Units.C / (wavelengthNm * 1e-9) / 1e6;   // MHz
        Complex w0 = new Complex(gammaMHzPerOe * h0Oe, -alpha * f);
        double wm = gammaMHzPerOe * fourPiMsGauss;
        Complex den = w0 * w0 - f * f;
        Complex mu = 1 + w0 * wm / den, kappa = f * wm / den;
        var i = Complex.ImaginaryOne;
        var muT = new Complex[,] { { mu, -i * kappa, 0 }, { i * kappa, mu, 0 }, { 0, 0, 1 } };
        return MaterialMatrix.FromBlocks(eps, muT);
    }

    /// <summary>The scalars μ and κ at a frequency, for tests and tables.</summary>
    public (Complex Mu, Complex Kappa) Polder(double frequencyGHz)
    {
        var m = At(Units.C / (frequencyGHz * 1e9) * 1e9, 0);
        return (m[3, 3], m[4, 3] / Complex.ImaginaryOne);
    }
}

/// <summary>A scalar ε(λ): one dispersion law (docs/stack-format.md, "Dispersion").</summary>
public abstract class Dispersion
{
    public abstract Complex Eps(double wavelengthNm);
}

public sealed class ConstantDispersion(Complex eps) : Dispersion
{
    public override Complex Eps(double wavelengthNm) => eps;
}

/// <summary>
/// Sellmeier: n² = 1 + A + Σ B_j λ² / (λ² − C_j), with C_j in the unit's square (the
/// refractiveindex.info "formula 2" form; a resonance wavelength c gives C = c²).
/// </summary>
public sealed class SellmeierDispersion(string unit, double a, (double B, double C)[] terms, double[]? range) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double l = Units.FromWavelengthNm(wavelengthNm, unit);
        if (range is not null && (l < range[0] || l > range[1]))
            throw new StackFormatException($"{l:G6} {unit} is outside the Sellmeier formula's range [{range[0]}, {range[1]}] {unit}");
        double l2 = l * l, n2 = 1 + a;
        foreach (var (b, c) in terms) n2 += b * l2 / (l2 - c);
        return n2;
    }
}

/// <summary>
/// Phonon oscillators, factorised (TO–LO) form, in exp(−iωt):
/// ε = ε∞ Π (ω_Lj² − ω² − iγ_Lj ω) / (ω_Tj² − ω² − iγ_Tj ω), frequencies in the given unit (cm-1 usually).
/// </summary>
public sealed class FactorisedPhononDispersion(string unit, double epsInf, (double To, double Lo, double GTo, double GLo)[] modes) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double w = Units.FromWavelengthNm(wavelengthNm, unit);
        Complex e = epsInf;
        var i = Complex.ImaginaryOne;
        foreach (var (to, lo, gto, glo) in modes)
            e *= (lo * lo - w * w - i * glo * w) / (to * to - w * w - i * gto * w);
        return e;
    }
}

/// <summary>
/// Phonon oscillators, additive form scaled by ε∞, in exp(−iωt):
/// ε = ε∞ [1 + Σ (ω_Tj² − ω_Lj²) / (ω² − ω_Tj² + iγ_j ω)].
/// </summary>
public sealed class AdditivePhononDispersion(string unit, double epsInf, (double To, double Lo, double Gamma)[] modes) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double w = Units.FromWavelengthNm(wavelengthNm, unit);
        Complex s = 1;
        var i = Complex.ImaginaryOne;
        foreach (var (to, lo, g) in modes)
            s += (to * to - lo * lo) / (w * w - to * to + i * g * w);
        return epsInf * s;
    }
}

/// <summary>
/// A table of n and k (or ε′ and ε″) against a spectral variable, interpolated linearly in that
/// variable. Outside the table the evaluation is refused, never extrapolated.
/// </summary>
public sealed class TableDispersion(string unit, double[] x, Complex[] values, bool valuesAreIndex) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double v = Units.FromWavelengthNm(wavelengthNm, unit);
        int n = x.Length;
        bool ascending = x[n - 1] > x[0];
        double lo = ascending ? x[0] : x[n - 1], hi = ascending ? x[n - 1] : x[0];
        if (v < lo || v > hi)
            throw new StackFormatException($"{v:G6} {unit} is outside the table's range [{lo:G6}, {hi:G6}] {unit}");
        int k = 0;
        while (k < n - 2 && (ascending ? x[k + 1] < v : x[k + 1] > v)) ++k;
        double t = (v - x[k]) / (x[k + 1] - x[k]);
        var z = values[k] + (values[k + 1] - values[k]) * t;
        return valuesAreIndex ? z * z : z;
    }
}

/// <summary>A material whose principal ε values follow dispersion laws: isotropic, uniaxial (axis z) or biaxial.</summary>
public sealed class DispersiveMaterial(Dispersion x, Dispersion y, Dispersion z, Complex[,] mu) : Material
{
    public override MaterialMatrix At(double wavelengthNm, double thicknessNm) =>
        MaterialMatrix.FromBlocks(MaterialMatrix.Diagonal(x.Eps(wavelengthNm), y.Eps(wavelengthNm), z.Eps(wavelengthNm)), mu);
}

/// <summary>A conductor: ε = ε_background + i σ / (ω ε0), with σ in S/m (exp(−iωt)).</summary>
public sealed class ConductorMaterial(double sigmaSPerM, Complex background) : Material
{
    public override MaterialMatrix At(double wavelengthNm, double thicknessNm)
    {
        // σ / (ω ε0) = σ Z0 λ / (2π), λ in metres.
        var eps = background + Complex.ImaginaryOne * sigmaSPerM * Z0 * wavelengthNm * 1e-9 / (2 * Math.PI);
        return MaterialMatrix.FromBlocks(MaterialMatrix.Scalar(eps), MaterialMatrix.Scalar(1));
    }
}

/// <summary>
/// A resistive sheet of R_s ohms per square, represented by the layer that holds it: a conductor of
/// σ = 1/(R_s t) over the layer's thickness t, so ε = 1 + i Z0 / (R_s k0 t). The layer should be
/// thin against the wavelength; the thin-layer error is of order k0 t.
/// </summary>
public sealed class ResistiveSheetMaterial(double ohmsPerSquare) : Material
{
    public override MaterialMatrix At(double wavelengthNm, double thicknessNm)
    {
        if (!(thicknessNm > 0))
            throw new StackFormatException("a resistive sheet needs a layer of positive thickness to hold it");
        double k0t = 2 * Math.PI * thicknessNm / wavelengthNm;
        var eps = new Complex(1, Z0 / (ohmsPerSquare * k0t));
        return MaterialMatrix.FromBlocks(MaterialMatrix.Scalar(eps), MaterialMatrix.Scalar(1));
    }
}
