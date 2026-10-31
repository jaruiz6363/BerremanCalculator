/* bc_delta.h — Berreman's Δ from the 6×6 material matrix M.
 *
 * The derivation is docs/derivation-delta.md; the section numbers below are that note's. Layouts:
 *
 *   M       36 complex, row-major, rows and columns ordered (Ex, Ey, Ez, H̃x, H̃y, H̃z)
 *   delta   16 complex, row-major, acting on ψ = (Ex, H̃y, Ey, −H̃x)
 *   G        8 complex, row-major 2×4: [Ez; H̃z] = G ψ
 */

#ifndef BC_DELTA_H
#define BC_DELTA_H

#include "bc_complex.h"

/* Section 3 and 4. Builds Δ, and G when G is not null. Returns BC_DELTA_SINGULAR, writing nothing,
 * when d = M33 M66 − M36 M63 is exactly zero. */
BC_HD static inline bc_status bc_build_delta(const bc_complex* M, double xi,
                                             bc_complex* delta, bc_complex* G)
{
    const bc_complex M33 = M[2 * 6 + 2], M36 = M[2 * 6 + 5];
    const bc_complex M63 = M[5 * 6 + 2], M66 = M[5 * 6 + 5];
    const bc_complex d = bc_sub(bc_mul(M33, M66), bc_mul(M36, M63));
    if (bc_is_zero(d)) return BC_DELTA_SINGULAR;

    const bc_complex X = bc_c(xi, 0.0);

    /* B: the right-hand sides of the z constraints in terms of ψ (Section 3). */
    // BC_FIXED_OK: 2×4, the shape of the physics, not a capacity
    bc_complex B[8];
    B[0] = bc_neg(M[2 * 6 + 0]);
    B[1] = bc_sub(bc_neg(X), M[2 * 6 + 4]);
    B[2] = bc_neg(M[2 * 6 + 1]);
    B[3] = M[2 * 6 + 3];
    B[4] = bc_neg(M[5 * 6 + 0]);
    B[5] = bc_neg(M[5 * 6 + 4]);
    B[6] = bc_sub(X, M[5 * 6 + 1]);
    B[7] = M[5 * 6 + 3];

    /* G = C⁻¹ B, with C⁻¹ = [[M66, −M36], [−M63, M33]] / d. */
    // BC_FIXED_OK: 2×4, the shape of the physics, not a capacity
    bc_complex g[8];
    for (int j = 0; j < 4; ++j)
    {
        g[j]     = bc_div(bc_sub(bc_mul(M66, B[j]), bc_mul(M36, B[4 + j])), d);
        g[4 + j] = bc_div(bc_sub(bc_mul(M33, B[4 + j]), bc_mul(M63, B[j])), d);
    }

    /* F (6×4): v = F ψ, with Ex = ψ1, Ey = ψ3, Ez = G1 ψ, H̃x = −ψ4, H̃y = ψ2, H̃z = G2 ψ. */
    // BC_FIXED_OK: 6×4, the shape of the physics, not a capacity
    bc_complex F[24];
    for (int k = 0; k < 24; ++k) F[k] = bc_c(0.0, 0.0);
    F[0 * 4 + 0] = bc_c(1.0, 0.0);
    F[1 * 4 + 2] = bc_c(1.0, 0.0);
    F[3 * 4 + 3] = bc_c(-1.0, 0.0);
    F[4 * 4 + 1] = bc_c(1.0, 0.0);
    for (int j = 0; j < 4; ++j) { F[2 * 4 + j] = g[j]; F[5 * 4 + j] = g[4 + j]; }

    /* Rows of M F that Δ needs: D̃x (0), D̃y (1), B̃x (3), B̃y (4). */
    for (int j = 0; j < 4; ++j)
    {
        bc_complex Dx = bc_c(0.0, 0.0), Dy = Dx, Bx = Dx, By = Dx;
        for (int m = 0; m < 6; ++m)
        {
            const bc_complex f = F[m * 4 + j];
            Dx = bc_add(Dx, bc_mul(M[0 * 6 + m], f));
            Dy = bc_add(Dy, bc_mul(M[1 * 6 + m], f));
            Bx = bc_add(Bx, bc_mul(M[3 * 6 + m], f));
            By = bc_add(By, bc_mul(M[4 * 6 + m], f));
        }
        delta[0 * 4 + j] = bc_add(By, bc_scale(g[j], xi));       /* B̃y + ξ Ez  */
        delta[1 * 4 + j] = Dx;                                   /* D̃x         */
        delta[2 * 4 + j] = bc_neg(Bx);                           /* −B̃x        */
        delta[3 * 4 + j] = bc_sub(Dy, bc_scale(g[4 + j], xi));   /* D̃y − ξ H̃z */
    }

    if (G != nullptr)
        for (int k = 0; k < 8; ++k) G[k] = g[k];
    return BC_OK;
}

/* The full six-component field v = (Ex, Ey, Ez, H̃x, H̃y, H̃z) of ψ, given G from bc_build_delta. */
BC_HD static inline void bc_full_field(const bc_complex* G, const bc_complex* psi, bc_complex* v)
{
    bc_complex ez = bc_c(0.0, 0.0), hz = ez;
    for (int j = 0; j < 4; ++j)
    {
        ez = bc_add(ez, bc_mul(G[j], psi[j]));
        hz = bc_add(hz, bc_mul(G[4 + j], psi[j]));
    }
    v[0] = psi[0];
    v[1] = psi[2];
    v[2] = ez;
    v[3] = bc_neg(psi[3]);
    v[4] = psi[1];
    v[5] = hz;
}

/* Section 6: the z component of the time-averaged Poynting vector, without the factor 1/(2 Z0). */
BC_HD static inline double bc_poynting_z(const bc_complex* psi)
{
    return bc_mul(psi[0], bc_conj(psi[1])).re + bc_mul(psi[2], bc_conj(psi[3])).re;
}

#endif /* BC_DELTA_H */
