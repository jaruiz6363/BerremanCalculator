/* bc_outer.h — modes of an isotropic outer medium (engine design, Section 2).
 *
 * Layout of a mode set: 16 complex, row-major 4×4, whose COLUMNS are the field vectors ψ of
 * (p forward, s forward, p backward, s backward), each of unit amplitude along p̂ or ŝ in the
 * contract's convention.
 */

#ifndef BC_OUTER_H
#define BC_OUTER_H

#include "bc_complex.h"
#include "bc_delta.h"
#include "bc_schur.h"

/* Exactly isotropic: ε and μ scalar multiples of I, ρ = ρ' = 0. A property of the input, tested
 * with exact comparisons, never a tolerance. On success *eps and *mu are the scalars. */
BC_HD static inline int bc_is_isotropic(const bc_complex* M, bc_complex* eps, bc_complex* mu)
{
    for (int i = 0; i < 6; ++i)
        for (int j = 0; j < 6; ++j)
        {
            if (i == j) continue;
            if (!bc_is_zero(M[i * 6 + j])) return 0;
        }
    const bc_complex e = M[0], m = M[3 * 6 + 3];
    for (int i = 1; i < 3; ++i)
    {
        const bc_complex ei = M[i * 6 + i], mi = M[(i + 3) * 6 + i + 3];
        if (ei.re != e.re || ei.im != e.im || mi.re != m.re || mi.im != m.im) return 0;
    }
    *eps = e;
    *mu = m;
    return 1;
}

/* The four modes of an isotropic medium at tangential wavevector ξ, and the forward normal
 * wavenumber q. Fails with BC_GRAZING_OUTER, writing nothing, if q = 0 or N = 0. */
BC_HD static inline bc_status bc_isotropic_modes(bc_complex eps, bc_complex mu, double xi,
                                                 bc_complex* modes, bc_complex* q_out)
{
    const bc_complex em = bc_mul(eps, mu);
    const bc_complex N = bc_sqrt(em);
    const bc_complex q = bc_branch_forward(bc_sqrt(bc_sub(em, bc_c(xi * xi, 0.0))));
    if (bc_is_zero(q) || bc_is_zero(N)) return BC_GRAZING_OUTER;

    const bc_complex zero = bc_c(0.0, 0.0), one = bc_c(1.0, 0.0);
    const bc_complex qN = bc_div(q, N), Nmu = bc_div(N, mu), qmu = bc_div(q, mu);

    /* Columns: p+ = (q/N, N/μ, 0, 0)   s+ = (0, 0, 1, q/μ)
     *          p- = (-q/N, N/μ, 0, 0)  s- = (0, 0, 1, -q/μ) */
    modes[0 * 4 + 0] = qN;   modes[0 * 4 + 1] = zero; modes[0 * 4 + 2] = bc_neg(qN); modes[0 * 4 + 3] = zero;
    modes[1 * 4 + 0] = Nmu;  modes[1 * 4 + 1] = zero; modes[1 * 4 + 2] = Nmu;        modes[1 * 4 + 3] = zero;
    modes[2 * 4 + 0] = zero; modes[2 * 4 + 1] = one;  modes[2 * 4 + 2] = zero;       modes[2 * 4 + 3] = one;
    modes[3 * 4 + 0] = zero; modes[3 * 4 + 1] = qmu;  modes[3 * 4 + 2] = zero;       modes[3 * 4 + 3] = bc_neg(qmu);

    if (q_out != nullptr) *q_out = q;
    return BC_OK;
}

/* ---- any outer medium ------------------------------------------------------------------------ */

/* Lossless exactly: M Hermitian, which is ε and μ Hermitian and ρ' = ρ† (derivation, Section 7). */
BC_HD static inline int bc_is_hermitian(const bc_complex* M)
{
    for (int i = 0; i < 6; ++i)
        for (int j = i; j < 6; ++j)
        {
            const bc_complex a = M[i * 6 + j], b = M[j * 6 + i];
            if (a.re != b.re || a.im != -b.im) return 0;
        }
    return 1;
}

