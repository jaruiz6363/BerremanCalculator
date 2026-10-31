# Samples

Each sample is a [stack file](../docs/stack-format.md) that reproduces an example from the
literature. Its `source` names the paper, and its `notes` record anything the paper leaves out and
what was assumed. Each one is checked by a test, against the numbers the paper gives, to the
precision the paper gives them.

| Sample | Source | What the paper gives | Agreement |
|---|---|---|---|
| [literature/mansuripur-1990/quadrilayer.json](literature/mansuripur-1990/quadrilayer.json) | Mansuripur, *J. Appl. Phys.* **67**, 6466 (1990) | Fig. 2(a) amplitude reflectivities against angle; Fig. 2(b) Kerr rotation and ellipticity; Fig. 3 power budget; the statement \|r_ps\| = \|r_sp\| | See below |
| [literature/berreman-1972/faraday-slab.json](literature/berreman-1972/faraday-slab.json) | Berreman, *J. Opt. Soc. Am.* **62**, 502 (1972), Eqs. 13, 56–60 | Closed form: circular waves see ε ± g | Jones eigenvalues equal the circular Airy transmissions to 5 × 10⁻¹⁴ |
| [literature/poddar-2020/gyrotropic-slab.json](literature/poddar-2020/gyrotropic-slab.json) | Poddar, MS thesis (2020), Appendix A | Closed form: ε and μ both gyrotropic | 2 × 10⁻¹⁵ |
| [literature/thompson-1956/transverse-ferrite.json](literature/thompson-1956/transverse-ferrite.json) | Thompson, *J. Brit. IRE* (1956), Eq. 13; Adam et al. (2023), Fig. 7.5 | Closed form: μ_eff = (μ² − κ²)/μ, total reflection where it is negative; the resonance at about 115 Oe | r to 10⁻¹⁵; total reflection in all 42 negative-μ_eff points; resonance at 114.5 Oe |
| [literature/mei-2012/tilted-bilayer.json](literature/mei-2012/tilted-bilayer.json) | Mei, Xu, Bai and Cui, *Opt. Express* **20**, 16955 (2012) | Closed form: no reflection at any angle (TM), and the phase of vacuum | R_p below 10⁻²⁴ at every angle; t_pp equals vacuum to 10⁻¹¹ (the inputs' 12 digits) |
| [literature/radi-2015/matched-slab.json](literature/radi-2015/matched-slab.json) | Ra'di, Simovski and Tretyakov, *Phys. Rev. Applied* **3**, 037001 (2015), Section V A 1 | Closed form: ε = μ reflects nothing | 10⁻¹² |
| [literature/costa-2016/salisbury.json](literature/costa-2016/salisbury.json) | Costa, Monorchio and Manara, *IEEE EMC Mag.* **5**(2), 67 (2016), Fig. 5(a), Table 1 | A −15 dB bandwidth of 45.4 % | The ideal 377 Ω screen gives 44.2 %, and this sample gives 44.1 %. The paper's figure probably used another sheet resistance; it does not state one. |

| [literature/passler-2017/sic-otto.json](literature/passler-2017/sic-otto.json) | Passler and Paarmann, *J. Opt. Soc. Am. B* **34**, 2128 (2017), Fig. 2 | The SiC SPhP dip near 910 cm⁻¹; critical coupling at a 5.5 µm gap; four curves | Dips at 900, 906, 909, 910 cm⁻¹ against the figure's ≈ 900, 907, 910, 911; deepest at 5.5 µm. Depths: see below. |
| [literature/passler-2017/gan-sic-otto.json](literature/passler-2017/gan-sic-otto.json) | the same, Fig. 3 | Modes near 680, 810–820 and 1000 cm⁻¹ | 663 (broad), 819, 997 |
| [literature/passler-2017/sic-gan-sic-otto.json](literature/passler-2017/sic-gan-sic-otto.json) | the same, Fig. 4 | Mode (ii) at the upper GaN edge, two modes around 900, one near 1000 cm⁻¹ | 771, 881 and 937, 996 |

| [literature/ting-2006/cholesteric-fig2.json](literature/ting-2006/cholesteric-fig2.json) | Ting, Lin, Liao and Fuh, *Opt. Express* **14**, 5594 (2006), Fig. 2 | A band centred at n̄P = 525.6 nm, width ΔnP = 41.2 nm, peak about 0.5 unpolarised | Peak 0.495; half-height centre 525.8 nm; R still at 0.91 and 0.90 of the plateau at n_oP = 505.0 and n_eP = 546.2 nm, and down to 0.07 and 0.01 10 nm outside. Doubling the sublayers changes R by 6 × 10⁻⁴. |

| [literature/anshin-2024/sandwich-radome.json](literature/anshin-2024/sandwich-radome.json) | Anshin, Politiko, Dyakonov and Pyzanov, RMC 2024, Figs. 3–5 | The paper's calculated \|T\| (dB), 2–24 GHz, 0–60°, p and s | Every dip within 0.04 dB and 0.2 GHz of the figure, except at 40° (0.15 dB, 0.2 GHz) and the deep 60° s dip (−8.65 against −8.98 dB, 19.5 against 19.9 GHz). The paper gives its thicknesses only approximately. |
| [literature/peshlov-2006/honeycomb-model3.json](literature/peshlov-2006/honeycomb-model3.json) | Peshlov, Dankov and Hadjistamov, EuCAP 2006, Table II and Fig. 11 | **Measured** insertion loss at 12.5 GHz, with error bars, against angle | 7 of 8 measurements inside their error bars. At 65°, s polarisation, −0.72 dB against −0.60 ± 0.10. |
| [literature/peshlov-2006/twintex.json](literature/peshlov-2006/twintex.json) | the same, Table I and Figs. 5–6 | Only the paper's approximate model | The s wave sees only ε∥ exactly (anisotropic = isotropic ε∥ to 0), which the paper's Fig. 5a does not show. |

| [literature/catarina-2020/polar-kerr-film.json](literature/catarina-2020/polar-kerr-film.json) | Catarina, Peres and Fernández-Rossier, *2D Mater.* **7**, 025011 (2020), Eqs. B.12–B.16 | Closed form: circular reflection coefficients r± and the Kerr rotation of a magnetic film on a substrate | E_x ± iE_y of the reflected wave equal r± to 3 × 10⁻¹⁶, with the same labels; Kerr rotation to 2 × 10⁻¹⁶ |
| [literature/lakhtakia-2018/magnetoelectric-halfspace.json](literature/lakhtakia-2018/magnetoelectric-halfspace.json) | Lakhtakia, arXiv 1811.05034 (2018), Eqs. 17–29, Fig. 3 parameters | Closed form: reflection by a half space with magnetoelectric gyrotropy (D = ε₀ε_rE − Γ×H, B = μ₀μ_rH + Γ×E) | Reflectances to 1.4 × 10⁻¹⁴ over 0–89°. The test first checks that the transcribed formulas reduce to Fresnel at Γ = 0. |
| [literature/schubert-1996/biaxial-film.json](literature/schubert-1996/biaxial-film.json) | Schubert, *Phys. Rev. B* **53**, 4265 (1996), Eqs. C2, C5, C6 | Closed form: the ellipsometric ratio ρ = tan Ψ e^{iΔ} of a biaxial film on a biaxial substrate | ρ to 3 × 10⁻¹⁵ (relative), at 0–80°. This confirms the program's p and s signs and Δ at oblique incidence, not only r_pp = −r_ss at normal incidence. |

The closed-form samples have values chosen where the paper gives only the formula; their `notes`
say so.

**Misprints found while reproducing the closed forms.** In each case the paper's own derivation
gives the form that agrees, and the test shows the printed form does not:

| Paper | As printed | What the paper's own equations give | Test |
|---|---|---|---|
| Berreman (1972), Eq. 70 | δ²/ε in the cholesteric eigenvalues | δ² (his Eqs. 68–69) | det(Δ_rot − κ): 7 × 10⁻¹⁵ against 2 × 10⁻² |
| Schubert (1996), Eq. C5 | differences in the denominators of r₁₂pp and r₁₂ss, which makes each 1 | sums, as in his r₀₁ | ρ: 3 × 10⁻¹⁵ against an error of 1.7 |
| Mei et al. (2012), Eq. 18b | the same sign of ε_xz for both slabs | opposite signs (his Eqs. 9–14) | see the Mei sample |

## Radome walls

**Anshin (2024)** prints every input: two fibreglass skins and a syntactic-foam core, with ε′ and
ε″ for each and thicknesses to "≈". The paper's curves are its own calculations and measurements,
which it says agree within 0.2 dB. The sample is compared with the calculated curves, read off the
figures enlarged.

**Peshlov (2006)** gives six-layer uniaxial data for a honeycomb radome (Table II, Model 3), and
measured insertion loss with error bars (Fig. 11). The paper's own model approximates anisotropic
propagation: its Eqs. 2–3 use ε⊥ for the phase of both polarisations. So the sample is compared
with the measurements, not with the paper's curves.

At 65° in s polarisation the exact result, −0.72 dB, lies 0.02 dB outside the measured
−0.60 ± 0.10. The paper's own Model 3 curve passes through that measurement.

For the one-layer TWINTEX panel the paper has no measurements. The exact result shows something its
Fig. 5a does not: an s wave, with E in the plane of the wall, sees only ε∥. So the anisotropic panel
is identical, for s, to the isotropic panel of ε∥, the paper's Model 3i.

## Cholesterics

At normal incidence a helix is uniform in a frame that turns with it. So the exact transfer matrix
is S(φ(d)) exp(i k₀ d Δ_rot) S(φ₀)ᵀ, with Δ_rot = Δ₀ + i(β/k₀)G constant. The tests compute it
independently and compare the sublayer result with it:
- **Convergence.** At 16, 32, 64 and 128 sublayers per pitch the error is 2.6 × 10⁻², 6.5 × 10⁻³,
  1.6 × 10⁻³ and 4.1 × 10⁻⁴. That is exactly the factor of 4 per doubling of the midpoint rule.
- **The reported doubling change** is about three-quarters of the true error, as a second-order
  method predicts.
- **Handedness.** At the band centre one circular polarisation is reflected (0.989) and the other is
  not (0.0004), and reversing the helix exchanges them.

**Berreman's Eq. 70 as printed has a misprint.** Its eigenvalues contain δ²/ε where his own
Eqs. 68–69 give δ²:
- Eqs. 68–69 give κ² = ε + m² ± √(4εm² + δ²), which is de Vries's result.
- The test evaluates det(Δ_rot − κ) at both: 7 × 10⁻¹⁵ for Eqs. 68–69, and 2 × 10⁻² for Eq. 70 as
  printed.

Ting's Fig. 2 leaves out four inputs, and the sample assumes them:
- the angle (normal incidence);
- the polarisation (unpolarised);
- the outer media (index-matched at 1.555);
- the handedness. Unpolarised reflection does not depend on it.

Ting's 41.2 nm is the stop band between n_oP and n_eP. The half-height width of a 12-pitch slab is
wider, 51.5 nm, because of the band's skirts.

## Passler and Paarmann (2017)

The inputs are complete once the authors' code is used for the material parameters
([literature](../docs/literature.md)). The KRS5 prism follows Rodney and Malitson's Sellmeier
formula, from the CC0 refractiveindex.info database. It equals the KRS5 table in the authors' code
to the table's printed digits.

**The engine agrees with an independent calculation for these inputs.** All the crystals are
c-cut, so the p wave stays in its own 2×2 block, which has a closed form. The engine matches it to
4 × 10⁻¹⁵ over the whole spectrum of all three samples.

**Mode frequencies agree with the paper.** Every frequency the text states is reproduced, and so is
every dip position in Fig. 2(a). The critical gap is 5.5 µm, as stated.

**Dip depths in Fig. 2(a) do not:**

| Gap | Figure (read off) | Here, Sellmeier prism | Here, prism index 2.4 |
|---|---|---|---|
| 2 µm | ≈ 0.77 | 0.83 | 0.81 |
| 3.5 µm | ≈ 0.41 | 0.55 | 0.48 |
| 5.5 µm | ≈ 0.0 | 0.05 | 0.01 |
| 7.5 µm | ≈ 0.39 | 0.24 | 0.42 |

- The figure shows weaker prism coupling than the Sellmeier index gives, at every gap.
- A prism index of 2.4, the "≈ 2.4" of the paper's text, comes within 0.07 at every gap. So does an
  angle of 30.5°.
- The authors' published code computes ξ = √ε_KRS5(ω) sin θ, as this program does, from the same
  KRS5 data. It should therefore give the Sellmeier column.
**Settled by running the authors' code** (2026-10-31, GNU Octave 11.3; a local script calling
their functions, outside the repository). Both the 2017 version (Zenodo 400486) and the 2019
version, from after the erratum, were run. They covered all three stacks at all four gaps, from 350
to 1400 cm⁻¹:

