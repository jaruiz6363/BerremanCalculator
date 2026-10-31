# The contract

This document fixes what the engine computes, in what conventions, and how C# and C++ divide the
work and the memory. It is agreed before any engine code exists, because it is the most expensive
thing to change later.

Nothing here is implemented yet. Decisions still open are marked **Open**.

## 1. Conventions

These are stated with every result the program writes.

| | Convention |
|---|---|
| Time dependence | exp(−iωt), as Berreman (1972). |
| Complex index | ñ = n + ik, with k ≥ 0 for an absorbing medium; ε = ñ² for an isotropic medium. |
| Axes | z is normal to the layers and points from the incident medium into the stack. The first interface is at z = 0. The plane of incidence is the x–z plane. |
| Tangential wavevector | ξ = k_x / k₀, the same in every medium. For an isotropic incident medium, ξ = n_inc sin θ. |
| Units | Lengths in nanometres, wavelengths in vacuum. The magnetic field is normalised as H̃ = Z₀H, so E and H̃ have the same units and every material constant is dimensionless. |
| Field vector | ψ = (E_x, H̃_y, E_y, −H̃_x), Berreman's ordering (his Eq. 23), so dψ/dz = i k₀ Δ ψ. |
| Other conventions | Data from papers that use exp(+iωt), and so n − ik, is converted by complex conjugation of the material constants when it is entered. The sample file records the source's convention. |

**s and p.** For each plane wave in an isotropic medium, ŝ = ŷ and p̂ is chosen so that (p̂, ŝ, k̂) is
right-handed. This one rule applies to incident, reflected and transmitted waves alike. A
consequence: at normal incidence on any isotropic interface, r_pp = −r_ss. **Open:** whether that
is the convention used by the ellipsometry examples we reproduce. If it is not, Ψ and Δ are
converted on output; the internal definition does not change.

**Euler angles.** A material's principal frame is rotated into the laboratory frame by z–x–z Euler
angles (φ, θ, ψ): ε_lab = R ε_principal Rᵀ with R = R_z(φ) R_x(θ) R_z(ψ), and the same for μ and the
optical-activity tensors. An azimuth of the plane of incidence is a rotation of the whole sample
about z, applied the same way. **Open:** the sense of each rotation is fixed when the first
uniaxial example is reproduced, and is then written here with a figure.

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
| Magnetic | μ ≠ I |

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

## 4. How a layer is propagated

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
4. **Fields against depth**, at depths the caller chooses (Section 6).

Derived in C# from the above:

5. **Mueller matrices** in reflection and transmission.
6. **Ellipsometric quantities**: Ψ and Δ, and the normalised Jones or Mueller elements of
   generalised ellipsometry for anisotropic samples.

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
| `thickness` | n_layers, real | C# |
| `points` | n_points × (wavelength index, ξ), mixed | C# |
| `field_depths` | n_depths, real; may be empty | C# |
| `jones` | n_points × 8 | native |
| `power` | n_points × 2 incident modes × 3 (R, T, A), real | native |
| `layer_absorption` | n_points × 2 × n_layers, real; may be skipped | native |
| `fields` | n_points × 2 × n_depths × 6; may be skipped | native |
| `diagnostics` | n_points × (status, worst residual, worst condition number) | native |
| `scratch` | the size `bc_plan` returns, per worker | C# |

Complex numbers are interleaved (re, im), the layout of `System.Numerics.Complex`, so arrays pass
with no conversion. An output that is not requested gets a null pointer and costs nothing.

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
