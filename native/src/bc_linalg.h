/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* bc_linalg.h — the small dense solves the engine needs, on caller-supplied storage.
 *
 * Row-major throughout. Everything is a fixed 4×4 or 4×2 shape set by the physics.
 */

#ifndef BC_LINALG_H
#define BC_LINALG_H

#include "bc_complex.h"

/* Solves A X = Y in place for a 4×4 A and a 4×nrhs Y, by LU with partial pivoting. A is
 * overwritten; X replaces Y. Returns BC_BOUNDARY_SINGULAR on an exactly zero pivot. */
BC_HD static inline bc_status bc_solve4(bc_complex* A, bc_complex* Y, int nrhs)
{
    for (int k = 0; k < 4; ++k)
    {
        int p = k;
        double best = bc_abs(A[k * 4 + k]);
        for (int i = k + 1; i < 4; ++i)
        {
            const double a = bc_abs(A[i * 4 + k]);
            if (a > best) { best = a; p = i; }
        }
        if (best == 0.0) return BC_BOUNDARY_SINGULAR;
        if (p != k)
        {
            for (int j = 0; j < 4; ++j) { bc_complex t = A[k * 4 + j]; A[k * 4 + j] = A[p * 4 + j]; A[p * 4 + j] = t; }
            for (int j = 0; j < nrhs; ++j) { bc_complex t = Y[k * nrhs + j]; Y[k * nrhs + j] = Y[p * nrhs + j]; Y[p * nrhs + j] = t; }
        }
        for (int i = k + 1; i < 4; ++i)
        {
            const bc_complex f = bc_div(A[i * 4 + k], A[k * 4 + k]);
            for (int j = k; j < 4; ++j) A[i * 4 + j] = bc_sub(A[i * 4 + j], bc_mul(f, A[k * 4 + j]));
            for (int j = 0; j < nrhs; ++j) Y[i * nrhs + j] = bc_sub(Y[i * nrhs + j], bc_mul(f, Y[k * nrhs + j]));
        }
    }
    for (int k = 3; k >= 0; --k)
        for (int j = 0; j < nrhs; ++j)
        {
            bc_complex s = Y[k * nrhs + j];
            for (int i = k + 1; i < 4; ++i) s = bc_sub(s, bc_mul(A[k * 4 + i], Y[i * nrhs + j]));
            Y[k * nrhs + j] = bc_div(s, A[k * 4 + k]);
        }
    return BC_OK;
}

/* The front system of engine design, Section 3: [W −B] [c; r] = ψ_inc, for both incident waves.
 *
 *   W        4×2  the subspace the stack allows at the front (for a bare interface, the exit
 *                 medium's forward modes)
 *   B        4×2  the incident medium's backward modes
 *   psi_inc  4×2  the incident medium's forward modes, one column per incident wave
 *   c, r     2×2  outputs; column j belongs to incident wave j, row i to outgoing wave i
 */
BC_HD static inline bc_status bc_front_solve(const bc_complex* W, const bc_complex* B,
                                             const bc_complex* psi_inc,
                                             bc_complex* c, bc_complex* r)
{
    // BC_FIXED_OK: 4×4 and 4×2, the shape of the physics, not a capacity
    bc_complex A[16];
    // BC_FIXED_OK: 4×2
    bc_complex Y[8];
    for (int i = 0; i < 4; ++i)
    {
        A[i * 4 + 0] = W[i * 2 + 0];
        A[i * 4 + 1] = W[i * 2 + 1];
        A[i * 4 + 2] = bc_neg(B[i * 2 + 0]);
        A[i * 4 + 3] = bc_neg(B[i * 2 + 1]);
        Y[i * 2 + 0] = psi_inc[i * 2 + 0];
        Y[i * 2 + 1] = psi_inc[i * 2 + 1];
    }
    const bc_status s = bc_solve4(A, Y, 2);
    if (s != BC_OK) return s;
    for (int k = 0; k < 4; ++k)
    {
        c[k] = Y[k];
        r[k] = Y[4 + k];
    }
    return BC_OK;
}

#endif /* BC_LINALG_H */
