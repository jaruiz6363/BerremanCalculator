/* test_sweep.cpp — the sweep through the stack (engine design, Section 6, tests 2–7).
 *
 * THE REFERENCE. For any stack whose Δ splits into a p block (ψ1, ψ2) and an s block (ψ3, ψ4), as
 * for isotropic layers and for layers whose axes are aligned with the stack, each block's
 * propagator has a closed form. With Δ_block = [[0, a], [b, 0]] and κ² = ab:
 *
 *     exp(i k0 h Δ_block) = [[cos θ, i a S], [i b S, cos θ]],   θ = k0 h κ,  S = sin θ / κ
 *
 * (Berreman 1972, Eq. 41, written without a branch: cos θ and S are even in κ, and S → k0 h at
 * κ = 0, where Δ is defective). The reference multiplies these 2×2 matrices; it shares no code
 * with the engine's exponential, sweep or front solve.
 *
 *   2. Isotropic stacks against the reference: random stacks, metals, μ ≠ 1, thick layers.
 *   3. An exactly degenerate layer (optic axis along z), and tilts of 10⁻¹ to 10⁻¹² rad.
 *   4. A layer exactly at its critical angle (q = 0, Δ defective).
 *   5. Thick metal: bulk reflectance, and the opaque flag.
 *   6. Frustrated total reflection.
 *   7. The step bound: error and time at β = 1 and 2 (m = 9) and 5.37 (m = 13).
 *   Also: energy conservation for random lossless (Hermitian) general M, and the per-layer
 *   absorption against 1 − R − T for random lossy general M.
 */

#include "bc_internal.h"
#include "bc_sweep.h"
#include "test_support.h"

#include <chrono>
#include <vector>

static const double PI = 3.14159265358979323846;
static const double LAMBDA = 500.0;   /* nm; only ratios matter */

/* ---- building problems ------------------------------------------------------------------- */

struct Mat6 { cplx m[36] = {}; };

static Mat6 iso(cplx eps, cplx mu = 1.0)
{
    Mat6 M;
    for (int i = 0; i < 3; ++i) { M.m[i * 7] = eps; M.m[(i + 3) * 7] = mu; }
    return M;
}

static Mat6 dielectric(const cplx* e3x3)
{
    Mat6 M;
    for (int i = 0; i < 3; ++i)
    {
        for (int j = 0; j < 3; ++j) M.m[i * 6 + j] = e3x3[i * 3 + j];
        M.m[(i + 3) * 7] = 1.0;
    }
    return M;
}

struct Result
{
    cplx J[8];          /* r_pp r_ps r_sp r_ss t_pp t_ps t_sp t_ss */
    double P[6];        /* per incident wave: R, T, A */
    std::vector<double> absorption;   /* [incident][layer] */
    bc_point_diagnostics diag;
    bc_status status;
};

static Result evaluate(const Mat6& in, const std::vector<Mat6>& layers, const std::vector<double>& d,
                       const Mat6& ex, double xi, bc_config cfg = bc_default_config)
{
    const int N = (int)layers.size();
    std::vector<bc_complex> M((N + 2) * 36);
    auto put = [&](int k, const Mat6& m) { for (int i = 0; i < 36; ++i) M[k * 36 + i] = to_bc(m.m[i]); };
    put(0, in);
    for (int k = 0; k < N; ++k) put(k + 1, layers[k]);
    put(N + 1, ex);

    double wl = LAMBDA;
    int32_t sp = 0;
    bc_problem p{};
    p.n_layers = N; p.n_spectral = 1; p.n_points = 1; p.n_depths = 0;
    p.M = M.data(); p.wavelength = &wl; p.thickness = d.empty() ? nullptr : d.data();
    p.point_spectral = &sp; p.point_xi = &xi; p.field_depths = nullptr;

    int64_t bytes = 0;
    Result res{};
    res.status = bc_plan(&p, &bytes);
    if (res.status != BC_OK) return res;
    std::vector<unsigned char> scratch((size_t)bytes);
    bc_complex J[8];
    res.absorption.assign(2 * (size_t)std::max(N, 1), 0.0);
    bc_output o{J, res.P, N > 0 ? res.absorption.data() : nullptr, nullptr, &res.diag};
    res.status = bc_evaluate_with(&p, 0, 1, scratch.data(), bytes, &o, &cfg);
    for (int i = 0; i < 8; ++i) res.J[i] = to_std(J[i]);
    return res;
}

