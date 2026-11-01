# BerremanCalculator

A Berreman 4×4 transfer-matrix evaluator for stratified media: plane waves through a stack of
homogeneous layers whose materials may be isotropic, uniaxial, biaxial or described by a general
dielectric tensor. It is for Windows, macOS on Apple Silicon and Linux.

It **evaluates** a given stack. It does not optimise thicknesses or material properties. Its purpose
is to reproduce examples from the literature, and every example it ships is checked against the
paper it comes from.

One shared API drives three programs:

- **the GUI**;
- **the command line**, for scripts;
- **an MCP server**, so an AI assistant can set up and evaluate stacks with the program.

**Status: the engine evaluates, from C++ only.** The native engine computes any stack of layers,
of any material, between any outer media. The incident medium must be lossless. It returns:
- the Jones coefficients;
- reflectance, transmittance and absorptance;
- the absorption in each layer;
- per-point diagnostics.

It is tested against closed forms (see the [engine design](docs/engine-design.md), Section 6).

Fields against depth are not built yet. A request for them is refused with a distinct status
rather than computed wrongly.

The C# API reads [stack files](docs/stack-format.md), including twisted (cholesteric) layers, which
it evaluates as sublayers and checks by doubling them. Materials are constants, dispersive (Sellmeier,
phonon oscillators, tables) or models (ferrite, conductor, resistive sheet).
- `berreman evaluate <file>` prints or writes a table.
- The MCP tool `evaluate_stack` returns the same table.
- The GUI edits a stack in tables: layers with their thickness, orientation (Euler angles or the
  optic axis) and twist; the outer media, also orientable; the materials; the units and the sweep.
  It evaluates as you edit, with undo and redo, and plots R, T, A and Ψ/Δ.

Seventeen literature samples are reproduced: Mansuripur (1990), nine closed forms, three of Passler
and Paarmann's (2017) infrared Otto-geometry examples, Ting's (2006) cholesteric, and three
microwave radome walls (Anshin 2024; Peshlov 2006, against measurements). See
[samples](samples/README.md).

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
| [User guide](docs/user-guide.md) | The GUI, the command line and the MCP server. |
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

Needs CMake 3.20 or later, a C++17 compiler and the .NET 8 SDK or later. Build the engine
first: the C# projects copy the library from `build/`.

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

MIT, copyright (c) 2026 Javier Ruiz. See [LICENSE](LICENSE).
