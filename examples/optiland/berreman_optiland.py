# Copyright (c) 2026 Javier Ruiz
# SPDX-License-Identifier: MIT

"""berreman_optiland.py — an Optiland coating whose film is evaluated by the BerremanCalculator
engine, ray by ray, in a polarization ray trace.

WHAT IT DOES. Optiland's own coatings are isotropic: ThinFilmCoating gives a diagonal Jones
matrix in s and p. BerremanCoating puts a thin anisotropic film on a surface instead. Its effect
depends on each ray's direction relative to the film's own axes, and it couples s and p. For
every ray, after Optiland has refracted (or reflected) it, the coating
  1. builds the ray's local frame: z along the normal, in the direction the ray goes; x the ray's
     tangential direction, so the plane of incidence is x-z; y = z × x, which is s;
  2. writes each layer's dielectric tensor in that frame, from the film's own axes, which are
     fixed to the SURFACE (see "Orientation");
  3. asks the engine for the 2×2 Jones matrices between the media either side;
  4. expresses the transmission (or reflection) matrix in Optiland's s/p basis for that ray and
     hands it to Optiland, which carries it in the ray's polarization matrix.
The full matrix is used, s-p coupling included. Optiland's polarization analyses (the
polarization pupil, transmission, Jones pupil) then include the film.

THE COATING IS THE WHOLE INTERFACE. In Optiland a surface with no coating changes no field, and
FresnelCoating is the bare interface. So the film's matrix is the interface's matrix: it includes
the reflections at the film's faces and between the media either side. Nothing is divided out.

ORIENTATION. A film's axes are fixed to the surface, in the surface's local frame (the frame
Optiland traces it in): z_s is the normal, pointing towards +z at the vertex; x_s is the local x
axis projected onto the tangent plane (local y where x is nearly normal to it); y_s = z_s × x_s.
Azimuths are measured in the tangent plane from x_s towards y_s. A ray in either direction sees
the same physical film. The film's layers are listed from its front face (towards -z) to its back
face; a ray going towards -z meets them in the other order.

CONVENTIONS. Both Optiland and the engine take a complex index n + ik with k >= 0, that is time
dependence exp(-iωt), so the engine's coefficients are used as they are. The engine's p is s × k
for every wave (docs/contract.md); Optiland's is k × s, with its own choice of s. The matrix is
therefore not copied entry by entry: the engine's field basis is projected onto Optiland's for
each ray, which holds whatever sign either side chooses.

THE ENGINE is BerremanCalculator's native library (berreman_native.dll, libberreman_native.so or
libberreman_native.dylib), called through ctypes. It is found from BERREMAN_NATIVE (the library's
full path), else in the BerremanCalculator install folder above this file, else in a CMake build
of the source repository. It is checked with Optiland's NumPy backend. The engine computes in
NumPy whatever the backend, so under PyTorch the film would not be differentiable.

UNITS. Thicknesses and pitches in micrometres, like Optiland's wavelengths; angles in degrees.
"""

from __future__ import annotations

import ctypes
import math
import os
import sys
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, ClassVar

import numpy as np
import optiland.backend as be
from optiland.coatings import BaseCoatingPolarized
from optiland.jones import BaseJones
from optiland.materials import BaseMaterial
from optiland.rays import PolarizedRays

# ---- the engine -------------------------------------------------------------------------------

_ABI_VERSION = 0
_STATUS = {
    0: "ok",
    1: "invalid argument",
    2: "not implemented",
    3: "scratch too small",
    4: "incident medium absorbing",
    5: "grazing in an outer medium",
    6: "singular layer",
    7: "outer eigenproblem failed",
    8: "singular boundary",
    9: "incident wave evanescent",
    10: "outer medium defective",
}


class _Problem(ctypes.Structure):
    """bc_problem in berreman.h, field for field."""

    _fields_ = [
        ("n_layers", ctypes.c_int32),
        ("n_spectral", ctypes.c_int32),
        ("n_points", ctypes.c_int32),
        ("n_depths", ctypes.c_int32),
        ("M", ctypes.c_void_p),
        ("wavelength", ctypes.c_void_p),
        ("thickness", ctypes.c_void_p),
        ("point_spectral", ctypes.c_void_p),
        ("point_xi", ctypes.c_void_p),
        ("field_depths", ctypes.c_void_p),
    ]


