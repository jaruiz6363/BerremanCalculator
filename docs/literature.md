# Examples from the literature

The papers whose results the program is to reproduce, what each gives, and whether its inputs are
complete. Examples whose inputs are incomplete are still useful; the assumptions made to complete
them are written beside the result.

There are three kinds of check:

- **Closed form**: the paper gives a formula, and any input can be compared exactly.
- **Numbers**: the paper states values, in a table or in the text.
- **Curve**: only a plotted curve, compared by reading the plot.

## In hand

### Berreman (1972)

D. W. Berreman, "Optics in Stratified and Anisotropic Media: 4×4-Matrix Formulation," *J. Opt.
Soc. Am.* **62**, 502–510 (1972).

The formulation the program is built on. Its conventions: exp(−iωt), Gaussian units, field vector
(E_x, H_y, E_y, −H_x). Its examples are closed forms; it gives no numerical results.

| Example | Equations | Check |
|---|---|---|
| Orthorhombic slab, principal axes along x, y, z | Δ (37), P(h) in closed form (41), eigenvalues and modes (42–44) | Closed form |
| Optically active isotropic medium (Drude) | Δ (47), secular equation (48–51); normal incidence (52–55) | Closed form |
| Faraday rotation (Born) | Δ (56), roots (57–59), modes (60) | Closed form |
| Cholesteric liquid crystal, Oseen–de Vries, normal incidence | Δ(z) (62), eigenvalues (70), the reflection band near ω ≈ cβ/√ε (71) | Closed form |
| A slab of zero thickness | (87–90) reduce to Fresnel's equations | Closed form |

The optically active example needs ρ and ρ'; the contract's 6×6 material matrix carries them.

### Park et al. (2002)

J. R. Park, G. Ryu, J. Byun, H. Hwang, S. T. Kim and I. Kim, "Numerical Modeling and Simulation of
a Cholesteric Liquid Crystal Polarizer," *Optical Review* **9**, 207–212 (2002).

Conventions: exp(+iωt) and ε = ε' − iε'', the opposite of ours. Its material constants are
converted by complex conjugation.

| Example | Inputs | Check |
|---|---|---|
| Δ of a biaxial film with in-plane axes (Eq. 5) | any | Closed form |
| P(h) of a uniaxial slab, optic axis in plane at azimuth φ (Eq. 12) | any | Closed form, entry by entry |
| P(h) of a uniaxial slab, optic axis along z (Eq. 13) | any | Closed form |
| Fig. 1: one cholesteric layer, R and T, 400–900 nm | 8°, pitch 365 nm, 2695 nm thick, dispersive ε∥ and ε⊥ given | Curve. Not given: outer media, incident polarisation, handedness, starting azimuth. The curves are averaged over a 6 nm Gaussian spectrum. |
| Fig. 3: six-layer cholesteric polariser with compensator and quarter-wave film | pitches and thicknesses of all six layers, both films' constants | Curve. Not given: the films' azimuths and the media between films. Cannot be recomputed exactly. |

### Ting et al. (2006)

C.-L. Ting, T.-H. Lin, C.-C. Liao and A. Y. G. Fuh, "Optical simulation of cholesteric liquid
crystal displays using the finite-difference time-domain method," *Opt. Express* **14**,
5594–5606 (2006).

An FDTD paper, but its Fig. 2 is compared there with a Berreman 4×4 program. It gives the director
tensor explicitly.

| Example | Inputs | Check |
|---|---|---|
| Fig. 2: cholesteric, reflectance | n_e 1.616, n_o 1.494, pitch 338 nm, thickness 12 pitches | Curve. Not given: angle (presumably normal), polarisation (a peak near 0.5 suggests unpolarised), outer media. |
| Band centre and width | λ₀ = n̄P cos Θ, Δλ = ΔnP: 525.6 nm and 41.2 nm for Fig. 2 | Numbers, approximate formulas |
| Fig. 3: with 0.1 mm glass substrates | n_g 1.533, cell gap 5 µm, three polarisation cases | Curve. Needs incoherent substrates. |
| Figs. 4, 6, 7, 18 | n_e, n_o, pitch, thickness; Fig. 6 at 0°, 30°, 45° | Curves. Outer media not given. |

