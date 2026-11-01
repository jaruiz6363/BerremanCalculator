# The material model

A material is what the engine receives at each wavelength: the 6×6 matrix
M = [[ε, ρ], [ρ′, μ]] of the contract, Section 2. This note says how C# builds M from what a person
writes, and why it is built that way. The file syntax is in [stack-format.md](stack-format.md),
"General materials".

## The form

    M(λ) = Σₖ fₖ(λ) Tₖ

Each term k is a scalar law fₖ, which is the constant 1 or one of the dispersion laws, times a
constant complex 6×6 shape Tₖ.

**Why this form.** A material is often called isotropic, uniaxial or biaxial, but that does not
describe what can disperse:
- **Monoclinic and triclinic crystals.** Each oscillator is polarised along its own direction ê.
  Its term is f(λ) êêᵀ, and the principal axes of the sum turn with wavelength. Schubert et al.
  (2016) wrote β-Ga₂O₃ this way, and it is the sample that checks the model.
- **Magneto-optic gyration.** A dispersing antisymmetric imaginary ε has the shape
  [[0, −i, 0], [i, 0, 0], [0, 0, 0]].
- **A ferrite.** Its Polder μ and κ are two laws, on two shapes in the μ block.
- **Chirality.** ρ and ρ′ terms.
- **Any element.** A shape may be any 6×6 matrix, so any of the 36 elements may disperse.

A diagonal material is the special case where every shape is diagonal.

**Nothing is assumed about symmetry.** The engine evaluates any M (contract, Section 2). The
program builds M from the terms exactly as given, and only reports its properties.

## Laws

All are in exp(−iωt), with losses positive.

| Law | f |
|---|---|
| none | 1 |
| `sellmeier` | n² = 1 + a + Σ b λ²/(λ² − c²) |
| `cauchy` | n² with n = A + B/λ² + C/λ⁴ (ThinFilmDesigner's form) |
| `lorentz` | S/(ω₀² − ω² − iγω), or with amplitude A, S = A² |
| `drude` | −ω_p²/(ω² + iγω) |
| `phonon` | ε∞ Π (ω_L² − ω² − iγ_Lω)/(ω_T² − ω² − iγ_Tω) |
| `phonon_additive` | ε∞ [1 + Σ (ω_T² − ω_L²)/(ω² − ω_T² + iγω)] |
| `table` | n + ik or ε′ + iε″ against λ, linear |
| `glass` | n² of a catalogue glass, read from the vendor's AGF |
| `conductivity` | iσ/(ωε₀) |
| `polder` | the ferrite's μ or κ |

Lorentz and Drude are written in a frequency unit (cm⁻¹, eV, THz, GHz). Cauchy and Sellmeier are
written in a wavelength unit. A law outside its stated range, or beyond the ends of a table, is
refused rather than extrapolated.

Still to add: Tauc–Lorentz, for amorphous films above the gap.

## Shapes

A term's shape is given by the blocks it fills (`eps`, `mu`, `rho`, `rho_prime`, each a scalar
times the identity or a 3×3 array), by a whole 6×6 `matrix`, or by a direction:
- `axis` [x, y, z], normalised, gives êêᵀ in the ε block.
- `axis_deg` α is the direction (cos α, sin α, 0).
- `axis_deg` [α, β] lifts that direction by β towards z.

A term with no law is a constant, the M₀ of the sum.

Directions are in the material's own frame. The layer's Euler angles turn the whole M, as for every
material.

When no term gives μ, μ = 1. ε has no default: a material must give it.

## Shorthand

The short forms of the stack file are lists of terms:

| Form | Terms |
|---|---|
| `n`, `eps`, `no`/`ne`, `nx`/`ny`/`nz`, `mu`, `rho`, ... | one constant term |
| a law alone | the law on ε = 1 |
| `uniaxial` | the ordinary law on diag(1, 1, 0) and the extraordinary law on diag(0, 0, 1) |
| `biaxial` | three laws on the three diagonal places |
| `model: conductor` | the background, and `conductivity` on ε = 1 |
| `model: ferrite` | ε; 1 on μ_zz; Polder μ on μ_xx and μ_yy; Polder κ on [[0, −i], [i, 0]] |

The short forms stay in the file format: they are what most materials need, and what people
write. `MaterialTerms.ToTerms` writes any of them as terms, so the editor can show every material
as terms and a person can add a term to any of them.

The managed tests check that every material in every sample evaluates, written as terms, to
exactly the same M, bit for bit.

The resistive sheet is the one exception. Its ε depends on the thickness of the layer that holds
it, so it stays a model.

## Libraries

The databases a material can come from are listed in [materials/README.md](../materials/README.md):
- **ThinFilmDesigner's two databases.** A database material is copied into the stack, which then
  owns it.
- **Five vendors' glass catalogues.** A glass is named, never copied, so its index always comes
  from the vendor's data.
- **The user's own file.** Any material can be saved there, terms included.

A crystal listed as `X (o)` and `X (e)` is also offered as one uniaxial material.

## Checks

These are reported to the user and never enforced:
- **Passivity.** In exp(−iωt), a medium absorbs power for every field when (M − M†)/2i is positive
  semidefinite. The least eigenvalue of that matrix is reported. A negative value means gain for
  some polarisation, which a measured fit sometimes shows near a resonance.
- **Reciprocity.** A reciprocal medium has ε = εᵀ, μ = μᵀ and ρ′ = −ρᵀ. The largest departure is
  reported, relative to the largest element of M. Magneto-optic and Faraday-active media are
  non-reciprocal by nature.
- **Principal axes.** The eigenvectors of the real symmetric part of ε, which the editor plots
  against wavelength.
