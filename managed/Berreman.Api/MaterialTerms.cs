// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// MaterialTerms.cs — every material form written as terms (docs/material-model.md, "Shorthand").
//
// The short forms of the stack file (n, eps, no/ne, a Sellmeier per axis, the ferrite and conductor
// models, ...) are each a particular list of terms. This writes that list, so the editor can show
// any material as terms and a person can add one to it. The result evaluates to the same M as the
// short form; the managed tests check that on every sample material.

using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Berreman.Api;

public static class MaterialTerms
{
    /// <summary>
    /// The material as {"terms": [...]}, or null for the one form that is not a list of terms: a
    /// resistive sheet, whose ε depends on the thickness of the layer holding it.
    /// </summary>
    public static JsonObject? ToTerms(JsonObject spec)
    {
        if (spec.ContainsKey("terms")) return (JsonObject)spec.DeepClone();
        var terms = new JsonArray();

        if (spec["model"] is JsonNode modelNode)
        {
            var model = modelNode.GetValue<string>();
            if (model == "resistive_sheet") return null;
            if (model == "conductor")
            {
                var bg = spec.ContainsKey("eps_background") ? MaterialParser.Cplx(spec["eps_background"], "eps_background") : Complex.One;
                terms.Add(new JsonObject { ["label"] = "background", ["eps"] = C(bg) });
                terms.Add(new JsonObject
                {
                    ["label"] = "conductivity",
                    ["conductivity"] = new JsonObject { ["sigma_s_per_m"] = spec["sigma_s_per_m"]!.DeepClone() },
                    ["eps"] = 1,
                });
                return Wrap(terms);
            }
            if (model == "ferrite")
            {
                /* ε from the same keys a constant material takes; μ from the Polder law. */
                var epsOnly = new JsonObject();
                foreach (var k in new[] { "eps", "n", "eps_r", "tan_delta", "no", "ne", "nx", "ny", "nz" })
                    if (spec.ContainsKey(k)) epsOnly[k] = spec[k]!.DeepClone();
                var e = ((ConstantMaterial)MaterialParser.Parse(epsOnly, "eps")).M;
                terms.Add(new JsonObject { ["label"] = "ε", ["eps"] = Block(e, 0, 0) });
                terms.Add(new JsonObject { ["label"] = "μ along the bias", ["mu"] = Rows(new Complex[,] { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 1 } }) });
                JsonObject Polder(string part)
                {
                    var p = new JsonObject();
                    foreach (var k in new[] { "four_pi_ms_gauss", "h0_oe", "alpha", "gamma_mhz_per_oe" })
                        if (spec.ContainsKey(k)) p[k] = spec[k]!.DeepClone();
                    p["part"] = part;
                    return p;
                }
                terms.Add(new JsonObject
                {
                    ["label"] = "Polder μ", ["polder"] = Polder("mu"),
                    ["mu"] = Rows(new Complex[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 0 } }),
                });
                var i = Complex.ImaginaryOne;
                terms.Add(new JsonObject
                {
                    ["label"] = "Polder κ", ["polder"] = Polder("kappa"),
                    ["mu"] = Rows(new Complex[,] { { 0, -i, 0 }, { i, 0, 0 }, { 0, 0, 0 } }),
                });
                return Wrap(terms);
            }
            return null;
        }

        bool dispersive = MaterialParser.DispersionKeys.Any(spec.ContainsKey) || spec.ContainsKey("uniaxial") || spec.ContainsKey("biaxial");
        if (dispersive)
        {
            void Axis(JsonNode? law, string label, Complex[,] shape)
            {
                var o = (JsonObject)law!;
                var t = new JsonObject { ["label"] = label };
                if (o.ContainsKey("n")) { var n = MaterialParser.Cplx(o["n"], label); t["eps"] = Rows(Scale(shape, n * n)); }
                else if (o.ContainsKey("eps")) t["eps"] = Rows(Scale(shape, MaterialParser.Cplx(o["eps"], label)));
                else
                {
                    var key = MaterialParser.DispersionKeys.First(o.ContainsKey);
                    t[key] = o[key]!.DeepClone();
                    t["eps"] = IsIdentity(shape) ? 1 : Rows(shape);
                }
                terms.Add(t);
            }
            Complex[,] D(double a, double b, double c) => MaterialMatrix.Diagonal(a, b, c);
            if (spec["uniaxial"] is JsonObject u)
            {
                Axis(u["ordinary"], "ordinary (x, y)", D(1, 1, 0));
                Axis(u["extraordinary"], "extraordinary (z)", D(0, 0, 1));
            }
            else if (spec["biaxial"] is JsonObject b)
            {
                Axis(b["x"], "x", D(1, 0, 0));
                Axis(b["y"], "y", D(0, 1, 0));
                Axis(b["z"], "z", D(0, 0, 1));
            }
            else
            {
                var key = MaterialParser.DispersionKeys.First(spec.ContainsKey);
                terms.Add(new JsonObject { ["label"] = key, [key] = spec[key]!.DeepClone(), ["eps"] = 1 });
            }
            var mu = spec.ContainsKey("mu") ? MaterialParser.Cplx(spec["mu"], "mu") : Complex.One;
            terms.Add(new JsonObject { ["label"] = "μ", ["mu"] = C(mu) });
            return Wrap(terms);
        }

        /* A constant: its M, block by block. */
        var m = ((ConstantMaterial)MaterialParser.Parse(spec, "material")).M;
        var term = new JsonObject { ["label"] = "constant", ["eps"] = Block(m, 0, 0), ["mu"] = Block(m, 3, 3) };
        if (!IsZero(m, 0, 3)) term["rho"] = Block(m, 0, 3);
        if (!IsZero(m, 3, 0)) term["rho_prime"] = Block(m, 3, 0);
        terms.Add(term);
        return Wrap(terms);
    }

    private static JsonObject Wrap(JsonArray terms) => new() { ["terms"] = terms };

    private static Complex[,] Scale(Complex[,] a, Complex s)
    {
        var o = new Complex[3, 3];
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) o[i, j] = a[i, j] * s;
        return o;
    }

    private static bool IsIdentity(Complex[,] a)
    {
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) if (a[i, j] != (i == j ? Complex.One : Complex.Zero)) return false;
        return true;
    }

    private static bool IsZero(MaterialMatrix m, int r, int c)
    {
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) if (m[r + i, c + j] != Complex.Zero) return false;
        return true;
    }

    /// <summary>A 3×3 block of M: a scalar when it is a multiple of the identity, else rows.</summary>
    private static JsonNode Block(MaterialMatrix m, int r, int c)
    {
        var a = new Complex[3, 3];
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) a[i, j] = m[r + i, c + j];
        bool scalar = a[0, 0] == a[1, 1] && a[1, 1] == a[2, 2];
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) if (i != j && a[i, j] != Complex.Zero) scalar = false;
        return scalar ? C(a[0, 0]) : Rows(a);
    }

    private static JsonArray Rows(Complex[,] a)
    {
        var rows = new JsonArray();
        for (int i = 0; i < 3; ++i) rows.Add(new JsonArray(C(a[i, 0]), C(a[i, 1]), C(a[i, 2])));
        return rows;
    }

    /// <summary>A complex number as the file writes it: a plain number when real, else [re, im].</summary>
    public static JsonNode C(Complex z) =>
        z.Imaginary == 0 ? JsonValue.Create(Clean(z.Real)) : new JsonArray(Clean(z.Real), Clean(z.Imaginary));

    // −0 prints as "-0"; it means 0.
    private static double Clean(double v) => v == 0 ? 0 : v;
}
