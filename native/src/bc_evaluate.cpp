/* bc_evaluate.cpp — the evaluation of a range of points.
 *
 * NOT IMPLEMENTED YET, AND LOUD ABOUT IT (rule 5). The call checks its arguments, then returns
 * BC_NOT_IMPLEMENTED without writing to any output, the diagnostics included: a caller that
 * ignored the status would find its buffers exactly as it left them, not plausible zeros.
 *
 * The engine replaces the body after the tests of engine-design Section 6 exist.
 */

#include "bc_internal.h"

extern "C" BC_API bc_status bc_evaluate(const bc_problem* problem,
                                        int32_t first_point, int32_t n_points,
                                        void* scratch, int64_t scratch_bytes,
                                        bc_output* output)
{
    if (problem == nullptr || output == nullptr) return BC_INVALID_ARGUMENT;
    if (first_point < 0 || n_points < 0) return BC_INVALID_ARGUMENT;
    if ((int64_t)first_point + n_points > problem->n_points) return BC_INVALID_ARGUMENT;
    if (scratch_bytes < bc_scratch_bytes(problem->n_layers)) return BC_SCRATCH_TOO_SMALL;
    if (scratch == nullptr && scratch_bytes > 0) return BC_INVALID_ARGUMENT;

    return BC_NOT_IMPLEMENTED;
}
