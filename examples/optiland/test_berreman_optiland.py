# Copyright (c) 2026 Javier Ruiz
# SPDX-License-Identifier: MIT

"""test_berreman_optiland.py — the Optiland coating (berreman_optiland.py), checked on the fields.

Every check compares the 3D electric field Optiland carries after the surface (the ray's
polarization matrix applied to an incident field) with a reference that shares nothing with the
coating's frame and basis code:
  1. No film: the Fresnel coefficients in closed form, transmitted and reflected; at normal
     incidence the reflected field is r times the incident one, whatever its polarization.
  2. One isotropic layer: the Airy formulas in closed form, s and p, transmitted and reflected,
     and R + T = 1 for a lossless layer.
  3. An O-plate and a cholesteric: the BerremanCalculator command line on a stack file, the film
     oriented by Euler angles and twist and the plane of incidence by the sample azimuth.
     Several planes of incidence, both hands, rays going either way.
  4. Layer order: a cholesteric's first layer on the back face is the film started on the front
     face at the azimuth the helix has there.
  5. A curved surface: the film follows the local normal (a rotated copy of a flat case).
  6. The whole plate in an Optic: the chief ray's intensity matches OpticStudio with
     US_BERREMAN.dll to its three printed decimals, and the coating survives to_dict/from_dict.

Optiland's own FresnelCoating and ThinFilmCoating are deliberately not used as references.

    pytest test_berreman_optiland.py

The command-line checks need the berreman command (BERREMAN_CLI, the install folder, or a build
of the source repository) and are skipped without it.
"""

from __future__ import annotations

import cmath
import csv
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
import optiland.backend as be
import pytest
from optiland.materials import IdealMaterial
from optiland.rays import PolarizedRays

sys.path.insert(0, str(Path(__file__).resolve().parent))

from berreman_optiland import (
    BerremanCoating,
    Cholesteric,
    Film,
    Layer,
    OPlate,
    Stack,
    uniaxial,
)

N1, N2, UM = 1.0, 1.5, 0.6
AIR, GLASS = IdealMaterial(N1), IdealMaterial(N2)


# ---- tracing one surface by hand ---------------------------------------------------------------


def ray(theta_deg: float, beta_deg: float, sign: int = 1) -> np.ndarray:
    """A direction at theta from the axis, in the plane at azimuth beta, going +z or -z."""
    t, b = math.radians(theta_deg), math.radians(beta_deg)
    return np.array([math.sin(t) * math.cos(b), math.sin(t) * math.sin(b), sign * math.cos(t)])


def through(
    coating, k: np.ndarray, reflect: bool = False, normal=(0.0, 0.0, 1.0), um=UM, n1=N1, n2=N2
):
    """One ray through a surface with this coating, as Optiland's refractive model does it:
    Optiland bends the ray, then the coating acts. Returns (polarization matrix, k_out)."""
    rays = PolarizedRays(
        np.zeros(1),
        np.zeros(1),
        np.zeros(1),
        np.array([k[0]]),
        np.array([k[1]]),
        np.array([k[2]]),
        np.ones(1),
        np.array([um]),
    )
    nx, ny, nz = (np.array([c], dtype=float) for c in normal)
    if reflect:
        rays.reflect(nx, ny, nz)
        coating.reflect(rays, nx, ny, nz)
    else:
        rays.refract(nx, ny, nz, n1, n2)
        coating.transmit(rays, nx, ny, nz)
    p = be.to_numpy(rays.p)[0]
    k_out = np.array([be.to_numpy(v)[0] for v in (rays.L, rays.M, rays.N)])
    return p, k_out


def transverse_fields(k: np.ndarray) -> list[np.ndarray]:
    """Three incident fields across k: two linear and one elliptical."""
    s = np.cross(k, [0.3, -0.7, 0.2])
    s /= np.linalg.norm(s)
    q = np.cross(k, s)
    return [s.astype(complex), q.astype(complex), (0.6 * s + (0.2 + 0.75j) * q)]


