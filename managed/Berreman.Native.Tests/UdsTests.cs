// UdsTests.cs — the OpticStudio user-defined surface example (examples/zemax-uds/us_berreman.cpp),
// called as OpticStudio calls it, against the BerremanCalculator API.
//
// THE REFERENCE IS INDEPENDENT OF THE DLL'S OWN FRAME CODE. The DLL builds each ray's local frame,
// writes the film's tensor in it and applies the Jones matrix to the ray's field. The reference is
// a stack file evaluated by the C# API: the film oriented by Euler angles, a cholesteric by the
// file's twist, the plane of incidence by the sample azimuth, and the field rebuilt in the test
// from the API's Jones matrix. They share only the engine.
//   1. No film: the outgoing field is the Fresnel transmission, in closed form.
//   2. An O-plate at oblique incidence, several planes of incidence, both incident polarisations.
//   3. The same O-plate, the ray going the other way (-z): the film is fixed to the surface.
//   4. A cholesteric, right- and left-handed, inside and outside its reflection band.
//   5. Geometry: the intercept and normal on a sphere.

using System.Numerics;
using System.Runtime.InteropServices;
using Berreman.Api;

namespace Berreman.Native.Tests;

internal static unsafe class UdsTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserData
    {
        public double x, y, z, l, m, n, ln, mn, nn, path, sag1, sag2, index, dndx, dndy, dndz, rel_surf_tran;
        public double r1, r2, r3, r4;
        public fixed byte str[20];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FixedData5
    {
        public int type, numb, surf, wave, unit, serial, is_a_mirror, is_in_mirror_space, is_air;
        public fixed int ireserved[100];
        public int did_polar, max_parameter, max_extradata;
        public double Exr, Exi, Eyr, Eyi, Ezr, Ezi, Ewr, Ewi;
        public fixed double dbreserved[100];
        public double wavelength, pwavelength, n1, n2, cv, thic, sdia, k;
        public fixed double param[256];
        public fixed byte glass[200];
        public fixed byte comment[200];
        public int datasurf_numvalues;
        public fixed double datasurf_values[4801];
    }

    [DllImport("US_BERREMAN")]
    private static extern int UserDefinedSurface5(ref UserData ud, FixedData5* fd);

    private sealed record Film(int Model, double Thickness, double No, double Ne, double A, double B, double C = 0, int Sublayers = 0);

    /// <summary>One ray through the DLL: direction d at the vertex, incoming field E; returns the
    /// outgoing field and direction.</summary>
    private static (Complex[] E, double[] Dir, int Status) Trace(Film f, double n1, double n2, double um, double[] d, Complex[] e, double cv = 0, double x = 0, double y = 0)
    {
        var fd = (FixedData5*)NativeMemory.AllocZeroed((nuint)sizeof(FixedData5));
        try
        {
            fd->type = 5; fd->surf = 3; fd->wavelength = um; fd->n1 = n1; fd->n2 = n2; fd->cv = cv;
            fd->param[0] = f.Model; fd->param[1] = f.Thickness; fd->param[2] = f.No; fd->param[3] = f.Ne;
            fd->param[4] = f.A; fd->param[5] = f.B; fd->param[6] = f.C; fd->param[7] = f.Sublayers;
            fd->Exr = e[0].Real; fd->Exi = e[0].Imaginary; fd->Eyr = e[1].Real; fd->Eyi = e[1].Imaginary; fd->Ezr = e[2].Real; fd->Ezi = e[2].Imaginary;
            var ud = new UserData { x = x, y = y, z = 0, l = d[0], m = d[1], n = d[2] };
            int status = UserDefinedSurface5(ref ud, fd);
            var outE = new[] { new Complex(fd->Exr, fd->Exi), new Complex(fd->Eyr, fd->Eyi), new Complex(fd->Ezr, fd->Ezi) };
            return (outE, [ud.l, ud.m, ud.n, ud.x, ud.y, ud.z, ud.ln, ud.mn, ud.nn, fd->did_polar], status);
        }
        finally { NativeMemory.Free(fd); }
    }

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    /// <summary>The API's transmission Jones matrix (t_pp, t_ps, t_sp, t_ss) for a stack file.</summary>
    private static Complex[] Jones(string layer, double n1, double n2, double um, double angle, double azimuth)
    {
        var doc = StackDocument.Parse($$"""
            { "format": "berreman-stack", "version": 1, "units": { "length": "um", "spectral": "um" },
              "materials": { "in": { "n": {{n1:R}} }, "out": { "n": {{n2:R}} }, "lc": { "no": 1.5, "ne": 1.7 }, "film": { "no": 1.52, "ne": 1.63 } },
              "incident": { "material": "in" }, "layers": [ {{layer}} ], "exit": { "material": "out" },
              "sweep": { "spectral": {{um:R}}, "angle_deg": {{angle:R}}, "azimuth_deg": {{azimuth:R}} } }
            """.Replace("[  ]", "[]"));
        return Evaluator.Evaluate(doc).Points[0].Jones[4..8];
    }

    /// <summary>The field the reference predicts: E = a p_in + b s in, t applied, out along d2.</summary>
    private static Complex[] Expected(Complex[] t, double[] s, double[] d2, Complex a, Complex b)
    {
        var pout = Cross(s, d2);
        Complex ep = t[0] * a + t[2] * b, es = t[1] * a + t[3] * b;
        return [ep * pout[0] + es * s[0], ep * pout[1] + es * s[1], ep * pout[2] + es * s[2]];
    }

    private static double Diff(Complex[] a, Complex[] b) => Enumerable.Range(0, 3).Max(i => (a[i] - b[i]).Magnitude);

    public static void Run()
    {
        const double um = 0.6, n1 = 1.0, n2 = 1.5;

        /* A ray in the plane at azimuth β (from global X), at θ in medium n1, going +z (sign = +1)
           or -z (sign = -1); the reference's s is (-sin β, cos β, 0) for +z. */
        (double[] D, double[] S, double[] Pin, double[] D2) Ray(double theta, double beta, int sign)
        {
            double t = theta * Math.PI / 180, b = beta * Math.PI / 180;
            var d = new[] { Math.Sin(t) * Math.Cos(b), Math.Sin(t) * Math.Sin(b), sign * Math.Cos(t) };
            double t2 = Math.Asin(n1 * Math.Sin(t) / n2);
            var d2 = new[] { Math.Sin(t2) * Math.Cos(b), Math.Sin(t2) * Math.Sin(b), sign * Math.Cos(t2) };
            var z = new double[] { 0, 0, sign };
            var x = new[] { Math.Cos(b), Math.Sin(b), 0.0 };
            var s = Cross(z, x);
            return (d, s, Cross(s, d), d2);
        }
        Complex[] Field(double[] p, double[] s, Complex a, Complex b) => [a * p[0] + b * s[0], a * p[1] + b * s[1], a * p[2] + b * s[2]];

        /* 1. No film: Fresnel. */
        {
            var (d, s, p, d2) = Ray(40, 25, +1);
            double c1 = Math.Cos(40 * Math.PI / 180), c2 = d2[2];
            Complex tp = 2 * n1 * c1 / (n2 * c1 + n1 * c2), ts = 2 * n1 * c1 / (n1 * c1 + n2 * c2);
            var (e, dir, st) = Trace(new Film(0, 0, 1.5, 1.6, 0, 0), n1, n2, um, d, Field(p, s, 0.6, new Complex(0, 0.8)));
            var want = Expected([tp, 0, 0, ts], s, d2, 0.6, new Complex(0, 0.8));
            Check(st == 0 && dir[9] == 2, "the DLL traces the ray and sets the field (did_polar = 2)");
            Check(Math.Abs(dir[0] - d2[0]) + Math.Abs(dir[1] - d2[1]) + Math.Abs(dir[2] - d2[2]) < 1e-14, "the ray refracts by Snell's law");
            Check(Diff(e, want) < 1e-13, $"no film: the field is the Fresnel transmission ({Diff(e, want):G3})");
        }

        /* 2 and 3. An O-plate, axis tilted 35° at azimuth 50° from the surface's x. */
        double worst = 0, worstBack = 0;
        foreach (var (theta, beta) in new[] { (0.0, 0.0), (30.0, 0.0), (30.0, 70.0), (55.0, -120.0), (20.0, 200.0) })
        {
            var euler = MaterialMatrix.EulerForAxis(35, 50);
            var layer = $$"""{ "material": "film", "thickness": 2.3, "euler_deg": [{{euler[0]:R}}, {{euler[1]:R}}, {{euler[2]:R}}] }""";
            var t = Jones(layer, n1, n2, um, theta, -beta);   /* the surface's x is at -β from the plane of incidence */
            var (d, s, p, d2) = Ray(theta, beta, +1);
            foreach (var (a, b) in new[] { (new Complex(1, 0), Complex.Zero), (Complex.Zero, new Complex(1, 0)), (new Complex(0.6, 0.1), new Complex(-0.3, 0.7)) })
            {
                var (e, _, _) = Trace(new Film(0, 2.3, 1.52, 1.63, 35, 50), n1, n2, um, d, Field(p, s, a, b));
                worst = Math.Max(worst, Diff(e, Expected(t, s, d2, a, b)));
            }

            /* The other way: the ray at -z sees the same film from behind. Its axis in that ray's frame
               (z' = -z, x' the tangential direction, y' = z' × x') sets the reference's orientation. */
            var (db, sb, pb, d2b) = Ray(theta, beta, -1);
            double tl = 35 * Math.PI / 180, al = 50 * Math.PI / 180;
            var axis = new[] { Math.Sin(tl) * Math.Cos(al), Math.Sin(tl) * Math.Sin(al), Math.Cos(tl) };
            var xb = new[] { Math.Cos(beta * Math.PI / 180), Math.Sin(beta * Math.PI / 180), 0.0 };
            var zb = new double[] { 0, 0, -1 };
            var yb = Cross(zb, xb);
            double ax = axis[0] * xb[0] + axis[1] * xb[1], ay = axis[0] * yb[0] + axis[1] * yb[1], az = -axis[2];
            var eb = MaterialMatrix.EulerForAxis(Math.Acos(az) * 180 / Math.PI, Math.Atan2(ay, ax) * 180 / Math.PI);
            var tb = Jones($$"""{ "material": "film", "thickness": 2.3, "euler_deg": [{{eb[0]:R}}, {{eb[1]:R}}, {{eb[2]:R}}] }""", n1, n2, um, theta, 0);
            var (eback, _, _) = Trace(new Film(0, 2.3, 1.52, 1.63, 35, 50), n1, n2, um, db, Field(pb, sb, 0.6, new Complex(0.2, 0.75)));
            worstBack = Math.Max(worstBack, Diff(eback, Expected(tb, sb, d2b, 0.6, new Complex(0.2, 0.75))));
        }
        Check(worst < 1e-12, $"an O-plate: the DLL's field is the API's, every plane of incidence and polarisation ({worst:G3})");
        Check(worstBack < 1e-12, $"and from behind, the same film fixed to the surface ({worstBack:G3})");

        /* 4. A cholesteric: no 1.5, ne 1.7, pitch 0.38 µm, 10 pitches, 640 sublayers; at 0.60 µm
           (in the band n̄P ≈ 0.608 µm) and 0.70 µm (outside it). */
        double worstC = 0, contrast = 0;
        foreach (var hand in new[] { +1, -1 })
            foreach (var (lambda, theta, beta) in new[] { (0.60, 0.0, 0.0), (0.60, 25.0, 40.0), (0.70, 35.0, -60.0) })
            {
                var layer = $$"""{ "material": "lc", "thickness": 3.8, "euler_deg": [90, 90, 0], "twist": { "pitch": 0.38, "handedness": "{{(hand > 0 ? "right" : "left")}}", "start_deg": 20, "sublayers": 640 } }""";
                var t = Jones(layer, n1, n2, lambda, theta, -beta);
                var (d, s, p, d2) = Ray(theta, beta, +1);
                foreach (var (a, b) in new[] { (new Complex(1 / Math.Sqrt(2), 0), new Complex(0, 1 / Math.Sqrt(2))), (new Complex(1 / Math.Sqrt(2), 0), new Complex(0, -1 / Math.Sqrt(2))) })
                {
                    var (e, _, _) = Trace(new Film(1, 3.8, 1.5, 1.7, 0.38, hand, 20, 640), n1, n2, lambda, d, Field(p, s, a, b));
                    worstC = Math.Max(worstC, Diff(e, Expected(t, s, d2, a, b)));
                }
                if (lambda == 0.60 && theta == 0)
                {
                    /* At normal incidence in the band one circular polarisation is reflected and the other not. */
                    var (e1, _, _) = Trace(new Film(1, 3.8, 1.5, 1.7, 0.38, hand, 20, 640), n1, n2, lambda, d, Field(p, s, 1 / Math.Sqrt(2), new Complex(0, 1 / Math.Sqrt(2))));
                    var (e2, _, _) = Trace(new Film(1, 3.8, 1.5, 1.7, 0.38, hand, 20, 640), n1, n2, lambda, d, Field(p, s, 1 / Math.Sqrt(2), new Complex(0, -1 / Math.Sqrt(2))));
                    double i1 = e1.Sum(c => c.Magnitude * c.Magnitude), i2 = e2.Sum(c => c.Magnitude * c.Magnitude);
                    contrast = Math.Max(contrast, Math.Max(i1, i2) / Math.Min(i1, i2));
                }
            }
        Check(worstC < 1e-11, $"a cholesteric, both hands, in and out of its band: the DLL's field is the API's ({worstC:G3})");
        Check(contrast > 10, $"in the band at normal incidence it passes one circular polarisation and not the other (ratio {contrast:G3}: ten pitches reflect about 95% of one hand)");

        /* 5. Geometry on a sphere of radius 20: the intercept lies on it, the normal is radial. */
        {
            double cv = 0.05, x0 = 3, y0 = -2;
            var d = new[] { 0.1, 0.05, Math.Sqrt(1 - 0.0125) };
            var (_, r, st) = Trace(new Film(0, 0, 1.5, 1.6, 0, 0), n1, n2, um, d, [1, 0, 0], cv, x0, y0);
            double hx = r[3], hy = r[4], hz = r[5];
            double onSphere = Math.Abs(hx * hx + hy * hy + (hz - 20) * (hz - 20) - 400);
            var radial = new[] { hx / 20, hy / 20, (hz - 20) / 20 };
            double normalErr = Math.Abs(r[6] - radial[0]) + Math.Abs(r[7] - radial[1]) + Math.Abs(r[8] - radial[2]);
            Check(st == 0 && onSphere < 1e-10 && normalErr < 1e-12, $"on a sphere the intercept is on it and the normal radial ({onSphere:G3}, {normalErr:G3})");
        }
    }
}
