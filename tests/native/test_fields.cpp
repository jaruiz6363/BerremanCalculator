/* test_fields.cpp — the fields against depth (engine design, Section 4).
 *
 *   1. Isotropic stacks against the block reference. The p block (E_x, H_y) and the s block
 *      (E_y, -H_x) each have a closed-form propagator (Berreman 1972, Eq. 41). The reference starts
 *      from the field at the front, f_in + r b_in, and carries it FORWARD through each layer, the
 *      opposite direction to the engine and with no code in common; in the outer media it uses the
 *      plane waves directly. E_z = -xi H_y / eps_zz and H_z = xi E_y / mu_zz complete the field.
 *      The stacks are kept thin enough that forward propagation is accurate.
 *   2. Random lossless general (Hermitian) stacks: S_z is the same at every depth, in the layers
 *      and in both outer media.
 *   3. Random lossy general stacks: the drop of S_z across each layer is that layer's absorption,
 *      as the engine reports it from the interface fields.
 * Depths include every interface, where an interface belongs to the medium behind it.
 */

#include "bc_internal.h"
#include "test_stack.h"

#include <vector>

struct FieldResult
{
    bc_status status;
    Result r;
    std::vector<cplx> fields;   /* [incident wave][depth][6] */
};

static FieldResult evaluate_fields(const Mat6& in, const std::vector<Mat6>& layers, const std::vector<double>& d,
                                   const Mat6& ex, double xi, const std::vector<double>& depths)
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
    p.n_layers = N; p.n_spectral = 1; p.n_points = 1; p.n_depths = (int32_t)depths.size();
    p.M = M.data(); p.wavelength = &wl; p.thickness = d.empty() ? nullptr : d.data();
    p.point_spectral = &sp; p.point_xi = &xi; p.field_depths = depths.data();

    FieldResult fr{};
    int64_t bytes = 0;
    fr.status = bc_plan(&p, &bytes);
    if (fr.status != BC_OK) return fr;
    std::vector<unsigned char> scratch((size_t)bytes);
    std::vector<bc_complex> f(2 * depths.size() * 6);
    bc_complex J[8];
    fr.r.absorption.assign(2 * (size_t)std::max(N, 1), 0.0);
    bc_output o{J, fr.r.P, N > 0 ? fr.r.absorption.data() : nullptr, f.data(), &fr.r.diag};
    fr.status = bc_evaluate(&p, 0, 1, scratch.data(), bytes, &o);
    for (int i = 0; i < 8; ++i) fr.r.J[i] = to_std(J[i]);
    for (const bc_complex& z : f) fr.fields.push_back(to_std(z));
    return fr;
}

/* The forward propagator exp(i k0 h Delta_block) = [[C, i a S], [i b S, C]]. */
static void block_propagator(Block k, double h, cplx* P)
{
    cplx Pi[4];
    block_inverse_propagator(k, h, Pi);
    P[0] = Pi[0]; P[1] = -Pi[1]; P[2] = -Pi[2]; P[3] = Pi[3];
}

static std::vector<double> depths_for(const std::vector<double>& d, Rng& rng)
{
    std::vector<double> z = {-180.0, -37.0, 0.0};
    double at = 0.0;
    for (double t : d)
    {
        z.push_back(at + rng.uniform(0.05, 0.95) * t);
        at += t;
        z.push_back(at);
    }
    z.push_back(at + 23.0);
    z.push_back(at + 250.0);
    return z;
}

/* ---- 1. isotropic stacks against the block reference ------------------------------------------ */