def predicted(coef, s, k_in, k_out, E):
    """The field a reference gives: coef = (pp, ps, sp, ss), first subscript incident, in the
    basis p = s × k for each wave (docs/contract.md)."""
    pp, ps, sp, ss = coef
    p_in, p_out = np.cross(s, k_in), np.cross(s, k_out)
    a_p, a_s = np.dot(E, p_in), np.dot(E, s)
    return (pp * a_p + sp * a_s) * p_out + (ps * a_p + ss * a_s) * s


def plane_s(k: np.ndarray, normal=(0.0, 0.0, 1.0)) -> np.ndarray:
    """s = z × x of the ray's frame: z the normal in the direction the ray goes, x tangential."""
    z = np.asarray(normal, dtype=float)
    z = z if np.dot(k, z) >= 0 else -z
    x = k - np.dot(k, z) * z
    x = x / np.linalg.norm(x) if np.linalg.norm(x) > 1e-14 else np.array([1.0, 0.0, 0.0])
    return np.cross(z, x)


# ---- 1 and 2: closed forms ---------------------------------------------------------------------


def fresnel(c1, c2, n1, n2):
    """r_s, r_p, t_s, t_p for p = s × k: at normal incidence r_p = -r_s."""
    rs = (n1 * c1 - n2 * c2) / (n1 * c1 + n2 * c2)
    rp = (n2 * c1 - n1 * c2) / (n2 * c1 + n1 * c2)
    ts = 2 * n1 * c1 / (n1 * c1 + n2 * c2)
    tp = 2 * n1 * c1 / (n2 * c1 + n1 * c2)
    return rs, rp, ts, tp


CASES = [(0.0, 0.0), (1e-4, 30.0), (25.0, 0.0), (40.0, 25.0), (65.0, -110.0), (80.0, 200.0)]


@pytest.mark.parametrize("theta,beta", CASES)
def test_no_film_is_fresnel(theta, beta):
    coating = BerremanCoating(AIR, GLASS, Stack([]))
    k = ray(theta, beta)
    c1 = math.cos(math.radians(theta))
    c2 = math.sqrt(1 - (N1 * math.sin(math.radians(theta)) / N2) ** 2)
    rs, rp, ts, tp = fresnel(c1, c2, N1, N2)
    s = plane_s(k)
    for reflect, coef in ((False, (tp, 0, 0, ts)), (True, (rp, 0, 0, rs))):
        p, k_out = through(coating, k, reflect)
        for E in transverse_fields(k):
            assert np.max(np.abs(p @ E - predicted(coef, s, k, k_out, E))) < 1e-13


def test_normal_reflection_keeps_the_polarization():
    """An isotropic interface at normal incidence reflects every field by the same r."""
    p, _ = through(BerremanCoating(AIR, GLASS, Stack([])), ray(0, 0), reflect=True)
    r = (N1 - N2) / (N1 + N2)
    for E in transverse_fields(ray(0, 0)):
        assert np.max(np.abs(p @ E - r * E)) < 1e-14


def interface(pol, na, ca, nb, cb):
    """r and t from medium a into medium b, s or p (p = s × k)."""
    if pol == "s":
        return (na * ca - nb * cb) / (na * ca + nb * cb), 2 * na * ca / (na * ca + nb * cb)
    return (nb * ca - na * cb) / (nb * ca + na * cb), 2 * na * ca / (nb * ca + na * cb)


def airy(theta, n1, n, n2, d, um):
    """r and t of one isotropic layer, s and p (p = s × k), by the Airy sums."""
    s1 = n1 * math.sin(math.radians(theta))
    c = [cmath.sqrt(1 - (s1 / m) ** 2) for m in (n1, n, n2)]
    beta = 2 * math.pi * n * d * c[1] / um
    out = {}
    for pol in ("s", "p"):
        r01, t01 = interface(pol, n1, c[0], n, c[1])
        r12, t12 = interface(pol, n, c[1], n2, c[2])
        e = np.exp(2j * beta)
        den = 1 + r01 * r12 * e
        out[pol] = ((r01 + r12 * e) / den, t01 * t12 * np.exp(1j * beta) / den)
    return out


