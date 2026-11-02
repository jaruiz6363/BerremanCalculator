/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* us_berreman.cpp — an OpticStudio user-defined surface (UDS) whose thin film is evaluated by the
 * BerremanCalculator engine, ray by ray, for polarization ray tracing.
 *
 * WHAT IT DOES. The surface is a standard conic (curvature, conic constant). On it lies a thin
 * anisotropic film, which OpticStudio's coatings cannot describe: its effect on a ray depends on
 * the ray's direction relative to the film's own axes, and it couples s and p. For every real ray
 * the DLL
 *   1. finds the intercept, the normal and the refracted direction (Snell's law for the media
 *      either side: the film is thin and does not move the ray);
 *   2. builds the ray's local frame: z along the normal, into the direction the ray goes; x the
 *      ray's tangential direction (the plane of incidence is x-z); y = z × x, which is s;
 *   3. writes the film's dielectric tensor in that frame, from its orientation, which is fixed to
 *      the SURFACE (see "Orientation");
 *   4. asks the engine for the 2×2 transmission Jones matrix between the media either side;
 *   5. applies it to the ray's electric field and returns the field to OpticStudio (did_polar = 2).
 * The full Jones matrix is used, s-p coupling included; that is why the field is set directly
 * rather than through the s and p coefficients OpticStudio also accepts, which are diagonal.
 *
 * THE FILMS (parameter 0 chooses):
 *   0  O-plate: one uniaxial layer, optic axis at a tilt from the normal and an azimuth.
 *   1  Cholesteric: a uniaxial layer whose director lies in the plane of the film and turns about
 *      the normal through the thickness (pitch, handedness, starting azimuth); evaluated as uniform
 *      sublayers at each one's midpoint angle, as the BerremanCalculator program does.
 *
 * ORIENTATION. A film's axes are fixed to the surface, not to the ray: x_s is the global X axis
 * projected onto the surface's tangent plane (global Y if X is nearly normal to it), z_s the normal
 * pointing towards +z at the vertex, and y_s = z_s × x_s. Azimuths are measured in the tangent plane
 * from x_s towards y_s. A ray in either direction then sees the same physical film.
 *
 * LAYER ORDER (parameter 8). Which face of the film its first layer is on: 0 the front face,
 * towards -z (the default), 1 the back face, towards +z. It is set by the user, never inferred from
 * the media either side. A cholesteric's starting azimuth is on that face; its handedness is the
 * film's own and does not change. An O-plate is one uniform layer, and has no order.
 *
 * UNITS. Film thickness and pitch in micrometres, whatever the lens units; angles in degrees.
 *
 * NOT HANDLED: a mirror surface (the ray is reflected, and its polarization is left to
 * OpticStudio), and absorbing media either side (OpticStudio passes real indices).
 *
 * THREADS. OpticStudio calls the DLL from several threads at once. Everything per ray is on the
 * stack or in thread_local buffers; nothing static is written after loading.
 */

#include "uds_abi.h"
#include "berreman.h"

#include <cmath>
#include <cstring>
#include <vector>

#if defined(_WIN32)
#  define UDS_EXPORT __declspec(dllexport)
#else
#  define UDS_EXPORT __attribute__((visibility("default")))
#endif

extern "C" UDS_EXPORT int UserDefinedSurface5(USER_DATA* UD, FIXED_DATA5* FD);

