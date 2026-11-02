/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* test_outer.cpp — anisotropic outer media (engine design, Section 2).
 *
 *   - The Schur decomposition: A = Q T Qᴴ, Q unitary, T upper triangular, eigenvalues equal to the
 *     tests' own characteristic-polynomial roots; and the diagonal swap.
 *   - The general mode path, applied to ISOTROPIC media where the closed form is known: every
 *     mode is an eigenvector with ±q, and the coincident pairs are resolved into exactly the p
 *     and s waves.
 *   - Uniaxial incident and exit media with the axis along z, against the block reference.
 *   - An incident medium whose optic axis is along z at normal incidence (coincident roots), and
 *     tilts of 10⁻¹ to 10⁻¹² rad: equal to the isotropic n_o result, continuous, labels kept.
 *   - Rotated biaxial outer media with random lossless layers: R + T = 1 for each incident wave.
 *   - Evanescent incident waves, and an absorbing incident medium: loud.
 */

#include "test_stack.h"
#include "bc_outer.h"

/* ---- Schur ---------------------------------------------------------------------------------- */

static void SchurDecomposition()
{
    Rng rng(4);
    double worst_res = 0, worst_unit = 0, worst_eig = 0;
    for (int trial = 0; trial < 500; ++trial)
    {
        cplx A[16];
        for (int i = 0; i < 16; ++i) A[i] = rng.complex(-1, 1);
        if (trial % 5 == 0) for (int i = 0; i < 4; ++i) A[i * 5] = 0.5;   /* repeated diagonal */

        bc_complex T[16], Q[16];
        for (int i = 0; i < 16; ++i) T[i] = to_bc(A[i]);
        check(bc_schur4(T, Q) == BC_OK, "Schur converges");

        for (int i = 1; i < 4; ++i)
            for (int j = 0; j < i; ++j) check(bc_is_zero(T[i * 4 + j]), "T is upper triangular");

        /* Q T Qᴴ − A and Qᴴ Q − I */
        double res = 0, unit = 0, na = 0;
        for (int i = 0; i < 4; ++i)
            for (int j = 0; j < 4; ++j)
            {
                cplx s = 0, u = 0;
                for (int k = 0; k < 4; ++k)
                {
                    for (int l = 0; l < 4; ++l) s += to_std(Q[i * 4 + k]) * to_std(T[k * 4 + l]) * std::conj(to_std(Q[j * 4 + l]));
                    u += std::conj(to_std(Q[k * 4 + i])) * to_std(Q[k * 4 + j]);
                }
                res = std::fmax(res, std::abs(s - A[i * 4 + j]));
                unit = std::fmax(unit, std::abs(u - ((i == j) ? 1.0 : 0.0)));
                na = std::fmax(na, std::abs(A[i * 4 + j]));
            }
        worst_res = std::fmax(worst_res, res / na);
        worst_unit = std::fmax(worst_unit, unit);

        /* Every diagonal entry is a root of the characteristic polynomial. */
        cplx c[4];
        charpoly4(A, c);
        for (int k = 0; k < 4; ++k)
        {
            const cplx lam = to_std(T[k * 5]);
            worst_eig = std::fmax(worst_eig, std::abs(poly4(c, lam)) / (1.0 + std::pow(std::abs(lam), 4)));
        }

        /* Swapping keeps the decomposition and exchanges the diagonal. */
        const int k = trial % 3;
        const bc_complex a = T[k * 5], b = T[(k + 1) * 5];
        bc_schur_swap(T, Q, k);
        check(T[k * 5].re == b.re && T[k * 5].im == b.im && T[(k + 1) * 5].re == a.re, "swap exchanges the diagonal");
        double res2 = 0;
        for (int i = 0; i < 4; ++i)
            for (int j = 0; j < 4; ++j)
            {
                cplx s = 0;
                for (int kk = 0; kk < 4; ++kk)
                    for (int l = 0; l < 4; ++l) s += to_std(Q[i * 4 + kk]) * to_std(T[kk * 4 + l]) * std::conj(to_std(Q[j * 4 + l]));
                res2 = std::fmax(res2, std::abs(s - A[i * 4 + j]));
            }
        check_small(res2 / na, 1e-13, "after a swap A = Q T Qᴴ");
    }
    check_small(worst_res, 1e-14, "A = Q T Qᴴ");
    check_small(worst_unit, 1e-14, "Q unitary");
    check_small(worst_eig, 1e-12, "diagonal of T are the eigenvalues");
    std::printf("  Schur: worst |A − Q T Qᴴ|/|A| %.1e, |QᴴQ − I| %.1e\n", worst_res, worst_unit);
}

