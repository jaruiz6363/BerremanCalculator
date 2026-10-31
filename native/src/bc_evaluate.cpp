/* bc_evaluate.cpp — the evaluation of a range of points (engine design, Sections 2–5).
 *
 * WHAT IS BUILT. Any outer media (closed-form modes when isotropic, Schur otherwise) and any
 * layers. The backward sweep, the front solve, the forward recovery of transmission and per-layer
 * absorption, and the diagnostics.
 *
 * WHAT IS NOT, AND IS LOUD (rule 5):
 *   - fields against depth: the whole call returns BC_NOT_IMPLEMENTED, touching nothing.
 *
 * THE TRANSMISSION FACTOR. Sweeping backward, each step gives P W_old = W_new R. The amplitudes
 * at the back of a layer follow from those at its front by c_back = R₁⁻¹ R₂⁻¹ ⋯ R_n⁻¹ c_front (R₁
 * the step at the back). The engine accumulates that product of INVERSES, L, per layer. It is the
 * physical map from front amplitudes to back amplitudes, so it never overflows: through an opaque
 * layer it underflows towards zero, which is the right answer. The product of the R's themselves,
 * as first planned, overflows in a polariser, where one wave grows by e^{a} sweeping backward
 * while the other passes. That would have discarded a transmitted wave that is really there.
 */

#include "bc_internal.h"
#include "bc_delta.h"
#include "bc_outer.h"
#include "bc_sweep.h"

#include <math.h>

static const double BC_PI = 3.14159265358979323846;

/* A 4×4 whose columns are the given mode set's columns (c0, c1) → a 4×2. */
BC_HD static inline void bc_columns(const bc_complex* modes, int c0, bc_complex* out)
{
    for (int i = 0; i < 4; ++i)
    {
        out[i * 2 + 0] = modes[i * 4 + c0];
        out[i * 2 + 1] = modes[i * 4 + c0 + 1];
    }
}

/* Column j of the 4×2 product W x, for a 2×2 x: the field of amplitudes x[·][j] on basis W. */
BC_HD static inline void bc_field(const bc_complex* W, const bc_complex* x, int j, bc_complex* psi)
{
    for (int i = 0; i < 4; ++i)
        psi[i] = bc_add(bc_mul(W[i * 2 + 0], x[0 * 2 + j]), bc_mul(W[i * 2 + 1], x[1 * 2 + j]));
}

/* 1-norm condition number of a 4×4, by its explicit inverse. */
BC_HD static inline double bc_cond1_4(const bc_complex* A)
{
    // BC_FIXED_OK: 4×4
    bc_complex T[16];
    // BC_FIXED_OK: 4×4
    bc_complex I[16];
    for (int k = 0; k < 16; ++k) { T[k] = A[k]; I[k] = bc_c((k % 5 == 0) ? 1.0 : 0.0, 0.0); }
    if (bc_solve4(T, I, 4) != BC_OK) return INFINITY;
    return bc_norm1_4(A) * bc_norm1_4(I);
}

/* One point. Writes its outputs only when it returns BC_OK; the diagnostics record is written by
 * the caller in every case. */