/* Unit 2-norm, with the larger of the tangential E components (ψ1, ψ3) made real and positive, so
 * a mode's phase is a property of the medium and not of the iteration that found it. */
BC_HD static inline void bc_normalise_mode(bc_complex* v)
{
    double n = 0.0;
    for (int i = 0; i < 4; ++i) n += v[i].re * v[i].re + v[i].im * v[i].im;
    n = sqrt(n);
    const bc_complex big = (bc_abs(v[0]) >= bc_abs(v[2])) ? v[0] : v[2];
    const double ab = bc_abs(big);
    const bc_complex ph = (ab == 0.0) ? bc_c(1.0, 0.0) : bc_scale(bc_conj(big), 1.0 / ab);
    for (int i = 0; i < 4; ++i) v[i] = bc_scale(bc_mul(v[i], ph), 1.0 / n);
}

/* The eigenvector of T[k][k] by back substitution in the upper-triangular T, mapped through Q.
 * Denominators below the rounding bound tol are replaced by tol: used only for the SIGN of S_z of
 * a real root, which the replacement does not change. */
BC_HD static inline void bc_schur_eigvec(const bc_complex* T, const bc_complex* Q, int k, double tol,
                                         bc_complex* v)
{
    // BC_FIXED_OK: a 4-vector
    bc_complex y[4];
    const bc_complex lam = T[k * 4 + k];
    for (int i = 0; i < 4; ++i) y[i] = bc_c(i == k ? 1.0 : 0.0, 0.0);
    for (int j = k - 1; j >= 0; --j)
    {
        bc_complex s = bc_c(0.0, 0.0);
        for (int i = j + 1; i <= k; ++i) s = bc_add(s, bc_mul(T[j * 4 + i], y[i]));
        bc_complex den = bc_sub(T[j * 4 + j], lam);
        if (bc_abs(den) < tol) den = bc_c(tol, 0.0);
        y[j] = bc_neg(bc_div(s, den));
    }
    for (int i = 0; i < 4; ++i)
    {
        bc_complex s = bc_c(0.0, 0.0);
        for (int j = 0; j <= k; ++j) s = bc_add(s, bc_mul(Q[i * 4 + j], y[j]));
        v[i] = s;
    }
}

/* The four modes of ANY medium, from the Schur form of its Δ (engine design, Section 2).
 *
 * Output layout as bc_isotropic_modes: columns (forward 0, forward 1, backward 0, backward 1).
 * Within each pair, the more p-like mode (larger |ψ1|² + |ψ2|² once normalised) comes first.
 * Each mode has unit 2-norm and the phase fixed by bc_normalise_mode.
 *
 * Coincident roots in a pair: every vector of the pair's subspace is a mode, and the basis is the
 * projection of the p-like and s-like unit vectors (ψ1 and ψ3) onto it, so labels stay continuous
 * as, for example, an optic axis tilts through the normal.
 *
 * Fails with BC_GRAZING_OUTER for a root at q = 0, BC_OUTER_EIGEN_FAILED if the QR iteration does
 * not converge or the roots do not split two forward and two backward, and BC_OUTER_DEFECTIVE if a
 * pair coincides without two independent modes. */
