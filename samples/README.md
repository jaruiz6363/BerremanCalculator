# Samples

Each sample is a [stack file](../docs/stack-format.md) that reproduces an example from the
literature. Its `source` names the paper, and its `notes` record anything the paper leaves out and
what was assumed. Each one is checked by a test, against the numbers the paper gives, to the
precision the paper gives them.

| Sample | Source | What the paper gives | Agreement |
|---|---|---|---|
| [literature/mansuripur-1990/quadrilayer.json](literature/mansuripur-1990/quadrilayer.json) | Mansuripur, *J. Appl. Phys.* **67**, 6466 (1990) | Fig. 2(a) amplitude reflectivities against angle; Fig. 2(b) Kerr rotation and ellipticity; Fig. 3 power budget; the statement \|r_ps\| = \|r_sp\| | See below |

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
