# The contract

This document fixes what the engine computes, in what conventions, and how C# and C++ divide the
work and the memory. It is agreed before any engine code exists, because it is the most expensive
thing to change later.

It was written first, and the engine, the API and the programs now implement it. Where a decision is
still open it is marked **Open**. Where the code and this document disagree, the ABI header
`native/include/berreman.h` is what ships, and this document is wrong.

## 1. Conventions

These are stated with every result the program writes.

| | Convention |
|---|---|
| Time dependence | exp(−iωt), as Berreman (1972). |
| Complex index | ñ = n + ik, with k ≥ 0 for an absorbing medium; ε = ñ² for an isotropic medium. |
| Axes | z is normal to the layers and points from the incident medium into the stack. The first interface is at z = 0. The plane of incidence is the x–z plane. |
| Tangential wavevector | ξ = k_x / k₀, the same in every medium. For an isotropic incident medium, ξ = n_inc sin θ. |
| Units | The engine is scale-free: only k₀d, the vacuum wavenumber times the thickness, enters, so it takes thicknesses and vacuum wavelengths in any one length unit. The units a person sees are C#'s business (Section 1a). The magnetic field is normalised as H̃ = Z₀H, so E and H̃ have the same units and every material constant is dimensionless. |
| Field vector | ψ = (E_x, H̃_y, E_y, −H̃_x), Berreman's ordering (his Eq. 23), so dψ/dz = i k₀ Δ ψ. |
| Other conventions | Data from papers that use exp(+iωt), and so n − ik, is converted by complex conjugation of the material constants when it is entered. The sample file records the source's convention. |

**s and p.** For each plane wave in an isotropic medium, ŝ = ŷ and p̂ is chosen so that (p̂, ŝ, k̂) is
right-handed. This one rule applies to incident, reflected and transmitted waves alike. A
consequence: at normal incidence on any isotropic interface, r_pp = −r_ss. This is also the
convention of Schubert (*Phys. Rev. B* **53**, 4265 (1996)): his incident and reflected field
vectors (Eqs. 37–39) and his Eq. (C5) give r_pp = −r_ss at normal incidence. Ψ and Δ are therefore
defined as his: ρ = tan Ψ e^{iΔ} = r_pp/r_ss for isotropic samples (his Eq. 4). For anisotropic
samples, they are given by his normalised elements R_pp = r_pp/r_ss, R_ps = r_ps/r_pp and
R_sp = r_sp/r_ss (his Eq. 8). The subscripts are (incident, outgoing), so r_sp = B_p/A_s.

**Euler angles.** A material's principal frame is rotated into the laboratory frame by z–x–z Euler
angles (φ, θ, ψ): ε_lab = R ε_principal Rᵀ with R = R_z(φ) R_x(θ) R_z(ψ), and the same for μ and the
optical-activity tensors. An azimuth of the plane of incidence is a rotation of the whole sample
about z, applied the same way. Each rotation is active and right-handed:

```
R_z(a) = [[cos a, −sin a, 0], [sin a, cos a, 0], [0, 0, 1]]
R_x(a) = [[1, 0, 0], [0, cos a, −sin a], [0, sin a, cos a]]
```

This is the matrix A of Yeh (*J. Opt. Soc. Am.* **69**, 742 (1979), Eq. 2), which Passler and
Paarmann also use. Multiplied out, its first row is (cos ψ cos φ − cos θ sin φ sin ψ,
−sin ψ cos φ − cos θ sin φ cos ψ, sin θ sin φ). For θ = ψ = 0, the principal x axis lies along
(cos φ, sin φ, 0). Schubert (1996) describes the same rotation through Goldstein, but does not print
the matrix, so his examples are checked against this one when used.

## 1a. Units shown and typed

The program covers the ultraviolet to microwaves, so the units are wider than ThinFilmDesigner's.
Each is saved with the stack. Changing one converts every number it applies to, so the stack stays
physically the same.

| Setting | Choices |
|---|---|
| Spectral axis | Wavelength (nm, µm, mm, cm, m), frequency (GHz, THz), wavenumber (cm⁻¹), photon energy (eV) |
| Length (thicknesses) | nm, µm, mm, cm, m |
| Layer thickness | Physical, always |
| Angles | Degrees |

**There is no QWOT or FWOT** (decided 2026-10-31). An optical thickness counts quarter waves of one
index, and an anisotropic layer has no one index: its two modes travel at different speeds, and
which one counts would be a convention to explain rather than a convenience. Thicknesses are
physical everywhere: in the file, the GUI, the CLI and the MCP server.

**Material constants in the microwave form** are accepted as well as n and k: ε′ with the loss
tangent tan δ = ε″/ε′, and μ′ with tan δ_μ, including tensors (for example a magnetised ferrite's
μ tensor). C# converts them to the 6×6 matrix (Section 2) in the exp(−iωt) convention.

## 2. The material model

Each layer, at each wavelength, is a complex 6×6 matrix M in Berreman's form (his Eqs. 3–6):

```
D̃ = ε E + ρ H̃
B̃ = ρ' E + μ H̃
```

with ε, μ, ρ, ρ' each 3×3 complex and dimensionless. This one form covers:

