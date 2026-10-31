/* bc_internal.h — definitions shared by the engine's translation units, not part of the ABI. */

#ifndef BC_INTERNAL_H
#define BC_INTERNAL_H

#include "berreman.h"

/* Scratch for one worker. Engine design Section 4: the sweep keeps, for every interface, the
 * orthonormal 4x2 basis W (8 complex) and the layer's 2x2 triangular factor R (4 complex). A stack
 * of n layers has n + 1 interfaces. Revised when the sweep is written, and tested against what the
 * sweep actually touches. */
BC_HD static inline int64_t bc_scratch_bytes(int32_t n_layers)
{
    return ((int64_t)n_layers + 1) * 12 * (int64_t)sizeof(bc_complex);
}

#endif /* BC_INTERNAL_H */