@pytest.mark.parametrize("theta,beta", CASES)
def test_isotropic_layer_is_airy(theta, beta):
    n, d = 1.8, 0.37
    coating = BerremanCoating(AIR, GLASS, Stack([Layer(d, uniaxial(n, n, [0, 0, 1]))]))
    k = ray(theta, beta)
    ab = airy(theta, N1, n, N2, d, UM)
    (rs, ts), (rp, tp) = ab["s"], ab["p"]
    s = plane_s(k)
    for reflect, coef in ((False, (tp, 0, 0, ts)), (True, (rp, 0, 0, rs))):
        p, k_out = through(coating, k, reflect)
        for E in transverse_fields(k):
            assert np.max(np.abs(p @ E - predicted(coef, s, k, k_out, E))) < 1e-12

    # Lossless: what is not reflected is transmitted, for s and p.
    c1 = math.cos(math.radians(theta))
    c2 = math.sqrt(1 - (N1 * math.sin(math.radians(theta)) / N2) ** 2)
    for r, t in ((rs, ts), (rp, tp)):
        assert abs(abs(r) ** 2 + (N2 * c2) / (N1 * c1) * abs(t) ** 2 - 1) < 1e-12


# ---- 3: the command line ----------------------------------------------------------------------


def find_cli() -> Path | None:
    explicit = os.environ.get("BERREMAN_CLI")
    if explicit:
        return Path(explicit)
    name = "berreman.exe" if sys.platform == "win32" else "berreman"
    here = Path(__file__).resolve().parent
    for folder in [here, *here.parents][:6]:
        for candidate in (
            folder / name,
            folder / "managed" / "Berreman.Cli" / "bin" / "Release" / "net8.0" / name,
            folder / "managed" / "Berreman.Cli" / "bin" / "Debug" / "net8.0" / name,
        ):
            if candidate.is_file():
                return candidate
    found = shutil.which("berreman")
    return Path(found) if found else None


CLI = find_cli()
needs_cli = pytest.mark.skipif(CLI is None, reason="the berreman command line was not found")


def cli_jones(layer: dict, um: float, theta: float, azimuth: float, n1=N1, n2=N2):
    """r and t (pp, ps, sp, ss) from the command line, for one layer of a stack file."""
    doc = {
        "format": "berreman-stack",
        "version": 1,
        "units": {"length": "um", "spectral": "um"},
        "materials": {
            "in": {"n": n1},
            "out": {"n": n2},
            "film": {"no": 1.52, "ne": 1.63},
            "lc": {"no": 1.5, "ne": 1.7},
        },
        "incident": {"material": "in"},
        "layers": [layer],
        "exit": {"material": "out"},
        "sweep": {"spectral": um, "angle_deg": theta, "azimuth_deg": azimuth},
    }
    with tempfile.TemporaryDirectory() as tmp:
        stack, table = Path(tmp) / "film.berr", Path(tmp) / "out.csv"
        stack.write_text(json.dumps(doc), encoding="utf-8")
        subprocess.run(
            [str(CLI), "evaluate", str(stack), "--jones", "--csv", str(table)],
            check=True,
            capture_output=True,
        )
        with table.open(encoding="utf-8-sig") as f:
            row = next(csv.DictReader(f))
    assert row["status"] == "ok"

    def c(name):
        return complex(float(row[name + "_re"]), float(row[name + "_im"]))

    r = tuple(c("r_" + e) for e in ("pp", "ps", "sp", "ss"))
    t = tuple(c("t_" + e) for e in ("pp", "ps", "sp", "ss"))
    return r, t


def euler_for_axis(tilt, azimuth):
    """Euler angles (φ, θ, ψ) turning a crystal's z axis to (tilt, azimuth): φ = azimuth + 90°."""
    return [azimuth + 90.0, tilt, 0.0]


# The command line prints about ten significant figures.
CLI_TOL = 1e-8