/* ---- the general path on isotropic media ----------------------------------------------------- */

static double parallel(const bc_complex* modes, int col, const bc_complex* ref, int rcol)
{
    /* |⟨u, v⟩| / (|u| |v|): 1 when the columns are proportional. */
    cplx dot = 0;
    double nu = 0, nv = 0;
    for (int i = 0; i < 4; ++i)
    {
        const cplx u = to_std(modes[i * 4 + col]), v = to_std(ref[i * 4 + rcol]);
        dot += std::conj(u) * v;
        nu += std::norm(u);
        nv += std::norm(v);
    }
    return std::abs(dot) / std::sqrt(nu * nv);
}

static void GeneralPathReproducesIsotropicModes()
{
    struct Md { cplx e, u; } media[] = {{2.25, 1.0}, {1.0, 1.0}, {cplx(4.0, 0.5), cplx(1.5, 0.1)}, {cplx(-12, 1.2), 1.0}};
    for (const Md& md : media)
        for (double xi : {0.0, 0.4, 0.9, 1.6})
        {
            const Mat6 M = iso(md.e, md.u);
            bc_complex m[36], general[16], closed[16], q;
            for (int i = 0; i < 36; ++i) m[i] = to_bc(M.m[i]);
            const bc_status s1 = bc_general_modes(m, xi, general);
            const bc_status s2 = bc_isotropic_modes(to_bc(md.e), to_bc(md.u), xi, closed, &q);
            if (s2 != BC_OK) { check(s1 != BC_OK, "general path fails where the closed form does"); continue; }
            check(s1 == BC_OK, "general path succeeds on an isotropic medium");
            /* Coincident pairs: the projection basis must be exactly the p and s waves. */
            for (int c = 0; c < 4; ++c)
                check_small(1.0 - parallel(general, c, closed, c), 1e-12,
                            "general mode is parallel to the closed-form p or s mode");
        }
}

/* ---- aligned uniaxial outer media against the reference --------------------------------------- */

static Mat6 diag_eps(cplx ex, cplx ey, cplx ez)
{
    Mat6 M = iso(1.0);
    M.m[0] = ex; M.m[7] = ey; M.m[14] = ez;
    return M;
}

static void AlignedAnisotropicOuterMediaMatchTheReference()
{
    const Mat6 in = diag_eps(2.25, 2.25, 3.1);                          /* lossless uniaxial, axis z */
    const Mat6 ex = diag_eps(cplx(4.0, 0.2), cplx(5.0, 0.1), cplx(2.0, 0.3));   /* absorbing biaxial */
    const std::vector<Mat6> L = {iso(1.9), diag_eps(2.0, 2.6, 2.3), iso(cplx(-10, 2))};
    const std::vector<double> d = {120.0, 300.0, 15.0};
    double worst = 0;
    for (double xi : {0.0, 0.3, 0.8, 1.2, 1.45})
    {
        const Result r = evaluate(in, L, d, ex, xi);
        check(r.status == BC_OK, "aligned anisotropic outer media evaluate");
        const RefPower f = reference_power(in, L, d, ex, xi);
        for (int pol = 0; pol < 2; ++pol)
        {
            worst = std::fmax(worst, std::fabs(r.P[pol * 3 + 0] - f.R[pol]));
            worst = std::fmax(worst, std::fabs(r.P[pol * 3 + 1] - f.T[pol]));
        }
        check_small(std::abs(r.J[1]) + std::abs(r.J[2]) + std::abs(r.J[5]) + std::abs(r.J[6]), 1e-13,
                    "aligned media do not couple p and s");
    }
    check_small(worst, 1e-12, "R and T against the block reference");
    std::printf("  aligned anisotropic outer media: worst R/T error %.1e\n", worst);
}

/* ---- coincident roots in the incident medium ---------------------------------------------------- */

