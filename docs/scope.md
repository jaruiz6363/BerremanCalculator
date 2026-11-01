# Scope

What BerremanCalculator computes and what it leaves out. Where a decision is still open it says so.

## What it is

An evaluator. Given a stack, the wavelengths and the angles of incidence, it returns the optical
response. It never changes the stack: there is no merit function and no optimiser. Fitting and
design belong to other programs (ThinFilmDesigner designs isotropic coatings).

## The physical model

- Plane waves, linear optics, one wavelength at a time.
- A stack of homogeneous layers with parallel planar interfaces, between an incident medium and an
  exit medium.
- Each medium is described at each wavelength by Berreman's 6×6 matrix: permittivity, permeability
  and the magnetoelectric blocks. Any element may disperse ([material-model.md](material-model.md)),
  so isotropic, uniaxial, biaxial, monoclinic and triclinic crystals, magneto-optic and chiral media,
  and ferrites are all cases of it, rotated by Euler angles.
- **Anisotropic incident and exit media are part of the model from the start.** The incident one
  must be lossless.
- Layers are coherent. A twisted (cholesteric) layer is evaluated as uniform sublayers, and the
  result is checked by doubling them. **Open:** incoherent (thick) layers, combined in intensity;
  a stack that asks for one is refused.

## Conventions

Fixed in [the contract](contract.md), Section 1: the time dependence, the sign of the extinction
coefficient, the axes and the Euler-angle convention, the definition of p and s in each medium, and
the signs of the reflection coefficients. Every result the program reports states them.

## Outputs

All are built:

1. Jones reflection and transmission coefficients: r_pp, r_ps, r_sp, r_ss and the t's.
2. Reflectance, transmittance and absorptance for each incident wave.
3. Mueller matrices in reflection and transmission.
4. Ellipsometric quantities: Ψ and Δ, and the generalised ones for anisotropic samples.
5. Absorption in each layer.
6. Fields against depth, in the stack and in the outer media.

From them the GUI draws spectra against any swept variable, the Mueller matrix as sixteen panels,
conoscopic maps and field and index profiles. The CLI and the MCP server give the same numbers.

## Interfaces

One C# API, used by three programs: a GUI (Avalonia), a command line and an MCP server. Installers
for Windows, macOS (Apple Silicon) and Linux are built by scripts kept in a separate repository.

## Verification

The purpose of the program is to reproduce examples from the literature, so verification is built
around them. Each example is a sample file and a test, with the paper's numbers and how closely
they are matched. A second, independent calculation is used where one exists. For isotropic stacks,
the 4×4 result must agree with the 2×2 (Abelès) result.

## Exchange with other programs

- **ThinFilmDesigner** designs (.tfd) are read and written. ThinFilmDesigner optimises isotropic
  coatings, and this program evaluates anything, so a coating can move between them. The tests
  agree with ThinFilmDesigner's engine to 10⁻¹² on its own samples.
- **ZEMAX and Optiland coatings** go through ThinFilmDesigner, which reads and writes both.
- **A design report** lists the layers and every material as plain tables, for a program without
  dispersion formulas.

## Left out

- Optimisation and fitting, by design.
- Incoherent layers (above).
- Rough or graded interfaces, except as thin layers; non-planar geometry; nonlinear optics.

## Order of the work, as it was done

The contract came first, then the list of literature examples, the native engine (tested against
the 2×2 limit and the first examples), the C# API and the command line, the MCP server, the GUI
and the installers. The GUI was then rebuilt as a stack editor, the general material model was
added, and the analyses followed.

## The earlier implementation

The author has an earlier, unpublished Berreman implementation (VB.NET, with C++ and C# ports). It
is a reference for structure only; none of its code is carried over. A survey of it found:

- Both outer media had to be isotropic.
- The degenerate case, which includes every isotropic layer, was handled by fixed tolerances on
  the roots and eigenvectors.
- The time and extinction sign conventions were never stated, and the ports mixed them.
- The incoherent-layer code had wrong indices and used n times the angle where n·cos θ belongs.
- No results were checked against reference values.

Each of these is a requirement on the new engine.