/* ---- the block reference ----------------------------------------------------------------- */

struct Block { cplx a, b; };   /* Δ_block = [[0, a], [b, 0]] */

static void block_inverse_propagator(Block k, double h, cplx* Pinv)
{
    const cplx kappa = std::sqrt(k.a * k.b);
    const cplx th = 2.0 * PI / LAMBDA * h * kappa;
    const cplx C = std::cos(th);
    const cplx S = (kappa == cplx(0)) ? cplx(2.0 * PI / LAMBDA * h) : std::sin(th) / kappa;
    /* det = 1, so the inverse of [[C, iaS], [ibS, C]] is [[C, −iaS], [−ibS, C]]. */
    const cplx I(0, 1);
    Pinv[0] = C; Pinv[1] = -I * k.a * S; Pinv[2] = -I * k.b * S; Pinv[3] = C;
}

/* r and t for one block, with the incident and exit media's block modes m± = (u, ±... ) given as
 * forward (f) and backward (bk) 2-vectors. ψ_front = f_in + r bk_in and ψ_exit = t f_ex, with
 * ψ_exit = P_N ⋯ P_1 ψ_front. */
static void block_rt(const std::vector<Block>& blocks, const std::vector<double>& d,
                     const cplx* f_in, const cplx* bk_in, const cplx* f_ex, cplx* r, cplx* t)
{
    cplx X[2] = {f_ex[0], f_ex[1]};
    for (int k = (int)blocks.size() - 1; k >= 0; --k)
    {
        cplx Pi[4];
        block_inverse_propagator(blocks[k], d[k], Pi);
        const cplx x0 = Pi[0] * X[0] + Pi[1] * X[1];
        const cplx x1 = Pi[2] * X[0] + Pi[3] * X[1];
        X[0] = x0; X[1] = x1;
    }
    /* [X, −bk_in] [t; r] = f_in */
    const cplx a00 = X[0], a01 = -bk_in[0], a10 = X[1], a11 = -bk_in[1];
    const cplx det = a00 * a11 - a01 * a10;
    *t = (f_in[0] * a11 - a01 * f_in[1]) / det;
    *r = (a00 * f_in[1] - a10 * f_in[0]) / det;
}

static cplx fwd_q(cplx eps, cplx mu, double xi)
{
    cplx q = std::sqrt(eps * mu - xi * xi);
    if (q.imag() < 0 || (q.imag() == 0 && q.real() < 0)) q = -q;
    return q;
}

/* The reference for a stack of diagonal-M layers (isotropic or aligned) between isotropic media.
 * Returns r_pp, r_ss, t_pp, t_ss. */
struct Ref { cplx rpp, rss, tpp, tss; };