static void IsotropicFieldsAgreeWithTheReference()
{
    Rng rng(9031);
    double worst = 0.0;
    for (int trial = 0; trial < 200; ++trial)
    {
        const cplx ei = (trial % 2) ? 2.25 : 1.0, ee = cplx(std::pow(rng.uniform(1.0, 2.0), 2), 0);
        const int N = 1 + (int)rng.uniform(0, 5);
        std::vector<Mat6> L;
        std::vector<double> d;
        std::vector<cplx> eps;
        for (int k = 0; k < N; ++k)
        {
            const bool metal = rng.uniform(0, 1) < 0.2;
            const cplx e = metal ? cplx(rng.uniform(-20, -2), rng.uniform(0.2, 3.0)) : cplx(std::pow(rng.uniform(1.3, 2.5), 2), rng.uniform(0, 0.1));
            eps.push_back(e);
            L.push_back(iso(e));
            d.push_back(metal ? rng.uniform(5, 40) : rng.uniform(20, 300));
        }
        const double xi = std::sqrt(ei.real()) * std::sin(rng.uniform(0, 1.3));
        auto z = depths_for(d, rng);
        FieldResult fr = evaluate_fields(iso(ei), L, d, iso(ee), xi, z);
        if (fr.status != BC_OK) { check(false, "the engine evaluates the isotropic stack with fields"); continue; }

        const Ref ref = reference(ei, 1.0, L, d, ee, 1.0, xi);
        const cplx Ni = std::sqrt(ei), Ne = std::sqrt(ee);
        const cplx qi = fwd_q(ei, 1.0, xi), qe = fwd_q(ee, 1.0, xi);
        const double k0 = 2 * PI / LAMBDA;
        const cplx I(0, 1);
        double total = 0.0;
        for (double t : d) total += t;

        for (int pol = 0; pol < 2; ++pol)
        {
            const cplx r = pol == 0 ? ref.rpp : ref.rss, t = pol == 0 ? ref.tpp : ref.tss;
            const cplx f_in[2] = {pol == 0 ? qi / Ni : 1.0, pol == 0 ? Ni : qi};
            const cplx b_in[2] = {pol == 0 ? -qi / Ni : 1.0, pol == 0 ? Ni : -qi};
            const cplx f_ex[2] = {pol == 0 ? qe / Ne : 1.0, pol == 0 ? Ne : qe};
            double scale = 0.0;
            for (size_t di = 0; di < z.size(); ++di)
            {
                cplx psi[2];
                cplx ezz = ei;
                if (z[di] < 0)
                {
                    const cplx a = std::exp(I * k0 * qi * z[di]), b = r * std::exp(-I * k0 * qi * z[di]);
                    psi[0] = f_in[0] * a + b_in[0] * b; psi[1] = f_in[1] * a + b_in[1] * b;
                }
                else if (z[di] >= total)
                {
                    const cplx a = t * std::exp(I * k0 * qe * (z[di] - total));
                    psi[0] = f_ex[0] * a; psi[1] = f_ex[1] * a;
                    ezz = ee;
                }
                else
                {
                    psi[0] = f_in[0] + r * b_in[0]; psi[1] = f_in[1] + r * b_in[1];
                    double at = 0.0;
                    for (int k = 0; k < N; ++k)
                    {
                        const Block blk = pol == 0 ? p_block(L[k], xi) : s_block(L[k], xi);
                        const double h = std::fmin(d[k], z[di] - at);
                        cplx P[4];
                        block_propagator(blk, h, P);
                        const cplx a0 = P[0] * psi[0] + P[1] * psi[1], a1 = P[2] * psi[0] + P[3] * psi[1];
                        psi[0] = a0; psi[1] = a1;
                        if (z[di] < at + d[k]) { ezz = eps[k]; break; }
                        at += d[k];
                    }
                }
                /* The six components the engine writes: (Ex, Ey, Ez, Hx, Hy, Hz). */
                cplx want[6] = {};
                if (pol == 0) { want[0] = psi[0]; want[4] = psi[1]; want[2] = -xi * psi[1] / ezz; }
                else          { want[1] = psi[0]; want[3] = -psi[1]; want[5] = xi * psi[0]; }
                const cplx* got = fr.fields.data() + ((size_t)pol * z.size() + di) * 6;
                for (int k = 0; k < 6; ++k)
                {
                    scale = std::fmax(scale, std::abs(want[k]));
                    worst = std::fmax(worst, std::abs(got[k] - want[k]) / std::fmax(1.0, scale));
                }
            }
        }
    }
    std::printf("  isotropic fields against the reference: worst %.3g\n", worst);
    check_small(worst, 1e-10, "isotropic stacks: all six components at every depth against the forward block reference");
}

/* ---- 2 and 3. general stacks: the Poynting flux through the depths ----------------------------- */

static double sz(const cplx* v) { return std::real(v[0] * std::conj(v[4])) - std::real(v[1] * std::conj(v[3])); }

static void GeneralStacksConserveAndAbsorbAsReported()
{
    Rng rng(4417);
    double worst_lossless = 0.0, worst_lossy = 0.0;
    for (int trial = 0; trial < 150; ++trial)
    {
        const bool lossy = trial % 2 == 1;
        const int N = 1 + (int)rng.uniform(0, 4);
        std::vector<Mat6> L;
        std::vector<double> d;
        for (int k = 0; k < N; ++k)
        {
            Mat6 M = random_hermitian(rng);
            if (lossy) for (int i = 0; i < 3; ++i) M.m[i * 7] += cplx(0, rng.uniform(0.0, 0.3));   /* passive: i × a positive diagonal */
            L.push_back(M);
            d.push_back(rng.uniform(30, 250));
        }
        const double xi = 1.2 * std::sin(rng.uniform(0, 1.2));
        auto z = depths_for(d, rng);
        FieldResult fr = evaluate_fields(iso(1.44), L, d, iso(2.25), xi, z);
        if (fr.status != BC_OK) { check(false, "the engine evaluates the general stack with fields"); continue; }
        for (int j = 0; j < 2; ++j)
        {
            auto S = [&](size_t di) { return sz(fr.fields.data() + ((size_t)j * z.size() + di) * 6); };
            /* The incident power, from the field in front: S_z there is s_inc (1 - R). */
            const double s_inc = S(0) / (1.0 - fr.r.P[j * 3 + 0]);
            if (!lossy)
                for (size_t di = 1; di < z.size(); ++di) worst_lossless = std::fmax(worst_lossless, std::fabs(S(di) - S(0)) / s_inc);
            else
            {
                /* Interfaces sit at indices 2 (z = 0), then 4, 6, ... (each layer's back face). */
                for (int k = 0; k < N; ++k)
                {
                    const double drop = (S(2 + 2 * k) - S(4 + 2 * k)) / s_inc;
                    worst_lossy = std::fmax(worst_lossy, std::fabs(drop - fr.r.absorption[(size_t)j * N + k]));
                }
            }
        }
    }
    std::printf("  general stacks: S_z constant to %.3g, absorption from S_z to %.3g\n", worst_lossless, worst_lossy);
    check_small(worst_lossless, 1e-10, "lossless general stacks: S_z is the same at every depth");
    check_small(worst_lossy, 1e-10, "lossy general stacks: S_z drops across each layer by its reported absorption");
}

int main()
{
    IsotropicFieldsAgreeWithTheReference();
    GeneralStacksConserveAndAbsorbAsReported();
    return report("fields");
}