static bc_status bc_point(const bc_problem* p, int32_t pt, const bc_config* cfg,
                          bc_complex* rec, bc_output* out, bc_point_diagnostics* diag)
{
    const int32_t N = p->n_layers;
    const int32_t s = p->point_spectral[pt];
    const double xi = p->point_xi[pt];
    const double k0 = 2.0 * BC_PI / p->wavelength[s];
    const bc_complex* Ms = p->M + (int64_t)s * (N + 2) * 36;

    /* ---- outer media (Section 2) ---------------------------------------------------------- */
    if (!bc_is_hermitian(Ms)) return BC_INCIDENT_ABSORBING;

    // BC_FIXED_OK: 4×4 mode sets
    bc_complex m_in[16];
    // BC_FIXED_OK: 4×4
    bc_complex m_ex[16];
    bc_status st = bc_outer_modes(Ms, xi, m_in);
    if (st != BC_OK) return st;
    st = bc_outer_modes(Ms + (int64_t)(N + 1) * 36, xi, m_ex);
    if (st != BC_OK) return st;

    diag->incident_condition = bc_cond1_4(m_in);
    diag->exit_condition = bc_cond1_4(m_ex);

    /* ---- the backward sweep (Section 3) --------------------------------------------------- */
    // BC_FIXED_OK: 4×2 subspace and step product
    bc_complex W[8];
    // BC_FIXED_OK: 4×2
    bc_complex PW[8];
    // BC_FIXED_OK: 2×2
    bc_complex R[4];
    bc_columns(m_ex, 0, W);
    st = bc_qr42(W, R);
    if (st != BC_OK) return st;

    /* Record 0: the exit. W = Q, factor = R0⁻¹, so t = R0⁻¹ c_exit. */
    for (int k = 0; k < 8; ++k) rec[k] = W[k];
    rec[8] = bc_c(1.0, 0.0); rec[9] = bc_c(0.0, 0.0); rec[10] = bc_c(0.0, 0.0); rec[11] = bc_c(1.0, 0.0);
    bc_right_mul_inv_upper(rec + 8, R);

    int32_t max_steps = 0;
    for (int32_t layer = N; layer >= 1; --layer)
    {
        bc_complex* r = rec + (int64_t)layer * BC_RECORD_COMPLEX;
        bc_complex* L = r + 8;
        L[0] = bc_c(1.0, 0.0); L[1] = bc_c(0.0, 0.0); L[2] = bc_c(0.0, 0.0); L[3] = bc_c(1.0, 0.0);

        const double d = p->thickness[layer - 1];
        if (d > 0.0)
        {
            // BC_FIXED_OK: 4×4
            bc_complex D[16];
            // BC_FIXED_OK: 4×4
            bc_complex E[16];
            st = bc_build_delta(Ms + (int64_t)layer * 36, xi, D, nullptr);
            if (st != BC_OK) return st;

            const double total = k0 * d * bc_norm1_4(D);
            double nsteps = ceil(total / cfg->beta);
            if (nsteps < 1.0) nsteps = 1.0;
            const int32_t n = (int32_t)nsteps;
            const double h = d / nsteps;

            /* A = −i k0 h Δ: the step from the back of the layer toward its front. */
            for (int k = 0; k < 16; ++k) D[k] = bc_mul(bc_c(0.0, -k0 * h), D[k]);
            st = bc_expm_pade(D, cfg->pade_m, E);
            if (st != BC_OK) return st;

            for (int32_t step = 0; step < n; ++step)
            {
                bc_mul_44_42(E, W, PW);
                for (int k = 0; k < 8; ++k) W[k] = PW[k];
                st = bc_qr42(W, R);
                if (st != BC_OK) return st;
                bc_right_mul_inv_upper(L, R);
            }
            if (n > max_steps) max_steps = n;
        }
        for (int k = 0; k < 8; ++k) r[k] = W[k];   /* W at the front of this layer */
    }

    /* ---- the front (Section 3) ------------------------------------------------------------ */
    // BC_FIXED_OK: 4×2 incident and reflected bases
    bc_complex inc[8];
    // BC_FIXED_OK: 4×2
    bc_complex B[8];
    // BC_FIXED_OK: 2×2 amplitudes
    bc_complex c[4];
    // BC_FIXED_OK: 2×2
    bc_complex rr[4];
    bc_columns(m_in, 0, inc);
    bc_columns(m_in, 2, B);
    {
        // BC_FIXED_OK: 4×4 front matrix, for its condition number
        bc_complex F[16];
        for (int i = 0; i < 4; ++i)
        {
            F[i * 4 + 0] = W[i * 2 + 0]; F[i * 4 + 1] = W[i * 2 + 1];
            F[i * 4 + 2] = bc_neg(B[i * 2 + 0]); F[i * 4 + 3] = bc_neg(B[i * 2 + 1]);
        }
        diag->front_condition = bc_cond1_4(F);
    }
    st = bc_front_solve(W, B, inc, c, rr);
    if (st != BC_OK) return st;

    /* ---- forward recovery: transmission and absorption (Section 4) ------------------------- */
    // BC_FIXED_OK: 4-vectors and 2×2 amplitudes
    bc_complex psi[4];
    // BC_FIXED_OK: 2×2
    bc_complex cb[4];
    /* The power each incident wave carries. An evanescent incident wave carries none, so its R, T,
     * A and layer absorption are undefined and written as NaN; its Jones column is still the
     * amplitude response. Both evanescent fails the point before anything is written. */
    // BC_FIXED_OK: one incident power per incident wave
    double s_inc[2];
    for (int j = 0; j < 2; ++j)
    {
        for (int i = 0; i < 4; ++i) psi[i] = inc[i * 2 + j];
        s_inc[j] = bc_poynting_z(psi);
        double size = 0.0;
        for (int i = 0; i < 4; ++i) size += psi[i].re * psi[i].re + psi[i].im * psi[i].im;
        if (!(s_inc[j] > 1e3 * BC_UNIT_ROUNDOFF * size)) s_inc[j] = NAN;
    }
    if (isnan(s_inc[0]) && isnan(s_inc[1])) return BC_INCIDENT_EVANESCENT;

    for (int32_t layer = 1; layer <= N; ++layer)
    {
        const bc_complex* r = rec + (int64_t)layer * BC_RECORD_COMPLEX;
        const bc_complex* back = (layer == N) ? rec : rec + (int64_t)(layer + 1) * BC_RECORD_COMPLEX;
        bc_mul_22(r + 8, c, cb);
        for (int j = 0; j < 2; ++j)
        {
            double sf, sb;
            bc_field(r, c, j, psi);     sf = bc_poynting_z(psi);
            bc_field(back, cb, j, psi); sb = bc_poynting_z(psi);
            if (out->layer_absorption != nullptr)
                out->layer_absorption[((int64_t)pt * 2 + j) * N + (layer - 1)] = (sf - sb) / s_inc[j];
        }
        for (int k = 0; k < 4; ++k) c[k] = cb[k];
    }

    // BC_FIXED_OK: 2×2
    bc_complex t[4];
    bc_mul_22(rec + 8, c, t);

    double tmax = 0.0;
    for (int k = 0; k < 4; ++k) { const double a = bc_abs(t[k]); if (a > tmax) tmax = a; }
    diag->opaque = (tmax < 1e-250) ? 1 : 0;
    if (diag->opaque) for (int k = 0; k < 4; ++k) t[k] = bc_c(0.0, 0.0);
    diag->max_steps = max_steps;

    /* ---- outputs ---------------------------------------------------------------------------
     * Jones: r then t, each [incident][outgoing] row-major, so (pp, ps, sp, ss) with the FIRST
     * subscript the incident wave (Schubert's order). The solve's rr and t are [outgoing][incident]. */
    if (out->jones != nullptr)
    {
        bc_complex* J = out->jones + (int64_t)pt * 8;
        for (int in = 0; in < 2; ++in)
            for (int o = 0; o < 2; ++o)
            {
                J[in * 2 + o] = rr[o * 2 + in];
                J[4 + in * 2 + o] = t[o * 2 + in];
            }
    }
    if (out->power != nullptr)
    {
        // BC_FIXED_OK: 4×2 exit forward modes
        bc_complex ef[8];
        bc_columns(m_ex, 0, ef);
        for (int j = 0; j < 2; ++j)
        {
            bc_field(B, rr, j, psi);
            const double Rj = -bc_poynting_z(psi) / s_inc[j];
            bc_field(ef, t, j, psi);
            const double Tj = bc_poynting_z(psi) / s_inc[j];
            double* P = out->power + ((int64_t)pt * 2 + j) * 3;
            P[0] = Rj;
            P[1] = Tj;
            P[2] = 1.0 - Rj - Tj;
        }
    }
    return BC_OK;
}

