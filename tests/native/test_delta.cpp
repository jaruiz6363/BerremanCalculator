/* test_delta.cpp — Berreman's Δ (docs/derivation-delta.md; engine design, Section 6, test 1).
 *
 * Five independent checks of one piece of algebra:
 *   1. Berreman's general formula (1972, Eqs. 20, 21, 24), for random general M.
 *   2. Berreman's closed forms for the optically active (Eq. 47) and Faraday (Eq. 56) media.
 *   3. The isotropic closed form (derivation, Section 5.1).
 *   4. The non-magnetic expansion (derivation, Section 5.2), for random ε.
 *   5. The Maxwell residual of every eigenpair, for random general M (derivation, Section 5.3).
 *      This one uses Maxwell's equations directly and none of the elimination.
 */

#include "bc_delta.h"
#include "test_support.h"

static void delta_of(const cplx* M, double xi, cplx* D, cplx* G = nullptr)
{
    bc_complex m[36], d[16], g[8];
    for (int i = 0; i < 36; ++i) m[i] = to_bc(M[i]);
    const bc_status s = bc_build_delta(m, xi, d, g);
    check(s == BC_OK, "bc_build_delta succeeds");
    for (int i = 0; i < 16; ++i) D[i] = to_std(d[i]);
    if (G) for (int i = 0; i < 8; ++i) G[i] = to_std(g[i]);
}

static void random_M(Rng& rng, cplx* M)
{
    for (int i = 0; i < 36; ++i) M[i] = rng.complex(-1.0, 1.0);
    for (int i = 0; i < 6; ++i) M[i * 7] += 2.5;   /* keeps d well away from zero */
}

static double max_abs(const cplx* a, int n)
{
    double m = 0;
    for (int i = 0; i < n; ++i) m = std::fmax(m, std::abs(a[i]));
    return m;
}

/* ---- 1. Berreman's general formula --------------------------------------------------------
 *
 * Eq. 21 gives E_z and H_z (his Γ3, Γ6) in terms of the other four; Eq. 24 gives his S, and the
 * matrix after Eq. 22 arranges S into Δ. X = cξ/ω is our ξ. Indices below are his, 1-based, mapped
 * to our 0-based M by m(i, j). */
static void BerremansGeneralFormula()
{
    Rng rng(1972);
    for (int trial = 0; trial < 200; ++trial)
    {
        cplx M[36];
        random_M(rng, M);
        const double X = rng.uniform(0.0, 2.0);
        auto m = [&](int i, int j) { return M[(i - 1) * 6 + (j - 1)]; };

        const cplx d = m(3, 3) * m(6, 6) - m(3, 6) * m(6, 3);
        const cplx a31 = (m(6, 1) * m(3, 6) - m(3, 1) * m(6, 6)) / d;
        const cplx a32 = ((m(6, 2) - X) * m(3, 6) - m(3, 2) * m(6, 6)) / d;
        const cplx a34 = (m(6, 4) * m(3, 6) - m(3, 4) * m(6, 6)) / d;
        const cplx a35 = (m(6, 5) * m(3, 6) - (m(3, 5) + X) * m(6, 6)) / d;
        const cplx a61 = (m(6, 3) * m(3, 1) - m(3, 3) * m(6, 1)) / d;
        const cplx a62 = (m(6, 3) * m(3, 2) - m(3, 3) * (m(6, 2) - X)) / d;
        const cplx a64 = (m(6, 3) * m(3, 4) - m(3, 3) * m(6, 4)) / d;
        const cplx a65 = (m(6, 3) * (m(3, 5) + X) - m(3, 3) * m(6, 5)) / d;

        auto S = [&](int r, int c) -> cplx {
            /* Row r of S uses row R of M with an X correction on one column (Eq. 24). */
            const int R[5] = {0, 1, 2, 4, 5};
            const int row = R[r];
            const int cols[5] = {0, 1, 2, 4, 5};
            const cplx a3[5] = {0, a31, a32, a34, a35};
            const cplx a6[5] = {0, a61, a62, a64, a65};
            cplx m3 = m(row, 3), m6 = m(row, 6);
            if (r == 2) m6 -= X;   /* S2j: (M26 − X) */
            if (r == 4) m3 += X;   /* S4j: (M53 + X) */
            return m(row, cols[c]) + m3 * a3[c] + m6 * a6[c];
        };

        /* The 4×4 after Eq. 22. */
        const cplx B[16] = {
            S(4, 1),  S(4, 4),  S(4, 2),  -S(4, 3),
            S(1, 1),  S(1, 4),  S(1, 2),  -S(1, 3),
            -S(3, 1), -S(3, 4), -S(3, 2), S(3, 3),
            S(2, 1),  S(2, 4),  S(2, 2),  -S(2, 3),
        };

        cplx D[16];
        delta_of(M, X, D);
        double err = 0;
        for (int i = 0; i < 16; ++i) err = std::fmax(err, std::abs(D[i] - B[i]));
        check_small(err / max_abs(B, 16), 1e-13, "Δ against Berreman Eqs. 21 and 24, random M");
    }
}

