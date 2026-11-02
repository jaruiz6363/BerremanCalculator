/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* bc_schur.h — complex Schur decomposition of a 4×4, and reordering of its diagonal (engine
 * design, Section 2).
 *
 * A = Q T Qᴴ with Q unitary and T upper triangular: Householder reduction to Hessenberg form, then
 * QR iterations with Wilkinson shifts and deflation. Backward stable, unlike the roots of the
 * characteristic quartic. Row-major 4×4 throughout; Q's columns are the Schur vectors.
 */

#ifndef BC_SCHUR_H
#define BC_SCHUR_H

#include "bc_sweep.h"

#define BC_UNIT_ROUNDOFF 1.1102230246251565e-16

/* A rotation G = [[conj(c), conj(s)], [−s, c]] with G (a, b)ᵀ = (r, 0)ᵀ, r = (|a|² + |b|²)^½. */
BC_HD static inline void bc_givens(bc_complex a, bc_complex b, bc_complex* c, bc_complex* s)
{
    const double r = hypot(bc_abs(a), bc_abs(b));
    if (r == 0.0) { *c = bc_c(1.0, 0.0); *s = bc_c(0.0, 0.0); return; }
    *c = bc_scale(a, 1.0 / r);
    *s = bc_scale(b, 1.0 / r);
}

/* Rows i, i+1 of the 4-column matrix A, from column j0: A ← G A. */
BC_HD static inline void bc_rot_rows(bc_complex* A, int i, int j0, bc_complex c, bc_complex s)
{
    for (int j = j0; j < 4; ++j)
    {
        const bc_complex x = A[i * 4 + j], y = A[(i + 1) * 4 + j];
        A[i * 4 + j] = bc_add(bc_mul(bc_conj(c), x), bc_mul(bc_conj(s), y));
        A[(i + 1) * 4 + j] = bc_sub(bc_mul(c, y), bc_mul(s, x));
    }
}

/* Columns i, i+1 of A, rows 0 .. i1 inclusive: A ← A Gᴴ. */
BC_HD static inline void bc_rot_cols(bc_complex* A, int i, int i1, bc_complex c, bc_complex s)
{
    for (int k = 0; k <= i1; ++k)
    {
        const bc_complex x = A[k * 4 + i], y = A[k * 4 + i + 1];
        A[k * 4 + i] = bc_add(bc_mul(c, x), bc_mul(s, y));
        A[k * 4 + i + 1] = bc_sub(bc_mul(bc_conj(c), y), bc_mul(bc_conj(s), x));
    }
}

/* T (in: A; out: upper triangular) and Q (out). Returns BC_OUTER_EIGEN_FAILED if the iteration
 * limit is reached. */
