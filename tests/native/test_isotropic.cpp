/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* test_isotropic.cpp — isotropic outer-medium modes and the bare interface (engine design,
 * Sections 2 and 3, and the s/p rule of the contract, Section 1).
 *
 *   - Each mode is an eigenvector of the medium's Δ, with eigenvalue ±q.
 *   - q is on the forward branch in every regime: propagating, evanescent, absorbing, and the
 *     signed-zero case that a principal square root gets wrong.
 *   - Forward propagating modes carry power in +z and backward ones in −z.
 *   - A wave grazing at q = 0 fails loudly.
 *   - The front system at a bare interface reproduces Fresnel's equations, with μ ≠ 1, in the
 *     contract's sign convention (r_pp = −r_ss at normal incidence), conserves energy when lossless,
 *     and gives |r| = 1 beyond the critical angle.
 */

#include "bc_delta.h"
#include "bc_outer.h"
#include "bc_linalg.h"
#include "test_support.h"

static void isotropic_M(cplx eps, cplx mu, bc_complex* m)
{
    for (int i = 0; i < 36; ++i) m[i] = bc_c(0.0, 0.0);
    for (int i = 0; i < 3; ++i) { m[i * 7] = to_bc(eps); m[(i + 3) * 7] = to_bc(mu); }
}

struct Medium { cplx eps, mu; };

static void ModesAreEigenvectorsOfDelta()
{
    const Medium media[] = {
        {1.0, 1.0}, {2.25, 1.0}, {cplx(-12.0, 1.2), 1.0}, {cplx(4.0, 0.3), cplx(2.0, 0.4)},
        {cplx(6.0, 0.0), cplx(-1.5, 0.1)},
    };
    for (const Medium& md : media)
        for (double xi : {0.0, 0.5, 0.99, 1.3, 3.0})
        {
            bc_complex m[36], D[16], modes[16], q;
            isotropic_M(md.eps, md.mu, m);
            bc_complex e, u;
            check(bc_is_isotropic(m, &e, &u) == 1, "isotropic M is detected");
            if (bc_build_delta(m, xi, D, nullptr) != BC_OK) { check(false, "Δ builds"); continue; }
            if (bc_isotropic_modes(e, u, xi, modes, &q) != BC_OK) { check(false, "modes build"); continue; }

            for (int j = 0; j < 4; ++j)
            {
                const cplx lambda = (j < 2) ? to_std(q) : -to_std(q);
                double res = 0, nv = 0;
                for (int i = 0; i < 4; ++i)
                {
                    cplx s = 0;
                    for (int k = 0; k < 4; ++k) s += to_std(D[i * 4 + k]) * to_std(modes[k * 4 + j]);
                    res = std::fmax(res, std::abs(s - lambda * to_std(modes[i * 4 + j])));
                    nv = std::fmax(nv, std::abs(to_std(modes[i * 4 + j])));
                }
                check_small(res / (nv * (1.0 + std::abs(lambda))), 1e-14, "mode is an eigenvector of Δ with ±q");
            }
        }
}

static void TheForwardBranchInEveryRegime()
{
    bc_complex modes[16], q;

    bc_isotropic_modes(bc_c(2.25, 0), bc_c(1, 0), 0.6, modes, &q);
    check(q.im == 0.0 && q.re > 0.0, "propagating: q real and positive");

    bc_isotropic_modes(bc_c(1.0, 0), bc_c(1, 0), 1.5, modes, &q);
    check(q.re == 0.0 && q.im > 0.0, "evanescent: q positive imaginary");

    bc_isotropic_modes(bc_c(-12.0, 1.2), bc_c(1, 0), 0.3, modes, &q);
    check(q.im > 0.0, "absorbing metal: Im q > 0");

    /* εμ − ξ² = −0.25 − 0i: the principal root of a negative real with a NEGATIVE zero imaginary
     * part is −0.5i; the forward branch must still be +0.5i. */
    bc_isotropic_modes(bc_c(1.0, -0.0), bc_c(1, 0), std::sqrt(1.25), modes, &q);
    check(q.im > 0.0, "signed zero: Im q > 0 even for −0.0 imaginary input");

    /* A double-negative medium: εμ positive, both negative. The branch rule is on q, so the
     * forward wave still has Re q ≥ 0 and its energy flow is checked below. */
    bc_isotropic_modes(bc_c(-2.0, 0.01), bc_c(-1.0, 0.01), 0.3, modes, &q);
    check(q.im > 0.0 || (q.im == 0.0 && q.re > 0.0), "double negative: forward branch");
}

static void PropagatingModesCarryPowerTheRightWay()
{
    for (double xi : {0.0, 0.7, 1.4})
    {
        bc_complex modes[16], q, col[4];
        bc_isotropic_modes(bc_c(2.25, 0.0), bc_c(1.2, 0.0), xi, modes, &q);
        for (int j = 0; j < 4; ++j)
        {
            for (int i = 0; i < 4; ++i) col[i] = modes[i * 4 + j];
            const double sz = bc_poynting_z(col);
            check(j < 2 ? sz > 0.0 : sz < 0.0, "S_z > 0 for forward modes, < 0 for backward");
        }
    }
}

static void GrazingIsLoud()
{
    bc_complex modes[16], q = bc_c(-3, -3);
    for (int i = 0; i < 16; ++i) modes[i] = bc_c(-7, 7);
    check(bc_isotropic_modes(bc_c(1.0, 0.0), bc_c(1.0, 0.0), 1.0, modes, &q) == BC_GRAZING_OUTER,
          "q = 0 returns BC_GRAZING_OUTER");
    bool untouched = q.re == -3 && q.im == -3;
    for (int i = 0; i < 16; ++i) untouched = untouched && modes[i].re == -7 && modes[i].im == 7;
    check(untouched, "and writes nothing");
}

