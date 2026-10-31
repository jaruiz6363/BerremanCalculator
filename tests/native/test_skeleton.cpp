/* test_skeleton.cpp — the calls exist, refuse bad input, and fail loudly where nothing is built.
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
    check(bytes == 3 * 12 * 16, "scratch = (n_layers + 1) * 12 complex");

    f.p.n_layers = 0;
    check_status(bc_plan(&f.p, &bytes), BC_OK, "plan a bare interface");
    check(bytes == 12 * 16, "scratch for a bare interface");
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

/* Rule 5. Every output buffer is filled with a sentinel; after the call, every byte must still be
 * the sentinel. Plausible zeros would be the silent failure this rule exists to prevent. */
static void EvaluateIsNotImplementedAndTouchesNothing()
{
    Fixture f;
    int64_t bytes = 0;
    bc_plan(&f.p, &bytes);

    unsigned char scratch[3 * 12 * 16];
    bc_complex jones[3 * 8];
    double power[3 * 6];
    double absorption[3 * 2 * 2];
    bc_complex fields[3 * 2 * 2 * 6];
    bc_point_diagnostics diagnostics[3];

    std::memset(jones, 0xA5, sizeof jones);
    std::memset(power, 0xA5, sizeof power);
    std::memset(absorption, 0xA5, sizeof absorption);
    std::memset(fields, 0xA5, sizeof fields);
    std::memset(diagnostics, 0xA5, sizeof diagnostics);

    bc_output out{jones, power, absorption, fields, diagnostics};
    check_status(bc_evaluate(&f.p, 0, 3, scratch, bytes, &out), BC_NOT_IMPLEMENTED,
                 "evaluate reports NOT_IMPLEMENTED");

    auto untouched = [](const void* buf, size_t n) {
        const unsigned char* b = static_cast<const unsigned char*>(buf);
        for (size_t i = 0; i < n; ++i) if (b[i] != 0xA5) return false;
        return true;
    };
    check(untouched(jones, sizeof jones), "jones untouched");
    check(untouched(power, sizeof power), "power untouched");
    check(untouched(absorption, sizeof absorption), "layer_absorption untouched");
    check(untouched(fields, sizeof fields), "fields untouched");
    check(untouched(diagnostics, sizeof diagnostics), "diagnostics untouched");

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
    EvaluateIsNotImplementedAndTouchesNothing();

    std::printf("%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