BC_HD static inline bc_status bc_schur4(bc_complex* T, bc_complex* Q)
{
    for (int k = 0; k < 16; ++k) Q[k] = bc_c((k % 5 == 0) ? 1.0 : 0.0, 0.0);

    /* Householder reduction to Hessenberg form. */
    for (int k = 0; k < 2; ++k)
    {
        double nx = 0.0;
        for (int i = k + 1; i < 4; ++i) nx += T[i * 4 + k].re * T[i * 4 + k].re + T[i * 4 + k].im * T[i * 4 + k].im;
        nx = sqrt(nx);
        if (nx == 0.0) continue;
        const bc_complex x0 = T[(k + 1) * 4 + k];
        const double a0 = bc_abs(x0);
        const bc_complex phase = (a0 == 0.0) ? bc_c(1.0, 0.0) : bc_scale(x0, 1.0 / a0);
        // BC_FIXED_OK: a Householder vector of the 4×4
        bc_complex v[4];
        for (int i = 0; i < 4; ++i) v[i] = (i > k) ? T[i * 4 + k] : bc_c(0.0, 0.0);
        v[k + 1] = bc_add(v[k + 1], bc_scale(phase, nx));   /* v = x + e^{iθ}‖x‖ e1 */
        double vv = 0.0;
        for (int i = k + 1; i < 4; ++i) vv += v[i].re * v[i].re + v[i].im * v[i].im;
        /* T ← H T with H = I − 2 v vᴴ / vᴴv */
        for (int j = 0; j < 4; ++j)
        {
            bc_complex s = bc_c(0.0, 0.0);
            for (int i = k + 1; i < 4; ++i) s = bc_add(s, bc_mul(bc_conj(v[i]), T[i * 4 + j]));
            s = bc_scale(s, 2.0 / vv);
            for (int i = k + 1; i < 4; ++i) T[i * 4 + j] = bc_sub(T[i * 4 + j], bc_mul(v[i], s));
        }
        /* T ← T H, Q ← Q H */
        for (int i = 0; i < 4; ++i)
        {
            bc_complex s = bc_c(0.0, 0.0), sq = bc_c(0.0, 0.0);
            for (int j = k + 1; j < 4; ++j)
            {
                s = bc_add(s, bc_mul(T[i * 4 + j], v[j]));
                sq = bc_add(sq, bc_mul(Q[i * 4 + j], v[j]));
            }
            s = bc_scale(s, 2.0 / vv);
            sq = bc_scale(sq, 2.0 / vv);
            for (int j = k + 1; j < 4; ++j)
            {
                T[i * 4 + j] = bc_sub(T[i * 4 + j], bc_mul(s, bc_conj(v[j])));
                Q[i * 4 + j] = bc_sub(Q[i * 4 + j], bc_mul(sq, bc_conj(v[j])));
            }
        }
        for (int i = k + 2; i < 4; ++i) T[i * 4 + k] = bc_c(0.0, 0.0);
    }

    /* Shifted QR on the active window [lo, hi]. */
    int hi = 3, iter = 0, total = 0;
    while (hi > 0)
    {
        if (total > 120) return BC_OUTER_EIGEN_FAILED;
        int lo = hi;
        while (lo > 0)
        {
            const double sub = bc_abs(T[lo * 4 + lo - 1]);
            const double diag = bc_abs(T[lo * 4 + lo]) + bc_abs(T[(lo - 1) * 4 + lo - 1]);
            if (sub <= BC_UNIT_ROUNDOFF * diag || sub == 0.0) { T[lo * 4 + lo - 1] = bc_c(0.0, 0.0); break; }
            --lo;
        }
        if (lo == hi) { --hi; iter = 0; continue; }

        /* Wilkinson shift from the trailing 2×2 of the window; exceptional shifts at 10 and 20. */
        const bc_complex a = T[(hi - 1) * 4 + hi - 1], b = T[(hi - 1) * 4 + hi];
        const bc_complex c = T[hi * 4 + hi - 1], d = T[hi * 4 + hi];
        bc_complex mu;
        if (iter == 10 || iter == 20)
            mu = bc_add(d, bc_c(0.75 * bc_abs(c), 0.0));
        else
        {
            const bc_complex half = bc_scale(bc_sub(a, d), 0.5);
            const bc_complex disc = bc_sqrt(bc_add(bc_mul(half, half), bc_mul(b, c)));
            const bc_complex mid = bc_scale(bc_add(a, d), 0.5);
            const bc_complex m1 = bc_add(mid, disc), m2 = bc_sub(mid, disc);
            mu = (bc_abs(bc_sub(m1, d)) <= bc_abs(bc_sub(m2, d))) ? m1 : m2;
        }

        for (int k = lo; k <= hi; ++k) T[k * 4 + k] = bc_sub(T[k * 4 + k], mu);
        // BC_FIXED_OK: at most three rotations in a 4×4 window
        bc_complex gc[3];
        // BC_FIXED_OK: three rotations
        bc_complex gs[3];
        for (int k = lo; k < hi; ++k)
        {
            bc_givens(T[k * 4 + k], T[(k + 1) * 4 + k], &gc[k - lo], &gs[k - lo]);
            bc_rot_rows(T, k, k, gc[k - lo], gs[k - lo]);
            T[(k + 1) * 4 + k] = bc_c(0.0, 0.0);
        }
        for (int k = lo; k < hi; ++k)
        {
            bc_rot_cols(T, k, (k + 2 < hi ? k + 2 : hi), gc[k - lo], gs[k - lo]);
            bc_rot_cols(Q, k, 3, gc[k - lo], gs[k - lo]);
        }
        for (int k = lo; k <= hi; ++k) T[k * 4 + k] = bc_add(T[k * 4 + k], mu);
        /* The row rotations reach the columns right of the window, and the column rotations the
         * rows above it, so the whole of T sees the same unitary similarity. */
        ++iter;
        ++total;
    }
    for (int i = 1; i < 4; ++i)
        for (int j = 0; j < i; ++j) T[i * 4 + j] = bc_c(0.0, 0.0);
    return BC_OK;
}

/* Swaps the diagonal entries k and k+1 of the upper-triangular T, updating Q, so that
 * A = Q T Qᴴ still holds. */
BC_HD static inline void bc_schur_swap(bc_complex* T, bc_complex* Q, int k)
{
    const bc_complex a = T[k * 4 + k], b = T[(k + 1) * 4 + k + 1];
    if (a.re == b.re && a.im == b.im) return;
    /* The eigenvector of b in the 2×2 block is (t, b − a); rotate it onto e1. */
    bc_complex c, s;
    bc_givens(T[k * 4 + k + 1], bc_sub(b, a), &c, &s);
    bc_rot_rows(T, k, k, c, s);
    bc_rot_cols(T, k, k + 1, c, s);
    bc_rot_cols(Q, k, 3, c, s);
    T[(k + 1) * 4 + k] = bc_c(0.0, 0.0);
    T[k * 4 + k] = b;
    T[(k + 1) * 4 + k + 1] = a;
}

#endif /* BC_SCHUR_H */
