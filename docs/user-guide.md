# User guide

BerremanCalculator evaluates a stack of layers: plane waves through homogeneous planar layers of
any material, between any two outer media. It computes reflection, transmission and absorption,
the Jones coefficients, Ψ and Δ, the Mueller matrices, the absorption in each layer and the
fields against depth. From those it draws spectra, Mueller-matrix panels, conoscopic maps and
field profiles. It never changes the stack: it evaluates, and ThinFilmDesigner, which shares its
design files, optimises.

A stack is described by a **stack file**, a JSON document ([the format](stack-format.md)). All
three programs read the same file:

| Program | For |
|---|---|
| `berreman-gui` | Editing a stack, evaluating it, and plotting the results |
| `berreman` | Scripts: evaluate a file, print or write a table, convert, report |
| `berreman-mcp` | An AI assistant: evaluation, samples, materials, analyses and conversion as tools |

The samples from the literature are installed beside the programs, under `samples/`. Each one
names the paper it reproduces. [samples/README.md](../samples/README.md) says how closely each is
reproduced.

## The GUI

The window shows the stack being edited on the left and its results on the right. Every change
is evaluated as you make it (**Analysis > Evaluate after every change**, on by default); **F5**
evaluates on demand. Every edit can be undone (**Ctrl+Z**) and redone (**Ctrl+Y**).

### Files

- **File > New stack** (Ctrl+N) starts from air, one 100 nm layer of index 1.38, and glass.
- **Open…** (Ctrl+O) opens a stack file. **Save** (Ctrl+S) and **Save as…** (Ctrl+Shift+S) write it.
- **File > Browse samples…** lists the literature samples. Search their titles, sources and notes,
  read each one's source, contents and notes, and open one as a copy (Save as… keeps it). **File >
  Open sample** opens one directly by name.
- **File > Import a ThinFilmDesigner design (.tfd)…** reads a design as a new stack. ThinFilmDesigner
  designs and optimises isotropic coatings, and this program evaluates any stack, so a coating can
  come in here to be made anisotropic, oriented and analysed.
  - **What comes across.** The layers (physical thicknesses), the materials (constant, Sellmeier,
    Cauchy, tables, catalogue glasses named as such), and the wavelengths and angles as the sweep.
  - **What does not.** The merit function, the optimisation settings and a back surface; the notes
    say so.
  - **The check.** The tests evaluate three of ThinFilmDesigner's samples, read this way, and
    agree with ThinFilmDesigner's own engine to 10⁻¹². One of them is a 52-layer bandpass with
    Cauchy, Sellmeier and catalogue-glass materials.
- **File > Export a design report…** writes the stack as numbers another program can take:
  - the layers in physical units, with each one's Euler angles and the tilt and azimuth of its z axis;
  - every material as a table over the sweep's range: n and k for an isotropic one, n and k along
    x, y and z for an aligned crystal, and otherwise every non-zero element of its 6×6 matrix.

  Values are in the material's own axes. The tables are tab-separated, and every other line starts
  with `#` or `[`. `berreman report` writes the same, with `--from`, `--to` and `--step`.
- Opening another stack, or closing the window, with unsaved changes asks whether to save them.
- Opening a stack file from Explorer, Finder or a file manager (`berreman-gui file.json`)
  evaluates it at once.

### The Stack tab

- **Description**: the title, the source it reproduces, and notes. All are saved in the file.
- **Units**: lengths (nm, µm, mm, cm, m) and the spectral axis (wavelength, GHz, THz, cm⁻¹ or eV).
  Changing a unit converts every number it applies to, so the stack stays physically the same.
  Thicknesses are always physical: there is no QWOT or FWOT (contract, Section 1a).
- **Incident medium** and **exit medium**: a material each. An anisotropic medium also has its own
  Euler angles φ, θ, ψ, so a substrate or prism can be oriented. For an isotropic medium, such as
  air or glass, the angles are hidden, because turning it changes nothing. Beside each medium is
  a short description of its material.