static Ref reference(cplx ei, cplx ui, const std::vector<Mat6>& layers, const std::vector<double>& d,
                     cplx ee, cplx ue, double xi)
{
    std::vector<Block> pb, sb;
    for (const Mat6& L : layers)
    {
        const cplx exx = L.m[0], eyy = L.m[7], ezz = L.m[14];
        const cplx uxx = L.m[21], uyy = L.m[28], uzz = L.m[35];
        pb.push_back({uyy - xi * xi / ezz, exx});
        sb.push_back({uxx, eyy - xi * xi / uzz});
    }
    const cplx Ni = std::sqrt(ei * ui), Ne = std::sqrt(ee * ue);
    const cplx qi = fwd_q(ei, ui, xi), qe = fwd_q(ee, ue, xi);

    Ref R;
    {
        const cplx f_in[2] = {qi / Ni, Ni / ui}, bk_in[2] = {-qi / Ni, Ni / ui}, f_ex[2] = {qe / Ne, Ne / ue};
        block_rt(pb, d, f_in, bk_in, f_ex, &R.rpp, &R.tpp);
    }
    {
        const cplx f_in[2] = {1.0, qi / ui}, bk_in[2] = {1.0, -qi / ui}, f_ex[2] = {1.0, qe / ue};
        block_rt(sb, d, f_in, bk_in, f_ex, &R.rss, &R.tss);
    }
    return R;
}

static double rel(cplx a, cplx b) { return std::abs(a - b) / std::fmax(1.0, std::abs(b)); }

static double compare(const Result& r, const Ref& f)
{
    double e = 0;
    e = std::fmax(e, rel(r.J[0], f.rpp));
    e = std::fmax(e, rel(r.J[3], f.rss));
    e = std::fmax(e, rel(r.J[4], f.tpp));
    e = std::fmax(e, rel(r.J[7], f.tss));
    e = std::fmax(e, std::abs(r.J[1]) + std::abs(r.J[2]) + std::abs(r.J[5]) + std::abs(r.J[6]));
    return e;
}

/* ---- 2. Isotropic stacks -------------------------------------------------------------------- */

static cplx random_eps(Rng& rng)
{
    switch ((int)rng.uniform(0, 4))
    {
    case 0: return cplx(std::pow(rng.uniform(1.3, 2.5), 2), 0.0);         /* dielectric */
    case 1: return cplx(rng.uniform(-30, -2), rng.uniform(0.1, 5.0));      /* metal */
    case 2: return cplx(rng.uniform(1.0, 9.0), rng.uniform(0.0, 2.0));     /* absorbing dielectric */
    default: return cplx(1.0, 0.0);                                        /* air gap */
    }
}

static void IsotropicStacksAgreeWithTheReference()
{
    Rng rng(742);
    double worst = 0;
    int lossless = 0;
    for (int trial = 0; trial < 400; ++trial)
    {
        const cplx ei = (trial % 2) ? 2.25 : 1.0;
        const cplx ee = random_eps(rng).real() > 0 ? cplx(std::pow(rng.uniform(1.0, 2.0), 2), 0) : cplx(-8, 1);
        const int N = 1 + (int)rng.uniform(0, 6);
        std::vector<Mat6> L;
        std::vector<double> d;
        bool all_lossless = ee.imag() == 0;
        for (int k = 0; k < N; ++k)
        {
            const cplx e = random_eps(rng);
            const cplx u = (rng.uniform(0, 1) < 0.25) ? cplx(rng.uniform(0.8, 2.0), rng.uniform(0, 0.3)) : cplx(1.0);
            all_lossless = all_lossless && e.imag() == 0 && u.imag() == 0;
            L.push_back(iso(e, u));
            d.push_back(rng.uniform(0, 1) < 0.1 ? 0.0 : rng.uniform(0.0, 3.0) * LAMBDA);
        }
        const double xi = rng.uniform(0.0, 0.99) * std::sqrt(ei.real());
        const Result r = evaluate(iso(ei), L, d, iso(ee), xi);
        check(r.status == BC_OK, "isotropic stack evaluates");
        const Ref f = reference(ei, 1.0, L, d, ee, 1.0, xi);
        worst = std::fmax(worst, compare(r, f));

        /* R from the Poynting flux equals |r|² in a lossless isotropic incident medium. */
        check_small(r.P[0] - std::norm(r.J[0]), 1e-12, "R_p = |r_pp|²");
        check_small(r.P[3] - std::norm(r.J[3]), 1e-12, "R_s = |r_ss|²");

        /* Per-layer absorption sums to 1 − R − T. */
        for (int j = 0; j < 2; ++j)
        {
            double sum = 0;
            for (int k = 0; k < N; ++k) sum += r.absorption[j * N + k];
            check_small(sum - r.P[j * 3 + 2], 1e-11, "Σ layer absorption = 1 − R − T");
            if (all_lossless) { ++lossless; check_small(r.P[j * 3 + 2], 1e-12, "lossless: R + T = 1"); }
        }
    }
    check_small(worst, 1e-10, "worst error, 400 random isotropic stacks");
    std::printf("  random isotropic stacks: worst error %.2e (%d lossless checks)\n", worst, lossless);

    /* One very thick lossless layer: 1000 waves. */
    {
        std::vector<Mat6> L = {iso(2.25)};
        std::vector<double> d = {1000.0 * LAMBDA / 1.5};
        const Result r = evaluate(iso(1.0), L, d, iso(1.69), 0.4);
        const Ref f = reference(1.0, 1.0, L, d, 1.69, 1.0, 0.4);
        const double e = compare(r, f);
        check_small(e, 1e-9, "1000-wave layer");
        std::printf("  1000-wave layer: error %.2e, %d steps\n", e, r.diag.max_steps);
    }
}

