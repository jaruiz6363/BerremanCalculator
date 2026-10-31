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

The closed-form samples have values chosen where the paper gives only the formula; their `notes`
say so. Mei's Eq. 18b is printed with the wrong sign for the second slab, and the sample uses the
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
