# Copyright (c) 2026 Javier Ruiz
# SPDX-License-Identifier: MIT

"""cholesteric_plate.py — the OpticStudio example's cholesteric plate, in Optiland.

A 2 mm N-BK7 plate in air with a right-handed cholesteric film on its front surface: pitch
0.38 µm, 3.8 µm thick, nₒ = 1.5, nₑ = 1.7, so its reflection band is centred near 0.6 µm. Its back
surface is uncoated (Optiland's FresnelCoating). The beam is 10 mm across, at fields of 0° and
20°, and circularly polarized: Ex = Ey with Ey a quarter wave ahead (+90°) or behind (-90°).

It prints the chief ray's transmitted intensity at 0.6 µm (in the band) and 0.7 µm (outside), for
both circular polarizations, beside what OpticStudio gives for the same plate with US_BERREMAN.dll
(examples/zemax-uds/README.md).

    python cholesteric_plate.py
"""

from __future__ import annotations

import math

import optiland.backend as be
from optiland.materials import Material
from optiland.optic import Optic
from optiland.rays import PolarizationState

from berreman_optiland import Cholesteric, add_film

# OpticStudio 2022 R2.02, polarization ray trace through us_berreman_cholesteric.zmx:
# (wavelength µm, field °) -> (Y phase +90°, Y phase -90°).
OPTICSTUDIO = {
    (0.6, 0.0): (0.906, 0.048),
    (0.6, 20.0): (0.893, 0.061),
    (0.7, 0.0): (0.888, 0.762),
    (0.7, 20.0): (0.920, 0.899),
}

FILM = Cholesteric(thickness=3.8, no=1.5, ne=1.7, pitch=0.38, hand=+1, start=0.0)


def plate(wavelength_um: float, phase_y_deg: float, film=FILM) -> Optic:
    """The plate, at one wavelength, with the film on its front surface."""
    lens = Optic()
    lens.surfaces.add(index=0, thickness=math.inf)
    glass = Material("N-BK7", catalog="schott")
    lens.surfaces.add(index=1, thickness=2.0, material=glass, is_stop=True)
    lens.surfaces.add(index=2, thickness=10.0, coating="fresnel")
    lens.surfaces.add(index=3)
    add_film(lens.surfaces[1], film)

    lens.set_aperture(aperture_type="EPD", value=10.0)
    lens.fields.set_type("angle")
    lens.fields.add(y=0.0)
    lens.fields.add(y=20.0)
    lens.wavelengths.add(value=wavelength_um, is_primary=True)
    amplitude = 1 / math.sqrt(2)
    lens.updater.set_polarization(
        PolarizationState(
            is_polarized=True,
            Ex=amplitude,
            Ey=amplitude,
            phase_x=0.0,
            phase_y=math.radians(phase_y_deg),
        )
    )
    return lens


def chief_ray_intensity(lens: Optic, field_deg: float) -> float:
    """The chief ray's intensity at the image, per unit incident intensity."""
    wavelength = lens.wavelengths.primary_wavelength.value
    rays = lens.trace_generic(Hx=0.0, Hy=field_deg / 20.0, Px=0.0, Py=0.0, wavelength=wavelength)
    # trace_generic leaves the intensity as launched; this applies the polarization state.
    rays.update_intensity(lens.polarization_state)
    return float(be.to_numpy(rays.i)[0])


def main() -> None:
    # ASCII only: a Windows console may not print other characters.
    print("Transmitted intensity of the chief ray, Y phase +90 / -90 degrees")
    print(f"{'um':>5} {'field':>6}   {'Optiland + engine':>17}   {'OpticStudio':>13}")
    for (wavelength, field), (zemax_plus, zemax_minus) in OPTICSTUDIO.items():
        plus = chief_ray_intensity(plate(wavelength, +90.0), field)
        minus = chief_ray_intensity(plate(wavelength, -90.0), field)
        print(
            f"{wavelength:5.2f} {field:4.0f} deg   {plus:7.3f} / {minus:5.3f}   "
            f"{zemax_plus:5.3f} / {zemax_minus:5.3f}"
        )


if __name__ == "__main__":
    main()