/* ---- 3. Degenerate optic axis ------------------------------------------------------------- */

static void rotated_uniaxial_x(double no, double ne, double theta, cplx* e)
{
    /* ε = Rx(θ) diag(no², no², ne²) Rx(θ)ᵀ: the optic axis tilted from z toward −y. */
    const double c = std::cos(theta), s = std::sin(theta);
    const double o = no * no, x = ne * ne;
    e[0] = o; e[1] = 0;                 e[2] = 0;
    e[3] = 0; e[4] = o * c * c + x * s * s; e[5] = (o - x) * c * s;
    e[6] = 0; e[7] = (o - x) * c * s;   e[8] = o * s * s + x * c * c;
}

static void DegenerateOpticAxisIsContinuous()
{
    const double no = 1.55, ne = 1.75, d = 1300.0;
    cplx e[9];
    rotated_uniaxial_x(no, ne, 0.0, e);
    const Result r0 = evaluate(iso(1.0), {dielectric(e)}, {d}, iso(2.25), 0.0);
    check(r0.status == BC_OK, "axis along z evaluates");

    /* At θ = 0 the layer is exactly degenerate: both waves see n_o. */
    const Ref f = reference(1.0, 1.0, {iso(no * no)}, {d}, 2.25, 1.0, 0.0);
    check_small(compare(r0, f), 1e-12, "axis along z equals an isotropic layer of n_o");

    double prev = 1e300;
    bool monotone = true;
    for (int k = 1; k <= 12; ++k)
    {
        const double th = std::pow(10.0, -k);
        rotated_uniaxial_x(no, ne, th, e);
        const Result r = evaluate(iso(1.0), {dielectric(e)}, {d}, iso(2.25), 0.0);
        double diff = 0;
        for (int i = 0; i < 8; ++i) diff = std::fmax(diff, std::abs(r.J[i] - r0.J[i]));
        check_small(diff, 100.0 * th * th + 1e-13, "tilt 10^-k changes r and t by O(θ²)");
        if (diff > prev + 1e-15) monotone = false;
        prev = diff;

        /* At normal incidence the tilted layer is still block diagonal: s sees ε_yy − ε_yz²/ε_zz. */
        Mat6 eff = iso(no * no);
        const cplx es = e[4] - e[5] * e[7] / e[8];
        eff.m[7] = es;
        check_small(compare(r, reference(1.0, 1.0, {eff}, {d}, 2.25, 1.0, 0.0)), 1e-12,
                    "tilted axis at normal incidence equals the extraordinary-index reference");
    }
    check(monotone, "the change shrinks monotonically as the tilt goes to zero");
}

