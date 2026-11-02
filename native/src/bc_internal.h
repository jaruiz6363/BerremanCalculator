/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* bc_internal.h — definitions shared by the engine's translation units, not part of the ABI. */

#ifndef BC_INTERNAL_H
#define BC_INTERNAL_H

#include "berreman.h"

/* Scratch for one worker. Engine design, Section 4: one record per interface, n_layers + 1 of
 * them, each holding a 4×2 orthonormal basis W (8 complex), a 2×2 factor (4 complex), and the 2×2
 * amplitudes on W of the field there for each incident wave (4 complex), which the fields against
 * depth start from. */
#define BC_RECORD_COMPLEX 16

BC_HD static inline int64_t bc_scratch_bytes(int32_t n_layers)
{
    return ((int64_t)n_layers + 1) * BC_RECORD_COMPLEX * (int64_t)sizeof(bc_complex);
}

/* The numerical choices of engine design, Section 3. bc_evaluate uses bc_default_config; the
 * step-bound measurement (engine design, Section 6, test 7) varies them. */
typedef struct bc_config
{
    double beta;     /* the step bound: ‖k0 h Δ‖₁ ≤ beta */
    int32_t pade_m;  /* 9 (beta ≤ 2.098) or 13 (beta ≤ 5.372) */
} bc_config;

static const bc_config bc_default_config = {5.37, 13};

bc_status bc_evaluate_with(const bc_problem* problem, int32_t first_point, int32_t n_points,
                           void* scratch, int64_t scratch_bytes, bc_output* output,
                           const bc_config* config);

#endif /* BC_INTERNAL_H */
