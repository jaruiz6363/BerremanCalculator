# BerremanCalculator

A Berreman 4×4 transfer-matrix evaluator for stratified media: plane waves through a stack of
homogeneous layers whose materials may be isotropic, uniaxial, biaxial or described by a general
dielectric tensor. It is for Windows, macOS on Apple Silicon and Linux.

**Installing.** Installers for all three are on the
[Releases](https://github.com/jaruiz6363/BerremanCalculator/releases) page; nothing needs to be
built. To change the program, see [building from source](docs/building.md).

It is a **simulation engine**: given a stack, it computes what light does in it. It does not
optimise thicknesses or material properties. The engine is a native library with a plain C
interface ([berreman.h](native/include/berreman.h)), so other programs can call it as well as
these three, which share one C# API:

- **the GUI**;
- **the command line**, for scripts;
- **an MCP server**, so an AI assistant can set up and evaluate stacks with the program.

Two examples put the engine inside a lens design program, where it evaluates an anisotropic film
on a surface ray by ray in a polarization ray trace: an
[OpticStudio user-defined surface](examples/zemax-uds/README.md) and an
[Optiland coating](examples/optiland/README.md).

**Its results are checked against the literature.** Every sample it ships reproduces a published
example, and a test compares it with the paper's numbers.

**Status.** The native engine computes any stack of layers, of any material, between any outer
media. The incident medium must be lossless. It returns:
- the Jones coefficients;
- reflectance, transmittance and absorptance;
- the absorption in each layer;
- the fields against depth, inside the stack and in the outer media;
- per-point diagnostics.

It is tested against closed forms (see the [engine design](docs/engine-design.md), Section 6).

The C# API reads [stack files](docs/stack-format.md), including twisted (cholesteric) layers, which
it evaluates as sublayers and checks by doubling them. Materials are constants, dispersive (Sellmeier,
Cauchy, Lorentz, Drude, phonon oscillators, tables, catalogue glasses) or models (ferrite, conductor,
resistive sheet). Or they are a list of terms, each a dispersion law times a 6×6 shape, so any
element may disperse: a monoclinic crystal, whose principal axes turn with wavelength, is written
this way ([the material model](docs/material-model.md)).
- `berreman evaluate <file>` prints or writes a table.
- The MCP tool `evaluate_stack` returns the same table.
- The GUI edits a stack in tables: layers with their thickness, orientation (Euler angles or the
  optic axis) and twist; the outer media, also orientable; the units and the sweep. Its Materials
  tab edits n and k, or each term's law and 6×6 shape; plots a material's response and principal
  axes; and adds materials from ThinFilmDesigner's databases and five vendors' glass catalogues.
  Its analyses are:
  - any derived quantity, against any swept variable;
  - the Mueller matrix as sixteen panels;
  - conoscopic maps;
  - field and index profiles through the stack.
  It evaluates as you edit, with undo and redo, and plots R, T, A and Ψ/Δ. It imports
  ThinFilmDesigner designs, and coatings from OpticStudio coating files and from Optiland.


Nineteen literature samples are reproduced: Mansuripur (1990), nine closed forms, three of Passler
and Paarmann's (2017) infrared Otto-geometry examples, Ting's (2006) cholesteric, three microwave
radome walls (Anshin 2024; Peshlov 2006, against measurements), monoclinic β-Ga₂O₃ (Schubert
et al. 2016), and light incident from one biaxial crystal onto another (Landry and Maldonado
1995). See [samples](samples/README.md).

The tests come before each part of the engine, as listed in the [engine design](docs/engine-design.md).

## Documents

| | |
|---|---|
| [Scope](docs/scope.md) | What the program computes, what it does not, and the order of work. |
| [The contract](docs/contract.md) | Conventions, the material model, the outputs, and how C# and C++ divide the work and the memory. |
| [Examples from the literature](docs/literature.md) | The papers to reproduce, what each gives, and the order. |
| [Engine design](docs/engine-design.md) | How one point is computed, why it is stable, and the tests written before the code. |
| [Derivation of Δ](docs/derivation-delta.md) | Berreman's 4×4 matrix from Maxwell's equations, in the program's conventions, with the checks. |
| [The stack file](docs/stack-format.md) | The file the CLI and MCP server evaluate: materials, layers, units and the sweep. |
| [The material model](docs/material-model.md) | How a material becomes the 6×6 matrix: laws times shapes, the short forms, the checks. |
| [Materials](materials/README.md) | The shipped databases and glass catalogues, and the user's own materials. |
| [User guide](docs/user-guide.md) | The GUI, the command line and the MCP server. |
| [Building from source](docs/building.md) | What to install on Windows, macOS and Linux; building the engine and the programs; running the tests. |
| [Samples](samples/README.md) | The examples from the literature, and how closely each is reproduced. |

## Shape

| | |
|---|---|
| **C#** | GUI (Avalonia), CLI, MCP server and API |
| **C++** | the evaluation engine |

## The rules

These are constraints on every line of native code, not preferences. They are the same rules
as in ThinFilmDesigner.

1. **All memory is dimensioned and allocated in C# and passed to C++.** The native evaluator
   allocates nothing: no `new`, no `malloc`, no `std::vector`, no RAII type that owns storage.
2. **No fixed-size arrays dimensioned by a constant.** No `MAX_LAYERS`. Finding one is a defect
   report, not a shortcut taken.
3. **The port to CUDA must be mechanical.** Nothing may be written whose translation to a kernel
   requires a redesign.
4. **Double precision.**
5. **An unimplemented path is loud**: a diagnostic, a distinct status code, and output buffers
   left untouched.

Rules 1 and 3 are the same rule. A function that allocates nothing, holds no state and touches
only caller-supplied buffers is what a kernel is.

## Layout

```
docs/       documents, the user guide, and the design documents
native/     C++ evaluation engine; native/include/berreman.h is the ABI
managed/    C#: API, GUI, CLI, MCP server, and their tests
tests/      the engine's tests, and fixtures both sides read
tools/      build checks, including the rules above
samples/    the examples from the literature
materials/  material databases and glass catalogues
examples/   a Berreman film in OpticStudio (examples/zemax-uds) and in Optiland (examples/optiland)
```

| Project | Builds |
|---|---|
| `berreman_native` | the C++ engine, a shared library |
| `managed/Berreman.Native` | the C# mirror of `berreman.h`, and the pinned buffers C# allocates |
| `managed/Berreman.Api` | the API the three programs share |
| `managed/Berreman.Cli` | `berreman`, the command line |
| `managed/Berreman.Mcp` | `berreman-mcp`, the MCP server (stdio) |
| `managed/Berreman.Gui.Core` | the testable half of the GUI: the stack being edited, undo, the layer table, plot arithmetic, the sample list |
| `managed/Berreman.Gui` | `berreman-gui` (Avalonia) |
| `managed/Berreman.Native.Tests` | the managed test suite |
| `managed/Berreman.Gui.Tests` | the window, driven headless with real mouse input |

## Building

[docs/building.md](docs/building.md) explains each step, on Windows, macOS and Linux: what to
install, what each command does, and what to do when one fails. In short, with CMake 3.20 or later,
a C++17 compiler and the .NET 8 SDK or later, build the engine first, because the C# projects copy
the library from `build/`:

```
cmake -S . -B build
cmake --build build --config Release
ctest --test-dir build -C Release

dotnet build managed/BerremanCalculator.sln -c Release
dotnet run --project managed/Berreman.Native.Tests -c Release
dotnet run --project managed/Berreman.Gui.Tests -c Release
```

The native tests include `constraints`, which fails the build if native code allocates, uses
`float`, declares a fixed-size array, throws, or has an inline function without `BC_HD`. Only the
GUI and its tests restore NuGet packages; everything else builds offline.

## Authors

Javier Ruiz and Claude Code. See [AUTHORS](AUTHORS).

## Licence

MIT, copyright (c) 2026 Javier Ruiz. See [LICENSE](LICENSE). Every source file opens with the same
notice (`SPDX-License-Identifier: MIT`), so it stays with a file that is copied on its own.
