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

/// <summary>
/// One Lorentz oscillator, in exp(−iωt): f = S / (ω₀² − ω² − iγω), with S, ω₀ and γ in a unit
/// proportional to frequency (cm-1, eV, THz, GHz). Schubert et al. (PRB 93, 125209 (2016), Eq. 10)
/// tabulate √S as the amplitude.
/// </summary>
public sealed class LorentzDispersion(string unit, double strength, double center, double gamma) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double w = Units.FromWavelengthNm(wavelengthNm, unit);
        return strength / new Complex(center * center - w * w, -gamma * w);
    }
}

/// <summary>Free carriers (Drude), in exp(−iωt): f = −ω_p² / (ω² + iγω), in a frequency unit.</summary>
public sealed class DrudeDispersion(string unit, double plasma, double gamma) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double w = Units.FromWavelengthNm(wavelengthNm, unit);
        return -plasma * plasma / new Complex(w * w, gamma * w);
    }
}

/// <summary>
/// Tauc–Lorentz (Jellison and Modine, Appl. Phys. Lett. 69, 371 (1996); erratum 69, 2137), in a
/// unit proportional to photon energy (eV usually): f = ε∞ + χ(E), with
///   Im χ = (1/E) A E₀ C (E − E_g)² / ((E² − E₀²)² + C²E²) above the gap E_g, and 0 below;
///   Re χ the Kramers–Kronig transform of Im χ, in closed form.
/// The closed form is transcribed from the Wikipedia article "Tauc–Lorentz model", whose source
/// is the paper with its erratum. It is checked against a numerical Kramers–Kronig integral of
/// Im χ in the managed tests, so a typo in either would show. C < 2E₀ is required (α real).
/// </summary>
public sealed class TaucLorentzDispersion(string unit, double epsInf, double a, double e0, double c, double eg) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double e = Units.FromWavelengthNm(wavelengthNm, unit);
        return epsInf + Chi(e, a, e0, c, eg);
    }

    public static Complex Chi(double E, double A, double E0, double C, double Eg)
    {
        double im = E > Eg ? A * E0 * C * (E - Eg) * (E - Eg) / ((E * E - E0 * E0) * (E * E - E0 * E0) + C * C * E * E) / E : 0;
        double E2 = E * E, Eg2 = Eg * Eg, E02 = E0 * E0, C2 = C * C;
        double alpha = Math.Sqrt(4 * E02 - C2), gamma2 = E02 - C2 / 2;
        double zeta4 = (E2 - gamma2) * (E2 - gamma2) + alpha * alpha * C2 / 4;
        double aLn = (Eg2 - E02) * E2 + Eg2 * C2 - E02 * (E02 + 3 * Eg2);
        double aAtan = (E2 - E02) * (E02 + Eg2) + Eg2 * C2;
        double k = A / (Math.PI * zeta4);
        double re =
              k * C * aLn / (2 * alpha * E0) * Math.Log((E02 + Eg2 + alpha * Eg) / (E02 + Eg2 - alpha * Eg))
            - k * aAtan / E0 * (Math.PI - Math.Atan((alpha + 2 * Eg) / C) + Math.Atan((alpha - 2 * Eg) / C))
            + 2 * k * E0 / alpha * Eg * (E2 - gamma2) * (Math.PI + 2 * Math.Atan(2 * (gamma2 - Eg2) / (alpha * C)))
            - k * E0 * C * (E2 + Eg2) / E * Math.Log(Math.Abs(E - Eg) / (E + Eg))
            + 2 * k * E0 * C * Eg * Math.Log(Math.Abs(E - Eg) * (E + Eg) / Math.Sqrt((E02 - Eg2) * (E02 - Eg2) + Eg2 * C2));
        return new Complex(re, im);
    }
}

/// <summary>Cauchy: n = A + B/λ² + C/λ⁴ with λ in the given wavelength unit, k = 0; f = n².</summary>
public sealed class CauchyDispersion(string unit, double a, double b, double c, double[]? range) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double l = Units.FromWavelengthNm(wavelengthNm, unit);
        if (range is not null && (l < range[0] || l > range[1]))
            throw new StackFormatException($"{l:G6} {unit} is outside the Cauchy formula's range [{range[0]}, {range[1]}] {unit}");
        double l2 = l * l, n = a + b / l2 + c / (l2 * l2);
        return n * n;
    }
}

/// <summary>A conductivity σ in S/m as a contribution to ε: f = iσ/(ωε₀) (exp(−iωt)).</summary>
public sealed class ConductivityDispersion(double sigmaSPerM) : Dispersion
{
    // σ / (ω ε0) = σ Z0 λ / (2π), λ in metres, as ConductorMaterial.
    public override Complex Eps(double wavelengthNm) =>
        Complex.ImaginaryOne * sigmaSPerM * Material.Z0 * wavelengthNm * 1e-9 / (2 * Math.PI);
}

/// <summary>
/// One of the two scalars of the Polder tensor, μ or κ, exactly as <see cref="FerriteMaterial"/>
/// computes them; the ferrite written as terms puts μ on the xx and yy places of the μ block and
/// κ on [[0, −i], [i, 0]].
/// </summary>
public sealed class PolderDispersion(double fourPiMsGauss, double h0Oe, double alpha, double gammaMHzPerOe, bool kappa) : Dispersion
{
    public override Complex Eps(double wavelengthNm)
    {
        double f = Units.C / (wavelengthNm * 1e-9) / 1e6;   // MHz
        Complex w0 = new Complex(gammaMHzPerOe * h0Oe, -alpha * f);
        double wm = gammaMHzPerOe * fourPiMsGauss;
        Complex den = w0 * w0 - f * f;
        return kappa ? f * wm / den : 1 + w0 * wm / den;
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