/* ---- 4. A layer at its critical angle ------------------------------------------------------ */

static void LayerAtItsCriticalAngle()
{
    /* Glass / 200 nm of air / glass, at ξ = 1 exactly: q = 0 in the air, Δ defective there. */
    std::vector<Mat6> L = {iso(1.0)};
    std::vector<double> d = {200.0};
    const Result r = evaluate(iso(2.25), L, d, iso(2.25), 1.0);
    check(r.status == BC_OK, "q = 0 inside a layer evaluates");
    check_small(compare(r, reference(2.25, 1.0, L, d, 2.25, 1.0, 1.0)), 1e-12,
                "q = 0 inside a layer equals the reference");
    for (int k = 3; k <= 9; k += 3)
        for (int sgn : {-1, 1})
        {
            const double xi = 1.0 + sgn * std::pow(10.0, -k);
            const Result n = evaluate(iso(2.25), L, d, iso(2.25), xi);
            double diff = 0;
            for (int i = 0; i < 8; ++i) diff = std::fmax(diff, std::abs(n.J[i] - r.J[i]));
            check_small(diff, 10.0 * std::pow(10.0, -k), "continuous through q = 0");
        }
}

/* ---- 5. Thick metal ----------------------------------------------------------------------- */

static void ThickMetal()
{
    const cplx em(-40.0, 12.0);   /* an aluminium-like ε at the test wavelength; not catalogue data */
    const cplx q1 = fwd_q(1.0, 1.0, 0.3), q2 = fwd_q(em, 1.0, 0.3);
    const cplx rp_bulk = (em * q1 - q2) / (em * q1 + q2);
    const cplx rs_bulk = (q1 - q2) / (q1 + q2);

    for (double d : {500.0, 5000.0, 50000.0})
    {
        const Result r = evaluate(iso(1.0), {iso(em)}, {d}, iso(2.25), 0.3);
        check(r.status == BC_OK, "thick metal evaluates");
        check_small(std::abs(r.J[0] - rp_bulk), 1e-13, "thick metal: r_pp is the bulk value");
        check_small(std::abs(r.J[3] - rs_bulk), 1e-13, "thick metal: r_ss is the bulk value");
        check_small(r.absorption[0] - r.P[2], 1e-12, "thick metal: absorption is all in the metal");
        std::printf("  metal %6.0f nm: T_p %.3e, opaque %d, %d steps\n", d, r.P[1], r.diag.opaque, r.diag.max_steps);
        if (d == 50000.0)
        {
            check(r.diag.opaque == 1, "50 µm is flagged opaque");
            check(r.P[1] == 0.0 && r.P[4] == 0.0, "and T is exactly zero");
        }
        if (d == 500.0)
        {
            check(r.diag.opaque == 0, "0.5 µm is not opaque");
            check(r.P[1] > 0.0, "and T is positive");
        }
    }
}

/* ---- A thick polariser at an angle ----------------------------------------------------------
 *
 * The case continuous orthonormalisation exists for, and the case that rules out accumulating
 * the R factors themselves. A dichroic layer passes one polarisation (x) and absorbs the other
 * (y) by e^{-80}; rotated by φ about z, the two waves mix. Sweeping backward, the absorbed wave
 * grows by e^{80} and, without re-orthonormalisation, drags both columns of W onto itself and
 * loses the transmitted wave. At normal incidence between identical media the reference is exact:
 * rotating the sample rotates the transmission Jones matrix, t(φ) = R(φ) diag(t_x, t_y) R(φ)ᵀ.
 */