class _Output(ctypes.Structure):
    """bc_output in berreman.h: any pointer may be null, and that output is not computed."""

    _fields_ = [
        ("jones", ctypes.c_void_p),
        ("power", ctypes.c_void_p),
        ("layer_absorption", ctypes.c_void_p),
        ("fields", ctypes.c_void_p),
        ("diagnostics", ctypes.c_void_p),
    ]


# bc_point_diagnostics: four int32 then three doubles, 40 bytes.
_DIAGNOSTICS = np.dtype(
    [
        ("status", np.int32),
        ("max_steps", np.int32),
        ("opaque", np.int32),
        ("reserved", np.int32),
        ("front_condition", np.float64),
        ("incident_condition", np.float64),
        ("exit_condition", np.float64),
    ]
)


def _library_name() -> str:
    if sys.platform == "win32":
        return "berreman_native.dll"
    if sys.platform == "darwin":
        return "libberreman_native.dylib"
    return "libberreman_native.so"


def find_engine() -> Path:
    """Where the engine library is: BERREMAN_NATIVE, else the install folder above this file,
    else a CMake build of the source repository above it."""
    explicit = os.environ.get("BERREMAN_NATIVE")
    if explicit:
        return Path(explicit)
    name = _library_name()
    here = Path(__file__).resolve().parent
    for folder in [here, *here.parents][:6]:
        for candidate in (
            folder / name,
            folder / "build" / "Release" / name,
            folder / "build" / name,
        ):
            if candidate.is_file():
                return candidate
    raise FileNotFoundError(
        f"{name} was not found. Set BERREMAN_NATIVE to its full path; it is in the "
        "BerremanCalculator install folder, or in build/Release after a CMake build."
    )


class Engine:
    """The native engine: one call evaluates many points, each with its own 6×6 matrices.

    A point here is one ray. The engine's "spectral index" (one wavelength and sample
    orientation, which the materials depend on) is per ray as well, because each ray sees the
    film's tensors in its own frame.
    """

    def __init__(self, path: str | os.PathLike | None = None, workers: int | None = None):
        self.path = Path(path) if path is not None else find_engine()
        self._lib = ctypes.CDLL(str(self.path))
        self._lib.bc_abi_version.restype = ctypes.c_int32
        self._lib.bc_plan.restype = ctypes.c_int
        self._lib.bc_plan.argtypes = [ctypes.POINTER(_Problem), ctypes.POINTER(ctypes.c_int64)]
        self._lib.bc_evaluate.restype = ctypes.c_int
        self._lib.bc_evaluate.argtypes = [
            ctypes.POINTER(_Problem),
            ctypes.c_int32,
            ctypes.c_int32,
            ctypes.c_void_p,
            ctypes.c_int64,
            ctypes.POINTER(_Output),
        ]
        abi = self._lib.bc_abi_version()
        if abi != _ABI_VERSION:
            raise RuntimeError(
                f"{self.path} has engine ABI {abi}; this file expects {_ABI_VERSION}"
            )
        self.workers = workers or max(1, min(8, os.cpu_count() or 1))

    def jones(
        self,
        M: np.ndarray,
        wavelength: np.ndarray,
        thickness: np.ndarray,
        xi: np.ndarray,
    ) -> tuple[np.ndarray, np.ndarray]:
        """The Jones matrices of n points, point i with the matrices M[i].

        Args:
            M: complex, shape (n, n_layers + 2, 6, 6): the incident medium, the layers in the
                order the ray meets them, the exit medium, each in the ray's own frame.
            wavelength: shape (n,), vacuum wavelength, in the unit of the thicknesses.
            thickness: shape (n_layers,), shared by every point.
            xi: shape (n,), the tangential wavevector k_x / k0, real.

        Returns:
            jones: complex, shape (n, 8): r_pp, r_ps, r_sp, r_ss, t_pp, t_ps, t_sp, t_ss, the
                first subscript the incident wave.
            status: shape (n,), the engine's status for each point (0 ok).
        """
        n, media = M.shape[0], M.shape[1]
        M = np.ascontiguousarray(M, dtype=np.complex128)
        wavelength = np.ascontiguousarray(wavelength, dtype=np.float64)
        thickness = np.ascontiguousarray(thickness, dtype=np.float64)
        xi = np.ascontiguousarray(xi, dtype=np.float64)
        spectral = np.arange(n, dtype=np.int32)
        jones = np.zeros((n, 8), dtype=np.complex128)
        diagnostics = np.zeros(n, dtype=_DIAGNOSTICS)
        if n == 0:
            return jones, diagnostics["status"].copy()

        problem = _Problem(
            n_layers=media - 2,
            n_spectral=n,
            n_points=n,
            n_depths=0,
            M=M.ctypes.data,
            wavelength=wavelength.ctypes.data,
            thickness=thickness.ctypes.data if media > 2 else None,
            point_spectral=spectral.ctypes.data,
            point_xi=xi.ctypes.data,
            field_depths=None,
        )
        output = _Output(jones=jones.ctypes.data, diagnostics=diagnostics.ctypes.data)
        need = ctypes.c_int64(0)
        status = self._lib.bc_plan(ctypes.byref(problem), ctypes.byref(need))
        if status != 0:
            raise RuntimeError(f"bc_plan refused the problem: {_STATUS.get(status, status)}")

        # Disjoint point ranges, each worker with its own scratch; ctypes releases the GIL.
        ranges = np.array_split(np.arange(n), min(self.workers, n))

        def run(points: np.ndarray) -> None:
            scratch = np.empty(max(int(need.value), 1), dtype=np.uint8)
            self._lib.bc_evaluate(
                ctypes.byref(problem),
                int(points[0]),
                len(points),
                scratch.ctypes.data,
                int(need.value),
                ctypes.byref(output),
            )

        with ThreadPoolExecutor(len(ranges)) as pool:
            list(pool.map(run, [r for r in ranges if len(r)]))
        return jones, diagnostics["status"].copy()


