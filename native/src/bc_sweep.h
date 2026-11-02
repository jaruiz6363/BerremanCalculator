/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* bc_sweep.h — the matrix exponential and the re-orthonormalisation step (engine design,
 * Section 3).
 *
 * Row-major throughout: a 4×4 is 16 complex, a 4×2 is 8, a 2×2 is 4.
 */

#ifndef BC_SWEEP_H
#define BC_SWEEP_H

#include "bc_linalg.h"

BC_HD static inline void bc_mat4_mul(const bc_complex* A, const bc_complex* B, bc_complex* C)
{
    for (int i = 0; i < 4; ++i)
        for (int j = 0; j < 4; ++j)
        {
            bc_complex s = bc_c(0.0, 0.0);
            for (int k = 0; k < 4; ++k) s = bc_add(s, bc_mul(A[i * 4 + k], B[k * 4 + j]));
            C[i * 4 + j] = s;
        }
}

/* The 1-norm: the largest column sum of moduli. Higham's θ_m bounds are in this norm. */
BC_HD static inline double bc_norm1_4(const bc_complex* A)
{
    double best = 0.0;
    for (int j = 0; j < 4; ++j)
    {
        double s = 0.0;
        for (int i = 0; i < 4; ++i) s += bc_abs(A[i * 4 + j]);
        if (s > best) best = s;
    }
    return best;
}

/* The [m/m] Padé approximant to exp(A), m = 9 or 13, with no scaling and squaring. Accurate in
 * Higham's backward sense when ‖A‖₁ ≤ θ_m (2.098 for m = 9, 5.372 for m = 13); the caller
 * guarantees that by its step size. The evaluation splits the numerator into even and odd parts,
 * p(A) = V + U and q(A) = p(−A) = V − U, with Higham's grouping (2005, Section 2): 5 matrix
 * products for m = 9 and 6 for m = 13, then one solve. The coefficients come from the recurrence
 * c₀ = 1, c_{j+1} = c_j (m − j) / ((2m − j)(j + 1)), so no table is transcribed.
 *
 * Returns BC_NOT_IMPLEMENTED for any other m, and BC_BOUNDARY_SINGULAR if q(A) is singular,
 * which Higham's bound on ‖q_m(A)⁻¹‖ rules out within θ_m. */
BC_HD static inline bc_status bc_expm_pade(const bc_complex* A, int m, bc_complex* E)
{
    if (m != 9 && m != 13) return BC_NOT_IMPLEMENTED;

    // BC_FIXED_OK: Padé coefficients, at most 14 for the largest supported degree
    double c[14];
    c[0] = 1.0;
    for (int j = 0; j < m; ++j) c[j + 1] = c[j] * (double)(m - j) / ((double)(2 * m - j) * (double)(j + 1));

    // BC_FIXED_OK: 4×4 working matrices
    bc_complex A2[16];
    // BC_FIXED_OK: 4×4
    bc_complex A4[16];
    // BC_FIXED_OK: 4×4
    bc_complex A6[16];
    // BC_FIXED_OK: 4×4
    bc_complex T[16];
    // BC_FIXED_OK: 4×4
    bc_complex U[16];
    // BC_FIXED_OK: 4×4
    bc_complex V[16];

    bc_mat4_mul(A, A, A2);
    bc_mat4_mul(A2, A2, A4);
    bc_mat4_mul(A4, A2, A6);

    if (m == 9)
    {
        // BC_FIXED_OK: 4×4
        bc_complex A8[16];
        bc_mat4_mul(A4, A4, A8);
        for (int k = 0; k < 16; ++k)
        {
            const double id = (k % 5 == 0) ? 1.0 : 0.0;
            T[k] = bc_add(bc_add(bc_add(bc_scale(A8[k], c[9]), bc_scale(A6[k], c[7])),
                                 bc_add(bc_scale(A4[k], c[5]), bc_scale(A2[k], c[3]))),
                          bc_c(c[1] * id, 0.0));
            V[k] = bc_add(bc_add(bc_add(bc_scale(A8[k], c[8]), bc_scale(A6[k], c[6])),
                                 bc_add(bc_scale(A4[k], c[4]), bc_scale(A2[k], c[2]))),
                          bc_c(c[0] * id, 0.0));
        }
        bc_mat4_mul(A, T, U);
    }
    else
    {
        // BC_FIXED_OK: 4×4
        bc_complex S[16];
        for (int k = 0; k < 16; ++k)
            S[k] = bc_add(bc_add(bc_scale(A6[k], c[13]), bc_scale(A4[k], c[11])), bc_scale(A2[k], c[9]));
        bc_mat4_mul(A6, S, T);
        for (int k = 0; k < 16; ++k)
        {
            const double id = (k % 5 == 0) ? 1.0 : 0.0;
            T[k] = bc_add(T[k], bc_add(bc_add(bc_scale(A6[k], c[7]), bc_scale(A4[k], c[5])),
                                       bc_add(bc_scale(A2[k], c[3]), bc_c(c[1] * id, 0.0))));
        }
        bc_mat4_mul(A, T, U);

        for (int k = 0; k < 16; ++k)
            S[k] = bc_add(bc_add(bc_scale(A6[k], c[12]), bc_scale(A4[k], c[10])), bc_scale(A2[k], c[8]));
        bc_mat4_mul(A6, S, V);
        for (int k = 0; k < 16; ++k)
        {
            const double id = (k % 5 == 0) ? 1.0 : 0.0;
            V[k] = bc_add(V[k], bc_add(bc_add(bc_scale(A6[k], c[6]), bc_scale(A4[k], c[4])),
                                       bc_add(bc_scale(A2[k], c[2]), bc_c(c[0] * id, 0.0))));
        }
    }

    /* q E = p, with q = V − U (overwritten by the solve) and p = V + U (becomes E). */
    for (int k = 0; k < 16; ++k)
    {
        E[k] = bc_add(V[k], U[k]);
        T[k] = bc_sub(V[k], U[k]);
    }
    return bc_solve4(T, E, 4);
}

