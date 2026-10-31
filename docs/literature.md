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

### Mansuripur (1990)

M. Mansuripur, "Analysis of multilayer thin-film structures containing magneto-optic and
anisotropic media at oblique incidence using 2×2 matrices," *J. Appl. Phys.* **67**, 6466–6475
(1990).

An independent formulation: a 2×2 reflection-matrix recursion, dielectric tensors only (μ = 1).
Conventions: exp[i(k·r − ωt)], n + ik. **Every input of its example is printed**, which makes it
the best numerical test in hand.

| Example | Inputs | Check |
|---|---|---|
| Magneto-optic quadrilayer at 830 nm, from air: SiOx quarter wave (143.2 nm implied) / MO film 20 nm, ε = −4.8984 + 19.415i, ε_xy = −ε_yx = 0.4322 + 0.0058i / SiOx 143.2 nm, n = 1.449 / Al 500 nm, 2.75 + 8.31i / glass, n = 1.5 | complete | |
| Fig. 3: power against depth at normal incidence | | **Numbers**: about 91 % absorbed in the MO layer, 3 % in the aluminium, 6 % reflected |
| Fig. 2: \|r_pp\|, \|r_ss\|, \|r_ps\| = \|r_sp\| against angle, 0–90°, polar magnetisation | | Curve; also the Kerr rotation and ellipticity, whose sign convention is not stated |
| Fig. 4: Kerr rotation against the magnetisation's polar angle | | Stated proportional to cos Θ_M |
| Fig. 5: longitudinal magnetisation, \|r_ps\| largest at 65° | | Curve, and a stated angle; the azimuth is not stated |
| Appendix A: the rotated polar magneto-optic tensor | | Closed form |

### Passler and Paarmann (2017)

N. C. Passler and A. Paarmann, "Generalized 4×4 matrix formalism for light propagation in
anisotropic stratified media: study of surface phonon polaritons in polar dielectric
heterostructures," *J. Opt. Soc. Am. B* **34**, 2128 (2017); read as arXiv 1707.00462v1 (July
2017), which predates the erratum, *JOSA B* **36**, 3246 (2019).

Conventions: exp(−iωt); field vector (E_x, H_y, E_y, −H_x), as ours. Its treatment of coincident
roots holds only for μ a scalar and no optical activity, so it does not cover the whole contract.
Its "T" is |t|², which it says is not the transmittance.

| Example | Inputs | Check |
|---|---|---|
| Figs. 2–4: Otto geometry (KRS5 prism, air gap, sample) at 30°, p-polarised, gaps 2, 3.5, 5.5, 7.5 µm: bare 6H-SiC; 2 µm GaN on SiC; 700 nm SiC / 2 µm GaN / SiC. Reflectance against wavenumber, and \|E_x\| against depth | Incomplete in the paper (no ε∞ or damping, prism index only "≈ 2.4"); **complete with the authors' code**, below | Curves; mode positions stated (for example the SiC surface phonon polariton dip near 910 cm⁻¹) |
| Fig. 5: α-quartz, c along x, y or z, 350–600 cm⁻¹ | Incomplete in the paper (data from Gervais and Piriou, 1975); **tabulated in the authors' code** | Curves |

**The missing parameters are in the authors' Matlab code** (Zenodo, concept DOI
10.5281/zenodo.601496; four versions, 2017-06 to 2019-10, licence CC BY-NC 4.0). They are the same
in every version, so the erratum did not change them. The figures used:

