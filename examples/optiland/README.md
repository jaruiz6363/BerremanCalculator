# An Optiland coating with a Berreman film

`berreman_optiland.py` adds a coating to [Optiland](https://github.com/optiland/optiland), the
open-source Python lens design program. The coating puts a thin anisotropic film on a surface and
evaluates it with the BerremanCalculator engine, ray by ray, in a polarization ray trace.

Optiland's own coatings are isotropic. Its `ThinFilmCoating` gives each ray a diagonal Jones matrix
in s and p. This film is not isotropic: its effect depends on each ray's direction relative to the
film's own axes, and it couples s and p. A cholesteric liquid crystal, for example, reflects one
circular polarization and passes the other. Optiland's polarization analyses then include the film.

It is the same film as the [OpticStudio surface example](../zemax-uds/README.md), with the same
parameters, orientation and layer order, and it gives the same results.

## What you need

- Python 3.10 or later, with Optiland (checked with 0.6.3 on Python 3.12) and NumPy:
  `pip install optiland`. The newest Python can be ahead of Optiland's dependencies: on 3.15,
  `pip install optiland` fails because its `vtk` dependency has no package for 3.15 yet. Use an
  earlier Python, such as 3.12, if that happens.
- The BerremanCalculator engine, `berreman_native.dll` (`libberreman_native.so` on Linux,
  `libberreman_native.dylib` on macOS). The program installs it in its own folder, and this file
  finds it there (see [Finding the engine](#finding-the-engine)).

## Trying it

In this folder, run:

```
python cholesteric_plate.py
```

Run it where it is installed: from there it finds the engine in the program's folder. A copy
elsewhere needs `BERREMAN_NATIVE` (see [Finding the engine](#finding-the-engine)). On Windows the
Start menu has a shortcut to this folder: *Optiland coating example*.

It builds the OpticStudio example's cholesteric plate in Optiland: a 2 mm N-BK7 plate in air with
the film on its front surface and an uncoated back surface. It then prints the chief ray's
transmitted intensity for the two circular polarizations, beside OpticStudio's figures for the same
plate:

```
Transmitted intensity of the chief ray, Y phase +90 / -90 degrees
   um  field   Optiland + engine     OpticStudio
 0.60    0 deg     0.906 / 0.048   0.906 / 0.048
 0.60   20 deg     0.894 / 0.061   0.893 / 0.061
 0.70    0 deg     0.888 / 0.762   0.888 / 0.762
 0.70   20 deg     0.920 / 0.900   0.920 / 0.899
```

At 0.6 µm, inside the film's reflection band, one circular polarization passes and the other is
reflected. At 0.7 µm, outside it, both pass. The two programs agree to 0.001.

## Using it in a lens

```python
from optiland.rays import PolarizationState

from berreman_optiland import OPlate, add_film

# ... build the Optic: surfaces, aperture, fields, wavelengths ...
add_film(lens.surfaces[1], OPlate(thickness=2.3, no=1.52, ne=1.63, tilt=35, azimuth=50))
lens.updater.set_polarization(PolarizationState(is_polarized=True, Ex=1, Ey=0, phase_x=0, phase_y=0))
```

`add_film` replaces the surface's coating. It reads the media on either side from the surface, so
add the surface's neighbours before calling it.

- **A polarized trace is needed.** Without one, Optiland ignores the film, as it ignores its other
  polarizing coatings.
- **`Optic.trace` applies the polarization state to the rays' intensity; `trace_generic` does
  not.** After `trace_generic`, call `rays.update_intensity(lens.polarization_state)`, as
  `cholesteric_plate.py` does.

## The films

Thicknesses and pitches are in micrometres, like Optiland's wavelengths. Angles are in degrees.

| Film | Parameters |
|---|---|
| `OPlate` | `thickness`, `no`, `ne`; `tilt` of the optic axis from the normal; its `azimuth` in the plane of the film |
| `Cholesteric` | `thickness`, `no`, `ne`, `pitch` (one full turn); `hand` +1 right, −1 left; `start`, the director's azimuth on the first layer's face; `sublayers` (0: 64 per pitch); `first_layer`, `"front"` (the default) or `"back"` |
| `Stack` | any list of `Layer(thickness, eps)`, each with its own 3×3 dielectric tensor ε in the film's frame, from the front face to the back |

- **O-plate.** One uniaxial layer, with its optic axis tilted from the normal and turned in the
  plane of the film.
- **Cholesteric.** A uniaxial layer whose director lies in the plane of the film and turns about
  the normal through the thickness. It is evaluated as uniform sublayers, each at its midpoint
  angle, as the BerremanCalculator program does. For a reflection band at λ₀ at normal incidence,
  choose the pitch so that λ₀ = P (nₒ + nₑ)/2.
- **Stack.** Anything else: a biaxial or absorbing layer is just its ε. `uniaxial(no, ne, axis)`
  writes a uniaxial ε. μ is 1 and there is no optical activity.

The indices do not change with wavelength. A dispersive film is a `Film` subclass whose `layers()`
reads the wavelength, which is a change to this file.

## Orientation

The film's axes are fixed to the **surface**, in the surface's local frame:
- z_s is the normal, pointing towards +z at the vertex;
- x_s is the local x axis projected onto the tangent plane (local y where x is nearly normal to it);
- y_s = z_s × x_s.

Azimuths are measured in the tangent plane, from x_s towards y_s. A ray from either side sees the
same physical film. On a curved surface the axes follow the local normal.

### Layer order

A film's layers run from its front face (towards −z) to its back face. **You choose which face
holds the first layer**; nothing infers it from the media either side:
- `first_layer="front"` (the default): the first layer is on the face towards −z;
- `first_layer="back"`: it is on the face towards +z.

For a film on the back of a lens, where light goes from glass to air, choose `"back"` to put the
first layer against the air. For a cholesteric, the first layer is where `start` applies. Its
handedness is the film's own: a right-handed helix is right-handed whichever face it starts from.
A `Stack` is listed from the front face.

## How it works

For each ray, after Optiland has refracted or reflected it, the coating:
1. builds the ray's local frame: z along the normal, in the direction the ray goes; x the ray's
   tangential direction, so the plane of incidence is x–z; and y = z × x, which is s;
2. writes each layer's ε in that frame;
3. asks the engine for the 2×2 Jones matrices between the media either side;
4. expresses the transmission (or reflection) matrix in Optiland's s/p basis for that ray, and
   hands it to Optiland.

**The coating is the whole interface.** In Optiland, a surface with no coating changes no field,
and `FresnelCoating` is the bare interface. So the film's matrix replaces the interface's: it
includes the reflections at the film's faces and between the media either side. Nothing is divided
out, unlike in OpticStudio, which applies its own bare interface after a surface DLL.

**Conventions.** Both Optiland and the engine write a complex index as n + ik, which is time
dependence exp(−iωt), so the engine's coefficients are used as they are. The engine's p is s × k
for every wave; Optiland's is k × s, with its own choice of s. So the matrix is not copied entry by
entry. Instead the engine's field basis is projected onto Optiland's for each ray, which holds
whatever sign either side chooses.

**Rays the engine refuses** are blocked, and `coating.jones.failures` counts
them for the last surface call.

## Finding the engine

`berreman_optiland.py` looks for the engine library in this order:
1. the full path in the environment variable `BERREMAN_NATIVE`;
2. the folders above this file: the BerremanCalculator install folder, when this folder is the
   installed `examples\optiland`;
3. `build\Release` or `build` in those folders: a CMake build of the source repository.

If you copied this folder elsewhere, set `BERREMAN_NATIVE`, for example on Windows:

```
set BERREMAN_NATIVE=C:\Program Files\BerremanCalculator\berreman_native.dll
```

For an install for one user, the folder is `%LOCALAPPDATA%\Programs\BerremanCalculator`. On macOS
the engine is in `BerremanCalculator.app/Contents/MacOS`, beside this folder's installed copy. On
Linux it is inside the AppImage, in `usr/lib/berremancalculator`; extract it with
`./BerremanCalculator-<version>-x86_64.AppImage --appimage-extract` and point `BERREMAN_NATIVE`
at `squashfs-root/usr/lib/berremancalculator/libberreman_native.so`.

## Limits

- **NumPy only.** The engine computes in NumPy whatever Optiland's backend is. It is checked with
  the NumPy backend. Under PyTorch the film would not be differentiable, so its thickness and axes
  cannot be optimization variables.
- **The medium before the film must be lossless.** The medium after it may absorb.
- **The media must be the surface's own.** `add_film` takes them from the surface. If you build a
  `BerremanCoating` yourself, give it the surface's `material_pre` and `material_post`; otherwise
  the film's exit medium is not the one Optiland refracted into.

## Speed

Each ray is one evaluation by the engine, spread over up to eight threads. On a 16-thread PC, an
O-plate costs about 0.005 ms per ray. A cholesteric of 640 sublayers costs about 0.3 ms per ray,
which is fine for polarization analyses and slow for very large ray counts.

## How it is checked

`test_berreman_optiland.py` (35 checks; `pytest test_berreman_optiland.py`) compares the 3D
electric field Optiland carries after the surface with references that share nothing with the
coating's frame and basis code:
- **No film.** The Fresnel coefficients in closed form, transmitted and reflected. At normal
  incidence the reflected field is r times the incident one, whatever its polarization.
- **One isotropic layer.** The Airy formulas in closed form, s and p, transmitted and reflected,
  and R + T = 1.
- **An O-plate and a cholesteric.** The BerremanCalculator command line on a stack file, with the
  film oriented by Euler angles and twist, and the plane of incidence by the sample azimuth.
  Several planes of incidence, both hands, rays going either way. These checks need the `berreman`
  command (on the PATH, in the install folder, or `BERREMAN_CLI`) and are skipped without it.
- **Layer order.** A cholesteric with its first layer on the back face is the film started on the
  front face at the azimuth the helix has there.
- **A curved surface.** The film follows the local normal.
- **The plate in an Optic.** The chief ray's intensity matches OpticStudio's figures above, and a
  coating survives `to_dict` and `from_dict`.

The agreement is 10⁻¹² or better with the closed forms, and 10⁻⁸ with the command line, which
prints about ten significant figures.

**Optiland's own coatings are not used as references.** On Optiland 0.6.3 and on its development
version of 2 November 2026 (4f5e9b4a):
- `FresnelCoating` at normal incidence reflects a field at 45° to −45°, where an isotropic
  interface reflects every field by the same r;
- `ThinFilmCoating`'s reflection coefficient is the complex conjugate of the Airy value its
  transmission agrees with;
- `ThinFilmCoating`'s p transmission at oblique incidence is the tangential field. For a lossless
  layer at 60°, R + T comes to 2.66.