@needs_cli
@pytest.mark.parametrize(
    "theta,beta", [(0.0, 0.0), (30.0, 0.0), (30.0, 70.0), (55.0, -120.0), (20.0, 200.0)]
)
def test_oplate_matches_the_command_line(theta, beta):
    film = OPlate(2.3, 1.52, 1.63, tilt=35.0, azimuth=50.0)
    coating = BerremanCoating(AIR, GLASS, film)
    layer = {"material": "film", "thickness": 2.3, "euler_deg": euler_for_axis(35.0, 50.0)}
    # The surface's x is at -beta from the plane of incidence.
    r, t = cli_jones(layer, UM, theta, -beta)
    k = ray(theta, beta)
    s = plane_s(k)
    for reflect, coef in ((False, t), (True, r)):
        p, k_out = through(coating, k, reflect)
        for E in transverse_fields(k):
            assert np.max(np.abs(p @ E - predicted(coef, s, k, k_out, E))) < CLI_TOL


@needs_cli
@pytest.mark.parametrize("theta,beta", [(0.0, 0.0), (30.0, 70.0), (55.0, -120.0)])
def test_oplate_from_behind_is_the_same_film(theta, beta):
    """A ray going -z meets the film fixed to the surface: its axis, in that ray's own frame
    (z' = -z, x' tangential, y' = z' × x'), orients the reference."""
    film = OPlate(2.3, 1.52, 1.63, tilt=35.0, azimuth=50.0)
    coating = BerremanCoating(AIR, GLASS, film)
    tl, al = math.radians(35.0), math.radians(50.0)
    axis = np.array([math.sin(tl) * math.cos(al), math.sin(tl) * math.sin(al), math.cos(tl)])
    xb = np.array([math.cos(math.radians(beta)), math.sin(math.radians(beta)), 0.0])
    zb = np.array([0.0, 0.0, -1.0])
    yb = np.cross(zb, xb)
    ax, ay, az = axis @ xb, axis @ yb, axis @ zb
    layer = {
        "material": "film",
        "thickness": 2.3,
        "euler_deg": euler_for_axis(math.degrees(math.acos(az)), math.degrees(math.atan2(ay, ax))),
    }
    r, t = cli_jones(layer, UM, theta, 0.0)
    k = ray(theta, beta, -1)
    s = plane_s(k)
    for reflect, coef in ((False, t), (True, r)):
        p, k_out = through(coating, k, reflect)
        for E in transverse_fields(k):
            assert np.max(np.abs(p @ E - predicted(coef, s, k, k_out, E))) < CLI_TOL


@needs_cli
@pytest.mark.parametrize("hand", [+1, -1])
@pytest.mark.parametrize(
    "um,theta,beta", [(0.60, 0.0, 0.0), (0.60, 25.0, 40.0), (0.70, 35.0, -60.0)]
)
def test_cholesteric_matches_the_command_line(hand, um, theta, beta):
    film = Cholesteric(3.8, 1.5, 1.7, pitch=0.38, hand=hand, start=20.0, sublayers=640)
    coating = BerremanCoating(AIR, GLASS, film)
    layer = {
        "material": "lc",
        "thickness": 3.8,
        "euler_deg": [90, 90, 0],
        "twist": {
            "pitch": 0.38,
            "handedness": "right" if hand > 0 else "left",
            "start_deg": 20,
            "sublayers": 640,
        },
    }
    r, t = cli_jones(layer, um, theta, -beta)
    k = ray(theta, beta)
    s = plane_s(k)
    for reflect, coef in ((False, t), (True, r)):
        p, k_out = through(coating, k, reflect, um=um)
        for E in transverse_fields(k):
            assert np.max(np.abs(p @ E - predicted(coef, s, k, k_out, E))) < CLI_TOL


