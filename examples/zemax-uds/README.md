# An OpticStudio surface with a Berreman film

`US_BERREMAN.dll` is a user-defined surface (UDS) for Ansys Zemax OpticStudio. It puts a thin
anisotropic film on a surface and evaluates it with the BerremanCalculator engine, ray by ray,
during polarization ray tracing. OpticStudio's coatings are isotropic and diagonal in s and p.
This film is not: its effect depends on each ray's direction relative to the film's own axes, and
it couples s and p. A cholesteric liquid crystal, for example, reflects one circular polarization
and passes the other.

The engine is compiled into the DLL, so the one file is all OpticStudio needs.

## The films

Parameter 0 chooses the film. Thicknesses and pitches are in micrometres, whatever the lens units.

| Par | O-plate (model 0) | Cholesteric (model 1) |
|---|---|---|
| 0 | 0 | 1 |
| 1 | film thickness (µm) | film thickness (µm) |
| 2 | ordinary index nₒ | ordinary index nₒ |
| 3 | extraordinary index nₑ | extraordinary index nₑ |
| 4 | optic-axis tilt from the normal (°) | pitch (µm), one full turn |
| 5 | optic-axis azimuth (°) | handedness: +1 right, −1 left |
| 6 | | starting azimuth of the director (°) |
| 7 | | sublayers (0: 64 per pitch) |

- **O-plate.** One uniaxial layer, with its optic axis tilted from the normal and turned in the
  plane of the film.
- **Cholesteric.** A uniaxial layer whose director lies in the plane of the film and turns about
  the normal through the thickness. It is evaluated as uniform sublayers, each at its midpoint
  angle, as the BerremanCalculator program does. For a reflection band at λ₀ at normal incidence,
  choose the pitch so that λ₀ = P (nₒ + nₑ)/2. The band's width is about P (nₑ − nₒ).

The surface itself is a standard conic: OpticStudio's curvature and conic columns.

## Orientation

The film's axes are fixed to the **surface**, not to the ray:
- x_s is the global X axis projected onto the surface's tangent plane (global Y where X is nearly
  normal to it);
- z_s is the normal, pointing towards +z at the vertex;
- y_s = z_s × x_s.

Azimuths are measured in the tangent plane, from x_s towards y_s. A ray from either side sees the
same physical film. On a flat surface x_s is simply the global X.

## What the DLL does for each real ray

1. Finds the intercept and the normal, and refracts the ray by Snell's law between the media either
   side. The film is thin and does not move the ray.
2. Builds the ray's local frame: z along the normal, in the direction the ray goes; x its tangential
   direction, so the plane of incidence is x–z; and y = z × x, which is s. p = s × k for every wave,
   the BerremanCalculator convention.
3. Writes the film's dielectric tensor in that frame, and asks the engine for the 2×2 transmission
   Jones matrix between the media either side.
4. Applies the matrix, s–p coupling included, to the ray's electric field, and returns the field to
   OpticStudio (`did_polar = 2`). The full matrix is the reason the field is set directly. The s and
   p coefficients OpticStudio also accepts are diagonal and could not represent this film.

Not handled: a mirror surface (the ray is reflected, and its polarization left to OpticStudio), and
absorbing media either side (OpticStudio passes real indices).

## Building

The DLL is a target of the main CMake build:

```
cmake -S . -B build
cmake --build build --config Release --target us_berreman
```

This gives `build/Release/US_BERREMAN.dll`. Copy it into OpticStudio's
`Documents\Zemax\DLL\Surfaces` folder, choose the surface type **User Defined** with that DLL, and
turn polarization on in the System Explorer.

`uds_abi.h` declares the two structures OpticStudio passes, in the layout OpticStudio's own
`usersurf.h` gives, so the example builds without it.

## Threads

OpticStudio calls the DLL from several threads at once. Everything per ray is on the stack or in
thread-local buffers, and nothing static is written after loading.

## Speed

Each ray is one evaluation by the engine: one layer for an O-plate, a few hundred sublayers for a
cholesteric of several pitches. A cholesteric therefore costs of the order of 0.1 ms per ray, which
is fine for polarization analyses and slow for very large ray counts.

## How it is checked

The managed suite calls the DLL as OpticStudio does and compares the outgoing field with the
BerremanCalculator API. The API orients the film by its own Euler angles and twist, and the plane of
incidence by the sample azimuth, so the two share only the engine.
- **No film.** The field is the closed-form Fresnel transmission.
- **An O-plate.** Several planes of incidence, both incident polarizations, and the ray from behind.
- **A cholesteric.** Both handednesses, in and out of its band.
- **Geometry.** The intercept and normal on a sphere.

The agreement is 10⁻¹² or better.

**Still to be checked against OpticStudio itself:** how OpticStudio normalises the field it is
given. The DLL returns the transmitted field amplitude, E-field per unit incident E-field.
OpticStudio may expect the field scaled so that |E|² is the transmitted power, which differs by
√(n₂ cos θ₂ / n₁ cos θ₁). The check is an uncoated surface, where the DLL's field must reproduce
OpticStudio's own Fresnel result.
