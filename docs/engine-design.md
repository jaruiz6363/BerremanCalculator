# Engine design

How the native engine computes one evaluation point: a wavelength, a tangential wavevector ξ and a
stack. This note decides what [the contract](contract.md) left open in its Section 4. Nothing here is
implemented yet.

## The problem it avoids

A layer's four modes are the eigenvectors of Berreman's Δ. Every 4×4 program needs them somewhere,
and they are where such programs break:

- **Coincident roots.** In every isotropic layer the two forward roots coincide, and so do the two
  backward ones. The eigenvectors are then not unique, and a formula that divides by the difference
  of two roots fails. The legacy code guarded this with fixed tolerances; Passler and Paarmann (2017)
  give separate formulas for it, valid only when μ is a scalar and there is no optical activity.
- **Defective Δ.** At exactly q = 0, at the critical angle or at grazing incidence in a layer, the
  forward and backward roots meet and Δ has only one eigenvector for the pair. No eigenvector basis
  exists at all.
- **Growing exponentials.** Multiplying transfer matrices through an evanescent or strongly absorbing
  layer mixes numbers of size e^{+a} and e^{−a}, and the small ones are lost.

The design below needs individual modes **only in the two outer media**, where they define the
incident, reflected and transmitted waves. Inside the stack, the field is propagated without any
eigen-decomposition, so coincident roots and defective Δ are not special cases there.

## 1. Building Δ

From the 6×6 material matrix M and ξ, Maxwell's equations with fields varying as
exp[i k₀(ξx − ct)]:

```
∇ × E  =  i k₀ (ρ' E + μ H̃)
∇ × H̃  = −i k₀ (ε E + ρ H̃)
```

The z-components of these are algebraic in E_z and H̃_z. Solving them and substituting into the other
four gives dψ/dz = i k₀ Δ ψ, with ψ = (E_x, H̃_y, E_y, −H̃_x). This is Berreman's elimination (his
Eqs. 19–24). It needs the 2×2 determinant d = M₃₃M₆₆ − M₃₆M₆₃ to be non-zero, and the point fails
with its own status code if it is zero.

Δ is derived from scratch in the derivation notes and coded once. It is tested against the Δ
matrices Berreman writes out (his Eqs. 37, 47, 56 and 62) and Passler's Eq. 8. Then, for random
general M, every eigenpair of Δ is turned back into the full six-component field and substituted
into Maxwell's equations with k = k₀(ξ, 0, q). This last test does not depend on any formula for Δ.

## 2. The outer media

The incident and exit media need individual waves, and this is the only place modes are computed.