static Mat6 rotated_uniaxial(double no, double ne, double theta, double phi)
{
    /* ε = Rz(φ) Rx(θ) diag(no², no², ne²) Rx(θ)ᵀ Rz(φ)ᵀ */
    const double o = no * no, e = ne * ne;
    double R[9], Rx[9] = {1, 0, 0, 0, std::cos(theta), -std::sin(theta), 0, std::sin(theta), std::cos(theta)};
    const double Rz[9] = {std::cos(phi), -std::sin(phi), 0, std::sin(phi), std::cos(phi), 0, 0, 0, 1};
    for (int i = 0; i < 3; ++i)
        for (int j = 0; j < 3; ++j)
        {
            R[i * 3 + j] = 0;
            for (int k = 0; k < 3; ++k) R[i * 3 + j] += Rz[i * 3 + k] * Rx[k * 3 + j];
        }
    const double D[3] = {o, o, e};
    Mat6 M = iso(1.0);
    for (int i = 0; i < 3; ++i)
        for (int j = 0; j < 3; ++j)
        {
            double s = 0;
            for (int k = 0; k < 3; ++k) s += R[i * 3 + k] * D[k] * R[j * 3 + k];
            M.m[i * 6 + j] = s;
        }
    /* A rotated symmetric tensor is symmetric only to rounding; the engine's losslessness test is
     * exact, so the lower triangle is mirrored from the upper (contract, Section 2). */
    for (int i = 0; i < 3; ++i) for (int j = 0; j < i; ++j) M.m[i * 6 + j] = M.m[j * 6 + i];
    return M;
}

static void CoincidentIncidentRootsAreContinuous()
{
    const double no = 1.5, ne = 1.7;
    const std::vector<Mat6> L = {iso(4.0), iso(2.0)};
    const std::vector<double> d = {80.0, 150.0};

    const Result r0 = evaluate(rotated_uniaxial(no, ne, 0.0, 0.0), L, d, iso(2.25), 0.0);
    const Result ri = evaluate(iso(no * no), L, d, iso(2.25), 0.0);
    check(r0.status == BC_OK, "axis along z at normal incidence evaluates");
    for (int k = 0; k < 6; ++k) check_small(r0.P[k] - ri.P[k], 1e-13, "equals an isotropic n_o incident medium");
    check_small(std::abs(r0.J[0]) - std::abs(ri.J[0]), 1e-13, "|r_pp| as isotropic");
    check_small(std::abs(r0.J[3]) - std::abs(ri.J[3]), 1e-13, "|r_ss| as isotropic");

    double prev = 1e300;
    for (int k = 1; k <= 12; ++k)
    {
        const double th = std::pow(10.0, -k);
        const Result r = evaluate(rotated_uniaxial(no, ne, th, 0.4), L, d, iso(2.25), 0.0);
        check(r.status == BC_OK, "tilted incident axis evaluates");
        double diff = 0;
        for (int i = 0; i < 6; ++i) diff = std::fmax(diff, std::fabs(r.P[i] - r0.P[i]));
        diff = std::fmax(diff, std::fabs(std::abs(r.J[0]) - std::abs(r0.J[0])));
        diff = std::fmax(diff, std::fabs(std::abs(r.J[3]) - std::abs(r0.J[3])));
        check_small(diff, 100.0 * th + 1e-12, "tilting the incident axis changes the result by O(θ), labels kept");
        check(diff <= prev + 1e-14, "and the change shrinks as the tilt does");
        prev = diff;
    }
}

/* ---- rotated biaxial outer media, random lossless layers --------------------------------------- */

static Mat6 rotated_biaxial(Rng& rng, bool lossy)
{
    const double a = rng.uniform(1.6, 2.4), b = rng.uniform(1.6, 2.4), c = rng.uniform(1.6, 2.4);
    Mat6 M = rotated_uniaxial(1.0, 1.0, 0, 0);
    /* R = Rz(φ) Rx(θ) Rz(ψ), random */
    const double ph = rng.uniform(0, 3.14), th = rng.uniform(0, 3.14), ps = rng.uniform(0, 3.14);
    auto rz = [](double t, double* m) { double r[9] = {std::cos(t), -std::sin(t), 0, std::sin(t), std::cos(t), 0, 0, 0, 1}; for (int i = 0; i < 9; ++i) m[i] = r[i]; };
    auto rx = [](double t, double* m) { double r[9] = {1, 0, 0, 0, std::cos(t), -std::sin(t), 0, std::sin(t), std::cos(t)}; for (int i = 0; i < 9; ++i) m[i] = r[i]; };
    auto mul = [](const double* A, const double* B, double* C) { for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) { C[i * 3 + j] = 0; for (int k = 0; k < 3; ++k) C[i * 3 + j] += A[i * 3 + k] * B[k * 3 + j]; } };
    double A1[9], A2[9], A3[9], T[9], R[9];
    rz(ph, A1); rx(th, A2); rz(ps, A3);
    mul(A1, A2, T); mul(T, A3, R);
    const cplx D[3] = {a, lossy ? cplx(b, 0.2) : cplx(b), c};
    for (int i = 0; i < 3; ++i)
        for (int j = 0; j < 3; ++j)
        {
            cplx s = 0;
            for (int k = 0; k < 3; ++k) s += R[i * 3 + k] * D[k] * R[j * 3 + k];
            M.m[i * 6 + j] = s;
        }
    for (int i = 0; i < 3; ++i) for (int j = 0; j < i; ++j) M.m[i * 6 + j] = M.m[j * 6 + i];   /* exactly symmetric */
    return M;
}