/* ---- 2. Berreman's closed forms ------------------------------------------------------------ */
static void BerremansOpticallyActiveAndFaradayExamples()
{
    const cplx eps(2.25, 0.01);
    const double X = 0.6;

    /* Eq. 12 (Drude): ρ = −ig I, ε = εI, ρ' = 0, μ = I. Eq. 47. */
    {
        const cplx g(0.03, 0.0), I(0, 1);
        cplx M[36] = {};
        for (int i = 0; i < 3; ++i) { M[i * 7] = eps; M[(i + 3) * 7] = 1.0; M[i * 6 + i + 3] = -I * g; }
        cplx D[16];
        delta_of(M, X, D);
        const cplx E[16] = {
            0.0, 1.0 - X * X / eps, I * g * X * X / eps, 0.0,
            eps, 0.0,               0.0,                 I * g,
            0.0, 0.0,               0.0,                 1.0,
            0.0, -I * g,            eps - X * X,         0.0,
        };
        for (int i = 0; i < 16; ++i) check_near(D[i], E[i], 1e-14, "Δ against Berreman Eq. 47 (optically active)");
    }

    /* Eq. 13 (Born): ε = [[ε, −iγ, 0], [iγ, ε, 0], [0, 0, ε]], μ = I. Eq. 56. */
    {
        const cplx gam(0.05, 0.0), I(0, 1);
        cplx M[36] = {};
        for (int i = 0; i < 3; ++i) { M[i * 7] = eps; M[(i + 3) * 7] = 1.0; }
        M[0 * 6 + 1] = -I * gam;
        M[1 * 6 + 0] = I * gam;
        cplx D[16];
        delta_of(M, X, D);
        const cplx E[16] = {
            0.0,     1.0 - X * X / eps, 0.0,         0.0,
            eps,     0.0,               -I * gam,    0.0,
            0.0,     0.0,               0.0,         1.0,
            I * gam, 0.0,               eps - X * X, 0.0,
        };
        for (int i = 0; i < 16; ++i) check_near(D[i], E[i], 1e-14, "Δ against Berreman Eq. 56 (Faraday)");
    }
}

/* ---- 3. Isotropic closed form, with μ --------------------------------------------------------- */
static void IsotropicClosedForm()
{
    const cplx eps(-12.0, 1.2), mu(1.3, 0.05);   /* a metal-like ε and a lossy μ */
    for (double X : {0.0, 0.4, 1.7})
    {
        cplx M[36] = {};
        for (int i = 0; i < 3; ++i) { M[i * 7] = eps; M[(i + 3) * 7] = mu; }
        cplx D[16];
        delta_of(M, X, D);
        const cplx E[16] = {
            0.0, mu - X * X / eps, 0.0,              0.0,
            eps, 0.0,              0.0,              0.0,
            0.0, 0.0,              0.0,              mu,
            0.0, 0.0,              eps - X * X / mu, 0.0,
        };
        for (int i = 0; i < 16; ++i) check_near(D[i], E[i], 1e-14, "Δ isotropic closed form");
    }
}