BC_HD static inline bc_status bc_general_modes(const bc_complex* M, double xi, bc_complex* modes)
{
    // BC_FIXED_OK: 4×4 working matrices
    bc_complex D[16];
    // BC_FIXED_OK: 4×4
    bc_complex T0[16];
    // BC_FIXED_OK: 4×4
    bc_complex Q0[16];
    bc_status st = bc_build_delta(M, xi, D, nullptr);
    if (st != BC_OK) return st;
    const double tol = 1e3 * BC_UNIT_ROUNDOFF * bc_norm1_4(D);
    for (int k = 0; k < 16; ++k) T0[k] = D[k];
    st = bc_schur4(T0, Q0);
    if (st != BC_OK) return st;

    /* Classify. */
    // BC_FIXED_OK: one flag per root
    int forward[4];
    int n_forward = 0;
    for (int k = 0; k < 4; ++k)
    {
        const bc_complex q = T0[k * 4 + k];
        if (bc_abs(q) <= tol) return BC_GRAZING_OUTER;
        if (q.im > tol) forward[k] = 1;
        else if (q.im < -tol) forward[k] = 0;
        else
        {
            // BC_FIXED_OK: a 4-vector
            bc_complex v[4];
            bc_schur_eigvec(T0, Q0, k, tol, v);
            const double sz = bc_poynting_z(v);
            if (sz == 0.0) return BC_GRAZING_OUTER;
            forward[k] = sz > 0.0 ? 1 : 0;
        }
        n_forward += forward[k];
    }
    if (n_forward != 2) return BC_OUTER_EIGEN_FAILED;

    for (int group = 1; group >= 0; --group)
    {
        // BC_FIXED_OK: 4×4 copies for this pair's reordering
        bc_complex T[16];
        // BC_FIXED_OK: 4×4
        bc_complex Q[16];
        // BC_FIXED_OK: which root sits at each position
        int f[4];
        for (int k = 0; k < 16; ++k) { T[k] = T0[k]; Q[k] = Q0[k]; }
        for (int k = 0; k < 4; ++k) f[k] = forward[k];

        /* Bubble this group's two roots to positions 0 and 1. */
        for (int target = 0; target < 2; ++target)
        {
            int k = target;
            while (f[k] != group) ++k;
            for (int j = k - 1; j >= target; --j)
            {
                bc_schur_swap(T, Q, j);
                const int t = f[j]; f[j] = f[j + 1]; f[j + 1] = t;
            }
        }

        // BC_FIXED_OK: the pair's two modes
        bc_complex v0[4];
        // BC_FIXED_OK: 4-vector
        bc_complex v1[4];
        const bc_complex a = T[0], c = T[1], b = T[5];
        if (bc_abs(bc_sub(b, a)) > tol)
        {
            const bc_complex w = bc_div(c, bc_sub(b, a));
            for (int i = 0; i < 4; ++i)
            {
                v0[i] = Q[i * 4 + 0];
                v1[i] = bc_add(bc_mul(w, Q[i * 4 + 0]), Q[i * 4 + 1]);
            }
        }
        else if (bc_abs(c) <= tol)
        {
            /* Coincident: project e1 (p-like) and e3 (s-like) onto span(q0, q1). */
            for (int i = 0; i < 4; ++i)
            {
                v0[i] = bc_add(bc_mul(Q[i * 4 + 0], bc_conj(Q[0 * 4 + 0])), bc_mul(Q[i * 4 + 1], bc_conj(Q[0 * 4 + 1])));
                v1[i] = bc_add(bc_mul(Q[i * 4 + 0], bc_conj(Q[2 * 4 + 0])), bc_mul(Q[i * 4 + 1], bc_conj(Q[2 * 4 + 1])));
            }
        }
        else return BC_OUTER_DEFECTIVE;

        bc_normalise_mode(v0);
        bc_normalise_mode(v1);
        const double p0 = v0[0].re * v0[0].re + v0[0].im * v0[0].im + v0[1].re * v0[1].re + v0[1].im * v0[1].im;
        const double p1 = v1[0].re * v1[0].re + v1[0].im * v1[0].im + v1[1].re * v1[1].re + v1[1].im * v1[1].im;
        const int col = (group == 1) ? 0 : 2;
        const bc_complex* first = (p0 >= p1) ? v0 : v1;
        const bc_complex* second = (p0 >= p1) ? v1 : v0;
        for (int i = 0; i < 4; ++i)
        {
            modes[i * 4 + col] = first[i];
            modes[i * 4 + col + 1] = second[i];
        }
    }
    return BC_OK;
}

/* The modes of an outer medium: closed form when it is exactly isotropic, Schur otherwise. */
BC_HD static inline bc_status bc_outer_modes(const bc_complex* M, double xi, bc_complex* modes)
{
    bc_complex eps, mu;
    if (bc_is_isotropic(M, &eps, &mu)) return bc_isotropic_modes(eps, mu, xi, modes, nullptr);
    return bc_general_modes(M, xi, modes);
}

#endif /* BC_OUTER_H */