static void RotatedBiaxialOuterMediaConserveEnergy()
{
    Rng rng(1996);
    double worst = 0, worst_abs = 0;
    int evaluated = 0;
    for (int trial = 0; trial < 300; ++trial)
    {
        const Mat6 in = rotated_biaxial(rng, false);
        const bool lossy_exit = trial % 3 == 0;
        const Mat6 ex = rotated_biaxial(rng, lossy_exit);
        const int N = (int)rng.uniform(0, 4);
        std::vector<Mat6> L;
        std::vector<double> d;
        for (int k = 0; k < N; ++k) { L.push_back(random_hermitian(rng)); d.push_back(rng.uniform(0.05, 2.0) * LAMBDA); }
        const double xi = rng.uniform(0, 1.2);   /* below the smallest principal index, 1.26 */
        const Result r = evaluate(in, L, d, ex, xi);
        check(r.status == BC_OK, "rotated biaxial outer media evaluate");
        ++evaluated;
        for (int j = 0; j < 2; ++j)
        {
            double sum = 0;
            for (int k = 0; k < N; ++k) sum += r.absorption[j * N + k];
            if (!lossy_exit) worst = std::fmax(worst, std::fabs(r.P[j * 3 + 2]));
            worst_abs = std::fmax(worst_abs, std::fabs(sum - r.P[j * 3 + 2]));
        }
    }
    check_small(worst, 1e-11, "lossless with biaxial outer media: R + T = 1");
    check_small(worst_abs, 1e-11, "Σ layer absorption = 1 − R − T");
    std::printf("  rotated biaxial outer media (%d stacks): worst |1 − R − T| %.1e\n", evaluated, worst);
}

/* ---- loud cases ----------------------------------------------------------------------------------- */

static void EvanescentAndAbsorbingIncidentMedia()
{
    /* Isotropic incident beyond its own index: both incident waves evanescent. */
    {
        const Result r = evaluate(iso(2.25), {iso(1.0)}, {100.0}, iso(2.25), 1.6);
        check(r.status == BC_INCIDENT_EVANESCENT, "ξ beyond the incident index is BC_INCIDENT_EVANESCENT");
        check(r.diag.status == BC_INCIDENT_EVANESCENT, "and the diagnostics say so");
    }
    /* Uniaxial incident, axis z, ε = (2.25, 2.25, 4): at ξ = 1.7 the s wave is evanescent and the
     * p wave propagates. The p wave's powers are defined; the s wave's are NaN. */
    {
        const Result r = evaluate(diag_eps(2.25, 2.25, 4.0), {iso(1.0)}, {100.0}, iso(4.0), 1.7);
        check(r.status == BC_OK, "one evanescent incident wave still evaluates");
        check_small(r.P[2], 1e-13, "the propagating incident wave: R + T = 1");
        check(std::isnan(r.P[3]) && std::isnan(r.P[4]) && std::isnan(r.P[5]), "the evanescent one: R, T, A are NaN");
        const RefPower f = reference_power(diag_eps(2.25, 2.25, 4.0), {iso(1.0)}, {100.0}, iso(4.0), 1.7);
        check_small(r.P[0] - f.R[0], 1e-12, "and its R matches the reference");
    }
    /* An absorbing incident medium. */
    {
        Mat6 in = diag_eps(2.25, 2.25, 2.25);
        in.m[1] = cplx(0.0, 0.01);   /* ε_xy ≠ conj(ε_yx): not Hermitian */
        const Result r = evaluate(in, {}, {}, iso(1.0), 0.2);
        check(r.status == BC_INCIDENT_ABSORBING, "a non-Hermitian incident medium is BC_INCIDENT_ABSORBING");
    }
}

int main()
{
    SchurDecomposition();
    GeneralPathReproducesIsotropicModes();
    AlignedAnisotropicOuterMediaMatchTheReference();
    CoincidentIncidentRootsAreContinuous();
    RotatedBiaxialOuterMediaConserveEnergy();
    EvanescentAndAbsorbingIncidentMedia();
    return report("outer");
}
