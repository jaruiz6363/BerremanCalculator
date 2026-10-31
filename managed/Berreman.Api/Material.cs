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