namespace
{
const double kPi = 3.14159265358979323846;
const double kDeg = kPi / 180.0;

struct V3 { double x, y, z; };
V3 operator+(V3 a, V3 b) { return {a.x + b.x, a.y + b.y, a.z + b.z}; }
V3 operator-(V3 a, V3 b) { return {a.x - b.x, a.y - b.y, a.z - b.z}; }
V3 operator*(double s, V3 a) { return {s * a.x, s * a.y, s * a.z}; }
double Dot(V3 a, V3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
V3 Cross(V3 a, V3 b) { return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x}; }
V3 Unit(V3 a) { const double n = std::sqrt(Dot(a, a)); return {a.x / n, a.y / n, a.z / n}; }

struct C { double re, im; };
C Mul(C a, C b) { return {a.re * b.re - a.im * b.im, a.re * b.im + a.im * b.re}; }
C Add(C a, C b) { return {a.re + b.re, a.im + b.im}; }
C Scale(double s, C a) { return {s * a.re, s * a.im}; }

/* ---- the parameters ------------------------------------------------------------------------- */

enum { kModel = 0, kThickness = 1, kNo = 2, kNe = 3, kTilt = 4, kAzimuth = 5,
       kPitch = 4, kHand = 5, kStart = 6, kSublayers = 7, kOrder = 8 };

/* ---- geometry: the conic, its normal, Snell's law -------------------------------------------- */

/* The sag of the conic at (x, y), and the other root (hyper-hemispheric). */
int Sag(double cv, double k, double x, double y, double* sag1, double* sag2)
{
    *sag1 = *sag2 = 0.0;
    if (cv == 0.0) return 0;
    const double r2 = x * x + y * y;
    const double a = 1.0 - (1.0 + k) * cv * cv * r2;
    if (a < 0.0) return -1;
    *sag1 = cv * r2 / (1.0 + std::sqrt(a));
    *sag2 = (a != 1.0) ? cv * r2 / (1.0 - std::sqrt(a)) : *sag1;
    return 0;
}

/* The ray from p (on the vertex tangent plane) along d to the conic
 *   F = cv (x² + y²) - 2 z + (1 + k) cv z² = 0,
 * the nearer root of A t² + 2 B t + C = 0. Returns false when the ray misses. */
bool Intercept(double cv, double k, V3 p, V3 d, double* t)
{
    if (cv == 0.0) { *t = 0.0; return true; }
    const double A = cv * (d.x * d.x + d.y * d.y) + (1.0 + k) * cv * d.z * d.z;
    const double B = cv * (p.x * d.x + p.y * d.y) - d.z + (1.0 + k) * cv * p.z * d.z;
    const double Cc = cv * (p.x * p.x + p.y * p.y) - 2.0 * p.z + (1.0 + k) * cv * p.z * p.z;
    const double disc = B * B - A * Cc;
    if (disc < 0.0) return false;
    const double q = B + (B >= 0.0 ? std::sqrt(disc) : -std::sqrt(disc));   /* the stable form */
    if (q == 0.0) { *t = 0.0; return true; }
    *t = -Cc / q;
    return true;
}

/* The normal OpticStudio expects: the gradient of F/2, which is (0, 0, -1) at the vertex. */
V3 Normal(double cv, double k, V3 p)
{
    return Unit({cv * p.x, cv * p.y, -1.0 + (1.0 + k) * cv * p.z});
}

/* Snell's law in vector form; false on total internal reflection. */
bool Refract(double n1, double n2, V3 d, V3 normal, V3* out)
{
    V3 nn = Dot(d, normal) >= 0.0 ? normal : -1.0 * normal;   /* along the ray */
    const double mu = n1 / n2, c = Dot(d, nn);
    const double rad = 1.0 - mu * mu * (1.0 - c * c);
    if (rad < 0.0) return false;
    *out = Unit(mu * d + (std::sqrt(rad) - mu * c) * nn);
    return true;
}

/* ---- the film, as the engine sees it --------------------------------------------------------- */

/* Per-thread buffers, sized by the film and reused from ray to ray. */
struct Work
{
    std::vector<bc_complex> M;
    std::vector<double> thickness;
    std::vector<unsigned char> scratch;
};
thread_local Work g_work;

/* A uniaxial ε = no² I + (ne² - no²) v vᵀ, v a unit vector in the ray's local frame, into the
 * 6×6 M (μ = 1, no magnetoelectric terms). */
void Uniaxial(double no, double ne, const double* v, bc_complex* M)
{
    for (int k = 0; k < 36; ++k) M[k] = bc_complex{0.0, 0.0};
    for (int i = 0; i < 3; ++i)
    {
        for (int j = 0; j < 3; ++j)
            M[i * 6 + j].re = (ne * ne - no * no) * v[i] * v[j] + (i == j ? no * no : 0.0);
        M[(3 + i) * 6 + 3 + i].re = 1.0;
    }
}

void Isotropic(double n, bc_complex* M)
{
    const double v[3] = {0.0, 0.0, 1.0};
    Uniaxial(n, n, v, M);
}

/* The ray's local frame (x, y, z) and the surface's own (xs, ys, zs), all in global coordinates. */
struct Frames { V3 x, y, z, xs, ys, zs; };

double Local(V3 v, const Frames& f, int c) { return c == 0 ? Dot(v, f.x) : c == 1 ? Dot(v, f.y) : Dot(v, f.z); }

/* The 2×2 transmission Jones matrix of the film between media n1 and n2, for the ray at
 * xi = n1 sin(incidence), in the engine's order t_pp, t_ps, t_sp, t_ss (first subscript incident).
 * Returns false if the engine refuses the point. */
bool Transmission(const FIXED_DATA5* FD, double n1, double n2, double xi, const Frames& f, bool film, bc_complex* t)
{
    const int model = (int)FD->param[kModel];
    const double d = FD->param[kThickness], no = FD->param[kNo], ne = FD->param[kNe];
    int layers = 1;
    if (model == 1)
    {
        const double pitch = FD->param[kPitch];
        int n = (int)FD->param[kSublayers];
        if (n <= 0) n = (int)std::ceil(64.0 * d / pitch);
        layers = n < 1 ? 1 : n;
    }
    if (!(d > 0.0) || !film) layers = 0;

    Work& w = g_work;
    w.M.resize((size_t)(layers + 2) * 36);
    w.thickness.resize(layers > 0 ? (size_t)layers : 1);
    Isotropic(n1, w.M.data());
    Isotropic(n2, w.M.data() + (size_t)(layers + 1) * 36);

    /* Depth runs along the ray (z); the film's own depth along zs. */
    const bool forward = Dot(f.z, f.zs) >= 0.0;
    for (int i = 0; i < layers; ++i)
    {
        V3 axis;
        if (model == 1)
        {
            const double h = d / layers;
            const double along = (i + 0.5) * h;                 /* midpoint, along the ray */
            const double depth = forward ? along : d - along;   /* from the front face, along +zs */
            /* The starting azimuth is on the first layer's face; the twist keeps the film's own hand. */
            const double first = FD->param[kOrder] == 1.0 ? d : 0.0;
            const double hand = FD->param[kHand] < 0.0 ? -1.0 : 1.0;
            const double phi = FD->param[kStart] * kDeg + hand * 2.0 * kPi * (depth - first) / FD->param[kPitch];
            axis = std::cos(phi) * f.xs + std::sin(phi) * f.ys;
            w.thickness[(size_t)i] = h;
        }
        else
        {
            const double tilt = FD->param[kTilt] * kDeg, az = FD->param[kAzimuth] * kDeg;
            axis = std::sin(tilt) * std::cos(az) * f.xs + std::sin(tilt) * std::sin(az) * f.ys + std::cos(tilt) * f.zs;
            w.thickness[(size_t)i] = d;
        }
        const double v[3] = {Local(axis, f, 0), Local(axis, f, 1), Local(axis, f, 2)};
        Uniaxial(no, ne, v, w.M.data() + (size_t)(i + 1) * 36);
    }

    double wavelength = FD->wavelength;
    int32_t spectral = 0;
    bc_problem p{};
    p.n_layers = layers; p.n_spectral = 1; p.n_points = 1; p.n_depths = 0;
    p.M = w.M.data(); p.wavelength = &wavelength; p.thickness = layers > 0 ? w.thickness.data() : nullptr;
    p.point_spectral = &spectral; p.point_xi = &xi; p.field_depths = nullptr;

    int64_t bytes = 0;
    if (bc_plan(&p, &bytes) != BC_OK) return false;
    if (w.scratch.size() < (size_t)bytes) w.scratch.resize((size_t)bytes);
    bc_complex jones[8];
    bc_output out{};
    out.jones = jones;
    if (bc_evaluate(&p, 0, 1, w.scratch.data(), bytes, &out) != BC_OK) return false;
    for (int k = 0; k < 4; ++k) t[k] = jones[4 + k];
    return true;
}

/* The surface's own axes at a point with normal n (OpticStudio's sign): zs towards +z at the
 * vertex, xs the global X projected onto the tangent plane. */
void SurfaceAxes(V3 normal, Frames* f)
{
    f->zs = -1.0 * normal;
    V3 x = V3{1, 0, 0} - Dot(V3{1, 0, 0}, f->zs) * f->zs;
    if (Dot(x, x) < 1e-12) x = V3{0, 1, 0} - Dot(V3{0, 1, 0}, f->zs) * f->zs;
    f->xs = Unit(x);
    f->ys = Cross(f->zs, f->xs);
}

/* The ray's local frame: z along the normal, in the direction the ray goes; x its tangential
 * direction (xs at normal incidence); y = z × x, the s direction. Returns sin(incidence). */
double RayFrame(V3 d, Frames* f)
{
    f->z = Dot(d, f->zs) >= 0.0 ? f->zs : -1.0 * f->zs;
    V3 tang = d - Dot(d, f->z) * f->z;
    const double s = std::sqrt(Dot(tang, tang));
    f->x = s > 1e-14 ? (1.0 / s) * tang : Unit(f->xs - Dot(f->xs, f->z) * f->z);
    f->y = Cross(f->z, f->x);
    return s;
}

C Div(C a, C b) { const double d = b.re * b.re + b.im * b.im; return {(a.re * b.re + a.im * b.im) / d, (a.im * b.re - a.re * b.im) / d}; }

/* Replaces OpticStudio's bare interface by the film.
 *
 * WHAT OPTICSTUDIO DOES WITH THE FIELD (found by comparison with OpticStudio 2022 R2; README.md).
 * It hands the DLL the incident field, along the incident ray's p and s. After the DLL returns,
 * it still applies its own uncoated interface to whatever field the DLL set: it takes the field's
 * components along the incident p and s, multiplies them by its Fresnel t_p and t_s, and puts
 * them along the refracted ray's p and s, in its own normalisation.
 * So the DLL returns, along the incident p and s, the film's transmitted amplitudes divided by the
 * bare interface's, t0 (the engine's with no film, the same Fresnel coefficients):
 *     [Ep'; Es'] = diag(1/t0_pp, 1/t0_ss)  t  [Ep; Es].
 * OpticStudio's own step then multiplies by t0 again and leaves t [Ep; Es] along the refracted p
 * and s, so with no film the result is OpticStudio's own exactly. p = s × k for every wave, the
 * BerremanCalculator convention (docs/contract.md). */
void ApplyFilm(FIXED_DATA5* FD, const bc_complex* t, const bc_complex* t0, const Frames& f, V3 din)
{
    const V3 s = f.y, pin = Cross(s, din);
    const C ex{FD->Exr, FD->Exi}, ey{FD->Eyr, FD->Eyi}, ez{FD->Ezr, FD->Ezi};
    auto Project = [&](V3 u) { return Add(Add(Scale(u.x, ex), Scale(u.y, ey)), Scale(u.z, ez)); };
    const C Ep = Project(pin), Es = Project(s);
    const C tpp{t[0].re, t[0].im}, tps{t[1].re, t[1].im}, tsp{t[2].re, t[2].im}, tss{t[3].re, t[3].im};
    /* p out: from p in (t_pp) and s in (t_sp); s out: from p in (t_ps) and s in (t_ss); each over
       the bare interface's coefficient, which OpticStudio applies afterwards. */
    const C Ep2 = Div(Add(Mul(tpp, Ep), Mul(tsp, Es)), C{t0[0].re, t0[0].im});
    const C Es2 = Div(Add(Mul(tps, Ep), Mul(tss, Es)), C{t0[3].re, t0[3].im});
    const C ox = Add(Scale(pin.x, Ep2), Scale(s.x, Es2));
    const C oy = Add(Scale(pin.y, Ep2), Scale(s.y, Es2));
    const C oz = Add(Scale(pin.z, Ep2), Scale(s.z, Es2));
    FD->Exr = ox.re; FD->Exi = ox.im; FD->Eyr = oy.re; FD->Eyi = oy.im; FD->Ezr = oz.re; FD->Ezi = oz.im;
    FD->did_polar = 2;
}

/* A name into the 20-character string OpticStudio reads, cut short if it is longer. */
void Name(USER_DATA* UD, const char* s)
{
    size_t i = 0;
    for (; i + 1 < sizeof UD->string && s[i] != '\0'; ++i) UD->string[i] = s[i];
    UD->string[i] = '\0';
}
} // namespace