- **The layer table**. Layer 1 faces the incident medium. Its columns:

  | Column | |
  |---|---|
  | Material | chosen from the stack's materials |
  | Type | what the material is, and how it disperses: "uniaxial · Sellmeier", "monoclinic · 16 terms". Hover for its principal indices at the sweep's first wavelength. See below |
  | Thickness | physical, in the length unit |
  | φ, θ, ψ | the layer's orientation: R = Rz(φ) Rx(θ) Rz(ψ) turns the material from its own axes into the stack's (contract, Section 1). Blank for an isotropic material, which turning does not change; the angles are kept |
  | Optic axis | where the material's own z axis ends up: its tilt from the normal and its azimuth from x. For a uniaxial material this is the optic axis. Blank for an isotropic material |
  | Twist | the pitch and handedness of a twisted (cholesteric) layer |
  | … | the layer's properties |

  A typed value is taken when the cell is left or Enter is pressed. A value that cannot be used
  (a negative thickness, a word) is refused and the status line says why.

  **How the type is decided.** The type is read from the material's 6×6 matrix at the sweep's first
  wavelength, in the material's own axes. It does not depend on how the material was written: a
  uniaxial written as terms with a tilted axis is still uniaxial. The rules are applied in order:

  1. **Bianisotropic**: any magnetoelectric block (ρ, ρ′) is present.
  2. **Gyromagnetic**: μ has an antisymmetric part, as a ferrite does.
  3. **Magneto-optic**: ε has an antisymmetric part.
  4. Otherwise ε is symmetric, and the type depends on how many of its eigenvalues coincide:
     - all three: **isotropic**;
     - two: **uniaxial**;
     - none: **biaxial**.

  A material that gives a crystal `lattice` with oblique angles is **monoclinic** (one oblique
  angle) or **triclinic** (more). So is a material whose index and absorption axes differ: only a
  monoclinic or triclinic crystal allows that. Such a material is called **monoclinic** when one
  coordinate axis is still a principal axis of both, and **monoclinic or triclinic** otherwise.
  β-Ga₂O₃ is an example.

  A lossless monoclinic material looks biaxial at a single wavelength. Give its `lattice` to have
  it called monoclinic.
- **Insert** copies the selected layer after it; **Delete** removes the selected layers; **Up** and
  **Down** move one. **Stack > Reverse layers** reverses their order; **Reverse layers and media**
  also swaps the outer media, so the light arrives from the other side.
- **Properties…** (or the … button, or a double click) edits one layer:
  - material and thickness;
  - the Euler angles, or the direction of the optic axis (tilt and azimuth), which fills them in;
  - a twist about z through the layer: pitch, handedness, starting angle, and how many uniform
    sublayers it is evaluated as.
- **Design string…** builds layers from text, in ThinFilmDesigner's notation: `(HL)8 H`,
  `0.5L (HL)6 H 0.5L`. Each letter is given a material and a physical thickness, and a number
  before a letter multiplies the thickness: with H = 60 nm, `0.5H` is 30 nm. The layers replace
  the stack or are inserted after the selected layer, and the leftmost layer can face either
  medium.
- **The sweep**: the spectral values, the angles of incidence (or ξ = kx/k₀ directly, which an
  anisotropic or lossy incident medium needs) and the azimuths. Each is a value (`633`), a list
  (`0, 30, 60`) or a range (`400 to 800 step 2`, `400 to 800 count 201`).

### The Materials tab

The list shows the stack's materials, each with a summary (`n = 1.52`,
`uniaxial, nₒ = 1.54, nₑ = 1.55`, `16 terms; …`). Beside it the tab shows where the selected
material is used.
- **New** and **Duplicate** add a material.
- **Rename** follows the material into every layer and medium.
- **Remove** is refused while the material is used.
- **From library…** adds materials from a library ([materials](../materials/README.md)). Choose a
  library, search it, and select one or several entries. Below the list, the dialog shows each
  entry's source and its index at the stack's first spectral point.
  - **ThinFilmDesigner's databases.** A material from them is copied into the stack.
  - **Five vendors' glass catalogues.** A glass is named, and its index is always read from the
    catalogue.
  - **Your own materials.** These are the materials you saved with the next button.

  A crystal listed as (o) and (e) is also offered as one uniaxial material.
- **Save to my materials** keeps the selected material, in any form, for use in other stacks.

The selected material has four views. Every change in them is one undoable edit, and a material
that would not evaluate is refused, with the reason in the status line.

