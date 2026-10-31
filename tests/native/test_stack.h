/* test_stack.h — building one-point problems, and the closed-form block reference, shared by the
 * stack tests.
 *
 * THE REFERENCE. For any stack whose Δ splits into a p block (ψ1, ψ2) and an s block (ψ3, ψ4), as
 * for isotropic layers and for layers whose axes are aligned with the stack, each block's
 * propagator has a closed form. With Δ_block = [[0, a], [b, 0]] and κ² = ab:
 *
 *     exp(i k0 h Δ_block) = [[cos θ, i a S], [i b S, cos θ]],   θ = k0 h κ,  S = sin θ / κ
 *
 * (Berreman 1972, Eq. 41, written without a branch: cos θ and S are even in κ, and S → k0 h at
 * κ = 0, where Δ is defective). The block's forward mode is (a, κ) and its backward mode (a, −κ).
 * The reference multiplies 2×2 matrices; it shares no code with the engine's exponential, sweep,
 * Schur form or front solve.
 */

#ifndef BC_TEST_STACK_H
#define BC_TEST_STACK_H

#include "bc_internal.h"
#include "test_support.h"

#include <vector>
#include <algorithm>

static const double PI = 3.14159265358979323846;
static const double LAMBDA = 500.0;   /* nm; only ratios matter */

/* ---- building problems ------------------------------------------------------------------- */

struct Mat6 { cplx m[36] = {}; };

static inline Mat6 iso(cplx eps, cplx mu = 1.0)
{
    Mat6 M;
    for (int i = 0; i < 3; ++i) { M.m[i * 7] = eps; M.m[(i + 3) * 7] = mu; }
    return M;
}

static inline Mat6 dielectric(const cplx* e3x3)
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

static inline Result evaluate(const Mat6& in, const std::vector<Mat6>& layers, const std::vector<double>& d,
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

static inline void block_inverse_propagator(Block k, double h, cplx* Pinv)
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
static inline void block_rt(const std::vector<Block>& blocks, const std::vector<double>& d,
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

static inline cplx fwd_q(cplx eps, cplx mu, double xi)
{
    cplx q = std::sqrt(eps * mu - xi * xi);
    if (q.imag() < 0 || (q.imag() == 0 && q.real() < 0)) q = -q;
    return q;
}

/* The reference for a stack of diagonal-M layers (isotropic or aligned) between isotropic media.
 * Returns r_pp, r_ss, t_pp, t_ss. */
struct Ref { cplx rpp, rss, tpp, tss; };

static inline Ref reference(cplx ei, cplx ui, const std::vector<Mat6>& layers, const std::vector<double>& d,
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

static inline double rel(cplx a, cplx b) { return std::abs(a - b) / std::fmax(1.0, std::abs(b)); }

static inline double compare(const Result& r, const Ref& f)
{
    double e = 0;
    e = std::fmax(e, rel(r.J[0], f.rpp));
    e = std::fmax(e, rel(r.J[3], f.rss));
    e = std::fmax(e, rel(r.J[4], f.tpp));
    e = std::fmax(e, rel(r.J[7], f.tss));
    e = std::fmax(e, std::abs(r.J[1]) + std::abs(r.J[2]) + std::abs(r.J[5]) + std::abs(r.J[6]));
    return e;
}

/* ---- outer media with aligned axes: powers from the block reference ------------------------
 *
 * For outer media whose M is diagonal (isotropic or aligned), each block's modes are (a, ±κ),
 * with the forward κ chosen by the engine's rule: Im κ > 0, or for real κ the sign that gives
 * S_z = Re(ψ1 ψ2*) > 0. Mode normalisation then differs from the engine's, so the comparison is of
 * powers, which do not depend on it. */

static inline Block p_block(const Mat6& L, double xi) { return {L.m[28] - xi * xi / L.m[14], L.m[0]}; }
static inline Block s_block(const Mat6& L, double xi) { return {L.m[21], L.m[7] - xi * xi / L.m[35]}; }

static inline cplx block_forward_kappa(Block k)
{
    cplx kap = std::sqrt(k.a * k.b);
    if (kap.imag() < 0) kap = -kap;
    if (kap.imag() == 0 && std::real(k.a * std::conj(kap)) < 0) kap = -kap;
    return kap;
}

struct RefPower { double R[2], T[2]; };   /* [p, s] */

static inline RefPower reference_power(const Mat6& in, const std::vector<Mat6>& layers,
                                const std::vector<double>& d, const Mat6& ex, double xi)
{
    RefPower out{};
    for (int pol = 0; pol < 2; ++pol)
    {
        auto blk = [&](const Mat6& L) { return pol == 0 ? p_block(L, xi) : s_block(L, xi); };
        std::vector<Block> bl;
        for (const Mat6& L : layers) bl.push_back(blk(L));
        const Block bi = blk(in), be = blk(ex);
        const cplx ki = block_forward_kappa(bi), ke = block_forward_kappa(be);
        const cplx f_in[2] = {bi.a, ki}, bk_in[2] = {bi.a, -ki}, f_ex[2] = {be.a, ke};
        cplx r, t;
        block_rt(bl, d, f_in, bk_in, f_ex, &r, &t);
        const double s_in = std::real(f_in[0] * std::conj(f_in[1]));
        const double s_r = std::real(r * bk_in[0] * std::conj(r * bk_in[1]));
        const double s_t = std::real(t * f_ex[0] * std::conj(t * f_ex[1]));
        out.R[pol] = -s_r / s_in;
        out.T[pol] = s_t / s_in;
    }
    return out;
}

static inline Mat6 random_hermitian(Rng& rng)
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

#endif /* BC_TEST_STACK_H */
