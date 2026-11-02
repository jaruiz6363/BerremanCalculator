/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* bc_complex.h — complex scalar arithmetic on bc_complex.
 *
 * Header-only and BC_HD throughout: these are the innermost operations, they must inline, and they
 * must compile for the device unchanged. No std::complex: it is not usable in device code on every
 * toolchain, and the ABI already fixes the layout.
 */

#ifndef BC_COMPLEX_H
#define BC_COMPLEX_H

#include "berreman.h"
#include <math.h>

BC_HD static inline bc_complex bc_c(double re, double im)
{
    bc_complex z; z.re = re; z.im = im; return z;
}

BC_HD static inline bc_complex bc_add(bc_complex a, bc_complex b) { return bc_c(a.re + b.re, a.im + b.im); }
BC_HD static inline bc_complex bc_sub(bc_complex a, bc_complex b) { return bc_c(a.re - b.re, a.im - b.im); }
BC_HD static inline bc_complex bc_neg(bc_complex a) { return bc_c(-a.re, -a.im); }
BC_HD static inline bc_complex bc_conj(bc_complex a) { return bc_c(a.re, -a.im); }
BC_HD static inline bc_complex bc_scale(bc_complex a, double s) { return bc_c(a.re * s, a.im * s); }

BC_HD static inline bc_complex bc_mul(bc_complex a, bc_complex b)
{
    return bc_c(a.re * b.re - a.im * b.im, a.re * b.im + a.im * b.re);
}

/* Smith's algorithm: no overflow for |b| near the top of the range, unlike the textbook formula. */
BC_HD static inline bc_complex bc_div(bc_complex a, bc_complex b)
{
    if (fabs(b.re) >= fabs(b.im))
    {
        double r = b.im / b.re, den = b.re + b.im * r;
        return bc_c((a.re + a.im * r) / den, (a.im - a.re * r) / den);
    }
    double r = b.re / b.im, den = b.re * r + b.im;
    return bc_c((a.re * r + a.im) / den, (a.im * r - a.re) / den);
}

BC_HD static inline double bc_abs(bc_complex a) { return hypot(a.re, a.im); }

BC_HD static inline int bc_is_zero(bc_complex a) { return a.re == 0.0 && a.im == 0.0; }

/* Principal square root: Re >= 0, and the sign of Im follows the sign of the argument's Im. A
 * negative real argument with a signed-zero Im (-0.0) gives a negative-imaginary root, so callers
 * that need a particular branch choose it explicitly (bc_branch_forward). */
BC_HD static inline bc_complex bc_sqrt(bc_complex z)
{
    if (bc_is_zero(z)) return bc_c(0.0, 0.0);
    double m = bc_abs(z);
    if (z.re >= 0.0)
    {
        double t = sqrt(0.5 * (m + z.re));
        return bc_c(t, z.im / (2.0 * t));
    }
    double t = sqrt(0.5 * (m - z.re));
    return bc_c(fabs(z.im) / (2.0 * t), copysign(t, z.im));
}

/* The forward branch of a normal wavenumber (engine design, Section 2): Im q > 0, or Im q = 0 and
 * Re q >= 0. Exact comparisons: the branch is a property of the number, not a tolerance. */
BC_HD static inline bc_complex bc_branch_forward(bc_complex q)
{
    if (q.im < 0.0 || (q.im == 0.0 && q.re < 0.0)) return bc_neg(q);
    return q;
}

#endif /* BC_COMPLEX_H */