- **Simple.** A constant n and k along each principal axis: isotropic, uniaxial (the optic axis is
  the material's z) or biaxial. A material that is not constant opens on Terms instead. **Replace
  with a constant n, k material** freezes its diagonal at the stack's first spectral point.
- **Terms.** The material as a list of terms, each a dispersion law times a 6×6 shape
  ([material-model.md](material-model.md)). This is how a monoclinic crystal is written: each
  oscillator along its own direction, so the principal axes turn with wavelength. Any element of
  the 6×6 matrix can disperse.
  - **Laws.** Choose a law, then type its parameters into its fields. A list, such as Sellmeier
    terms, phonon modes or the rows of a table, is typed as lines of numbers under its column
    names.
  - **Shape.** A direction (by angles or by a vector), or the 6×6 matrix cell by cell. Type a
    complex cell as `2`, `-0.08`, `0.1i` or `2+0.1i`.
  - **Apply term** puts the term in the material.
  - **A material in a short form** (a Sellmeier glass, a uniaxial crystal, a ferrite) is shown as
    the terms it stands for. Any change made here writes it as terms, with the same numbers.
- **Response.** The material across a range, which defaults to the stack's spectral range:
  - n and k along its principal axes;
  - any one element of M, real and imaginary;
  - the directions of its principal axes, as an azimuth from x and a tilt from z. Each axis is
    followed from point to point, so a crossing of two values does not swap them.

  Above the plot it says whether the material is passive (no gain for any field) and reciprocal.
  Neither is required: a magneto-optic medium is not reciprocal, and a fit sometimes shows gain.
  Below the plot it prints the 6×6 M at one point.
- **Text.** The material as the stack file writes it ([the forms](stack-format.md#materials)).

The status line reports what the last edit did. The time an evaluation took is shown beside the
layer count, so it does not replace an error message.

### The File tab

The stack exactly as it is saved. Edits made in the tables show here at once; an edit made here
is taken by **Apply**, as one undoable change. The file is written in a standard layout: comments
in a hand-written file are not kept (the notes field is the place for them), and keys this
version does not know are.

### Results

- **Beside the layer count** the window shows the number of points and the time taken. For a stack
  with twisted layers it also shows how much doubling the sublayers changes the result.
- **Failed points** show as gaps in the plot, and the status line names the first failure.
- **The plot** puts on its x axis, unless **x** says otherwise, whatever the sweep varies: the
  spectral variable if it has more than one value, otherwise the angle (or ξ), otherwise the
  azimuth. Every other combination of sweep values is its own curve, labelled by its values.
- **Show** chooses the curves: R, T and A; the p and s waves (modes 0 and 1 with anisotropic outer
  media) and their mean, which is the unpolarised value; Ψ and Δ, on their own axis in degrees.
- **More…** adds any other quantity, each unit on its own y axis. The quantities are:
  - generalised ellipsometry (Ψ_ps, Δ_ps, Ψ_sp, Δ_sp);
  - the conversion reflectances R_ps and R_sp;
  - |r|, |t| and their phases;
  - any single Mueller element.
- **x** chooses the variable on the x axis, among those the sweep varies.
- **view** shows the curves, or the Mueller matrix in reflection or in transmission. The Mueller
  matrix is shown as 16 panels, the way ellipsometry papers draw it. Each element is divided by
  M11, M11 itself is the unpolarised reflectance (or transmittance), and there is a curve for each
  value of the other swept variables.
- **Zoom and read values.** Drag a rectangle on the plot to zoom into it, and double-click to see
  everything again. The zoom stays while you edit, so a feature you are studying stays in view.
  Hovering shows the nearest point's curve, x and y.
- **File > Export results as CSV…** writes every point, with the Jones coefficients, Ψ and Δ, the
  absorption in each layer, and the quantities chosen with More….
- **File > Export the plot as PNG…** writes the plot as shown, curves or Mueller panels, at twice
  the screen's resolution.

### Conoscopic maps

**Analysis > Conoscopic map…** shows one quantity over every direction of incidence at one spectral
value:
- **The picture.** The radius is the angle of incidence, and the polar angle is the sample's
  azimuth (the program turns the sample about the normal, and the plane of incidence stays x–z).
- **Settings.** The spectral value, the largest angle, and how many angles and azimuths to compute.
  Compute evaluates them all. The quantity can then be changed without evaluating again.
- **Polarisers.** Besides every derived quantity, the map offers the transmittance between crossed
  or parallel polarisers. They are fixed to the sample, the polariser at a chosen angle from the
  sample's x. This needs isotropic incident and exit media. A uniaxial plate cut normal to its axis
  then shows the classic Maltese cross and rings. The tests check that the rings fall where the
  closed-form phase difference is a multiple of 2π.
- **Reading and saving.** Hovering reads a value, and the map saves as PNG or CSV (angle, azimuth,
  value).

### Field profile

**Analysis > Field profile…** shows the fields against depth at one point: one spectral value, one
angle (or ξ) and one azimuth.
- **Depth** runs from the first interface into the stack, with margins in front and behind, and
  the interfaces are drawn as dashed lines.
- **Quantities.** |E|², or |Ex|², |Ey|² and |Ez|² separately, |H̃|², or the real parts of E, for
  incident p, incident s or the unpolarised mean.
- **Scale.** Fields are per unit incident amplitude, so |E|² is relative to the incident wave. The
  status line gives the point's R and T.
- **Index profile.** The same window shows n and k along x, y and z through the stack, at that
  wavelength and azimuth, each layer's orientation applied.
- **Saving.** PNG, or CSV with every depth's six complex components for both incident waves.

## The command line

```
berreman evaluate stack.json                      print R, T and A for every point
berreman evaluate stack.json --jones              add the Jones coefficients
berreman evaluate stack.json --ellipsometry       add Ψ and Δ
berreman evaluate stack.json --absorption         add the absorption in each layer
berreman evaluate stack.json --mueller            add the Mueller matrix in reflection (each / M11)
berreman evaluate stack.json --quantities Mr34,Psi_ps   add quantities by name
berreman quantities                               list every quantity, with what it is
berreman report stack.json --step 10 --out r.txt  the layers and every material as tables
berreman convert design.tfd stack.json            read a ThinFilmDesigner design as a stack file
berreman convert stack.json design.tfd            write an isotropic stack as a ThinFilmDesigner design
berreman evaluate stack.json --csv out.csv        write the table to a file
berreman status                                   whether the engine loads
```

Writing a ThinFilmDesigner design is done only from the command line (`convert stack.json
design.tfd`); the window imports designs but does not export them.
- **What it needs.** Every material must be isotropic and in a form ThinFilmDesigner holds, with no
  twisted layer. Anything else is refused, and the message names the material or layer.
- **ε tables.** A table given as ε′, ε″ is written as finely spaced n and k, because
  ThinFilmDesigner interpolates n and k.
- **The result** has no merit function. Its wavelengths and angles are the stack's sweep.

The exit code is 0 for success, 1 when some point failed (its status is in the table), and 2 for
a file that cannot be read or used, with a message that says where in the file the problem is.

## The MCP server

`berreman-mcp` speaks the Model Context Protocol over stdio. Its tool `evaluate_stack` takes a
`path` to a stack file, or the file's JSON as `stack`. It returns the same table as the CLI, as
CSV, with optional Jones coefficients, Ψ/Δ and per-layer absorption. It also takes `mueller`, and
`quantities` (a list of names). `list_quantities` names them all, and `engine_status` reports
whether the engine loads.

The other tools are stateless, like `evaluate_stack`. A stack goes in as `path` or as `stack`
JSON, and a tool that changes a stack returns the new JSON.

| Tool | What it does |
|---|---|
| `list_samples`, `read_sample` | The literature samples: a filtered list, and a sample's stack JSON. |
| `describe_stack` | A stack in words: units, media, layers with orientation and twist, and each material with its passivity and reciprocity over the sweep. |
| `search_materials` | Library materials in their stack-file form: ThinFilmDesigner's databases, the glass catalogues and your own. |
| `build_layers` | Layers from a design string such as `(HL)8 H`, each letter a material and a physical thickness. |
| `import_tfd`, `export_tfd` | ThinFilmDesigner designs in and out, as File > Import and Export do. |
| `field_profile` | The fields against depth at one point, as CSV, with the interfaces. |
| `conoscope_map` | One quantity over every direction of incidence, or the transmission between crossed or parallel polarisers. |
| `material_response` | One material across a range: n and k along its principal axes, passivity and reciprocity. |
| `design_report` | The stack as numbers, as File > Export a design report writes it. |

**ZEMAX and Optiland coatings** come in and go out through ThinFilmDesigner, which reads and writes
both: open the coating there, save it as a .tfd, and import that here; or the reverse for an
isotropic stack.

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
- **Mueller matrices** are divided by M11. An isotropic sample gives [[1, −N, 0, 0], [−N, 1, 0, 0],
  [0, 0, C, −S], [0, 0, S, C]], with N = cos 2Ψ, C = sin 2Ψ cos Δ and S = sin 2Ψ sin Δ. The sign of
  the fourth row and column is Schubert et al. (2016)'s, checked against their Fig. 5 (contract,
  Section 5).