| Material | How M is filled |
|---|---|
| Isotropic | ε = ñ² I, μ = I, ρ = ρ' = 0 |
| Uniaxial, biaxial | ε = diag(εx, εy, εz) in the principal frame, rotated by the Euler angles |
| General anisotropic | ε as nine complex numbers |
| Magneto-optic (Faraday, Kerr) | off-diagonal ε, as Berreman's Eq. 13 |
| Optically active | ρ and ρ', as Berreman's Eq. 12 |
| Magnetic | μ ≠ I, including the gyrotropic μ tensor of a magnetised ferrite (microwaves) |

**A lossless material must reach the engine exactly Hermitian.** The engine tests losslessness
exactly (derivation note, Section 7): an incident medium that is not exactly Hermitian is refused
as absorbing. Rotating a symmetric tensor in floating point gives ε_ij and ε_ji that differ in the
last bit. So C# computes each block's upper triangle and mirrors it (conjugated) into the lower one,
rather than computing both.

**The C++ engine sees only M.** Dispersion formulas, tables, Euler rotation and the material
types above are all C#, so the engine has one code path for every material. Material data uses
ThinFilmDesigner's material format where that format can express it.

**Layers whose properties vary with depth** (cholesteric and twisted nematic liquid crystals,
graded layers) are divided by C# into thin homogeneous sublayers. The number of sublayers is a
setting, and the program reports how the result changes when it is doubled.

## 3. The stack

```
incident medium | layer 1 | layer 2 | ... | layer N | exit medium
```

- Each layer has a thickness, a material, Euler angles and a coherence flag.
- **Both outer media may be anisotropic.** The incident medium must be lossless at the wavelength,
  so that ξ is real and the incident power is defined. If it is not, the evaluation fails loudly.
  The exit medium may absorb.
- For an isotropic incident medium, the caller gives θ and C# computes ξ. For an anisotropic
  incident medium, the caller chooses the incident mode and its angle; C# solves for ξ.
- **Evanescent incident waves.** An incident wave that carries no power (ξ beyond what the
  incident medium supports for it) has no defined R, T or A.
  - Its power and layer-absorption outputs are NaN.
  - Its Jones column is still the amplitude response.
  - If both incident waves are evanescent, the point fails with `BC_INCIDENT_EVANESCENT`.
- **The modes of an anisotropic outer medium** are labelled by the engine:
  - within each pair, the more p-like mode (larger share of |ψ₁|² + |ψ₂|²) comes first;
  - each mode has unit 2-norm;
  - the larger of E_x and E_y is real and positive.

  The Jones coefficients connect these modes.

## 4. How a layer is propagated

> **Superseded in part by [the engine design](engine-design.md).** Individual modes are now computed
> only in the two outer media. Through the stack, the field is carried as a two-dimensional subspace,
> propagated by a matrix exponential in bounded steps and re-orthonormalised after each step. This
> needs no eigenvectors inside the stack, so coincident roots and a defective Δ there are not
> special cases. The direction rule (point 2 below) still holds for the outer media. The aims below
> are unchanged; points 1 and 3 describe the earlier plan.

The legacy code found the four modes of a layer from a closed-form quartic, with fixed tolerances
deciding which roots and eigenvectors to keep. It broke for isotropic layers, where two pairs of
roots coincide, which is the most common case. The new engine does this:

1. **Modes.** The eigenvalues q of Δ are found, and each eigenvector is taken from the null space of
   (Δ − qI), computed with a 4×4 decomposition that returns a two-dimensional null space when two
   roots coincide. The degenerate case is therefore the same code as the general case, and a scaled
   residual decides the null-space dimension, not a fixed tolerance on q.
2. **Direction.** A mode is forward (+z) if Im q > 0, or, when q is real, if the z-component of its
   time-averaged Poynting vector is positive. The legacy code used Re q > 0, which is wrong for a
   tilted optic axis, where the energy and the wave normal can point to opposite sides of the
   interface.
3. **Stacking.** Layers are combined with a scattering-matrix recursion rather than by multiplying
   transfer matrices. A transfer-matrix product contains growing exponentials from evanescent and
   strongly absorbing layers and loses all precision in a thick metal or beyond the critical angle.
   A scattering matrix only ever contains decaying exponentials.

Every evaluation reports the largest eigenvector residual and the condition number of each layer's
mode matrix, so an ill-conditioned case is visible rather than silent.

**Open:** the exact decomposition (QR with column pivoting, or SVD) and the residual threshold. They
are decided in the engine design note, with tests at and near degeneracy.

## 5. Outputs

From the native engine, per evaluation point:

1. **Jones coefficients**, reflection and transmission, 2×2 complex each. With isotropic outer media
   these are r_pp, r_ps, r_sp, r_ss and the t's. With anisotropic outer media they connect the
   media's own modes, and which mode is which is reported alongside.
2. **Reflectance, transmittance and absorptance** for each incident mode, from the z-component of
   the Poynting vector, so they are correct for anisotropic and absorbing exit media.
3. **Absorption in each layer**, from the Poynting flux at the layer's two faces. The layers' sum
   and R + T + A = 1 are checked on every evaluation.