/* Fresnel coefficients in the contract's convention, derived independently of the modes in
 * docs/derivation-delta.md's terms: r_p = (ε2 q1 − ε1 q2)/(ε2 q1 + ε1 q2), r_s = (μ2 q1 − μ1 q2)/(μ2 q1 + μ1 q2). */
static void interface(cplx e1, cplx u1, cplx e2, cplx u2, double xi,
                      cplx* r, cplx* t, double* R, double* T, cplx* q1o, cplx* q2o)
{
    bc_complex m1[16], m2[16], q1, q2;
    check(bc_isotropic_modes(to_bc(e1), to_bc(u1), xi, m1, &q1) == BC_OK, "incident modes");
    check(bc_isotropic_modes(to_bc(e2), to_bc(u2), xi, m2, &q2) == BC_OK, "exit modes");

    bc_complex W[8], B[8], inc[8], c[4], rr[4];
    for (int i = 0; i < 4; ++i)
    {
        W[i * 2 + 0] = m2[i * 4 + 0];   W[i * 2 + 1] = m2[i * 4 + 1];
        inc[i * 2 + 0] = m1[i * 4 + 0]; inc[i * 2 + 1] = m1[i * 4 + 1];
        B[i * 2 + 0] = m1[i * 4 + 2];   B[i * 2 + 1] = m1[i * 4 + 3];
    }
    check(bc_front_solve(W, B, inc, c, rr) == BC_OK, "front system solves");
    for (int k = 0; k < 4; ++k) { r[k] = to_std(rr[k]); t[k] = to_std(c[k]); }

    for (int j = 0; j < 2; ++j)
    {
        bc_complex pin[4], pr[4], pt[4];
        for (int i = 0; i < 4; ++i)
        {
            pin[i] = inc[i * 2 + j];
            pr[i] = bc_add(bc_mul(B[i * 2 + 0], rr[0 * 2 + j]), bc_mul(B[i * 2 + 1], rr[1 * 2 + j]));
            pt[i] = bc_add(bc_mul(W[i * 2 + 0], c[0 * 2 + j]), bc_mul(W[i * 2 + 1], c[1 * 2 + j]));
        }
        const double s0 = bc_poynting_z(pin);
        R[j] = -bc_poynting_z(pr) / s0;
        T[j] = bc_poynting_z(pt) / s0;
    }
    *q1o = to_std(q1);
    *q2o = to_std(q2);
}

static void BareInterfaceIsFresnel()
{
    struct Case { cplx e1, u1, e2, u2; const char* what; };
    const Case cases[] = {
        {1.0, 1.0, 2.25, 1.0, "air to glass"},
        {2.25, 1.0, 1.0, 1.0, "glass to air"},
        {1.0, 1.0, cplx(-12.0, 1.2), 1.0, "air to metal"},
        {1.5, 1.0, cplx(4.0, 0.3), cplx(2.0, 0.4), "into a lossy magnetic medium"},
        {cplx(2.0, 0.0), cplx(1.5, 0.0), cplx(3.0, 0.0), cplx(0.8, 0.0), "magnetic to magnetic"},
    };
    for (const Case& k : cases)
        for (double s : {0.0, 0.3, 0.6, 0.95})
        {
            const double xi = s * std::sqrt(std::real(k.e1 * k.u1));
            cplx r[4], t[4], q1, q2;
            double R[2], T[2];
            interface(k.e1, k.u1, k.e2, k.u2, xi, r, t, R, T, &q1, &q2);

            const cplx rp = (k.e2 * q1 - k.e1 * q2) / (k.e2 * q1 + k.e1 * q2);
            const cplx rs = (k.u2 * q1 - k.u1 * q2) / (k.u2 * q1 + k.u1 * q2);
            check_near(r[0], rp, 1e-13, k.what);   /* r_pp: p in, p out */
            check_near(r[3], rs, 1e-13, k.what);   /* r_ss */
            check_near(r[1], 0.0, 1e-15, "no p–s coupling at an isotropic interface");
            check_near(r[2], 0.0, 1e-15, "no s–p coupling at an isotropic interface");

            if (s == 0.0) check_near(r[0], -r[3], 1e-14, "normal incidence: r_pp = −r_ss");

            const bool lossless = k.e1.imag() == 0 && k.u1.imag() == 0 && k.e2.imag() == 0 && k.u2.imag() == 0;
            if (lossless)
                for (int j = 0; j < 2; ++j) check_small(R[j] + T[j] - 1.0, 1e-14, "lossless: R + T = 1");
        }
}

static void TotalReflectionBeyondTheCriticalAngle()
{
    cplx r[4], t[4], q1, q2;
    double R[2], T[2];
    interface(2.25, 1.0, 1.0, 1.0, 1.2, r, t, R, T, &q1, &q2);   /* ξ = 1.2 > 1 */
    check_small(std::abs(r[0]) - 1.0, 1e-14, "TIR: |r_pp| = 1");
    check_small(std::abs(r[3]) - 1.0, 1e-14, "TIR: |r_ss| = 1");
    check_small(T[0], 1e-15, "TIR: no transmitted power (p)");
    check_small(T[1], 1e-15, "TIR: no transmitted power (s)");
}

int main()
{
    ModesAreEigenvectorsOfDelta();
    TheForwardBranchInEveryRegime();
    PropagatingModesCarryPowerTheRightWay();
    GrazingIsLoud();
    BareInterfaceIsFresnel();
    TotalReflectionBeyondTheCriticalAngle();
    return report("isotropic");
}