| Material | Model | Parameters (cm⁻¹ unless stated) |
|---|---|---|
| 6H-SiC, ordinary (in-plane) | ε∞ (ω² − ω_L² + iγ_L ω)/(ω² − ω_T² + iγ_T ω) | ε∞ 6.56, ω_T 794.78, ω_L 967.93, γ_T = γ_L = 2.535 |
| 6H-SiC, extraordinary (c axis) | ε∞ [1 + Σ_j (ω_Tj² − ω_Lj²)/(ω² − ω_Tj² + iγ_j ω)], three terms | ε∞ 6.78; main ω_T 783.67, ω_L 962.0, γ 2.535; weak modes ω_T/ω_L 881.0/881.2 and 886/886.1333, γ 1.3 |
| GaN, ordinary | as SiC ordinary | ε∞ 5.04, ω_T 560.1, ω_L 742.1, γ_T = γ_L = 4.0 |
| GaN, extraordinary | as SiC ordinary | ε∞ 5.01, ω_T 537, ω_L 732.5, γ_T = γ_L = 4.0 |
| KRS5 prism | n tabulated against wavenumber, interpolated (pchip) | a table, not a formula |
| α-quartz (Fig. 5) | ε_o and ε_e tabulated, 0–1499 cm⁻¹ | a table, not a formula |

The +iγω terms give Im ε > 0, so the code's convention is exp(−iωt), as ours. The SiC and GaN films
are c-cut: the c axis is along z. The code's test script reproduces Figs. 2–4 exactly: 350–1400
cm⁻¹, 30°, gaps 2, 3.5, 5.5 and 7.5 µm, the stacks KRS5 / vacuum gap / SiC, KRS5 / gap / 2 µm GaN
/ SiC, and KRS5 / gap / 0.7 µm SiC / 2 µm GaN / SiC.

Oscillator parameters are facts and are cited here. The code itself is non-commercial and is not
copied. The KRS5 and quartz tables are not copied into the repository either. **Open:** a KRS5
index from a source the repository can carry. The code may be run as an independent check, and the
2019 version's material library (about 20 polar crystals, including hBN, MoO₃, AlN and Al₂O₃) is a
source of further examples.

### Lakhtakia (2018)

A. Lakhtakia, "Planewave response of a simple Lorentz-nonreciprocal medium with magnetoelectric
gyrotropy," arXiv 1811.05034v1 (2018).

A medium with D = ε₀ε_r E − Γ × H and B = μ₀μ_r H + Γ × E: isotropic ε and μ with magnetoelectric
coupling, so it tests the ρ and ρ' blocks of the 6×6 matrix, at any angle and azimuth.
Conventions: exp(−iωt) implied by ζ = ζ' + iζ'' with ζ'' > 0 dissipative. Its p and s vectors
(Eq. 11) give the same signs of r_p as the contract's.

| Example | Check |
|---|---|
| Reflection by a half space (Section 3) | Closed form, through the auxiliary fields of its Eq. 3 |
| Reflection and transmission by a slab (Section 4) | Closed form or numbers, to be read |

### Passler and Paarmann, erratum (2019)

N. C. Passler and A. Paarmann, "…: erratum," *J. Opt. Soc. Am. B* **36**, 3246–3248 (2019).

It corrects three things:
- The eigenvector components γ₁₃ and γ₃₃ (Eq. 20\*). It also normalises the eigenvectors (new
  Eq. E1), which is needed for the cross-polarised r and t of birefringent media.
- The signs of t_ss, t_ps and t_sp, to follow Yeh (Eqs. 34\*–36\*).
- The field calculation in birefringent media (Section 2.C, replaced by Eqs. 37\*, 38 and E2).

It changes no material parameters and does not mention the figures. Its reference list prints
Yeh's paper as "J. Opt. Soc. Am. B 69"; that should read *J. Opt. Soc. Am.* 69. For us, it is a
list of the places where a 4×4 program is easy to get wrong: each one becomes a test.

### Yeh (1979) and Yeh (1980)

P. Yeh, "Electromagnetic propagation in birefringent layered media," *J. Opt. Soc. Am.* **69**,
742–756 (1979); P. Yeh, "Optics of anisotropic layered media: a new 4×4 matrix algebra," *Surf.
Sci.* **96**, 41–53 (1980).

Conventions: exp(−iωt), as ours. The continuity vector is (E_x, H_y, E_y, H_x), with H_x not negated.
In 1979, reflection is written in a fixed Cartesian basis (r_xx, r_xy, …), so neither paper fixes the
sign of p.