static void ThickPolariserAtAnAngle()
{
    const cplx ex(2.25, 0.001), ey(2.25, 1.0);
    const double d = 20000.0;
    Mat6 aligned = iso(ex);
    aligned.m[7] = ey;
    const Ref f = reference(1.0, 1.0, {aligned}, {d}, 1.0, 1.0, 0.0);   /* t_pp ↔ x, t_ss ↔ y */
    std::printf("  polariser: |t_x| %.3e, |t_y| %.3e\n", std::abs(f.tpp), std::abs(f.tss));

    for (double phi : {0.3, 0.7854, 1.2})
    {
        const double c = std::cos(phi), s = std::sin(phi);
        Mat6 M = iso(1.0);
        /* ε = Rz(φ) diag(ex, ey, ex) Rz(φ)ᵀ */
        M.m[0] = c * c * ex + s * s * ey;
        M.m[1] = c * s * (ex - ey);
        M.m[6] = c * s * (ex - ey);
        M.m[7] = s * s * ex + c * c * ey;
        M.m[14] = ex;
        const Result r = evaluate(iso(1.0), {M}, {d}, iso(1.0), 0.0);
        check(r.status == BC_OK, "polariser evaluates");

        const cplx txx = c * c * f.tpp + s * s * f.tss;
        const cplx tyy = s * s * f.tpp + c * c * f.tss;
        const cplx txy = c * s * (f.tpp - f.tss);
        check_near(r.J[4], txx, 1e-12, "polariser at an angle: t_pp");
        check_near(r.J[7], tyy, 1e-12, "polariser at an angle: t_ss");
        check_near(r.J[5], txy, 1e-12, "polariser at an angle: t_ps");
        check_near(r.J[6], txy, 1e-12, "polariser at an angle: t_sp");
        check(r.diag.opaque == 0, "a polariser is not opaque");
    }
}

/* ---- 6. Frustrated total reflection --------------------------------------------------------- */

static void FrustratedTotalReflection()
{
    double prev = 2.0;
    for (double g : {0.02, 0.1, 0.3, 1.0, 2.0})
    {
        std::vector<Mat6> L = {iso(1.0)};
        std::vector<double> d = {g * LAMBDA};
        const Result r = evaluate(iso(2.25), L, d, iso(2.25), 1.2);
        check_small(compare(r, reference(2.25, 1.0, L, d, 2.25, 1.0, 1.2)), 1e-12, "FTIR equals the reference");
        check_small(r.P[2] + r.P[5], 1e-13, "FTIR: lossless");
        check(r.P[1] < prev, "FTIR: T falls as the gap widens");
        prev = r.P[1];
    }
}

/* ---- Energy: random lossless and lossy general M --------------------------------------------- */

static Mat6 random_hermitian(Rng& rng)
{
    Mat6 M;
    for (int i = 0; i < 6; ++i)
        for (int j = i; j < 6; ++j)
        {
            const cplx z = (i == j) ? cplx(rng.uniform(1.5, 4.0), 0) : 0.3 * rng.complex(-1, 1);
            M.m[i * 6 + j] = z;
            M.m[j * 6 + i] = std::conj(z);
        }
    return M;
}

static void GeneralStacksConserveEnergy()
{
    Rng rng(2128);
    double worst_lossless = 0, worst_absorption = 0;
    for (int trial = 0; trial < 300; ++trial)
    {
        const int N = 1 + (int)rng.uniform(0, 4);
        std::vector<Mat6> L;
        std::vector<double> d;
        const bool lossy = trial % 2;
        for (int k = 0; k < N; ++k)
        {
            Mat6 M = random_hermitian(rng);
            if (lossy) for (int i = 0; i < 36; ++i) M.m[i] += cplx(0, 0.2) * rng.complex(0, 1);
            L.push_back(M);
            d.push_back(rng.uniform(0.05, 2.0) * LAMBDA);
        }
        const double xi = rng.uniform(0, 1.4);
        const Result r = evaluate(iso(2.25), L, d, iso(1.69), xi);
        check(r.status == BC_OK, "general stack evaluates");
        for (int j = 0; j < 2; ++j)
        {
            if (!lossy) worst_lossless = std::fmax(worst_lossless, std::fabs(r.P[j * 3 + 2]));
            double sum = 0;
            for (int k = 0; k < N; ++k) sum += r.absorption[j * N + k];
            worst_absorption = std::fmax(worst_absorption, std::fabs(sum - r.P[j * 3 + 2]));
        }
    }
    check_small(worst_lossless, 1e-11, "lossless general M (Hermitian): R + T = 1");
    check_small(worst_absorption, 1e-11, "lossy general M: Σ layer absorption = 1 − R − T");
    std::printf("  general M: worst |1 − R − T| lossless %.2e; worst absorption mismatch %.2e\n",
                worst_lossless, worst_absorption);
}

