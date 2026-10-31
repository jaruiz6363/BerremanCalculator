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

**Status: being set up. Nothing is implemented yet.** The documents below come first. No engine
code is written until they are agreed.

## Documents

| | |
|---|---|
| [Scope](docs/scope.md) | What the program computes, what it does not, and the order of work. |

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
native/     C++ evaluation engine
managed/    C#: API, GUI, CLI, MCP server, and their tests
tests/      the engine's tests, and fixtures both sides read
samples/    the examples from the literature
```

## Authors

Javier Ruiz and Claude Code. See [AUTHORS](AUTHORS).

## Licence

MIT, copyright (c) 2026 Javier Ruiz. See [LICENSE](LICENSE).