- **The Euler angles (1979, Eq. 2) are the contract's.** The contract now adopts this matrix.
- **No numerical example has complete inputs.** The Šolc filter figures (1979, Figs. 4–8) omit the
  indices, the angle and the number of plates.
- **Closed forms:**
  - the Šolc unit-cell matrix (1979, Eqs. 32–37);
  - r and t from the 4×4 matrix (1979, Eqs. 87–94; 1980, Eqs. 22–30);
  - the uniaxial ordinary and extraordinary modes (1979, Eqs. A10–A13).

### Schubert (1996)

M. Schubert, "Polarization-dependent optical parameters of arbitrarily anisotropic homogeneous
layered systems," *Phys. Rev. B* **53**, 4265–4274 (1996).

Conventions: exp(−iωt) and n + ik, as ours. The field vector is ordered (E_x, E_y, H_x, H_y). It defines
Ψ, Δ and the normalised Jones elements of generalised ellipsometry (Eqs. 4–9), and its p vectors
give r_pp = −r_ss at normal incidence. **The contract adopts its definitions.** It has no numerical
examples. Its closed forms:

| Example | Equations | Check |
|---|---|---|
| Biaxial medium with axes along x, y and z: Δ, eigenvalues, exit matrix | B1–B4 | Closed form |
| Aligned biaxial film on a biaxial substrate: ρ in Airy form | C1–C6 | Closed form; the best exact ellipsometry test in hand |
| Eigenvalues of an arbitrary biaxial medium | 21–22 | Closed form |
| A continuously twisted biaxial slab at normal incidence | 28–35 | Closed form, stated to be exact |

### Not usable as solver tests

- R.-C. Ma, L.-L. Gao, T. Zhang and Y. Jin, Proc. SPIE 10452, 104525A (2017). It uses a
  non-standard static magnetisation term and no permittivity tensor.
- D. A. Allwood, G. Xiong, M. D. Cooke and R. P. Cowburn, *J. Phys. D* **36**, 2175–2182 (2003).
  It is an instrument model, with no stack or optical constants. Its Eqs. 10–11 could test a
  polariser–analyser model built on the program's Jones output, later.
- M. S. Heimburger, "A Magneto-Optical Kerr Effect Study," BS thesis, College of William and Mary
  (2015). Measurements only.

## Microwaves

The engine is scale-free, so a microwave stack is the same calculation at a longer wavelength. What
microwaves add is materials: complex μ, the gyrotropic μ tensor of a magnetised ferrite, and
constants given as ε′ and tan δ. Tests that need no particular paper:

- **Faraday rotation in a ferrite magnetised along z**, at normal incidence: the two circular
  modes see μ ± κ of the Polder tensor, so the rotation is a closed form. This is Berreman's
  Faraday example with the gyrotropy moved from ε to μ, and tests the μ block.
- **Absorbers with magnetic loss** (a Dallenbach layer on metal): an isotropic stack with ε and μ
  both complex. The 2×2 result is exact, so this tests μ ≠ 1 against an independent formula.

Microwave papers mostly use exp(+jωt) and ε = ε′ − jε″, and are converted by conjugation (contract
§1). Ferrite papers also differ in the sign of the off-diagonal term of the Polder tensor, depending
on the sign of γ in the equation of motion. That sign is fixed against a physical statement (the
sense of Faraday rotation relative to the bias), never copied from one paper.

### Examples with complete inputs

