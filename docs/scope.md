# Scope

What BerremanCalculator computes, what it leaves out, and the order the work is done in. Where a
decision is still open it says so.

## What it is

An evaluator. Given a stack, the wavelengths and the angles of incidence, it returns the optical
response. It never changes the stack: there is no merit function and no optimiser. Fitting and
design belong to other programs (ThinFilmDesigner designs isotropic coatings).

## The physical model

- Plane waves, linear optics, one wavelength at a time.
- A stack of homogeneous layers with parallel planar interfaces, between an incident medium and an
  exit medium.
- Each layer is described by its thickness and its 3×3 relative permittivity tensor at the
  wavelength. Isotropic, uniaxial and biaxial materials, rotated by Euler angles, are special cases
  of the tensor.
- **Anisotropic incident and exit media are part of the model from the start**, not added later.
- Layers may be coherent or incoherent (thick). How incoherence is treated is an open decision.

## Conventions

To be fixed in the contract before any code: the time dependence, the sign of the extinction
coefficient, the axes and the Euler-angle convention, the definition of p and s and of their
directions in each medium, and the sign conventions of the reflection coefficients. Every result
the program reports states them.

## Outputs (to be agreed)

Candidates, roughly in the order the literature uses them:

1. Jones reflection and transmission coefficients: r_pp, r_ps, r_sp, r_ss and the t's.
2. Reflectance, transmittance and absorptance for any incident polarisation.
3. Mueller matrices in reflection and transmission.
4. Ellipsometric quantities: Ψ and Δ, and the generalised ones for anisotropic samples.
5. Absorption in each layer.
6. Fields and absorption against depth.

## Interfaces

One C# API, used by three programs: a GUI (Avalonia), a command line and an MCP server. Installers
for Windows, macOS (Apple Silicon) and Linux are built by scripts kept in a separate repository.

## Verification

The purpose of the program is to reproduce examples from the literature, so verification is built
around them. Each example is a sample file and a test, with the paper's numbers and how closely
they are matched. A second, independent calculation is used where one exists. For isotropic stacks,
the 4×4 result must agree with the 2×2 (Abelès) result.

## Order of work

1. **The contract**: conventions, the stack description, the outputs, the native calls and who
   allocates what. Agreed before any engine code exists.
2. **The literature list**: the examples to reproduce, starting with Berreman (1972), with the
   figures and the numbers each one gives.
3. The native engine, tested against the 2×2 limit and the first examples.
4. The C# API and the command line.
5. The MCP server.
6. The GUI.
7. Installers.

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