/* Thin QR of a 4×2 W, in place: W ← Q with orthonormal columns, R (2×2, upper triangular) such
 * that the old W = Q R. Gram–Schmidt with one re-orthogonalisation, which is enough for two
 * columns ("twice is enough"). Returns BC_BOUNDARY_SINGULAR if a column vanishes exactly. */
BC_HD static inline bc_status bc_qr42(bc_complex* W, bc_complex* R)
{
    double n1 = 0.0;
    for (int i = 0; i < 4; ++i) n1 += W[i * 2].re * W[i * 2].re + W[i * 2].im * W[i * 2].im;
    n1 = sqrt(n1);
    if (n1 == 0.0) return BC_BOUNDARY_SINGULAR;
    for (int i = 0; i < 4; ++i) W[i * 2] = bc_scale(W[i * 2], 1.0 / n1);

    bc_complex r12 = bc_c(0.0, 0.0);
    for (int pass = 0; pass < 2; ++pass)
    {
        bc_complex s = bc_c(0.0, 0.0);
        for (int i = 0; i < 4; ++i) s = bc_add(s, bc_mul(bc_conj(W[i * 2]), W[i * 2 + 1]));
        for (int i = 0; i < 4; ++i) W[i * 2 + 1] = bc_sub(W[i * 2 + 1], bc_mul(s, W[i * 2]));
        r12 = bc_add(r12, s);
    }

    double n2 = 0.0;
    for (int i = 0; i < 4; ++i) n2 += W[i * 2 + 1].re * W[i * 2 + 1].re + W[i * 2 + 1].im * W[i * 2 + 1].im;
    n2 = sqrt(n2);
    if (n2 == 0.0) return BC_BOUNDARY_SINGULAR;
    for (int i = 0; i < 4; ++i) W[i * 2 + 1] = bc_scale(W[i * 2 + 1], 1.0 / n2);

    R[0] = bc_c(n1, 0.0);
    R[1] = r12;
    R[2] = bc_c(0.0, 0.0);
    R[3] = bc_c(n2, 0.0);
    return BC_OK;
}

/* L ← L R⁻¹ for a 2×2 L and an upper-triangular R with real positive diagonal (from bc_qr42).
 * R⁻¹ = [[1/a, −b/(a d)], [0, 1/d]]. */
BC_HD static inline void bc_right_mul_inv_upper(bc_complex* L, const bc_complex* R)
{
    const double a = R[0].re, d = R[3].re;
    const bc_complex b = R[1];
    const bc_complex i12 = bc_scale(b, -1.0 / (a * d));
    for (int i = 0; i < 2; ++i)
    {
        const bc_complex l0 = L[i * 2 + 0], l1 = L[i * 2 + 1];
        L[i * 2 + 0] = bc_scale(l0, 1.0 / a);
        L[i * 2 + 1] = bc_add(bc_mul(l0, i12), bc_scale(l1, 1.0 / d));
    }
}

/* C = A B for 4×4 A and 4×2 B. */
BC_HD static inline void bc_mul_44_42(const bc_complex* A, const bc_complex* B, bc_complex* C)
{
    for (int i = 0; i < 4; ++i)
        for (int j = 0; j < 2; ++j)
        {
            bc_complex s = bc_c(0.0, 0.0);
            for (int k = 0; k < 4; ++k) s = bc_add(s, bc_mul(A[i * 4 + k], B[k * 2 + j]));
            C[i * 2 + j] = s;
        }
}

/* C = A B for 2×2 A and B. */
BC_HD static inline void bc_mul_22(const bc_complex* A, const bc_complex* B, bc_complex* C)
{
    C[0] = bc_add(bc_mul(A[0], B[0]), bc_mul(A[1], B[2]));
    C[1] = bc_add(bc_mul(A[0], B[1]), bc_mul(A[1], B[3]));
    C[2] = bc_add(bc_mul(A[2], B[0]), bc_mul(A[3], B[2]));
    C[3] = bc_add(bc_mul(A[2], B[1]), bc_mul(A[3], B[3]));
}

#endif /* BC_SWEEP_H */