/* ---- 7. The step bound ------------------------------------------------------------------------ */

static void StepBoundMeasurement()
{
    struct Cfg { double beta; int m; } cfgs[] = {{0.5, 9}, {1.0, 9}, {2.0, 9}, {2.098, 9}, {5.37, 13}};
    struct Case { const char* what; Mat6 layer; double d; double xi; };
    const Case cases[] = {
        {"dielectric, 50 waves", iso(4.0), 50.0 * LAMBDA / 2.0, 0.5},
        {"metal, 200 nm", iso(cplx(-40, 12)), 200.0, 0.5},
        {"evanescent gap", iso(1.0), 1.0 * LAMBDA, 1.3},
    };
    std::printf("  step bound: error against the reference, and time per point\n");
    std::printf("  %-22s", "case");
    for (const Cfg& c : cfgs) std::printf("   b=%-5.3g m=%-2d     ", c.beta, c.m);
    std::printf("\n");
    for (const Case& k : cases)
    {
        std::printf("  %-22s", k.what);
        const cplx ei = 2.25;
        const Ref f = reference(ei, 1.0, {k.layer}, {k.d}, 2.25, 1.0, k.xi);
        for (const Cfg& c : cfgs)
        {
            const bc_config cfg = {c.beta, c.m};
            const auto t0 = std::chrono::steady_clock::now();
            Result r{};
            const int reps = 200;
            for (int i = 0; i < reps; ++i) r = evaluate(iso(ei), {k.layer}, {k.d}, iso(2.25), k.xi, cfg);
            const double us = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - t0).count() / reps;
            const double e = compare(r, f);
            std::printf("   %.1e %7.1fus", e, us);
            if (c.beta <= 2.098) check_small(e, 1e-11, "error within θ9");
            else check_small(e, 1e-10, "error at θ13");
        }
        std::printf("\n");
    }

    /* The same configurations over many random stacks, so the choice does not rest on three cases:
     * 400 random isotropic stacks against the reference, and 300 random lossless general stacks
     * against energy conservation. */
    std::printf("  over random stacks:     ");
    for (const Cfg& c : cfgs)
    {
        Rng rng(31337);
        double worst_iso = 0, worst_energy = 0;
        for (int trial = 0; trial < 400; ++trial)
        {
            const int N = 1 + (int)rng.uniform(0, 6);
            std::vector<Mat6> L;
            std::vector<double> d;
            for (int k = 0; k < N; ++k) { L.push_back(iso(random_eps(rng))); d.push_back(rng.uniform(0.0, 5.0) * LAMBDA); }
            const double xi = rng.uniform(0.0, 1.45);
            const Result r = evaluate(iso(2.25), L, d, iso(1.69), xi, {c.beta, c.m});
            worst_iso = std::fmax(worst_iso, compare(r, reference(2.25, 1.0, L, d, 1.69, 1.0, xi)));
        }
        for (int trial = 0; trial < 300; ++trial)
        {
            const int N = 1 + (int)rng.uniform(0, 4);
            std::vector<Mat6> L;
            std::vector<double> d;
            for (int k = 0; k < N; ++k) { L.push_back(random_hermitian(rng)); d.push_back(rng.uniform(0.05, 5.0) * LAMBDA); }
            const Result r = evaluate(iso(2.25), L, d, iso(1.69), rng.uniform(0, 1.4), {c.beta, c.m});
            worst_energy = std::fmax(worst_energy, std::fmax(std::fabs(r.P[2]), std::fabs(r.P[5])));
        }
        std::printf("   %.1e/%.1e  ", worst_iso, worst_energy);
        check_small(worst_iso, 1e-10, "random isotropic stacks at this configuration");
        check_small(worst_energy, 1e-10, "random lossless general stacks at this configuration");
    }
    std::printf("\n");
}