| Paper | Example | Inputs | Check |
|---|---|---|---|
| Anshin et al., "Syntactic foams with low dielectric permittivity," 2024 IEEE 9th All-Russian Microwave Conf. (RMC), pp. 256–260 | A-sandwich radome wall in free space: skins ε = 3.1 − j0.05, about 1.8 mm; core ε = 1.37 − j0.01, about 6.9 mm | Complete. Thicknesses are printed as approximate, and the polarisation labels are unclear (Fig. 4 appears to be TM, Fig. 5 TE). | Curves: \|T\| in dB, 2–24 GHz, at 0°, 20°, 40° and 60° (Figs. 4–5), measured and calculated, agreeing within 0.2 dB |
| Peshlov, Dankov and Hadjistamov, "Models of multilayer antenna radomes with anisotropic materials," Proc. EuCAP 2006 (ESA SP-626) | **Uniaxial** walls, axis along z. Table I: TWINTEX, 1.90 mm, ε′∥ 3.25, ε′⊥ 2.85, tan δ∥ 0.0062, tan δ⊥ 0.0045. Table II: a honeycomb wall as 1, 3 or 6 layers, every ε′ and tan δ given | Complete | Curves: insertion and return loss against frequency and angle (Figs. 5, 6, 10, 11), with **measured** points in Fig. 11. Their own model approximates the uniaxial physics, so we compare with the measurements, not their curves. |
| Costa, Monorchio and Manara, *IEEE EMC Magazine* **5**(2), 67–74 (2016) | Salisbury screen, 5 mm air spacer on metal (Fig. 5a); Jaumann absorber, three 4 mm layers (ε 1, 2, 2) with sheets of 150 and 500 Ω/sq (Fig. 5b) | Salisbury: sheet resistance assumed 377 Ω/sq. Jaumann: the sheets' positions are assumed. | Numbers: the −15 dB bandwidths 45.4 % and 111.9 % (Table 1); curves |

**Resistive sheets** (Salisbury and Jaumann) are a layer of zero thickness with a sheet
conductance. C# can represent one as a thin layer with ε″ = 1/(ωε₀R_s d), in the limit d → 0. Whether
to add a true sheet boundary condition instead is **Open**.

### Closed forms for microwave materials

| Source | Result | Equations |
|---|---|---|
| Thompson, "Ferrites in waveguides," *J. Brit. IRE*, June 1956, pp. 311–328 | Faraday rotation per unit length, longitudinal bias, lossless, exact; the high- and low-field limits | 4, 7, 8, 9. The printed κ (Eq. 6) lacks a factor ω. |
| Thompson (1956) | Transverse bias: μ_eff = (μ² − κ²)/μ, negative (total reflection) above a field | 13 |
| Adam, Linkhart and Geiler, Ch. 7 of *Modern Ferrites*, Vol. 2, ed. V. G. Harris (IEEE/Wiley, 2023) | Polder tensor with loss (ω₀ → ω₀ + jαω); propagation constants at any angle to the bias; worked curves at 1 GHz, 4πM = 1000 G, α = 0.01 | 7.15–7.16, 7.32–7.33, 7.46–7.50; Figs. 7.2–7.6. Its text gives χ″ the wrong sign for its own convention. |
| Kodera and Caloz, *IEEE AWPL* **17** (2018) | Faraday rotation with Gilbert loss, in Pozar's form | 3b, 6 |
| Poddar, MS thesis (2020), Appendix A; arXiv 2205.03560 | Slab with gyrotropic ε **and** μ at normal incidence, bias along z: r, t, Faraday and Kerr rotation | A.1–A.151 |
| Mei et al., *Opt. Express* **20**, 16955 (2012) | Two slabs with **tilted optic axes**, ε_u ε_v = 1: zero reflection at every angle, TM, any thickness, and the transmitted phase equal to that of vacuum | 8, 11, 13–14. Scale-free, so it serves at any wavelength. The printed slab 2 (Eq. 18b) contradicts the paper's own conditions; the consistent values are ε_zz = 1, ε_xx = 5.0055, ε_xz = ∓2.0014 for the two slabs. That is our derivation, checked when the test is written. |
| Ra'di, Simovski and Tretyakov, *Phys. Rev. Applied* **3**, 037001 (2015) | A slab with ε_r = μ_r reflects nothing at normal incidence, at any thickness; a sheet of admittance 1/η₀ + j/(η₀ tan k₀d) at any spacing on metal absorbs perfectly | §V A 1; 8–9 |
| Costa et al. (2016); Ra'di et al. (2015) | Rozanov's thickness–bandwidth limit, with numbers for the Salisbury and Jaumann examples | 5; 89 |