/* ---- 4. Non-magnetic expansion ------------------------------------------------------------ */
static void NonMagneticExpansion()
{
    Rng rng(552);
    for (int trial = 0; trial < 200; ++trial)
    {
        cplx e[9];
        for (int i = 0; i < 9; ++i) e[i] = rng.complex(-1.0, 1.0);
        for (int i = 0; i < 3; ++i) e[i * 4] += 2.0;
        const double X = rng.uniform(0.0, 2.0);
        auto E = [&](int i, int j) { return e[(i - 1) * 3 + (j - 1)]; };

        cplx M[36] = {};
        for (int i = 0; i < 3; ++i)
        {
            for (int j = 0; j < 3; ++j) M[i * 6 + j] = e[i * 3 + j];
            M[(i + 3) * 7] = 1.0;
        }
        cplx D[16];
        delta_of(M, X, D);

        const cplx e33 = E(3, 3);
        const cplx F[16] = {
            -X * E(3, 1) / e33,                  1.0 - X * X / e33,  -X * E(3, 2) / e33,                      0.0,
            E(1, 1) - E(1, 3) * E(3, 1) / e33,   -X * E(1, 3) / e33, E(1, 2) - E(1, 3) * E(3, 2) / e33,       0.0,
            0.0,                                 0.0,                0.0,                                     1.0,
            E(2, 1) - E(2, 3) * E(3, 1) / e33,   -X * E(2, 3) / e33, E(2, 2) - E(2, 3) * E(3, 2) / e33 - X*X, 0.0,
        };
        double err = 0;
        for (int i = 0; i < 16; ++i) err = std::fmax(err, std::abs(D[i] - F[i]));
        check_small(err / max_abs(F, 16), 1e-14, "Δ against the non-magnetic expansion, random ε");
    }
}

/* ---- 5. Maxwell residual -------------------------------------------------------------------- */
static void EveryEigenpairSatisfiesMaxwell()
{
    Rng rng(4265);
    double worst = 0;
    for (int trial = 0; trial < 300; ++trial)
    {
        cplx M[36];
        random_M(rng, M);
        const double X = rng.uniform(0.0, 2.0);

        cplx D[16], G[8];
        delta_of(M, X, D, G);

        cplx c[4], q[4];
        charpoly4(D, c);
        roots4(c, q);

        for (int k = 0; k < 4; ++k)
        {
            cplx psi[4];
            eigenvector4(D, q[k], psi);

            bc_complex g[8], p[4], v6[6];
            for (int i = 0; i < 8; ++i) g[i] = to_bc(G[i]);
            for (int i = 0; i < 4; ++i) p[i] = to_bc(psi[i]);
            bc_full_field(g, p, v6);

            cplx v[6], Mv[6];
            for (int i = 0; i < 6; ++i) v[i] = to_std(v6[i]);
            for (int i = 0; i < 6; ++i)
            {
                Mv[i] = 0;
                for (int j = 0; j < 6; ++j) Mv[i] += M[i * 6 + j] * v[j];
            }

            /* k = (ξ, 0, q): k × E = B̃ and k × H̃ = −D̃. */
            const cplx Ex = v[0], Ey = v[1], Ez = v[2], Hx = v[3], Hy = v[4], Hz = v[5];
            const cplx Q = q[k];
            const cplx res[6] = {
                (-Q * Ey) - Mv[3],
                (Q * Ex - X * Ez) - Mv[4],
                (X * Ey) - Mv[5],
                (-Q * Hy) + Mv[0],
                (Q * Hx - X * Hz) + Mv[1],
                (X * Hy) + Mv[2],
            };
            double scale = 0, vn = 0;
            for (int i = 0; i < 6; ++i) vn = std::fmax(vn, std::abs(v[i]));
            scale = (max_abs(M, 36) + std::abs(Q) + X) * vn;
            const double r = max_abs(res, 6) / scale;
            worst = std::fmax(worst, r);
        }
    }
    check_small(worst, 1e-11, "worst Maxwell residual over 1200 eigenpairs of random M");
    std::printf("  worst relative Maxwell residual: %.2e\n", worst);
}

static void SingularDeltaIsLoud()
{
    bc_complex m[36] = {}, d[16], g[8];
    for (int i = 0; i < 6; ++i) m[i * 7].re = 1.0;
    m[2 * 7].re = 0.0;   /* ε_zz = 0 with μ = I: d = 0 */
    for (int i = 0; i < 16; ++i) d[i] = bc_c(-7.0, 7.0);
    check(bc_build_delta(m, 0.3, d, g) == BC_DELTA_SINGULAR, "d = 0 returns BC_DELTA_SINGULAR");
    bool untouched = true;
    for (int i = 0; i < 16; ++i) untouched = untouched && d[i].re == -7.0 && d[i].im == 7.0;
    check(untouched, "and leaves Δ untouched");
}

int main()
{
    BerremansGeneralFormula();
    BerremansOpticallyActiveAndFaradayExamples();
    IsotropicClosedForm();
    NonMagneticExpansion();
    EveryEigenpairSatisfiesMaxwell();
    SingularDeltaIsLoud();
    return report("delta");
}