/* The entry point. FD->type says what OpticStudio wants; 0 is returned when the answer is
 * meaningful, the surface number when the ray misses it, minus that on total internal reflection. */
extern "C" UDS_EXPORT int UserDefinedSurface5(USER_DATA* UD, FIXED_DATA5* FD)
{
    switch (FD->type)
    {
    case 0:   /* the surface's name, symmetry and gradient flags */
        if (FD->numb == 0) Name(UD, "Berreman film");
        else UD->string[0] = '\0';   /* not rotationally symmetric (the film has axes); not a gradient */
        return 0;

    case 1:   /* the parameter columns' names */
    {
        const int model = (int)FD->param[kModel];
        const char* oplate[] = {"Model 0=O 1=Chol", "Film um", "n ordinary", "n extraord", "Axis tilt", "Axis azim"};
        const char* chol[] = {"Model 0=O 1=Chol", "Film um", "n ordinary", "n extraord", "Pitch um", "Hand +1/-1", "Start azim", "Sublayers",
                              "1st layer 0=-z 1=+z"};
        UD->string[0] = '\0';
        if (model == 1 && FD->numb >= 0 && FD->numb < 9) Name(UD, chol[FD->numb]);
        else if (model != 1 && FD->numb >= 0 && FD->numb < 6) Name(UD, oplate[FD->numb]);
        return 0;
    }

    case 2:   /* the extra data columns: none */
        UD->string[0] = '\0';
        return 0;

    case 3:   /* the sag */
        return Sag(FD->cv, FD->k, UD->x, UD->y, &UD->sag1, &UD->sag2);

    case 4:   /* a paraxial trace. The surface is its vertex plane, with the conic's power
                 phi = (n2 - n1) cv; in x and in y the ray's slope u = l/n obeys n2 u' = n1 u - h phi.
                 The thin film has no power. */
    {
        UD->ln = 0.0; UD->mn = 0.0; UD->nn = -1.0;
        if (UD->n == 0.0) return 0;
        const double phi = (FD->n2 - FD->n1) * FD->cv;
        const V3 slope{(FD->n1 * (UD->l / UD->n) - phi * UD->x) / FD->n2,
                       (FD->n1 * (UD->m / UD->n) - phi * UD->y) / FD->n2, 1.0};
        const V3 d = Unit(slope);
        UD->l = d.x; UD->m = d.y; UD->n = d.z;
        return 0;
    }

    case 5:   /* a real ray: intercept, normal, refraction, and the film's effect on the field */
    {
        const V3 p{UD->x, UD->y, UD->z}, din{UD->l, UD->m, UD->n};
        double t;
        if (!Intercept(FD->cv, FD->k, p, din, &t)) return FD->surf;
        const V3 hit = p + t * din;
        UD->x = hit.x; UD->y = hit.y; UD->z = hit.z;
        UD->path = t;
        const V3 normal = Normal(FD->cv, FD->k, hit);
        UD->ln = normal.x; UD->mn = normal.y; UD->nn = normal.z;

        if (FD->is_a_mirror)
        {
            const V3 r = din - 2.0 * Dot(din, normal) * normal;
            UD->l = r.x; UD->m = r.y; UD->n = r.z;
            return 0;
        }
        const double n1 = std::fabs(FD->n1), n2 = std::fabs(FD->n2);
        V3 dout;
        if (!Refract(n1, n2, din, normal, &dout)) return -FD->surf;
        UD->l = dout.x; UD->m = dout.y; UD->n = dout.z;

        if (FD->did_polar == 0)
        {
            Frames f;
            SurfaceAxes(normal, &f);
            const double sinI = RayFrame(din, &f);
            bc_complex jt[4], j0[4];
            if (!Transmission(FD, n1, n2, n1 * sinI, f, true, jt)) return -1;
            if (!Transmission(FD, n1, n2, n1 * sinI, f, false, j0)) return -1;
            ApplyFilm(FD, jt, j0, f, din);
        }
        return 0;
    }

    case 6:   /* gradient index only */
        UD->index = FD->n2;
        UD->dndx = UD->dndy = UD->dndz = 0.0;
        return 0;

    case 7:   /* the values a new surface starts with: a 2 µm O-plate, axis 45° tilted */
        for (int i = 0; i <= 8; ++i) FD->param[i] = 0.0;
        FD->param[kThickness] = 2.0;
        FD->param[kNo] = 1.5;
        FD->param[kNe] = 1.6;
        FD->param[kTilt] = 45.0;
        return 0;

    default:  /* 8, 9 (load and unload) and 10 (scaling) need nothing here */
        return 0;
    }
}