/* ---- the exponential itself --------------------------------------------------------------- */

static void reference_expm(const cplx* A, cplx* E)
{
    /* Scaling and squaring with a long Taylor series: slow, simple, independent of Padé. */
    double n = 0;
    for (int j = 0; j < 4; ++j) { double s = 0; for (int i = 0; i < 4; ++i) s += std::abs(A[i * 4 + j]); n = std::fmax(n, s); }
    int sq = 0;
    while (n / std::pow(2.0, sq) > 0.05) ++sq;
    cplx B[16], T[16], X[16];
    for (int i = 0; i < 16; ++i) { B[i] = A[i] / std::pow(2.0, sq); E[i] = (i % 5 == 0) ? 1.0 : 0.0; T[i] = E[i]; }
    for (int k = 1; k <= 20; ++k)
    {
        mat4_mul(T, B, X);
        for (int i = 0; i < 16; ++i) { T[i] = X[i] / double(k); E[i] += T[i]; }
    }
    for (int s = 0; s < sq; ++s) { mat4_mul(E, E, X); for (int i = 0; i < 16; ++i) E[i] = X[i]; }
}

static void PadeMatchesTheExponential()
{
    Rng rng(1179);
    double worst9 = 0, worst13 = 0;
    for (int trial = 0; trial < 500; ++trial)
    {
        cplx A[16];
        for (int i = 0; i < 16; ++i) A[i] = rng.complex(-1, 1);
        double n = 0;
        for (int j = 0; j < 4; ++j) { double s = 0; for (int i = 0; i < 4; ++i) s += std::abs(A[i * 4 + j]); n = std::fmax(n, s); }
        for (int m : {9, 13})
        {
            const double theta = (m == 9) ? 2.097847961257068 : 5.371920351148152;
            const double target = rng.uniform(0.01, 1.0) * theta;
            cplx S[16], R[16];
            bc_complex a[16], e[16];
            for (int i = 0; i < 16; ++i) { S[i] = A[i] * (target / n); a[i] = to_bc(S[i]); }
            check(bc_expm_pade(a, m, e) == BC_OK, "Padé evaluates");
            reference_expm(S, R);
            double err = 0, nr = 0;
            for (int i = 0; i < 16; ++i) { err = std::fmax(err, std::abs(to_std(e[i]) - R[i])); nr = std::fmax(nr, std::abs(R[i])); }
            (m == 9 ? worst9 : worst13) = std::fmax(m == 9 ? worst9 : worst13, err / nr);
        }
    }
    check_small(worst9, 1e-13, "Padé m = 9 within θ9");
    check_small(worst13, 1e-13, "Padé m = 13 within θ13");
    std::printf("  Padé: worst relative error m=9 %.2e, m=13 %.2e\n", worst9, worst13);

    bc_complex a[16] = {}, e[16];
    check(bc_expm_pade(a, 7, e) == BC_NOT_IMPLEMENTED, "an unsupported degree is loud");
}

int main()
{
    PadeMatchesTheExponential();
    IsotropicStacksAgreeWithTheReference();
    DegenerateOpticAxisIsContinuous();
    LayerAtItsCriticalAngle();
    ThickMetal();
    ThickPolariserAtAnAngle();
    FrustratedTotalReflection();
    GeneralStacksConserveEnergy();
    StepBoundMeasurement();
    return report("sweep");
}
