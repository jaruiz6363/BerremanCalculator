/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* test_skeleton.cpp — the calls exist, refuse bad input, and write every output asked for.
 *
 * No test framework, as in ThinFilmDesigner: a dependency that buys a nicer assertion message is
 * not worth the build friction. Failures print what was expected and what came out.
 */

#include "berreman.h"

#include <cmath>
#include <cstdio>
#include <cstring>

static int g_failures = 0;
static int g_checks   = 0;

static void check(bool ok, const char* what)
{
    ++g_checks;
    if (!ok)
    {
        ++g_failures;
        std::printf("  FAIL  %s\n", what);
    }
}

static void check_status(bc_status got, bc_status expected, const char* what)
{
    ++g_checks;
    if (got != expected)
    {
        ++g_failures;
        std::printf("  FAIL  %-60s got status %d, expected %d\n", what, (int)got, (int)expected);
    }
}

/* A valid two-layer problem over caller-owned arrays, as C# will build it. */
struct Fixture
{
    bc_complex M[2 * 4 * 36];
    double wavelength[2] = {550.0, 633.0};
    double thickness[2]  = {100.0, 0.0};
    int32_t point_spectral[3] = {0, 1, 1};
    double point_xi[3] = {0.0, 0.5, 0.9};
    double depths[2] = {0.0, 50.0};
    bc_problem p{};

    Fixture()
    {
        std::memset(M, 0, sizeof M);
        for (int s = 0; s < 2; ++s)
            for (int m = 0; m < 4; ++m)
                for (int i = 0; i < 6; ++i)
                    M[(s * 4 + m) * 36 + 7 * i].re = 1.0;   /* vacuum everywhere */

        p.n_layers = 2; p.n_spectral = 2; p.n_points = 3; p.n_depths = 2;
        p.M = M; p.wavelength = wavelength; p.thickness = thickness;
        p.point_spectral = point_spectral; p.point_xi = point_xi; p.field_depths = depths;
    }
};

static void TheAbiVersionIsTheHeaders()
{
    check(bc_abi_version() == BC_ABI_VERSION, "bc_abi_version() == BC_ABI_VERSION");
}

static void TheLayoutIsWhatCSharpMirrors()
{
    check(sizeof(bc_complex) == 16, "sizeof(bc_complex) == 16");
    check(sizeof(bc_point_diagnostics) == 40, "sizeof(bc_point_diagnostics) == 40");
    check(sizeof(bc_problem) == 16 + 6 * sizeof(void*), "sizeof(bc_problem)");
    check(sizeof(bc_output) == 5 * sizeof(void*), "sizeof(bc_output)");
}

static void AValidProblemPlansAndSizesScratchFromTheLayerCount()
{
    Fixture f;
    int64_t bytes = -1;
    check_status(bc_plan(&f.p, &bytes), BC_OK, "plan a valid problem");
    check(bytes == 3 * 16 * 16, "scratch = (n_layers + 1) * 16 complex");

    f.p.n_layers = 0;
    check_status(bc_plan(&f.p, &bytes), BC_OK, "plan a bare interface");
    check(bytes == 16 * 16, "scratch for a bare interface");
}

static void PlanRefusesWhatTheEngineCouldNotUse()
{
    int64_t bytes = 0;
    check_status(bc_plan(nullptr, &bytes), BC_INVALID_ARGUMENT, "null problem");

    { Fixture f; check_status(bc_plan(&f.p, nullptr), BC_INVALID_ARGUMENT, "null scratch_bytes"); }
    { Fixture f; f.p.n_spectral = 0;         check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "no spectral index"); }
    { Fixture f; f.p.n_layers = -1;          check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "negative layer count"); }
    { Fixture f; f.p.M = nullptr;            check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "no M"); }
    { Fixture f; f.p.thickness = nullptr;    check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "layers without thicknesses"); }
    { Fixture f; f.wavelength[1] = 0.0;      check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "zero wavelength"); }
    { Fixture f; f.thickness[0] = -1.0;      check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "negative thickness"); }
    { Fixture f; f.thickness[0] = NAN;       check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "NaN thickness"); }
    { Fixture f; f.point_spectral[2] = 2;    check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "spectral index out of range"); }
    { Fixture f; f.point_xi[0] = INFINITY;   check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "infinite xi"); }
    { Fixture f; f.p.field_depths = nullptr; check_status(bc_plan(&f.p, &bytes), BC_INVALID_ARGUMENT, "depths counted but absent"); }
}

/* Every output is written. The fixture is vacuum everywhere, so the field at each depth is the
 * incident wave itself, of unit amplitude, with the phase e^{i k0 q z}; nothing is reflected. */
static void EvaluateWritesEveryOutput()
{
    Fixture f;
    int64_t bytes = 0;
    bc_plan(&f.p, &bytes);

    unsigned char scratch[3 * 16 * 16];
    bc_complex jones[3 * 8];
    double power[3 * 6];
    double absorption[3 * 2 * 2];
    bc_complex fields[3 * 2 * 2 * 6];
    bc_point_diagnostics diagnostics[3];
    std::memset(fields, 0xA5, sizeof fields);

    bc_output out{jones, power, absorption, fields, diagnostics};
    check_status(bc_evaluate(&f.p, 0, 3, scratch, bytes, &out), BC_OK, "evaluate succeeds, fields included");

    const double PI = 3.14159265358979323846;
    double worst = 0.0;
    for (int pt = 0; pt < 3; ++pt)
    {
        const double k0 = 2 * PI / f.wavelength[f.point_spectral[pt]];
        const double q = std::sqrt(1.0 - f.point_xi[pt] * f.point_xi[pt]);
        for (int j = 0; j < 2; ++j)
        {
            const bc_complex* a = fields + ((pt * 2 + j) * 2 + 0) * 6;   /* depth 0 */
            const bc_complex* b = fields + ((pt * 2 + j) * 2 + 1) * 6;   /* depth 50 */
            const double c = std::cos(k0 * q * 50.0), sn = std::sin(k0 * q * 50.0);
            double e2 = 0.0;
            for (int k = 0; k < 6; ++k)
            {
                const double re = a[k].re * c - a[k].im * sn, im = a[k].re * sn + a[k].im * c;
                worst = std::fmax(worst, std::hypot(b[k].re - re, b[k].im - im));
                if (k < 3) e2 += a[k].re * a[k].re + a[k].im * a[k].im;
            }
            worst = std::fmax(worst, std::fabs(std::sqrt(e2) - 1.0));
        }
    }
    check(worst < 1e-12, "in vacuum the field is the incident wave, |E| = 1, with phase e^{i k0 q z}");

    check_status(bc_evaluate(&f.p, 0, 3, scratch, bytes - 1, &out), BC_SCRATCH_TOO_SMALL,
                 "less scratch than planned");
    check_status(bc_evaluate(&f.p, 2, 2, scratch, bytes, &out), BC_INVALID_ARGUMENT,
                 "point range past the end");
}

int main()
{
    TheAbiVersionIsTheHeaders();
    TheLayoutIsWhatCSharpMirrors();
    AValidProblemPlansAndSizesScratchFromTheLayerCount();
    PlanRefusesWhatTheEngineCouldNotUse();
    EvaluateWritesEveryOutput();

    std::printf("%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