Figs. 5 and 9–17 vary laterally and are outside a stratified model.

### Catarina et al. (2020)

G. Catarina, N. M. R. Peres and J. Fernández-Rossier, "Magneto-optical Kerr effect in spin split
two-dimensional massive Dirac materials," *2D Mater.* **7**, 025011 (2020).

Conventions: exp(−iωt), as ours. ε = [[εxx, εxy, 0], [−εxy, εxx, 0], [0, 0, εzz]].

| Example | Inputs | Check |
|---|---|---|
| Polar Kerr effect of a magnetic film between air and a substrate, normal incidence (Eqs. B.13–B.16) | any εxx, εxy, d, λ, εr | Closed form: r± for circular polarisation, Kerr rotation and ellipticity |
| The two-dimensional sheet limit (Eqs. B.7, B.11, B.17–B.19) | ε = 1 + iσ/(ωd) | Closed form, as d → 0 |

Its MoS₂ figures need a conductivity model the paper does not give in closed form, so they are not
used.

### Not usable as solver tests

- R.-C. Ma, L.-L. Gao, T. Zhang and Y. Jin, Proc. SPIE 10452, 104525A (2017). It uses a
  non-standard static magnetisation term and no permittivity tensor.
- D. A. Allwood, G. Xiong, M. D. Cooke and R. P. Cowburn, *J. Phys. D* **36**, 2175–2182 (2003).
  It is an instrument model, with no stack or optical constants. Its Eqs. 10–11 could test a
  polariser–analyser model built on the program's Jones output, later.
- M. S. Heimburger, "A Magneto-Optical Kerr Effect Study," BS thesis, College of William and Mary
  (2015). Measurements only.

## To obtain

| Paper | Why | Availability |
|---|---|---|
| N. C. Passler and A. Paarmann, "Generalized 4×4 matrix formalism for light propagation in anisotropic stratified media: study of surface phonon polaritons in polar dielectric heterostructures," *J. Opt. Soc. Am. B* **34**, 2128 (2017), with the erratum in *JOSA B* **36**, 3246 (2019). | A modern formulation that addresses the isotropic-layer degeneracy and gives fields against depth. Its examples include hyperbolic media and the Otto geometry. The erratum corrects birefringent films. | Open: arXiv 1707.00462 |
| M. Mansuripur, "Analysis of multilayer thin-film structures containing magneto-optic and anisotropic media at oblique incidence using 2×2 matrices," *J. Appl. Phys.* **67**, 6466–6475 (1990). | Magneto-optic and birefringent multilayers at oblique incidence, by an independent formulation. | Open: on the author's website |
| P. Yeh, "Optics of anisotropic layered media: a new 4×4 matrix algebra," *Surf. Sci.* **96**, 41 (1980). | The other classic 4×4 formulation. | Citation not yet confirmed |
| M. Schubert, "Polarization-dependent optical parameters of arbitrarily anisotropic homogeneous layered systems," *Phys. Rev. B* **53**, 4265 (1996). | Generalised ellipsometry: Ψ, Δ and the normalised Jones elements for anisotropic samples. | Citation not yet confirmed |

Each is read in full before it is used. Programs published with a paper may be run as independent
checks, but no code is taken from them.

## Order

1. The closed forms: Berreman's five, Park's Eqs. 5, 12 and 13, and Catarina's polar Kerr film.
   These test the engine exactly and come first.
2. Passler and Paarmann, and Mansuripur: numerical examples at oblique incidence, with absorbing,
   magneto-optic and birefringent layers.
3. The cholesteric curves of Ting (Fig. 2 first) and Park (Fig. 1), which test sublayer
   discretisation, with the missing inputs stated.
4. Schubert's generalised ellipsometry, which tests the derived outputs.
