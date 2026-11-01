# The stack file

A stack file is JSON. It describes the materials, the stack, the units and the sweep to evaluate,
and it records where the example comes from. `berreman evaluate <file>` and the MCP tool
`evaluate_stack` both read it. This is version 1.

```json
{
  "format": "berreman-stack",
  "version": 1,
  "title": "Air / glass",
  "source": "the paper, figure or table this reproduces",
  "notes": "assumptions made where the source is incomplete",
  "units": { "length": "nm", "spectral": "nm" },
  "materials": {
    "air":   { "n": 1 },
    "glass": { "n": 1.5 }
  },
  "incident": { "material": "air" },
  "layers": [ { "material": "glass", "thickness": 100 } ],
  "exit": { "material": "glass" },
  "sweep": { "spectral": 633, "angle_deg": { "from": 0, "to": 80, "step": 10 } }
}
```

Comments (`//`) and trailing commas are accepted. Every error names the place in the file it comes
from.

## Units

| Key | Choices |
|---|---|
| `units.length` | `nm`, `um`, `mm`, `cm`, `m`; applies to every thickness |
| `units.spectral` | wavelength `nm`, `um`, `mm`, `cm`, `m`; frequency `GHz`, `THz`; wavenumber `cm-1`; photon energy `eV` |

Thicknesses are physical. There is no QWOT or FWOT (contract, Section 1a): an anisotropic layer has no
one index to count a quarter wave with.

## Materials

Each material is an object in `materials`. It is a constant (the same at every wavelength), a
dispersive material, or one of the models below.

ε is given in exactly one of these ways:

| Key | Meaning |
|---|---|
| `n` | complex index n + ik, as a number or `[n, k]`; ε = n² |
| `eps` | ε as a complex scalar, or a 3×3 array of complex entries |
| `eps_r`, `tan_delta` | microwave form: ε = ε′(1 + i tan δ) |
| `no`, `ne` | uniaxial: principal indices, optic axis along the principal z |
| `nx`, `ny`, `nz` | biaxial: principal indices |

μ is given as `mu` (scalar or 3×3) or `mu_r` with `tan_delta_mu`. It defaults to 1.

The magnetoelectric blocks `rho` and `rho_prime` are 3×3 complex arrays in the units of the
[derivation note](derivation-delta.md), Section 1. A paper's own parameters must be converted, and
the conversion recorded in `notes`.

A complex number is a plain number (real) or `[re, im]`.

**Convention.** `"convention": "exp(+jwt)"` marks data given with the engineering time dependence
(ε = ε′ − jε″). It is conjugated on entry. The default is `"exp(-iwt)"`, the program's own
convention.

### Dispersion

A material whose ε depends on wavelength gives one dispersion law for all three principal values,
or one law per principal axis:

```json
"KRS5": { "sellmeier": { ... } },
"SiC":  { "uniaxial": { "ordinary": { "phonon": { ... } }, "extraordinary": { "phonon_additive": { ... } } } },
"X":    { "biaxial": { "x": { ... }, "y": { ... }, "z": { ... } } }
```

The uniaxial optic axis, and the biaxial axes, are the principal axes. `euler_deg` turns them. A
principal value may also be a constant, `{ "n": … }` or `{ "eps": … }`. `mu` is an optional constant
scalar. Every law has a `unit`, the spectral unit its parameters are in.

