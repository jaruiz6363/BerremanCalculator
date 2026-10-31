/* bc_plan.cpp — input checks and scratch sizing.
 *
 * Everything the evaluator would otherwise have to check per point is checked here, once, so that
 * bc_evaluate can trust its inputs.
 */

#include "bc_internal.h"

#include <math.h>

extern "C" BC_API int32_t bc_abi_version(void)
{
    return BC_ABI_VERSION;
}

static bool finite_positive(double x) { return isfinite(x) && x > 0.0; }
static bool finite_non_negative(double x) { return isfinite(x) && x >= 0.0; }

extern "C" BC_API bc_status bc_plan(const bc_problem* problem, int64_t* scratch_bytes)
{
    if (problem == nullptr || scratch_bytes == nullptr) return BC_INVALID_ARGUMENT;

    const bc_problem& p = *problem;

    if (p.n_layers < 0 || p.n_spectral < 1 || p.n_points < 0 || p.n_depths < 0)
        return BC_INVALID_ARGUMENT;

    if (p.M == nullptr || p.wavelength == nullptr) return BC_INVALID_ARGUMENT;
    if (p.n_layers > 0 && p.thickness == nullptr) return BC_INVALID_ARGUMENT;
    if (p.n_points > 0 && (p.point_spectral == nullptr || p.point_xi == nullptr))
        return BC_INVALID_ARGUMENT;
    if (p.n_depths > 0 && p.field_depths == nullptr) return BC_INVALID_ARGUMENT;

    for (int32_t s = 0; s < p.n_spectral; ++s)
        if (!finite_positive(p.wavelength[s])) return BC_INVALID_ARGUMENT;

    for (int32_t k = 0; k < p.n_layers; ++k)
        if (!finite_non_negative(p.thickness[k])) return BC_INVALID_ARGUMENT;

    for (int32_t i = 0; i < p.n_points; ++i)
    {
        if (p.point_spectral[i] < 0 || p.point_spectral[i] >= p.n_spectral) return BC_INVALID_ARGUMENT;
        if (!isfinite(p.point_xi[i])) return BC_INVALID_ARGUMENT;
    }

    for (int32_t d = 0; d < p.n_depths; ++d)
        if (!isfinite(p.field_depths[d])) return BC_INVALID_ARGUMENT;

    *scratch_bytes = bc_scratch_bytes(p.n_layers);
    return BC_OK;
}