bc_status bc_evaluate_with(const bc_problem* problem, int32_t first_point, int32_t n_points,
                           void* scratch, int64_t scratch_bytes, bc_output* output,
                           const bc_config* config)
{
    if (problem == nullptr || output == nullptr || config == nullptr) return BC_INVALID_ARGUMENT;
    if (first_point < 0 || n_points < 0) return BC_INVALID_ARGUMENT;
    if ((int64_t)first_point + n_points > problem->n_points) return BC_INVALID_ARGUMENT;
    if (scratch_bytes < bc_scratch_bytes(problem->n_layers)) return BC_SCRATCH_TOO_SMALL;
    if (scratch == nullptr) return BC_INVALID_ARGUMENT;

    /* Fields against depth are not built yet. Loud, and nothing is written. */
    if (output->fields != nullptr && problem->n_depths > 0) return BC_NOT_IMPLEMENTED;

    bc_complex* rec = static_cast<bc_complex*>(scratch);
    bc_status first_failure = BC_OK;
    for (int32_t pt = first_point; pt < first_point + n_points; ++pt)
    {
        bc_point_diagnostics diag;
        diag.status = BC_OK; diag.max_steps = 0; diag.opaque = 0; diag.reserved = 0;
        diag.front_condition = 0.0; diag.incident_condition = 0.0; diag.exit_condition = 0.0;

        const bc_status st = bc_point(problem, pt, config, rec, output, &diag);
        diag.status = st;
        if (st != BC_OK && first_failure == BC_OK) first_failure = st;
        if (output->diagnostics != nullptr) output->diagnostics[pt] = diag;
    }
    return first_failure;
}

extern "C" BC_API bc_status bc_evaluate(const bc_problem* problem,
                                        int32_t first_point, int32_t n_points,
                                        void* scratch, int64_t scratch_bytes,
                                        bc_output* output)
{
    return bc_evaluate_with(problem, first_point, n_points, scratch, scratch_bytes, output,
                            &bc_default_config);
}