| Law | Parameters | ε |
|---|---|---|
| `sellmeier` | `unit`; `a` (default 0); `terms`: `[{ "b", "c2" }]` or `[{ "b", "c" }]`; optional `range` [min, max] | n² = 1 + a + Σ b λ² / (λ² − c2), with c2 = c² when a resonance wavelength c is given. This is refractiveindex.info's "formula 2". |
| `phonon` | `unit`; `eps_inf`; `modes`: `[{ "to", "lo", "gamma_to", "gamma_lo" }]` | ε∞ Π (ω_L² − ω² − iγ_L ω) / (ω_T² − ω² − iγ_T ω), the factorised TO–LO form |
| `phonon_additive` | `unit`; `eps_inf`; `modes`: `[{ "to", "lo", "gamma" }]` | ε∞ [1 + Σ (ω_T² − ω_L²) / (ω² − ω_T² + iγω)] |
| `table` | `unit`; `columns`: `["x", "n", "k"]` or `["x", "eps_re", "eps_im"]`; `data`: rows | Linear interpolation in x. A table must be monotonic in x. |
| `glass` | `catalog` (SCHOTT, OHARA, HOYA, CDGM, SUMITA); `name` | n² from the vendor's catalogue ([materials](../materials/README.md)); k = 0. The glass is named, never copied. |

All the laws are in the program's exp(−iωt) convention, with losses positive. Outside a table, or
outside a formula's `range`, the evaluation is refused rather than extrapolated.

`lorentz`, `drude`, `cauchy`, `conductivity` and `polder`, described under "General materials", may
also be used here, as a whole material's law or as one principal value's.

### General materials

A material may instead be a list of terms. Each term is a law times a constant shape, and M is
their sum ([material-model.md](material-model.md)). Any element of the 6×6 matrix may disperse,
and each oscillator may have its own direction. This is how a monoclinic or triclinic crystal, a
dispersing gyration or a chiral medium is written.

```json
"Ga2O3": { "terms": [
  { "eps": [[3.21, -0.08, 0], [-0.08, 3.75, 0], [0, 0, 3.71]] },
  { "lorentz": { "unit": "cm-1", "amplitude": 266.2, "center": 743.48, "gamma": 11.0 }, "axis_deg": 47.8 },
  { "lorentz": { "unit": "cm-1", "amplitude": 544.9, "center": 663.17, "gamma": 3.2 }, "axis": [0, 0, 1] },
  { "drude": { "unit": "cm-1", "plasma": 1058.3, "gamma": 370 }, "axis": [1, 0, 0] }
] }
```

**A term** has at most one law and at least one shape. A term with no law is a constant. A term
may also have a `label`, which is shown and otherwise ignored. A material written as terms has
nothing else beside them, except `source` and `notes`.

| Shape key | The shape |
|---|---|
| `eps`, `mu`, `rho`, `rho_prime` | That 3×3 block: a complex scalar (times the identity) or a 3×3 array. Several may be given together. |
| `matrix` | The whole 6×6, alone |
| `axis` | [x, y, z], normalised: êêᵀ in the ε block |
| `axis_deg` | α: ê = (cos α, sin α, 0); or [α, β], lifted by β towards z |

When no term gives μ, μ = 1.

| Law | Parameters | f |
|---|---|---|
| `lorentz` | `unit` (a frequency: cm-1, eV, THz, GHz); `center`; `gamma` (default 0); `strength` S or `amplitude` A | S/(ω₀² − ω² − iγω), S = A² |
| `drude` | `unit` (a frequency); `plasma`; `gamma` (default 0) | −ω_p²/(ω² + iγω) |
| `cauchy` | `unit` (a wavelength); `a`; `b`, `c` (default 0); optional `range` | n², n = a + b/λ² + c/λ⁴ |
| `conductivity` | `sigma_s_per_m` | iσ/(ωε₀) |
| `polder` | as the ferrite model, and `part`: `"mu"` or `"kappa"` | the Polder μ or κ |
| `sellmeier`, `phonon`, `phonon_additive`, `table` | as above | the law's ε |

A material that is not passive, or not reciprocal, is evaluated as written. The editor reports both
properties ([material-model.md](material-model.md), "Checks").

### Models

A material with a `model` key is computed at each frequency. Its parameters are physical, so it
takes no `convention`.

