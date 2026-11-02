// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// FieldProfileTests.cs — fields against depth through the API (Gui.Core/FieldProfile.cs), against
// closed forms:
//   1. a bare glass surface at normal incidence: the standing wave in front is
//      |E|² = |e^{ikz} + r e^{-ikz}|², and behind it |t|², r = (1 - n)/(1 + n), t = 2/(1 + n);
//   2. a quarter-wave antireflection layer of index √n_s reflects nothing: |E|² = 1 in front;
//   3. total reflection: behind the surface the field decays as e^{-2κz}, κ = k0 √(ξ² - 1);
//   4. the index profile is each medium's n, in steps.

using System.Numerics;
using Berreman.Api;
using Berreman.Gui.Core;

namespace Berreman.Native.Tests;

internal static class FieldProfileTests
{
    public static int Checks, Failures;

    private static void Check(bool ok, string what)
    {
        ++Checks;
        if (!ok) { ++Failures; Console.WriteLine($"  FAIL  {what}"); }
    }

    private static StackModel Stack(string materials, string layers, string exit, string incident = "air") => StackModel.FromJson($$"""
        { "format": "berreman-stack", "version": 1, "units": { "length": "nm", "spectral": "nm" },
          "materials": { {{materials}} }, "incident": { "material": "{{incident}}" }, "layers": [ {{layers}} ],
          "exit": { "material": "{{exit}}" }, "sweep": { "spectral": 600, "angle_deg": 0 } }
        """);

    public static void Run()
    {
        /* 1. Bare glass. */
        var bare = FieldProfile.Compute(Stack("""
            "air": { "n": 1 }, "glass": { "n": 1.5 }
            """, "", "glass"), 600, 0, 0, 401, 600, 300);
        double k = 2 * Math.PI / 600, r = (1 - 1.5) / (1 + 1.5), t = 2 / (1 + 1.5);
        var e2 = FieldProfile.Curves(bare, 0, 0)[0];
        double worst = 0;
        for (int i = 0; i < e2.X.Length; ++i)
        {
            double z = e2.X[i];
            double want = z < 0 ? (Complex.Exp(new Complex(0, k * z)) + r * Complex.Exp(new Complex(0, -k * z))).Magnitude : t;
            if (z < 0) want *= want; else want = t * t;
            worst = Math.Max(worst, Math.Abs(e2.Y[i] - want));
        }
        Check(worst < 1e-12, $"bare glass: the standing wave in front and |t|² behind ({worst:G3})");
        Check(Math.Abs(FieldProfile.Curves(bare, 0, 1)[0].Y[50] - e2.Y[50]) < 1e-12, "at normal incidence p and s give the same |E|²");

        /* 2. A quarter-wave antireflection layer. */
        double nl = Math.Sqrt(1.5), d = 600 / (4 * nl);
        var ar = FieldProfile.Compute(Stack($$"""
            "air": { "n": 1 }, "low": { "n": {{nl:R}} }, "glass": { "n": 1.5 }
            """, $$"""{ "material": "low", "thickness": {{d:R}} }""", "glass"), 600, 0, 0, 301, 600, 300);
        var front = FieldProfile.Curves(ar, 0, 0)[0];
        double flat = front.X.Zip(front.Y).Where(p => p.First < 0).Max(p => Math.Abs(p.Second - 1));
        Check(flat < 1e-12, $"a quarter-wave antireflection layer: |E|² = 1 in front, no standing wave ({flat:G3})");

        /* 3. Total reflection, glass to air at 60°, s polarised. */
        var tir = FieldProfile.Compute(Stack("""
            "air": { "n": 1 }, "glass": { "n": 1.5 }
            """, "", "air", "glass"), 600, 60, 0, 601, 300, 300);
        var behind = FieldProfile.Curves(tir, 0, 1)[0];
        double xi = 1.5 * Math.Sin(Math.PI / 3), kappa = k * Math.Sqrt(xi * xi - 1);
        int a = Array.FindIndex(behind.X, z => z > 50), b = Array.FindIndex(behind.X, z => z > 250);
        double slope = Math.Log(behind.Y[b] / behind.Y[a]) / (behind.X[b] - behind.X[a]);
        Check(Math.Abs(slope + 2 * kappa) < 1e-9 * kappa, $"total reflection: the field behind decays as e^(-2κz) ({slope:G8} against {-2 * kappa:G8})");
        Check(tir.Point.Power[3] > 1 - 1e-12, "and all of it is reflected");

        /* 4. The index profile. */
        var steps = FieldProfile.IndexProfile(ar);
        var nx = steps.First(s => s.Name == "n_x");
        Check(nx.Y.Length == 6 && nx.Y[0] == 1 && Math.Abs(nx.Y[2] - nl) < 1e-15 && nx.Y[4] == 1.5, "the index profile steps from air to the layer to the glass");
        Check(nx.X[1] == 0 && nx.X[2] == 0 && Math.Abs(nx.X[3] - d) < 1e-9, "at the interfaces");
    }
}
