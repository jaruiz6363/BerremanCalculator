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

QWOT and FWOT thicknesses (contract, Section 1a) are not in version 1.

## Materials

Each material is an object in `materials`. It is either a constant, the same at every wavelength
of the sweep, or one of the models below. Tabulated and formula dispersion (`table`, `sellmeier`,
…) is refused until it is built, never approximated.

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
