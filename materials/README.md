# Materials

The material databases and glass catalogues that ship with the program. The Materials tab's
**From library…** offers them, and so does the API (`MaterialLibrary`).

| File | What it is |
|---|---|
| `thinfilm-materials.txt` | ThinFilmDesigner's thin-film database: Sellmeier and Cauchy fits, and measured n, k tables (Palik, refractiveindex.info). Each entry's `SOURCE` line says where it is from. |
| `literature-materials.txt` | ThinFilmDesigner's literature database: the constants and tables used by published coating designs (Vasicek 1953, Johnson and Christy 1972, ...). |
| `glass/*.AGF` | The vendors' glass catalogues in ZEMAX AGF form: CDGM, HOYA, OHARA, SCHOTT and SUMITA. |

The files are ThinFilmDesigner's, byte for byte (same authors, MIT). Their comments say "for
ThinFilm", because they are kept identical so that either program can read the other's. They are
in micrometres.

**Database materials are copied into the stack.** Choosing one writes its definition into the stack
file, as ThinFilmDesigner does. The stack then owns it and may edit it, and a later change to the
database never changes a stack already made from it. A crystal listed as `X (o)` and `X (e)` is also
offered as one uniaxial material, `X (uniaxial)`, with its optic axis along the material's z.

**Catalogue glasses are named, never copied.** The stack says
`{ "glass": { "catalog": "SCHOTT", "name": "N-BK7" } }`, and the index always comes from the
catalogue: a stack that altered a vendor's melt data would no longer be using that glass. The
catalogues use three dispersion formulas: 1 (Schott), 2 (Sellmeier 1) and 3 (Herzberger). A glass
with any other formula is refused, never guessed. A glass is refused outside the catalogue's
wavelength range, and its k is 0: the catalogues' internal transmittance is not read yet.

**Your own materials** are kept in `user-materials.json`, in the per-user application-data folder
(`%APPDATA%\BerremanCalculator` on Windows). **Save to my materials** adds the selected material
there. Any material form may be saved, terms included.