The tilted-axis bilayer of Mei et al. is the most valuable of these. It is an exact,
angle-independent result for an off-diagonal ε, which is the part of the engine a 2×2 program cannot
check.

### Read and not usable

- Reid, "Microwave ferrites," *Trans. SAIEE* **65** (1964).
- Sahasrabudhe et al., PIERS 2017.
- Harris, Ch. 14 of *Microwave Materials and Applications* (Wiley, 2017).
- Iqbal, PhD thesis, UMIST (1996).
- Vinoy and Jha, *Sādhanā* **20**, 815 (1995).
- Abu Sanad et al., *Phys. Status Solidi A* **221**, 2300828 (2024).
- Nair and Jha, *IEEE APM* **56**(4), 276 (2014).
- Uher and Hoefer, "Tunable microwave and millimeter-wave band-pass filters," *IEEE Trans. MTT*
  **39**, 643–653 (1991). This is a survey of filters in waveguide and planar circuits. It gives the
  Kittel resonance (its Eq. 10) and the magnetisation and permittivity of several commercial ferrites
  (Figs. 9–11, Section II-B).

They are devices or waveguides, not layered stacks, or they give formulas without complete examples.
Some of their oblique-incidence formulas use the angle of incidence where the angle of refraction
belongs, so they are not used as references.

Poddar's work on a gyrotropic slab at normal incidence:
- S. Poddar, *Design and Analysis of Fully-Electronic Magnet-Free Non-Reciprocal Metamaterial*, MS
  thesis, University of Wisconsin–Milwaukee (2020). Open access: dc.uwm.edu/etd/2578.
- S. Poddar, R. S. Rafi and M. T. Hasan, arXiv 2205.03560 (2022).
- S. Poddar, A. M. Holmes and G. W. Hanson, *IEEE Trans. Instrum. Meas.* **71**, 8006207 (2022).

The thesis's Appendix A (Eqs. A.1–A.151, the same derivation as the arXiv paper's Section III) is
**a closed form, not a numerical example**. It covers a slab between isotropic media, at normal
incidence with the bias along z, with both ε and μ gyrotropic at once. It gives:
- r and t in Cartesian and circular bases;
- Faraday and Kerr rotation for any incident polarisation;
- the thin-slab limit.

Its time convention is exp(+jωt) (thesis Fig. 2-14). Berreman's Faraday example has gyrotropy in ε
only, so this is the test for both blocks together. The 2022 article measures a transistor-loaded
metasurface, with no material inputs.

## To obtain

| Paper | Why | Availability |
|---|---|---|
| A published ferrite slab example with every input printed (4πM_s, bias, linewidth, ε, thickness, frequency) and stated results | None of the eight ferrite sources read has one | Still to find |

Each is read in full before it is used. Programs published with a paper may be run as independent
checks, but no code is taken from them.

## Order

1. The closed forms, which test the engine exactly and come first:
   - Berreman's five; Park's Eqs. 5, 12 and 13; Catarina's polar Kerr film; Lakhtakia's half space;
   - Mei's tilted-axis bilayer;
   - Thompson's and Adam's ferrite propagation constants;
   - Ra'di's ε_r = μ_r slab.
2. Mansuripur's quadrilayer, whose inputs are complete. Then Passler and Paarmann, whose inputs are
   complete with the authors' code, checking each point of the erratum.
3. The microwave stacks with complete inputs: Anshin's sandwich, Peshlov's uniaxial walls against
   their measurements, and Costa's absorbers.
4. The cholesteric curves of Ting (Fig. 2 first) and Park (Fig. 1), which test sublayer
   discretisation, with the missing inputs stated.
5. Schubert's generalised ellipsometry (his Eq. C6 first), which tests the derived outputs.