| `model` | Parameters | Gives |
|---|---|---|
| `ferrite` | `four_pi_ms_gauss`; `h0_oe` (the internal static field); `alpha` (default 0); `gamma_mhz_per_oe` (default 2.8); ε in any of the ways above | The Polder μ tensor with the bias along the principal z, in the form of Kodera and Caloz (*IEEE AWPL* 17, 2018, Eq. 3), conjugated into exp(−iωt): [[μ, −iκ, 0], [iκ, μ, 0], [0, 0, 1]], μ = 1 + ω₀ω_m/(ω₀² − ω²), κ = ωω_m/(ω₀² − ω²), loss by ω₀ → ω₀ − iαω. Rotate the bias with `euler_deg`. |
| `conductor` | `sigma_s_per_m`; `eps_background` (default 1) | ε = ε_background + iσ/(ωε₀) |
| `resistive_sheet` | `ohm_per_square` | A sheet of R_s, represented by the layer holding it: ε = 1 + iZ₀/(R_s k₀ t), with t that layer's thickness. Keep the layer thin; the error is of order k₀t. |

Papers write the Polder tensor with either sign of the off-diagonal term, which reverses the sense
of Faraday rotation. A paper's own tensor can be entered as a constant `mu` instead of a model.

## Layers and outer media

`incident` and `exit` name a material. Each entry of `layers` has:
- `material`;
- `thickness`, in `units.length`;
- optionally `euler_deg`: `[φ, θ, ψ]`, the rotation R = R_z(φ) R_x(θ) R_z(ψ) taking the material's
  principal frame to the laboratory frame (contract, Section 1).

The outer media may be rotated too.

`"coherent": false` is refused until incoherent layers are built.

### Twisted layers (cholesterics)

A layer may twist about z through its thickness:

```json
{ "material": "LC", "thickness": 4056, "euler_deg": [90, 90, 0],
  "twist": { "pitch": 338, "handedness": "right", "start_deg": 0, "sublayers_per_pitch": 128 } }
```

- **The twist.** At depth z into the layer, the material, already turned by `euler_deg`, is turned
  further about z by φ(z) = start_deg ± 360° · z/pitch.
- **Pitch and handedness.** The pitch is one full turn. `"right"` makes φ increase with z, a
  right-handed helix as in Berreman's Eq. 61; `"left"` makes it decrease.
- **Sublayers.** The layer is evaluated as uniform sublayers, each at the angle of its midpoint.
  Give `sublayers_per_pitch` (default 64) or a total `sublayers`.
- **The check.** Every stack with a twisted layer is evaluated twice, the second time with double
  the sublayers. The largest change in R, T or A is reported with the results. The midpoint rule
  is second order, so the error of the coarser evaluation is about 4/3 of that change.
- **Absorption** is reported for the twisted layer as a whole.

For a nematic director lying in the plane of the layer, give the material as uniaxial (`no`, `ne`),
whose optic axis is its principal z. Then `euler_deg` [90, 90, 0] turns that axis to x.

## Sweep

| Key | Meaning |
|---|---|
| `spectral` | in `units.spectral` |
| `angle_deg` | angle of incidence in the incident medium, in [0, 90). Needs an isotropic, lossless incident medium. |
| `xi` | ξ = k_x/k₀ directly; for any incident medium. Give this or `angle_deg`, not both. |
| `azimuth_deg` | rotation of the whole sample about z; default 0 |

Each sweep value is a number, an array, `{"from", "to", "step"}` or `{"from", "to", "count"}`.
Points run over the spectral values, then azimuths, then angles.

## Results

`berreman evaluate` prints one row per point with these columns:
- R, T and A for each incident wave;
- optionally the Jones coefficients (`--jones`). Their first subscript is the incident wave, so
  r_ps is p in, s out;
- optionally Ψ and Δ (`--ellipsometry`), from r_pp/r_ss = tan Ψ e^{iΔ};
- optionally the absorption in each layer (`--absorption`);
- a status.

With isotropic outer media the waves are p and s. With anisotropic ones they are the media's own
modes 0 and 1, labelled as the contract, Section 3, describes.

A point that fails shows NaN and its status. A wave that carries no power shows NaN for its powers.