_default_engine: Engine | None = None


def default_engine() -> Engine:
    """One engine for the process, loaded on first use."""
    global _default_engine
    if _default_engine is None:
        _default_engine = Engine()
    return _default_engine


# ---- films ------------------------------------------------------------------------------------


def uniaxial(no: complex, ne: complex, axis: np.ndarray) -> np.ndarray:
    """ε = no² I + (ne² - no²) v vᵀ, v a unit optic axis in the film's frame."""
    v = np.asarray(axis, dtype=np.float64)
    v = v / np.linalg.norm(v)
    return no**2 * np.eye(3, dtype=np.complex128) + (ne**2 - no**2) * np.outer(v, v)


@dataclass
class Layer:
    """One uniform layer: thickness (µm) and dielectric tensor ε (3×3) in the film's frame
    (x_s, y_s, z_s). μ is 1 and there is no optical activity."""

    thickness: float
    eps: np.ndarray

    def to_dict(self) -> dict[str, Any]:
        eps = np.asarray(self.eps, dtype=np.complex128)
        return {
            "thickness": float(self.thickness),
            "eps": [[[float(e.real), float(e.imag)] for e in row] for row in eps],
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Layer:
        eps = np.array([[complex(re, im) for re, im in row] for row in data["eps"]])
        return cls(data["thickness"], eps)


class Film:
    """A film: its layers from the front face (towards -z) to the back face."""

    _registry: ClassVar[dict[str, type[Film]]] = {}

    def __init_subclass__(cls, **kwargs):
        super().__init_subclass__(**kwargs)
        Film._registry[cls.__name__] = cls

    def layers(self) -> list[Layer]:  # pragma: no cover
        raise NotImplementedError

    def to_dict(self) -> dict[str, Any]:  # pragma: no cover
        raise NotImplementedError

    @staticmethod
    def from_dict(data: dict[str, Any]) -> Film:
        kind = Film._registry[data["type"]]
        return kind._from_dict(data)


@dataclass
class Stack(Film):
    """Any layers, each with its own ε, from the front face to the back face."""

    items: list[Layer] = field(default_factory=list)

    def layers(self) -> list[Layer]:
        return list(self.items)

    def to_dict(self) -> dict[str, Any]:
        return {"type": "Stack", "layers": [layer.to_dict() for layer in self.items]}

    @classmethod
    def _from_dict(cls, data: dict[str, Any]) -> Stack:
        return cls([Layer.from_dict(d) for d in data["layers"]])


@dataclass
class OPlate(Film):
    """One uniaxial layer, its optic axis tilted from the normal and turned in the plane.

    Args:
        thickness: µm.
        no, ne: ordinary and extraordinary indices.
        tilt: the optic axis's angle from the normal z_s, degrees.
        azimuth: its azimuth in the tangent plane, from x_s towards y_s, degrees.
    """

    thickness: float
    no: float
    ne: float
    tilt: float = 0.0
    azimuth: float = 0.0

    def layers(self) -> list[Layer]:
        t, a = math.radians(self.tilt), math.radians(self.azimuth)
        axis = [math.sin(t) * math.cos(a), math.sin(t) * math.sin(a), math.cos(t)]
        return [Layer(self.thickness, uniaxial(self.no, self.ne, axis))]

    def to_dict(self) -> dict[str, Any]:
        return {"type": "OPlate", **self.__dict__}

    @classmethod
    def _from_dict(cls, data: dict[str, Any]) -> OPlate:
        return cls(**{k: v for k, v in data.items() if k != "type"})


@dataclass
class Cholesteric(Film):
    """A uniaxial layer whose director lies in the plane of the film and turns about the normal
    through the thickness, evaluated as uniform sublayers each at its midpoint angle, as the
    BerremanCalculator program does.

    Args:
        thickness: µm.
        no, ne: ordinary and extraordinary indices.
        pitch: µm, one full turn of the director.
        hand: +1 right-handed, -1 left-handed: the director turns by +hand·360° per pitch
            going from the front face towards the back (+z_s).
        start: the director's azimuth on the first layer's face, degrees from x_s.
        sublayers: how many; 0 means 64 per pitch.
        first_layer: "front" (the face towards -z, the default) or "back": which face the first
            layer, and so the start azimuth, is on. The handedness is the film's own either way.
    """

    thickness: float
    no: float
    ne: float
    pitch: float
    hand: int = 1
    start: float = 0.0
    sublayers: int = 0
    first_layer: str = "front"

    def layers(self) -> list[Layer]:
        if self.first_layer not in ("front", "back"):
            raise ValueError('first_layer is "front" or "back"')
        n = self.sublayers or max(1, math.ceil(64 * self.thickness / self.pitch))
        h = self.thickness / n
        first = self.thickness if self.first_layer == "back" else 0.0
        hand = -1.0 if self.hand < 0 else 1.0
        out = []
        for i in range(n):
            depth = (i + 0.5) * h  # from the front face, along +z_s
            phi = math.radians(self.start) + hand * 2 * math.pi * (depth - first) / self.pitch
            out.append(Layer(h, uniaxial(self.no, self.ne, [math.cos(phi), math.sin(phi), 0.0])))
        return out

    def to_dict(self) -> dict[str, Any]:
        return {"type": "Cholesteric", **self.__dict__}

    @classmethod
    def _from_dict(cls, data: dict[str, Any]) -> Cholesteric:
        return cls(**{k: v for k, v in data.items() if k != "type"})


# ---- the Jones model --------------------------------------------------------------------------


def _unit(v: np.ndarray) -> np.ndarray:
    return v / np.linalg.norm(v, axis=-1, keepdims=True)


def _dot(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.sum(a * b, axis=-1)


def _complex_index(material: BaseMaterial, w: np.ndarray) -> np.ndarray:
    n = np.broadcast_to(be.to_numpy(material.n(be.array(w))), w.shape)
    k = np.broadcast_to(be.to_numpy(material.k(be.array(w))), w.shape)
    return n.astype(np.complex128) + 1j * k


class JonesBerreman(BaseJones):
    """The film's Jones matrix for each ray, in Optiland's s/p basis.

    calculate_matrix needs the surface normal at each ray, which Optiland passes to a coating's
    reflect and transmit but not to a Jones model; BerremanCoating passes it on. Without it the
    normal is taken as the local z axis, which is right for a plane surface only.
    """

    def __init__(
        self,
        material_pre: BaseMaterial,
        material_post: BaseMaterial,
        film: Film,
        engine: Engine | None = None,
        max_bytes: int = 64 << 20,
    ):
        self.material_pre = material_pre
        self.material_post = material_post
        self.film = film
        self.engine = engine
        self.max_bytes = max_bytes
        self.failures = 0  # rays the engine refused in the last call; they are blocked

    def calculate_matrix(
        self,
        rays,
        reflect: bool = False,
        aoi=None,
        normal: tuple[Any, Any, Any] | None = None,
    ):
        engine = self.engine or default_engine()
        k0 = np.stack([be.to_numpy(rays.L0), be.to_numpy(rays.M0), be.to_numpy(rays.N0)], -1)
        k1 = np.stack([be.to_numpy(rays.L), be.to_numpy(rays.M), be.to_numpy(rays.N)], -1)
        k0, k1 = k0.astype(np.float64), k1.astype(np.float64)
        n = k0.shape[0]
        w = np.broadcast_to(be.to_numpy(rays.w), (n,)).astype(np.float64)
        if normal is None or any(c is None for c in normal):
            normal = (0.0, 0.0, 1.0)
        nrm = np.stack([np.broadcast_to(be.to_numpy(c), (n,)) for c in normal], -1)
        nrm = nrm.astype(np.float64)

        # Optiland's basis for these rays, from its own routine, so it is the one it multiplies by.
        s_o, p0_o, p1_o, _, _ = PolarizedRays.get_local_basis(be.array(k0), be.array(k1))
        s_o, p0_o, p1_o = (be.to_numpy(v).astype(np.float64) for v in (s_o, p0_o, p1_o))

        # The film's axes at each ray: z_s towards +z, x_s the local x on the tangent plane.
        zs = _unit(nrm) * np.where(nrm[:, 2:3] < 0, -1.0, 1.0)
        x = np.array([1.0, 0.0, 0.0]) - zs[:, 0:1] * zs
        y = np.array([0.0, 1.0, 0.0]) - zs[:, 1:2] * zs
        xs = _unit(np.where(_dot(x, x)[:, None] < 1e-12, y, x))
        ys = np.cross(zs, xs)

        # The ray's frame: z along the normal in the direction the ray goes, x tangential, y = s.
        forward = _dot(k0, zs) >= 0
        z = np.where(forward[:, None], zs, -zs)
        tang = k0 - _dot(k0, z)[:, None] * z
        st = np.linalg.norm(tang, axis=-1)
        xr = np.where((st > 1e-14)[:, None], tang / np.maximum(st, 1e-300)[:, None], xs)
        yr = np.cross(z, xr)

        n1 = _complex_index(self.material_pre, w)
        n2 = _complex_index(self.material_post, w)
        if np.any(np.abs(n1.imag) > 0):
            raise ValueError("the medium before the film must be lossless (k = 0)")
        xi = n1.real * st

        ok = (
            np.all(np.isfinite(k0), -1)
            & np.all(np.isfinite(k1), -1)
            & np.all(np.isfinite(nrm), -1)
            & np.isfinite(w)
            & (w > 0)
        )

        # Engine coefficients, r then t, first subscript incident.
        layers = self.film.layers()
        coef = np.zeros((n, 8), dtype=np.complex128)
        status = np.zeros(n, dtype=np.int32)
        # R: the ray frame's axes in the film's frame, rows x, y, z; ε_ray = R ε Rᵀ.
        R = np.stack(
            [np.stack([_dot(e, xs), _dot(e, ys), _dot(e, zs)], -1) for e in (xr, yr, z)],
            1,
        )
        eps = np.array([layer.eps for layer in layers], dtype=np.complex128).reshape(-1, 3, 3)
        thick = np.array([layer.thickness for layer in layers], dtype=np.float64)
        per_ray = (len(layers) + 2) * 36 * 16
        chunk = max(1, self.max_bytes // per_ray)
        for direction in (True, False):
            idx = np.nonzero(ok & (forward == direction))[0]
            order = slice(None) if direction else slice(None, None, -1)
            for start in range(0, len(idx), chunk):
                part = idx[start : start + chunk]
                M = np.zeros((len(part), len(layers) + 2, 6, 6), dtype=np.complex128)
                eye = np.eye(3)
                M[:, 0, :3, :3] = (n1[part] ** 2)[:, None, None] * eye
                M[:, -1, :3, :3] = (n2[part] ** 2)[:, None, None] * eye
                if len(layers):
                    Rp = R[part]
                    M[:, 1:-1, :3, :3] = np.einsum(
                        "nab,lbc,ndc->nlad", Rp, eps[order], Rp, optimize=True
                    )
                M[:, :, 3:, 3:] = eye
                c, st_part = engine.jones(M, w[part], thick[order], xi[part])
                coef[part], status[part] = c, st_part
        good = ok & (status == 0)
        self.failures = int(np.count_nonzero(ok & (status != 0)))

        X = coef[:, 0:4] if reflect else coef[:, 4:8]  # pp, ps, sp, ss
        E = np.empty((n, 2, 2), dtype=np.complex128)  # out × in, on (p, s)
        E[:, 0, 0], E[:, 0, 1] = X[:, 0], X[:, 2]
        E[:, 1, 0], E[:, 1, 1] = X[:, 1], X[:, 3]

        # The engine's basis: s = y of the ray frame, p = s × k for each wave.
        s_e = yr
        p_in, p_out = np.cross(s_e, k0), np.cross(s_e, k1)
        B_in = np.stack(
            [
                np.stack([_dot(p_in, s_o), _dot(p_in, p0_o)], -1),
                np.stack([_dot(s_e, s_o), _dot(s_e, p0_o)], -1),
            ],
            1,
        )  # (a, b) along Optiland's s, p0  ->  (p, s) along the engine's
        B_out = np.stack(
            [
                np.stack([_dot(s_o, p_out), _dot(s_o, s_e)], -1),
                np.stack([_dot(p1_o, p_out), _dot(p1_o, s_e)], -1),
            ],
            1,
        )  # (p, s) along the engine's  ->  (a, b) along Optiland's s, p1
        J2 = B_out @ E @ B_in
        J2[~good] = 0.0

        J = np.zeros((n, 3, 3), dtype=np.complex128)
        J[:, :2, :2] = J2
        J[:, 2, 2] = -1.0 if reflect else 1.0  # the k component, as Optiland's own models
        # be.array keeps the backend's real dtype, so the two parts go across separately.
        return be.to_complex(be.array(J.real)) + 1j * be.to_complex(be.array(J.imag))


class BerremanCoating(BaseCoatingPolarized):
    """A thin anisotropic film on a surface, evaluated by the BerremanCalculator engine.

    Args:
        material_pre: the medium before the surface (the incident side of the film). It must be
            the surface's own material_pre, and lossless.
        material_post: the medium after the surface. It must be the surface's material_post.
        film: an OPlate, Cholesteric or Stack.
        engine: an Engine; by default one is loaded on first use (see find_engine).

    Only a polarized trace (Optic.set_polarization) carries the film's effect; an unpolarized
    trace ignores it, as it ignores Optiland's other polarizing coatings.
    """

    def __init__(
        self,
        material_pre: BaseMaterial,
        material_post: BaseMaterial,
        film: Film,
        engine: Engine | None = None,
    ):
        self.material_pre = material_pre
        self.material_post = material_post
        self.film = film
        self._jones = JonesBerreman(material_pre, material_post, film, engine)

    @property
    def jones(self) -> JonesBerreman:
        return self._jones

    def _interact(self, rays, reflect: bool, nx, ny, nz):
        if not isinstance(rays, PolarizedRays):
            rays.update()
            return rays
        jones = self._jones.calculate_matrix(rays, reflect=reflect, normal=(nx, ny, nz))
        rays.update(jones)
        return rays

    def reflect(self, rays, nx=None, ny=None, nz=None):
        return self._interact(rays, True, nx, ny, nz)

    def transmit(self, rays, nx=None, ny=None, nz=None):
        return self._interact(rays, False, nx, ny, nz)

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": self.__class__.__name__,
            "material_pre": self.material_pre.to_dict(),
            "material_post": self.material_post.to_dict(),
            "film": self.film.to_dict(),
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> BerremanCoating:
        return cls(
            BaseMaterial.from_dict(data["material_pre"]),
            BaseMaterial.from_dict(data["material_post"]),
            Film.from_dict(data["film"]),
        )


def add_film(surface, film: Film, engine: Engine | None = None) -> BerremanCoating:
    """Puts a film on a surface of an Optic, between the surface's own media, replacing its
    coating. Add the surface's neighbours first: the media are read now."""
    coating = BerremanCoating(surface.material_pre, surface.material_post, film, engine)
    surface.interaction_model.coating = coating
    return coating
