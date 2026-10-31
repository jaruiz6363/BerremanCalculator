/* berreman.h — the native evaluation contract.
 *
 * This header is the ABI. docs/contract.md (Section 6) and docs/engine-design.md are its prose;
 * where they disagree with this file, this file is what ships and the documents are wrong.
 *
 * FIVE RULES, which are constraints rather than preferences:
 *
 *   1. All memory is dimensioned and allocated by the caller. Nothing below allocates: no malloc,
 *      no new, no std::vector, no RAII type that owns storage.
 *   2. No fixed-size array dimensioned by a constant. There is no BC_MAX_LAYERS and there will not
 *      be one. Finding one is a defect report.
 *   3. The port to CUDA must be mechanical. Everything here is POD with C linkage; the evaluation
 *      path has no exceptions, no virtuals and no recursion.
 *   4. Double precision throughout.
 *   5. An unimplemented path is LOUD: a distinct status, and output buffers left untouched.
 *
 * STATUS. bc_evaluate computes any stack of layers between ISOTROPIC outer media: Jones, R/T/A,
 * per-layer absorption and diagnostics. Still loud (rule 5):
 *   - a point whose incident or exit medium is anisotropic gets BC_NOT_IMPLEMENTED in its
 *     diagnostics, and its other outputs are untouched;
 *   - requesting fields against depth makes the whole call return BC_NOT_IMPLEMENTED, touching
 *     nothing.
 * The ABI is not frozen: BC_ABI_VERSION 0 means it may still change without notice.
 *
 * RETURN VALUE OF bc_evaluate: BC_OK if every point succeeded, otherwise the status of the first
 * point that did not. Every point is still attempted, and each one's status is in its
 * diagnostics record.
 */

#ifndef BERREMAN_H
#define BERREMAN_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ---------------------------------------------------------------------------------------------
 * Portability markers
 *
 * BC_HD marks every function that will run on the device, from the day it is written, so the CUDA
 * port does not have to find them.
 * ------------------------------------------------------------------------------------------- */

#if defined(__CUDACC__)
#  define BC_HD __host__ __device__
#else
#  define BC_HD
#endif

#if defined(_WIN32)
#  define BC_API __declspec(dllexport)
#else
#  define BC_API __attribute__((visibility("default")))
#endif

#define BC_ABI_VERSION 0

/* ---------------------------------------------------------------------------------------------
 * Scalars
 *
 * bc_complex is two adjacent doubles, real first: the layout of System.Numerics.Complex, so
 * complex arrays pass from C# with no conversion.
 * ------------------------------------------------------------------------------------------- */

typedef struct bc_complex
{
    double re;
    double im;
} bc_complex;

/* ---------------------------------------------------------------------------------------------
 * Status
 *
 * One per call, and one per point in the diagnostics. The point codes are engine-design Section 5.
 * ------------------------------------------------------------------------------------------- */

typedef enum bc_status
{
    BC_OK                  = 0,
    BC_INVALID_ARGUMENT    = 1,  /* a count, pointer or index the call cannot use */
    BC_NOT_IMPLEMENTED     = 2,  /* outputs untouched */
    BC_SCRATCH_TOO_SMALL   = 3,  /* less scratch than bc_plan asked for */
    BC_INCIDENT_ABSORBING  = 4,  /* the incident medium is not lossless */
    BC_GRAZING_OUTER       = 5,  /* a root at q = 0 in an outer medium */
    BC_DELTA_SINGULAR      = 6,  /* M33 M66 - M36 M63 = 0 in some layer */
    BC_OUTER_EIGEN_FAILED  = 7,  /* the QR iteration on an outer medium did not converge */
    BC_BOUNDARY_SINGULAR   = 8   /* the 4x4 system at the front has a zero pivot */
} bc_status;

