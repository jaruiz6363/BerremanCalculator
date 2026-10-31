/* bc_outer.h — modes of an isotropic outer medium (engine design, Section 2).
 *
 * Layout of a mode set: 16 complex, row-major 4×4, whose COLUMNS are the field vectors ψ of
 * (p forward, s forward, p backward, s backward), each of unit amplitude along p̂ or ŝ in the
 * contract's convention.
 */

#ifndef BC_OUTER_H
#define BC_OUTER_H

#include "bc_complex.h"

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

#endif /* BC_OUTER_H */