def test_cholesteric_reflects_one_circular_polarization():
    """In its band, a right-handed cholesteric passes one circular polarization and reflects
    the other; the opposite hand does the opposite."""
    k = ray(0, 0)
    right = 1 / math.sqrt(2) * np.array([1, 1j, 0])
    left = 1 / math.sqrt(2) * np.array([1, -1j, 0])
    out = {}
    for hand in (+1, -1):
        film = Cholesteric(3.8, 1.5, 1.7, pitch=0.38, hand=hand)
        p, _ = through(BerremanCoating(AIR, GLASS, film), k)
        out[hand] = [N2 / N1 * np.linalg.norm(p @ E) ** 2 for E in (right, left)]
    assert out[+1][0] > 0.9 and out[+1][1] < 0.1
    assert out[-1][1] > 0.9 and out[-1][0] < 0.1


# ---- 4: layer order ---------------------------------------------------------------------------


@pytest.mark.parametrize("hand", [+1, -1])
@pytest.mark.parametrize("sign", [+1, -1])
def test_layer_order(hand, sign):
    """With the first layer on the back face (z = d) the director is phi0 + h 360 (z - d) / P,
    the front-face film started at phi0 - h 360 d / P. 3.7 µm is not a whole number of pitches,
    so the two orders are different films."""
    d, pitch, phi0 = 3.7, 0.38, 20.0
    k = ray(30, 20, sign)

    def field(film):
        p, _ = through(BerremanCoating(AIR, GLASS, film), k)
        return p @ transverse_fields(k)[2]

    back = field(Cholesteric(d, 1.5, 1.7, pitch, hand, phi0, 640, first_layer="back"))
    front = field(Cholesteric(d, 1.5, 1.7, pitch, hand, phi0 - hand * 360 * d / pitch, 640))
    plain = field(Cholesteric(d, 1.5, 1.7, pitch, hand, phi0, 640))
    assert np.max(np.abs(back - front)) < 1e-12
    assert np.max(np.abs(back - plain)) > 1e-3


# ---- 5: a curved surface ----------------------------------------------------------------------


def test_film_follows_the_local_normal():
    """Turn a flat case about x by alpha: the normal, the ray and the film's y_s, z_s turn with
    it (x_s stays the local x), so the outgoing field is the flat one turned."""
    alpha = math.radians(17.0)
    Rx = np.array(
        [[1, 0, 0], [0, math.cos(alpha), -math.sin(alpha)], [0, math.sin(alpha), math.cos(alpha)]]
    )
    film = OPlate(2.3, 1.52, 1.63, tilt=35.0, azimuth=50.0)
    coating = BerremanCoating(AIR, GLASS, film)
    k = ray(30.0, 70.0)
    p_flat, _ = through(coating, k)
    p_turned, _ = through(coating, Rx @ k, normal=Rx @ np.array([0.0, 0.0, 1.0]))
    for E in transverse_fields(k):
        assert np.max(np.abs(p_turned @ (Rx @ E) - Rx @ (p_flat @ E))) < 1e-12


# ---- 6: the plate in an Optic -----------------------------------------------------------------


def test_plate_matches_opticstudio():
    from cholesteric_plate import OPTICSTUDIO, chief_ray_intensity, plate

    for (um, field), expected in OPTICSTUDIO.items():
        for phase, want in zip((+90.0, -90.0), expected):
            got = chief_ray_intensity(plate(um, phase), field)
            # OpticStudio's figures have three decimals.
            assert abs(got - want) <= 0.0015, (um, field, phase, got, want)


def test_coating_survives_to_dict():
    films: list[Film] = [
        OPlate(2.3, 1.52, 1.63, 35.0, 50.0),
        Cholesteric(3.7, 1.5, 1.7, 0.38, -1, 20.0, 100, "back"),
        Stack([Layer(0.2, uniaxial(1.5 + 0.01j, 1.7, [0.3, 0.4, 0.8]))]),
    ]
    k = ray(30.0, 70.0)
    for film in films:
        coating = BerremanCoating(AIR, GLASS, film)
        again = BerremanCoating.from_dict(json.loads(json.dumps(coating.to_dict())))
        p1, _ = through(coating, k)
        p2, _ = through(again, k)
        assert np.max(np.abs(p1 - p2)) == 0
