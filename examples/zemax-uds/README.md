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

### What OpticStudio does with the returned field

OpticStudio hands the DLL the **incident** field. After the DLL returns, OpticStudio still crosses
the surface as an uncoated interface, using whatever field the DLL set:
- it takes the field's components along the incident p and s;
- it multiplies them by its own Fresnel t_p and t_s;
- it puts them along the refracted p and s.

So the DLL returns the film's result divided by the bare interface's. That is, along the incident
p and s,

    [Ep′; Es′] = diag(1/t₀ₚ, 1/t₀ₛ) · t · [Ep; Es]

where t₀ is the engine's transmission with no film. These are the same Fresnel coefficients
OpticStudio uses. OpticStudio's own step multiplies by t₀ again, which leaves t · [Ep; Es] along
the refracted p and s. Any overall scale OpticStudio puts on its fields is kept. With no film, the
DLL hands back the field unchanged.

**Leave the surface's coating column blank.** OpticStudio would apply a coating in its own step
in place of the bare interface, and the film would then be counted together with that coating.

Not handled: a mirror surface (the ray is reflected, and its polarization left to OpticStudio), and
absorbing media either side (OpticStudio passes real indices).

## Example lens files

Two lens files open in OpticStudio once the DLL is in its `Surfaces` folder. Each is a 2 mm N-BK7
plate. The film is on its front surface, which is surface 1, of type User Defined. The beam is
10 mm in diameter at fields of 0° and 20°. The input polarization is circular: Jx = Jy = 1, with
a Y phase of 90°. To see the other hand, set the Y phase to −90° in System Explorer › Polarization.

- **`us_berreman_cholesteric.zmx`**: a right-handed cholesteric. Its pitch is 0.38 µm, its
  thickness 3.8 µm, nₒ = 1.5 and nₑ = 1.7. Its band is centred near 0.6 µm, so 0.6 µm is in the band
  and 0.7 µm is outside it.
- **`us_berreman_oplate.zmx`**: a 2.3 µm O-plate, nₒ = 1.52 and nₑ = 1.63, with its axis tilted
  35° at azimuth 50°.

Transmitted intensity through the cholesteric file, from OpticStudio's polarization ray trace
(Y phase +90° / −90°). The figures include the plate's uncoated back surface:

| λ | 0° field | 20° field |
|---|---|---|
| 0.6 µm (in band) | 0.906 / 0.048 | 0.893 / 0.061 |
| 0.7 µm (outside) | 0.888 / 0.762 | 0.920 / 0.899 |

## Installing

The Windows installer includes the built DLL. It is in `examples\zemax-uds\` in the install
folder, together with this page, the source, the Visual Studio project and the two lens files. The
Start menu also has a shortcut to that folder: *OpticStudio surface example*.

1. Copy `US_BERREMAN.dll` into OpticStudio's `Documents\Zemax\DLL\Surfaces` folder. Its exact
   place is under Setup › Project Preferences › Folders.
2. Open one of the lens files.

The DLL is self-contained. It has the engine compiled in and the C runtime linked statically, so
nothing else needs to be installed.

## Changing the film

All the optics of the film are in `us_berreman.cpp`. The engine (`bc_evaluate.cpp` and the files
it includes) knows nothing about O-plates or cholesterics: it is given, for one ray, a list of
layers, each a 6×6 material matrix M and a thickness, between two isotropic media, and returns
the Jones matrices. So a different film means changing `us_berreman.cpp` only:
- `Transmission()` decides how many layers there are and the thickness of each;
- `Uniaxial()` writes one layer's M from nₒ, nₑ and the optic axis. A biaxial, absorbing or
  magneto-optic layer is another such function: ε goes in the top-left 3×3 of M, μ in the
  bottom-right (1 on its diagonal for a non-magnetic film), and ρ, ρ′ in the other two blocks
  (`docs/contract.md`, Section 2, gives the layout);
- the parameter names OpticStudio shows are in `UserDefinedSurface5`, case 1, and the starting
  values in case 7.

Leave the engine's files as they are.

## Building in Visual Studio

You need Visual Studio 2022 or later, with the workload **Desktop development with C++** (chosen
in the Visual Studio Installer). Nothing else.

1. Copy the whole `examples\zemax-uds` folder somewhere you can write, such as Documents. The copy
   in the install folder is read-only. Keep its `engine` folder with it: the project finds the
   engine there.
2. Double-click `us_berreman.sln`. Visual Studio opens it.
3. In the toolbar, set the two drop-down boxes to **Release** and **x64**.
4. Choose Build › Build Solution (Ctrl+Shift+B). The Output window ends with
   `1 succeeded, 0 failed`.
5. The DLL is `bin\Release\US_BERREMAN.dll`, beside the solution. Copy it into OpticStudio's
   `Documents\Zemax\DLL\Surfaces` folder, replacing the one there. Close OpticStudio first, since
   it keeps a loaded DLL open.

In the Solution Explorer, Source Files holds `us_berreman.cpp`, the file to change, and the
engine's two `.cpp` files. **Debug** builds `bin\Debug\US_BERREMAN.dll`, which is slower but can
be stepped through in the debugger with OpticStudio attached (Debug › Attach to Process).

Every compiler warning is treated as an error, as in the engine's own build, so a mistake shows
up at once.

## Building with CMake

The DLL is also a target of the main CMake build, which is how the release is built and tested:

```
cmake -S . -B build -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded
cmake --build build --config Release --target us_berreman
```

This gives `build/Release/US_BERREMAN.dll`. Choose the surface type **User Defined** with that
DLL, and turn polarization on in the System Explorer.

The Visual Studio project and CMake compile the same three files; a test checks that they do.
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

The agreement is 10⁻¹² or better. The suite applies OpticStudio's step after the DLL, as described
above, and compares the field OpticStudio would carry on with.

**Against OpticStudio itself** (2022 R2.02, standalone, polarization ray trace through a flat
air–N-BK7 surface at 0° and 20° field):
- With no film, the field at the image matches OpticStudio's Standard surface to every digit
  printed: 7 decimals, for s, p and an elliptical input.
- For a 2.3 µm O-plate, the field is predicted from the engine's t and OpticStudio's own
  bare-surface run. This means film first, then OpticStudio's own p and s factors for the rest of
  the path. The prediction matches OpticStudio to 7 decimals, s–p coupling included.
- A right-handed cholesteric in its band (pitch 0.38 µm, 3.8 µm thick, at 0.6 µm, normal
  incidence) transmits 0.90 of one circular polarization and 0.048 of the other.