**Isotropic outer medium.** Detected exactly from M (ε and μ scalar multiples of I, ρ = ρ' = 0); this
is a property of the input, not a tolerance. The modes are written down, from H̃ = (k/k₀) × E / μ.
Here N = (εμ)^½ on the principal branch, and q = (εμ − ξ²)^½ on the branch Im q > 0, or Im q = 0 and
Re q > 0. Each vector is the field of unit amplitude along p̂ or ŝ, in the contract's convention:

| Wave | ψ = (E_x, H̃_y, E_y, −H̃_x) |
|---|---|
| p forward | (q/N, N/μ, 0, 0) |
| s forward | (0, 0, 1, q/μ) |
| p backward | (−q/N, N/μ, 0, 0) |
| s backward | (0, 0, 1, −q/μ) |

For μ = 1 at normal incidence this gives r_pp = (n₂ − n₁)/(n₁ + n₂) and r_ss = (n₁ − n₂)/(n₁ + n₂),
so r_pp = −r_ss, as the contract says. The p vectors need N ≠ 0, and every vector needs q ≠ 0. An
outer medium with ε = 0, or a wave grazing at q = 0, fails with its own status.

**Anisotropic outer medium.**

1. The eigenvalues of Δ come from the complex Schur form, computed by shifted QR on the 4×4 matrix.
   This is backward stable, unlike the characteristic quartic, whose roots lose half their digits
   when two of them are close.
2. **Forward or backward.** Im q > 0 is forward. A root counts as real when |Im q| is within a
   rounding bound, a small multiple of the unit roundoff times ‖Δ‖. A real root is then forward if
   its time-averaged Poynting vector has S_z > 0. The bound comes from the backward error of the QR
   iteration; it is not a physical tolerance.
3. The Schur form is reordered so the two forward roots come first. Its first two Schur vectors are
   then an orthonormal basis of the forward subspace, well defined even when the two roots coincide.
   The same is done for the backward pair.
4. **Individual modes.** Within each pair, the eigenvectors come from the 2×2 triangular block.
   - When the two roots coincide, every vector of the subspace is a mode. The basis is then chosen by
     projecting the s-like and p-like vectors (ŷ, and x̂ in the plane of incidence) onto the subspace,
     so the labels stay continuous as an optic axis is tilted through the normal.
   - When the roots are close but not equal, the modes are ill-conditioned, and that is physical
     rather than numerical. The condition number of the mode basis is reported.

   R, T and A summed over the modes do not depend on the choice of basis. Only the coefficients
   between individual modes do.
5. **The incident medium must be lossless.** This is tested exactly on the input: ε and μ
   Hermitian, and ρ' = ρ†. Otherwise the point fails loudly (contract §3).
6. **A root at q = 0 in an outer medium** is a wave grazing the interface. The incident or
   transmitted wave is then undefined, so the point fails with its own status rather than returning
   numbers.

## 3. Through the stack

The method is a backward sweep, from the exit medium to the incident medium. It carries a
two-dimensional subspace: the fields that satisfy the exit condition, which allows transmitted waves
only.

1. **Start.** W, a 4×2 matrix with orthonormal columns, spans the exit medium's two forward modes.
2. **Through a layer**, from its back face to its front face, the field obeys ψ(front) = P ψ(back)
   with P = exp(−i k₀ d Δ). The layer is divided into steps h, so that ‖k₀ h Δ‖₁ ≤ β. Each step does
   two things:
   - W ← exp(−i k₀ h Δ) W;
   - W is re-orthonormalised with a 2-column QR factorisation, W = Q R, keeping Q and folding the
     2×2 upper-triangular R into the layer's running factor.
3. **At the front**, the incident wave ψ_inc plus the reflected waves B r (B being the incident
   medium's backward modes) must lie in the span of W:

   ```
   ψ_inc + B r = W c        →        [ W  −B ] [c; r] = ψ_inc
   ```

   This is a 4×4 linear system, solved by LU with partial pivoting for both incident waves at once.
   r is the Jones reflection matrix.
4. **Transmission.** If each step k gives P_k W_{k+1} = W_k R_k, then c at the front relates to the
   transmitted amplitudes by t = R_N⁻¹ ⋯ R₁⁻¹ c, applied in the forward direction as a sequence of 2×2
   triangular solves.

**Why this is stable.** Re-orthonormalising after every bounded step is Godunov's method for
shooting through stiff linear differential equations (S. Godunov, *Uspehi Mat. Nauk* **16**, 171
(1961)). Conte (S. D. Conte, "The numerical solution of linear boundary value problems," *SIAM
Rev.* **8**, 309–321 (1966)) describes it, and proposes re-orthonormalising (Gram–Schmidt) only
where a non-orthogonality test is exceeded: the smallest angle between the columns, or the largest
column norm (his Eqs. 26–27). Our recovery of the transmitted amplitudes is the homogeneous case of
his backward resolution (his Eq. 25), run in mirror image. Within one step, the largest and
smallest growth factors differ by at most about e^{2β}, so the QR step loses at most that factor
times the unit roundoff. The step bound β therefore sets the accuracy directly. Every step is kept
because a two-column QR of a 4×2 matrix is cheap next to the exponential; Conte's selective
version is an optimisation to measure later, not a correctness question.

Consider a mode that grows e^{a} through a thick metal, sweeping backward. It dominates the subspace,
which is physically right: the decaying partner is negligible at the front. Its size goes into R,
not into W. Coincident roots, and a defective Δ at q = 0 inside a layer, need nothing special: the
exponential of any matrix is defined, and the sweep never asks for an eigenvector.

**The matrix exponential.** Each step uses a fixed-degree [m/m] Padé approximant r_m without scaling
and squaring. Higham (N. J. Higham, "The scaling and squaring method for the matrix exponential
revisited," *SIAM J. Matrix Anal. Appl.* **26**, 1179–1193 (2005), Tables 2.1–2.3) shows:

- In exact arithmetic, r_m(A) = e^{A+E}, with E commuting with A and ‖E‖ ≤ u‖A‖ (u = 2⁻⁵³),
  whenever ‖A‖ ≤ θ_m in any subordinate norm; his algorithm uses the 1-norm. This is a bound on
  **backward** error. The forward error also depends on the conditioning of the exponential.
- θ₇ = 0.950, θ₉ = 2.098, θ₁₃ = 5.372.
- Rounding errors are bounded separately: for ‖A‖ ≤ θ_m, ‖q_m(A)⁻¹‖ ≤ 1.6, 3.0 and 17 for m = 7, 9
  and 13, so the linear solve is well conditioned.
- The costs are 4, 5 and 6 matrix products plus one solve.

Evaluating r_m unscaled whenever ‖A‖₁ ≤ θ_m is exactly what his Algorithm 2.3 does, so the design
is covered by the paper. It starts with m = 9 and β = 1. Section 6 measures accuracy and cost at
β = 1 and 2 with m = 9, and at β = 5.37 with m = 13, before the final choice is written here. Degree
9 is not covered above β = 2.098.

**Opaque stacks.** If the running R factors exceed about 10²⁵⁰, the transmitted amplitude is below
10⁻²⁵⁰ of the field at the front. t, T and the fields beyond are set to exactly zero, and the point
records that it was opaque. Reflection is not affected, because it depends only on W at the front.

## 4. Powers, absorption and fields

- **Powers.** S_z ∝ Re(ψ₁ψ₂* + ψ₃ψ₄*) for any field, in any medium.
  - R = −S_z(B r)/S_z(ψ_inc) and T = S_z(W_exit t)/S_z(ψ_inc), per incident wave.
  - A = 1 − R − T.
  - In a lossless incident medium the cross terms between incident and reflected waves vanish.
  - In an absorbing exit medium, T is the power crossing into it, cross terms included.
- **Absorption per layer.** S_z at the layer's front face minus S_z at its back face. The field at
  every interface is W_k c_k with c_{k+1} = R_k⁻¹ c_k, so the sweep stores, per interface, W_k (4×2)
  and the layer's 2×2 factor. That is scratch proportional to the layer count, sized by `bc_plan`.
  The sum over layers, plus R and T, must make 1; the residual is reported.
- **Fields against depth.** A depth z inside layer k is reached from the layer's back face,
  stepping backward with the same bounded steps. This is the stable direction for the same reason
  as the sweep. It needs no storage beyond the interface fields.

## 5. Status codes and diagnostics

| Status | Meaning |
|---|---|
| `OK` | |
| `INCIDENT_ABSORBING` | the incident medium is not lossless |
| `GRAZING_OUTER` | a root at q = 0 in an outer medium |
| `DELTA_SINGULAR` | d = M₃₃M₆₆ − M₃₆M₆₃ = 0 in some layer |
| `OUTER_EIGEN_FAILED` | the QR iteration on an outer medium did not converge in its iteration limit |
| `BOUNDARY_SINGULAR` | the 4×4 front system has a zero pivot |
| `NOT_IMPLEMENTED` | for example an incoherent layer; outputs untouched |

Each point also reports:
- the condition number of the front system;
- the condition number of each outer medium's mode basis;
- the largest number of steps in any layer;
- whether it was opaque.

C# adds the energy residual |R + T + ΣA_layers − 1| (and |R + T − 1| for lossless stacks) to every
result table.

## 6. Tests written before the engine

Each of these is a test before the code exists. The degenerate cases come first, because that is
where the legacy code failed.

1. **Δ.** Berreman's Eqs. 37, 47, 56 and 62 and Passler's Eq. 8. Then the Maxwell residual of every
   eigenpair, for random M.
2. **Isotropic stacks against the 2×2 Abelès result**, closed form:
   - thicknesses from 0 to 1000 waves;
   - angles below, at ±10⁻ᵏ rad from, and beyond the critical angle;
   - absorbing metals.
3. **Degeneracy and continuity.** A uniaxial layer with its optic axis along z, at normal incidence,
   is exactly degenerate. Tilting the axis by 10⁻ᵏ rad, for k = 1 to 12, must change r and t smoothly,
   with no jump.
4. **A defective Δ inside the stack.** A layer exactly at its critical angle (q = 0) must give the
   same result as angles 10⁻ᵏ rad either side.
5. **Thick metal.** 0.5, 5 and 50 µm of aluminium: R must equal the bulk reflectance, and 50 µm must
   be flagged opaque.
6. **Frustrated total reflection**, the Otto geometry with isotropic media: closed form.
7. **Exponential and step bound.** Steps against the exact isotropic propagator at β = 1 and 2 with
   m = 9, and β = 5.37 with m = 13, measuring error and time. The result fixes β and m.
8. **Symmetries.** Reciprocity, and a sample rotated about z by 90° and 180°.

## 7. The native rules

- Everything is a fixed-size 4×4, 4×2 or 2×2 complex value held in local variables, so nothing is
  allocated.
- Loops run over layers and steps. The step count per layer is computed, not stored.
- The only per-layer storage is the interface record, in caller-allocated scratch.
- The QR iteration in the outer media has an iteration limit and reports failure.
- No recursion, no exceptions, no `MAX_*`.

This is what a CUDA kernel looks like, so the port stays mechanical.