4. **Fields against depth**, at depths the caller chooses (Section 6), per unit incident amplitude:
   (E_x, E_y, E_z, H̃_x, H̃_y, H̃_z) for each incident wave. Depth is measured from the first interface,
   negative in the incident medium, and an interface belongs to the medium behind it.

Derived in C# from the above:

5. **Mueller matrices** in reflection and transmission, each element divided by M11. With J the
   Jones matrix (E_out = J E_in in the (p, s) basis), M = A (J ⊗ J*) A⁻¹ for the Stokes vector
   S0 = |Ep|² + |Es|², S1 = |Ep|² − |Es|², S2 = 2 Re(Ep Es*), S3 = 2 Im(Ep Es*). An isotropic
   sample then gives [[1, −N, 0, 0], [−N, 1, 0, 0], [0, 0, C, −S], [0, 0, S, C]] with N = cos 2Ψ,
   C = sin 2Ψ cos Δ and S = sin 2Ψ sin Δ.
   - **The sign of S3** (the handedness of circular light) varies between books. It is fixed by
     Schubert et al. (2016), Fig. 5. With the β-Ga₂O₃ sample, M41 and M43 take that paper's signs
     at all three azimuths with this choice, and the opposite signs with the other. M42, which was
     not used to choose, then agrees too (docs/literature.md).
   - **What the comparison also confirms.** The paper's Ψ, Δ, p, s and Euler angles are this
     program's, so its off-diagonal elements, which the p sign and the Euler handedness would flip,
     confirm both.
6. **Ellipsometric quantities**: Ψ and Δ, and the normalised Jones elements of generalised
   ellipsometry, r_ps/r_pp and r_sp/r_ss as Ψ_ps, Δ_ps, Ψ_sp, Δ_sp (Schubert 1996, Eq. 8).

Every derived quantity has one name, in Api/Quantities.cs. The GUI, the CLI (`--quantities`) and
the MCP server (`quantities`) all use it.

**Incoherent (thick) layers** are combined in intensity, through Mueller matrices, between coherent
groups of layers. They come after the coherent engine is verified, and until then a stack with an
incoherent layer fails loudly.

## 6. Native calls and memory

The rules in the README apply: C# allocates everything, nothing is dimensioned by a constant, the
code ports to CUDA mechanically, and everything is double precision. The sequence is
ThinFilmDesigner's:

```
1.  C#   computes the plan buffer sizes from counts alone
2.  C#   allocates the plan buffers
3.  C++  bc_plan(...)       checks the stack, chooses a path per point, returns the SCRATCH SIZE
4.  C#   allocates scratch, one block per worker thread
5.  C++  bc_evaluate(...)   over a range of points; called once per worker
```

**Evaluation points.** A point is one (wavelength, ξ, sample orientation). M is supplied per layer
per (wavelength, orientation), not per point, because the angles share it:

| Buffer | Size (complex doubles unless stated) | Filled by |
|---|---|---|
| `M` | (n_layers + 2) × n_wavelength_orientations × 36 | C# |
| `wavelength` | n_wavelength_orientations, real: the vacuum wavelength of each | C# |
| `thickness` | n_layers, real | C# |
| `point_spectral`, `point_xi` | n_points each: the wavelength–orientation index (integer) and ξ (real) | C# |
| `field_depths` | n_depths, real; may be empty | C# |
| `jones` | n_points × 8 | native |
| `power` | n_points × 2 incident modes × 3 (R, T, A), real | native |
| `layer_absorption` | n_points × 2 × n_layers, real; may be skipped | native |
| `fields` | n_points × 2 × n_depths × 6; may be skipped | native |
| `diagnostics` | n_points × (status, worst residual, worst condition number) | native |
| `scratch` | the size `bc_plan` returns, per worker | C# |

Complex numbers are interleaved (re, im), the layout of `System.Numerics.Complex`, so arrays pass
with no conversion. An output that is not requested gets a null pointer and costs nothing. The
exact layout, including the order of media in `M`, is in `native/include/berreman.h`, which is the
ABI; where this section and the header disagree, the header is right.

**Status codes.** Every point gets a status: ok, incident medium absorbing, a layer's mode matrix
singular, or path not implemented. An unimplemented path leaves its outputs untouched.

## 7. Verification

Every result is checked in one of three ways, in this order of strength:

1. **Exact identities.** Zero thickness gives the Fresnel coefficients. An isotropic stack agrees with
   the 2×2 Abelès result, computed independently by ThinFilmDesigner's engine. Lossless stacks
   conserve energy. Reciprocity and the symmetry of a sample rotated about z hold.
2. **Closed forms from the literature.** Berreman (1972) gives exact results for an orthorhombic slab
   (Eq. 41), an optically active medium (Eqs. 48–55), Faraday rotation (Eqs. 56–60) and a cholesteric
   at normal incidence (Eq. 70).
3. **Published numerical examples.** These are listed in [literature.md](literature.md). Each
   becomes a sample file and a test, with the paper's numbers and the agreement reached.

Other open-source Berreman programs may be used as independent checks. No code is taken from them.
