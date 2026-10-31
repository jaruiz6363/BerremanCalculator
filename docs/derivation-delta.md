# Derivation of Berreman's Δ

This note derives the 4×4 matrix Δ from Maxwell's equations, in the conventions of
[the contract](contract.md), for a general 6×6 material matrix M. The engine codes exactly what is
written here, and its tests (engine design, Section 6) check it. It is derived from scratch, not
transcribed: Berreman (1972, Eqs. 19–24) is the reference it is checked against, and where his
Gaussian units and ours differ, this note is what the engine follows.

## 1. Maxwell's equations in normalised form

Time dependence is exp(−iωt). In SI units, with no free charges or currents:

```
∇ × E =  iω B
∇ × H = −iω D
```

The magnetic field is normalised as H̃ = Z₀H, with Z₀ = (μ₀/ε₀)^½, so that E and H̃ have the same
units. Define the dimensionless constitutive relation by

```
D = ε₀ D̃,     B = B̃ / c,     with     [D̃; B̃] = M [E; H̃],     M = [[ε, ρ], [ρ', μ]]
```

For an isotropic medium (ε = ε_r I, μ = μ_r I, ρ = ρ' = 0), this gives D = ε₀ε_r E and
B = μ_r Z₀ H / c = μ₀μ_r H, as it should. Using ω/c = k₀ and Z₀ε₀ω = k₀:

```
∇ × E  =  i k₀ B̃  =  i k₀ (ρ' E + μ H̃)                                          (1)
∇ × H̃  = −i k₀ D̃  = −i k₀ (ε E + ρ H̃)                                          (2)
```

Every constant in M is dimensionless, and only k₀ carries a length.

## 2. Fields in a stratified medium

The medium depends on z only. The tangential wavevector is conserved, so every field varies as
exp(i k₀ ξ x) with ξ the same in every layer, and nothing depends on y:

```
∂/∂x → i k₀ ξ,      ∂/∂y → 0
```

Write the derivative in z as d/dz = i k₀ 𝒟, so 𝒟 = (1/(i k₀)) d/dz. The components of (1) and (2) are:

| | from (1), ∇ × E = i k₀ B̃ | from (2), ∇ × H̃ = −i k₀ D̃ |
|---|---|---|
| x | −dE_y/dz = i k₀ B̃_x | −dH̃_y/dz = −i k₀ D̃_x |
| y | dE_x/dz − i k₀ξ E_z = i k₀ B̃_y | dH̃_x/dz − i k₀ξ H̃_z = −i k₀ D̃_y |
| z | i k₀ ξ E_y = i k₀ B̃_z | i k₀ ξ H̃_y = −i k₀ D̃_z |

The z rows contain no derivative. They are algebraic constraints:

```
B̃_z =  ξ E_y                                                                 (3)
D̃_z = −ξ H̃_y                                                                 (4)
```

The x and y rows are four first-order equations. With ψ = (E_x, H̃_y, E_y, −H̃_x), Berreman's
ordering:

```
𝒟 ψ₁ = 𝒟 E_x    =  B̃_y + ξ E_z                                               (5)
𝒟 ψ₂ = 𝒟 H̃_y    =  D̃_x                                                        (6)
𝒟 ψ₃ = 𝒟 E_y    = −B̃_x                                                        (7)
𝒟 ψ₄ = 𝒟(−H̃_x) =  D̃_y − ξ H̃_z                                               (8)
```

Once E_z and H̃_z are expressed in terms of ψ, the right-hand sides are linear in ψ, and
dψ/dz = i k₀ Δ ψ.

## 3. Eliminating E_z and H̃_z

Number the components of v = (E_x, E_y, E_z, H̃_x, H̃_y, H̃_z) from 1 to 6, so that M_kj is the element
of M in row k and column j. Rows 3 and 6 of M give D̃_z and B̃_z. Constraints (4) and (3) become

```
M₃₃ E_z + M₃₆ H̃_z = −ξ H̃_y − (M₃₁ E_x + M₃₂ E_y + M₃₄ H̃_x + M₃₅ H̃_y)
M₆₃ E_z + M₆₆ H̃_z =  ξ E_y − (M₆₁ E_x + M₆₂ E_y + M₆₄ H̃_x + M₆₅ H̃_y)
```

In terms of ψ (with H̃_x = −ψ₄, H̃_y = ψ₂), this is C [E_z; H̃_z] = B ψ, with

```
C = [ M₃₃  M₃₆ ]          B = [ −M₃₁   −ξ − M₃₅   −M₃₂       M₃₄ ]
    [ M₆₃  M₆₆ ]              [ −M₆₁   −M₆₅        ξ − M₆₂   M₆₄ ]
```

It is solvable when

```
d = det C = M₃₃ M₆₆ − M₃₆ M₆₃ ≠ 0
```

and then [E_z; H̃_z] = G ψ with G = C⁻¹ B, a 2×4 matrix. If d = 0, the point fails with
`BC_DELTA_SINGULAR` (engine design, Section 5). For a non-magnetic medium, d = ε_zz, which is zero
only at an epsilon-near-zero condition along z, where the 4×4 method itself does not apply.

## 4. Assembling Δ

Let L be the 6×4 matrix that places ψ in v with E_z = H̃_z = 0:

```
E_x = ψ₁,   E_y = ψ₃,   H̃_x = −ψ₄,   H̃_y = ψ₂
```

Then the full field is

```
v = L ψ + e₃ (G ψ)₁ + e₆ (G ψ)₂ = F ψ,        F = L + e₃ G₁ + e₆ G₂        (6×4)
```

where G₁ and G₂ are the rows of G, and e₃ and e₆ are unit vectors. The flux densities are
[D̃; B̃] = M F ψ. Equations (5)–(8) pick rows of M F:

```
Δ₁ = (M F)₅ + ξ G₁          (B̃_y + ξ E_z)
Δ₂ = (M F)₁                 (D̃_x)
Δ₃ = −(M F)₄                (−B̃_x)
Δ₄ = (M F)₂ − ξ G₂          (D̃_y − ξ H̃_z)
```

Here Δ_k is row k of Δ, and (M F)_k is row k of the 6×4 product. **This is the form the engine
codes.** It builds F, forms M F, and picks rows, with no hand-expanded entries to mistype. The cost
is one 2×2 solve and one 6×6 by 6×4 product per layer and wavelength, which is negligible next to
the exponentials.

## 5. Checks

### 5.1 Isotropic medium

With ε = ε I, μ = μ I and ρ = ρ' = 0: C = diag(ε, μ), so E_z = −ξ H̃_y/ε and H̃_z = ξ E_y/μ, and

```
    [ 0   μ − ξ²/ε   0          0 ]
Δ = [ ε   0          0          0 ]
    [ 0   0          0          μ ]
    [ 0   0          ε − ξ²/μ   0 ]
```

The p block [[0, μ − ξ²/ε], [ε, 0]] has eigenvalues q² = ε(μ − ξ²/ε) = εμ − ξ², and so does the s
block. Its eigenvectors are exactly the modes in engine design, Section 2:

- p: Δ(q/N, N/μ, 0, 0) = ((μ − ξ²/ε)N/μ, εq/N, 0, 0) = q (q/N, N/μ, 0, 0), using N² = εμ and
  q² = εμ − ξ².
- s: Δ(0, 0, 1, q/μ) = (0, 0, q, ε − ξ²/μ) = q (0, 0, 1, q/μ).

### 5.2 Non-magnetic anisotropic medium

With μ = I and ρ = ρ' = 0, but any ε: d = ε₃₃, E_z = −(ε₃₁ψ₁ + ξψ₂ + ε₃₂ψ₃)/ε₃₃ and H̃_z = ξψ₃. Then:

```
Δ₁₁ = −ξ ε₃₁/ε₃₃            Δ₁₂ = 1 − ξ²/ε₃₃            Δ₁₃ = −ξ ε₃₂/ε₃₃                 Δ₁₄ = 0
Δ₂₁ = ε₁₁ − ε₁₃ε₃₁/ε₃₃      Δ₂₂ = −ξ ε₁₃/ε₃₃            Δ₂₃ = ε₁₂ − ε₁₃ε₃₂/ε₃₃           Δ₂₄ = 0
Δ₃₁ = 0                     Δ₃₂ = 0                     Δ₃₃ = 0                          Δ₃₄ = 1
Δ₄₁ = ε₂₁ − ε₂₃ε₃₁/ε₃₃      Δ₄₂ = −ξ ε₂₃/ε₃₃            Δ₄₃ = ε₂₂ − ε₂₃ε₃₂/ε₃₃ − ξ²      Δ₄₄ = 0
```

This is the familiar Δ of a dielectric tensor, and it must agree with Berreman's Eq. 37 for an
orthorhombic medium aligned with the axes. These expanded entries are a test fixture, compared
entry by entry with the general assembly of Section 4. They are not code.

### 5.3 Maxwell residual, independent of Δ

For a random complex M (with d ≠ 0) and a random real ξ:

1. Compute each eigenpair (q, ψ) of Δ.
2. Reconstruct the full field v = F ψ.
3. With k = (ξ, 0, q), a plane wave exp(i k₀ k·r) satisfies (1) and (2) exactly when

   ```
   k × E = B̃ = (M v)_{4..6},        k × H̃ = −D̃ = −(M v)_{1..3}
   ```

4. The residual of these six equations, relative to |M||v|, must be at the rounding level.

This test uses Maxwell's equations directly and none of the algebra above, so an error in the
elimination cannot pass it.

## 6. Power flow

The time-averaged Poynting vector is S = ½ Re(E × H*). Its z component, in terms of ψ:

```
S_z = ½ Re(E_x H_y* − E_y H_x*) = (1/(2Z₀)) Re(ψ₁ ψ₂* + ψ₃ ψ₄*)
```

Only ratios of S_z enter R, T and A, so the engine drops the factor 1/(2Z₀). This is the expression
in engine design, Section 4, and the direction test of Section 2.

## 7. Propagation and the losslessness test

In a homogeneous layer Δ is constant, so

```
ψ(z + h) = exp(i k₀ h Δ) ψ(z)
```

The backward sweep of engine design, Section 3, uses exp(−i k₀ h Δ).

A medium is lossless when the time average of ∇·S is zero for every field. With the normalisation
of Section 1, that means M is Hermitian: ε = ε†, μ = μ† and ρ' = ρ†. This is the exact test applied to
the incident medium (engine design, Section 2.5, and the contract, Section 3).

## 8. Rotation of M

Rotating a material by R (contract, Section 1) rotates every block of M the same way, because E, H̃,
D̃ and B̃ are all vectors:

```
M_lab = 𝓡 M 𝓡ᵀ,       𝓡 = [[R, 0], [0, R]]
```

R is real and orthogonal, so 𝓡ᵀ = 𝓡⁻¹. This is done in C#; the engine sees only M_lab.

Two kinds of material need a check before they are entered, because the papers' definitions differ
from ours:

- For **optically active and magnetoelectric media**, ρ and ρ' must be in this note's units. A
  paper's chirality or magnetoelectric parameters are converted using its own definitions (for
  example Lakhtakia's Γ, Berreman's Eq. 12), and the conversion is written beside the example that
  uses it.
- For the **ferrite μ tensor**, the conversion from exp(+jωt) is a complex conjugation of every
  element. That flips the sign of the imaginary off-diagonal term, as it should.