| Comparison | Largest \|ΔR_p\| over 12 spectra × 1051 frequencies |
|---|---|
| Their code (either version) against this program | 9 × 10⁻⁵, on the steepest resonance slopes |
| Their 2019 code with this program's Sellmeier prism, against this program | 5 × 10⁻¹¹, the precision the results were printed to |

- The two versions give identical p reflectances, so the erratum does not touch these figures.
- The only difference between their code and this program is the prism index: their KRS5 table,
  interpolated, against Rodney and Malitson's formula. With the same prism the two programs agree
  to the printed digits.
- Their code gives this program's depths (0.834, 0.549, 0.050, 0.243 at the four gaps), not the
  figure's. So the published Fig. 2(a) was made with inputs other than the published code and
  data. A prism index near 2.4, the "≈ 2.4" of the text, reproduces it.

The tests assert the frequencies and the critical gap. The depths are printed, not asserted. Mei's Eq. 18b is printed with the wrong sign for the second slab, and the sample uses the
sign the paper's own equations require.

## Mansuripur (1990)

Every input is printed in the paper. Its results are figures and rounded text, so the agreement is
to the precision they can be read:

| Quantity | Paper | Here |
|---|---|---|
| \|r_pp\| = \|r_ss\| at normal incidence, Fig. 2(a) | ≈ 0.235 | 0.2349 |
| 100 \|r_ps\| at normal incidence, Fig. 2(a) | ≈ 1.0 | 1.016 |
| The \|r_ss\| dip, Fig. 2(a) | ≈ 0.17 near 60° | 0.168 at 60° |
| \|r_ps\| = \|r_sp\| at every angle (text) | stated | equal to every printed digit |
| Kerr rotation and ellipticity for P light at 0°, Fig. 2(b) | ≈ 1.75° and 1.7° | 1.76° and 1.74°, opposite in sign |
| Reflected, absorbed in the MO layer, absorbed in the Al, at normal incidence, Fig. 3 | ≈ 6 %, 91 %, 3 % | 5.5 %, 92.1 %, 2.3 % |

The paper does not state the sign convention of its Kerr angles, so only magnitudes are compared.
The Fig. 3 percentages in the caption are readings of a curve whose last step (into the aluminium)
is 0.02–0.03 on a 0–1 scale. The test tolerances are ±0.8 %, ±1.5 % and ±0.8 %.