/* ---------------------------------------------------------------------------------------------
 * The problem: everything the engine reads. All caller-owned.
 *
 * A SPECTRAL INDEX is one (wavelength, sample orientation). Materials depend on it and not on the
 * angle, so M is given once per spectral index and shared by every point that uses it.
 *
 * MEDIA ARE NUMBERED 0 .. n_layers + 1: 0 is the incident medium, 1 .. n_layers the layers in
 * order from the incident side, n_layers + 1 the exit medium.
 *
 * M holds one 6x6 complex matrix per (spectral index, medium), row-major, in Berreman's form
 * [[eps, rho], [rho', mu]]:
 *
 *     M[((s * (n_layers + 2)) + m) * 36 + 6 * i + j]
 *
 * LENGTHS are in any one unit, the same for thickness, wavelength and field_depths: the engine
 * uses only their ratios.
 * ------------------------------------------------------------------------------------------- */

typedef struct bc_problem
{
    int32_t n_layers;            /* >= 0 */
    int32_t n_spectral;          /* >= 1 */
    int32_t n_points;            /* >= 0 */
    int32_t n_depths;            /* >= 0; 0 means no fields */

    const bc_complex* M;         /* n_spectral * (n_layers + 2) * 36 */
    const double* wavelength;    /* n_spectral, vacuum wavelength, > 0 */
    const double* thickness;     /* n_layers, >= 0; may be null when n_layers == 0 */
    const int32_t* point_spectral; /* n_points, each in [0, n_spectral) */
    const double* point_xi;      /* n_points, xi = k_x / k0, real */
    const double* field_depths;  /* n_depths, measured from the first interface; null if none */
} bc_problem;

/* ---------------------------------------------------------------------------------------------
 * Outputs. Any pointer may be null, and that output is then not computed.
 *
 * jones             n_points * 8 complex: r (2x2) then t (2x2), each [incident][outgoing]
 *                   row-major, so (pp, ps, sp, ss) with the FIRST subscript the incident wave
 *                   (Schubert's order: r_ps is p in, s out). With anisotropic outer media, the
 *                   media's own modes. t is the amplitude of the exit medium's forward modes at
 *                   the last interface, per unit incident amplitude at the first; 0 exactly when
 *                   the point is opaque (below 1e-250).
 * power             n_points * 2 * 3 real: for each incident wave, R, T, A.
 * layer_absorption  n_points * 2 * n_layers real.
 * fields            n_points * 2 * n_depths * 6 complex: (E_x, E_y, E_z, H_x, H_y, H_z), H as
 *                   Z0 H.
 * diagnostics       n_points records.
 * ------------------------------------------------------------------------------------------- */

typedef struct bc_point_diagnostics
{
    int32_t status;              /* a bc_status */
    int32_t max_steps;           /* the largest number of exponential steps in any layer */
    int32_t opaque;              /* 1 if the transmitted amplitude fell below 1e-250 */
    int32_t reserved;            /* zero; keeps the doubles 8-byte aligned */
    double front_condition;      /* condition number of the 4x4 system at the front */
    double incident_condition;   /* of the incident medium's mode basis */
    double exit_condition;       /* of the exit medium's mode basis */
} bc_point_diagnostics;

typedef struct bc_output
{
    bc_complex* jones;
    double* power;
    double* layer_absorption;
    bc_complex* fields;
    bc_point_diagnostics* diagnostics;
} bc_output;

/* ---------------------------------------------------------------------------------------------
 * Calls
 * ------------------------------------------------------------------------------------------- */

/* The ABI this library was built with. The caller checks it before anything else. */
BC_API int32_t bc_abi_version(void);

/* Checks the problem and returns the scratch, in bytes, that ONE worker needs for bc_evaluate.
 * Allocates nothing and writes only *scratch_bytes. */
BC_API bc_status bc_plan(const bc_problem* problem, int64_t* scratch_bytes);

/* Evaluates points [first_point, first_point + n_points). Called once per worker, each with its
 * own scratch; workers may run concurrently on disjoint point ranges. */
BC_API bc_status bc_evaluate(const bc_problem* problem,
                             int32_t first_point, int32_t n_points,
                             void* scratch, int64_t scratch_bytes,
                             bc_output* output);

#ifdef __cplusplus
}
#endif

#endif /* BERREMAN_H */
