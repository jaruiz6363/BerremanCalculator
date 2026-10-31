/* test_support.h — checks, random inputs and test-only numerics shared by the native tests.
 *
 * Nothing here is engine code, and nothing here may be used by the engine: the tests' own
 * eigen-solver exists precisely so that the engine's algebra is checked by something that shares
 * none of it. Test code may allocate and may use std::complex.
 */

#ifndef BC_TEST_SUPPORT_H
#define BC_TEST_SUPPORT_H

#include "berreman.h"

#include <complex>
#include <cmath>
#include <cstdio>
#include <cstdint>

using cplx = std::complex<double>;

static int g_failures = 0;
static int g_checks   = 0;

static inline cplx to_std(bc_complex z) { return cplx(z.re, z.im); }
static inline bc_complex to_bc(cplx z) { bc_complex r; r.re = z.real(); r.im = z.imag(); return r; }

static inline void check(bool ok, const char* what)
{
    ++g_checks;
    if (!ok) { ++g_failures; std::printf("  FAIL  %s\n", what); }
}

static inline void check_near(cplx got, cplx expected, double tol, const char* what)
{
    ++g_checks;
    const double err = std::abs(got - expected);
    if (!(err <= tol))
    {
        ++g_failures;
        std::printf("  FAIL  %-58s got (%+.15g, %+.15g) expected (%+.15g, %+.15g) |err| %.3g\n",
                    what, got.real(), got.imag(), expected.real(), expected.imag(), err);
    }
}

static inline void check_small(double value, double tol, const char* what)
{
    ++g_checks;
    if (!(std::fabs(value) <= tol))
    {
        ++g_failures;
        std::printf("  FAIL  %-58s %.3g exceeds %.3g\n", what, value, tol);
    }
}

static inline int report(const char* suite)
{
    std::printf("%s: %d checks, %d failures\n", suite, g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}

/* Deterministic random numbers, so a failure reproduces. */
struct Rng
{
    uint64_t s;
    explicit Rng(uint64_t seed) : s(seed) {}
    double uniform(double lo, double hi)
    {
        s = s * 6364136223846793005ULL + 1442695040888963407ULL;
        return lo + (hi - lo) * ((s >> 11) * (1.0 / 9007199254740992.0));
    }
    cplx complex(double lo, double hi) { return cplx(uniform(lo, hi), uniform(lo, hi)); }
};

/* ---- test-only eigen-solver for a 4×4 complex matrix -------------------------------------
 *
 * Characteristic polynomial by Faddeev–LeVerrier, roots by Durand–Kerner polished with Newton,
 * eigenvectors by inverse iteration. Slow and simple, and independent of anything the engine does.
 */

static inline void mat4_mul(const cplx* A, const cplx* B, cplx* C)
{
    for (int i = 0; i < 4; ++i)
        for (int j = 0; j < 4; ++j)
        {
            cplx s = 0;
            for (int k = 0; k < 4; ++k) s += A[i * 4 + k] * B[k * 4 + j];
            C[i * 4 + j] = s;
        }
}

/* det(qI − A) = q⁴ + c[3] q³ + c[2] q² + c[1] q + c[0]. */
static inline void charpoly4(const cplx* A, cplx* c)
{
    cplx Mk[16], AM[16];
    for (int i = 0; i < 16; ++i) Mk[i] = (i % 5 == 0) ? 1.0 : 0.0;   /* M1 = I */
    for (int k = 1; k <= 4; ++k)
    {
        mat4_mul(A, Mk, AM);
        cplx tr = 0;
        for (int i = 0; i < 4; ++i) tr += AM[i * 5];
        const cplx ck = -tr / double(k);
        c[4 - k] = ck;
        for (int i = 0; i < 16; ++i) Mk[i] = AM[i] + ((i % 5 == 0) ? ck : 0.0);
    }
}

static inline cplx poly4(const cplx* c, cplx q) { return (((q + c[3]) * q + c[2]) * q + c[1]) * q + c[0]; }
static inline cplx dpoly4(const cplx* c, cplx q) { return ((4.0 * q + 3.0 * c[3]) * q + 2.0 * c[2]) * q + c[1]; }

static inline void roots4(const cplx* c, cplx* z)
{
    for (int i = 0; i < 4; ++i) z[i] = std::pow(cplx(0.4, 0.9), i);
    for (int it = 0; it < 2000; ++it)
        for (int i = 0; i < 4; ++i)
        {
            cplx den = 1;
            for (int j = 0; j < 4; ++j) if (j != i) den *= (z[i] - z[j]);
            if (den != cplx(0)) z[i] -= poly4(c, z[i]) / den;
        }
    for (int i = 0; i < 4; ++i)
        for (int it = 0; it < 5; ++it)
        {
            const cplx d = dpoly4(c, z[i]);
            if (d != cplx(0)) z[i] -= poly4(c, z[i]) / d;
        }
}

/* Solves (A − qI) x = b by Gaussian elimination with partial pivoting; a zero pivot is replaced
 * by a tiny one, which is what inverse iteration wants. */
static inline void shifted_solve4(const cplx* A, cplx q, const cplx* b, cplx* x)
{
    cplx T[16], y[4];
    for (int i = 0; i < 16; ++i) T[i] = A[i] - ((i % 5 == 0) ? q : 0.0);
    for (int i = 0; i < 4; ++i) y[i] = b[i];
    for (int k = 0; k < 4; ++k)
    {
        int p = k;
        for (int i = k + 1; i < 4; ++i) if (std::abs(T[i * 4 + k]) > std::abs(T[p * 4 + k])) p = i;
        for (int j = 0; j < 4; ++j) std::swap(T[k * 4 + j], T[p * 4 + j]);
        std::swap(y[k], y[p]);
        if (T[k * 4 + k] == cplx(0)) T[k * 4 + k] = 1e-300;
        for (int i = k + 1; i < 4; ++i)
        {
            const cplx f = T[i * 4 + k] / T[k * 4 + k];
            for (int j = k; j < 4; ++j) T[i * 4 + j] -= f * T[k * 4 + j];
            y[i] -= f * y[k];
        }
    }
    for (int k = 3; k >= 0; --k)
    {
        cplx s = y[k];
        for (int j = k + 1; j < 4; ++j) s -= T[k * 4 + j] * x[j];
        x[k] = s / T[k * 4 + k];
    }
}

static inline void normalise4(cplx* x)
{
    double n = 0;
    for (int i = 0; i < 4; ++i) n += std::norm(x[i]);
    n = std::sqrt(n);
    for (int i = 0; i < 4; ++i) x[i] /= n;
}

static inline void eigenvector4(const cplx* A, cplx q, cplx* x)
{
    cplx b[4] = {1.0, cplx(0.3, 0.7), cplx(-0.5, 0.2), cplx(0.8, -0.6)};
    for (int it = 0; it < 3; ++it)
    {
        shifted_solve4(A, q, b, x);
        normalise4(x);
        for (int i = 0; i < 4; ++i) b[i] = x[i];
    }
}

#endif /* BC_TEST_SUPPORT_H */
