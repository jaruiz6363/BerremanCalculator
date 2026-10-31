# User guide

BerremanCalculator evaluates a stack of layers: plane waves through homogeneous planar layers of
any material, between any two outer media. It computes reflection, transmission and absorption,
the Jones coefficients, Ψ and Δ, and the absorption in each layer. It never changes the stack.

A stack is described by a **stack file**, a JSON document ([the format](stack-format.md)). All
three programs read the same file:

| Program | For |
|---|---|
| `berreman-gui` | Editing a stack, evaluating it, and plotting the results |
| `berreman` | Scripts: evaluate a file, print or write a table |
| `berreman-mcp` | An AI assistant: the same evaluation as a tool |

The samples from the literature are installed beside the programs, under `samples/`. Each one
names the paper it reproduces. [samples/README.md](../samples/README.md) says how closely each is
reproduced.

## The GUI

- **Open…** (Ctrl+O) opens a stack file. **Save** (Ctrl+S) and **Save as…** write it.
- **The sample list** opens a literature sample. Samples open read-only on disk: Save as… keeps a
  copy to change.
- **The left pane is the stack file itself**, editable as text. What the GUI evaluates is exactly
  what the CLI and the MCP server would.
- **Evaluate** (F5) runs the stack in the background.
  - The status line reports the number of points and the time taken.
  - Points that failed show as gaps in the plot, and the status line names the first failure.
  - For a stack with twisted layers, it shows how much doubling the sublayers changes the result.
- **The plot** puts on its x axis whatever the sweep varies:
  - the spectral variable if there is more than one value;
  - otherwise the angle (or ξ);
  - otherwise the azimuth.

  Every other combination of sweep values is its own curve, labelled by its values.
- **Show** chooses the curves:
  - R, T and A;
  - the p and s waves (modes 0 and 1 with anisotropic outer media), and their mean, which is the
    unpolarised value;
  - Ψ and Δ, on their own axis in degrees.
- **Export CSV…** writes every point, with the Jones coefficients, Ψ and Δ, and the absorption in
  each layer.

Opening a stack file from Explorer or a file manager (`berreman-gui file.json`) evaluates it at
once.

## The command line

```
berreman evaluate stack.json                      print R, T and A for every point
berreman evaluate stack.json --jones              add the Jones coefficients
berreman evaluate stack.json --ellipsometry       add Ψ and Δ
berreman evaluate stack.json --absorption         add the absorption in each layer
berreman evaluate stack.json --csv out.csv        write the table to a file
berreman status                                   whether the engine loads
```

The exit code is 0 for success, 1 when some point failed (its status is in the table), and 2 for
a file that cannot be read or used, with a message that says where in the file the problem is.

## The MCP server

`berreman-mcp` speaks the Model Context Protocol over stdio. Its tool `evaluate_stack` takes a
`path` to a stack file, or the file's JSON as `stack`. It returns the same table as the CLI, as
CSV, with optional Jones coefficients, Ψ/Δ and per-layer absorption. `engine_status` reports whether
the engine loads.

## Conventions

These are fixed, and stated in full in [the contract](contract.md):
- **Time dependence:** exp(−iωt), so an absorbing medium has n + ik with k > 0.
- **Converting data:** a material from a paper that uses exp(+jωt) is marked
  `"convention": "exp(+jwt)"` and is conjugated on entry.
- **p and s:** p̂, ŝ and k̂ are right-handed for every wave, with ŝ along y. So at normal incidence
  r_pp = −r_ss.
- **Ellipsometry:** Ψ and Δ are defined as Schubert (1996) defines them, ρ = r_pp/r_ss =
  tan Ψ e^{iΔ}.
- **Jones coefficients** carry the incident wave as their first subscript: r_ps is p in, s out.
