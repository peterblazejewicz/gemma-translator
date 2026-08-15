# 12. CAD and OpenSCAD

This file loads when Claude works with a file under `cad/`. It is section 12 of
the `CLAUDE.md` at the root, and it keeps the numbers of that section, thus a
pointer such as "section 12.2 item 3" still finds its text.

This section covers the OpenSCAD source of the enclosure in `cad/`: the tool
that makes the parts, the rules of the source, and how to make sure of a
change.

Section 3 item 4 and section 4.5 give the goal: files for a printer that make a
case for the new hardware. The printer makes three parts: the body, the deck,
and the carrier frame that holds the display module.

Upstream supplies `.stl` binaries and no CAD source, thus the `.scad` source
has no upstream equivalent. See section 10 for the header that it gets.

### 12.1 The OpenSCAD software

**CAUTION: DO NOT PUT THE LOCATION OF OPENSCAD IN A FILE THAT GIT HOLDS. THE
LOCATION IS A PROPERTY OF THE DEVELOPMENT HOST. IT IS NOT A PROPERTY OF THIS
SOFTWARE.**

Start OpenSCAD with the `OPENSCAD` variable of the environment. Set the value
in `.claude/settings.local.json`, which git ignores:

```json
{ "env": { "OPENSCAD": "<the full path of openscad.exe>" } }
```

If the variable is empty, stop and speak to the user. Do not look for
OpenSCAD, and do not put its location in this document.

Do not start a bare `openscad`. That name can find the 2021.01 release, which
does not have the Manifold engine.

| Item | Value |
| --- | --- |
| Version on the development host | 2025.09.07 |
| Engine | `--backend=Manifold` |

Give `--backend=Manifold` to each command. Manifold is 10 to 100 times more
fast than CGAL for a boolean operation. The parts of this case have many
boolean operations in each other.

### 12.2 Traps that give an incorrect result that looks correct

Each trap below gives an incorrect result that looks like correct work.

1. **PowerShell does not wait.** On Windows `openscad.exe` is GUI software.
   PowerShell starts it and continues immediately. Thus a test of the output
   file says "not found" although the command is correct.

   Send the output through a pipe, for example `| Out-String`. Then PowerShell
   waits.

2. **`--render` needs the `=` character.** With `--render file.scad` OpenSCAD
   reads the name of the file as the value of `--render`. It then writes its
   help text and does no work. Write `--render=cgal`.

3. **Manifold does not tell you about a bad mesh.** For two cubes that touch at
   one edge only, Manifold writes `Status: NoError`. For the same source, CGAL
   writes `WARNING: Object may not be a valid 2-manifold and may need repair!`

   Thus `Status: NoError` from Manifold is not proof. A `Genus` value less than
   0 is a signal of a problem. See section 12.5.

4. **A flat part gets no test at all.** A part that is one `linear_extrude` of
   a 2D shape stays a `PolySet`, and OpenSCAD sends it to no geometry engine.
   Then `--summary geometry` writes NO status line and CGAL gives NO warning.

   That looks the same as a part that passed, but nothing was tested. `render()`
   does not correct this; version 2025.09.07 removes it for a `PolySet`. A
   boolean operation makes the engine do the work. `console.scad` has a `gate()`
   module that puts each part in an `intersection()` with a large cube. Do not
   remove it.

   Proof that it operates: the deck gives a `Genus` value, which is the count
   of its holes. See section 12.5.

5. **A rounded rectangle can be an EMPTY 2D shape.** `square()` with a side of
   0 makes no shape, and `offset()` of no shape is no shape. Thus a fully
   circular end, where the radius is one half of the smaller side, makes no
   shape at all, and the `difference()` that uses it removes nothing.

   The model then gives `Status: NoError` and the wall has no slot. The
   `rrect` of `util.scad` keeps a side of `eps` for this. Do not remove that
   `max()`.

6. **A hole in ONE wall has the colour of that wall.** Look down at the
   enclosure and you see the floor through the bay. Thus a hole 0.5 mm from a
   wall and a hole that goes into that wall look the same.

   Put the part in a slab of 1 mm with `intersection()`, give each part its
   own colour, and look at the slab.

7. **A model of a part that we buy must show all of that part.** A test of a
   collision against a model that has no nut finds no collision where the nut
   has one, and the row for that part then reads EMPTY.

   The M19 switch is the example. Its bezel is 22 mm across and its nut is 25.
   A model with the bezel, the barrel and the terminals, and no nut, hides a
   nut that stands in a side wall, in the cover glass of the display module,
   and in the two deck bosses.

   Before you agree with an EMPTY row, count the parts of that model against
   the document of the manufacturer.

8. **A test that reads a constant of the model tests nothing.** A row that
   takes its keep-out from the same variable that made the geometry moves with
   that variable, thus it always gives a correct result.

   A test must hold the value of the document. `DISP_BOSS_XY` is the example:
   it is an open item of the display section of `dims.scad`.

9. **The window of OpenSCAD keeps a cache.** It can show an error for a file
   that the command line makes with no error. Use `Design`, then
   `Flush Caches`.

### 12.3 Files and directories

The `.scad` source is in `cad/` and our files for the printer are in
`stl/console/`. Git holds those two. `notes/docs/` has the datasheets and
`notes/build/` has the images and the output files. Git ignores `notes/`.

**IMPORTANT: git ignores `notes/`.** A person who clones this repository gets
the dimensions from section 4 and from the comments of the `.scad` source, and
from no other location.

Put each value and the identifier of its document in one of those two
locations. Do not point at a file of `notes/`: a reader cannot follow the
pointer.

`cad/` holds five files and no more. Do not add a sixth without a decision from
the user.

The `.stl` files at the top of `stl/` are the enclosure of upstream. They are
not our work. Do not change them and do not remove them.

`console.scad` selects one printed part with `-D 'part="body"'`,
`-D 'part="deck"'` or `-D 'part="carrier"'`. `PARTS` at the top of that file
gives the values of `part`, and the message of the assert at the end comes from
it.

Write a 3MF file for the printer. Write an STL file as an alternative.

### 12.4 The rules of the source

- Put each dimension in a variable at the top of the file, or in
  `cad/tolerances.scad`. Do not put a number in the body of a module.

- Write `$fn = $preview ? 32 : 180;`. The quantity of facets changes the
  dimension of a large hole, thus this value is not only cosmetic. For a large
  hole, use the larger radius `r / cos(180/$fn)`.

- Write `eps = 0.01`. Make each cut longer than the material by `eps`. Two
  faces at the same location make geometry with no thickness, and a slicer
  refuses it.

- Use BOSL2 (`cuboid(rounding=)`, `attach()`, `screw_hole()`). Do not write a
  chain of `hull()` or `minkowski()` if BOSL2 has a module for the shape.
  BOSL2 is in the library directory of OpenSCAD on the development host, thus
  `include <BOSL2/std.scad>` finds it and no variable of the environment is
  necessary.

- Show a part that we buy with the `%` modifier. Then `--render=cgal` does not
  put it in the output. `ref-hardware.scad` holds these models. Give
  `convexity = 10` to an import that has holes.

- The datasheets are the authority, and a model of a board from a library is
  not. Put the identifier of the document in a comment adjacent to the value.
  The four identifiers are `RP-008347-DS-1`, `RP-010430-MM-1`, the Speak2 40
  datasheet, and the Geekworm X1201 DXF. See section 11.

### 12.5 How to make sure of the work

**IMPORTANT: do not tell the user that a change is complete before you look at
it.**

Section 5.2 says that this fork has no test project. For CAD work an image is
the equivalent of `dotnet build`.

**An image alone is not proof.** It shows the shape that you looked for and it
hides the shape that you did not. After each change, do these steps:

1. Write a PNG image of the ISO view, the top view, the front view, and a view
   of a cut that `difference()` makes.
2. Read each image.
3. Compare each dimension with the datasheet of its part.
4. Make each of the three parts and compare the `Genus` value.
5. Then speak to the user.

```powershell
& $env:OPENSCAD --backend=Manifold -o notes/build/iso.png --imgsize=1200,900 `
    --camera=0,0,0,55,0,25,600 --colorscheme=Tomorrow --render=cgal `
    cad/console.scad | Out-String
& $env:OPENSCAD --backend=Manifold -D 'part="body"' -o notes/build/body.3mf `
    --summary geometry cad/console.scad | Out-String
```

The geometry gate: `body` gives `Genus: 138`, `deck` gives 6, and `carrier`
gives 13, each with `Status: NoError`. A different value says that the geometry
changed; if you did not intend the change, remove it. A value less than 0 is a
signal of a bad mesh.

Before a file goes to the printer, make the same part again with
`--backend=CGAL`. CGAL must give no warning about a 2-manifold. Manifold alone
is not sufficient. See section 12.2 item 3.

### 12.6 What is not decided

Two decisions of section 4 apply to each part:

- The panel operates in landscape. The case must hold the panel on its side,
  with the long edge horizontal. See section 4.2.

- The Jabra Speak2 40 goes in a well at the front of the case. Its top is
  2.5 mm above the deck, and its captive cable goes out of the rear wall. See
  section 4.5.

These items are open:

- The printer and the material are not selected. `cad/tolerances.scad` gives
  values for a printer that we do not know. Do a test print of one small part
  and measure it before the full enclosure goes to the printer.

- The four bosses that hold the carrier frame have no bore. `carrier_ledges()`
  makes the boss and the carrier has a clearance hole above it, but nothing
  cuts the bore for the insert. The two deck bosses have their bore:
  `INSERT_BORE_D` 4.2 mm and `INSERT_DEPTH` 6 mm for an M3 heat-set insert.

- `DISP_BOSS_XY` keeps the pattern of the four screw bosses central on the long
  axis of the display module, and `RP-010430-MM-1` does not. The carrier cuts
  its clearance at this pattern, thus a value that is out puts a boss of the
  module on the material of the carrier.

- Each other open value is a `TO BE UNDERSTOOD` comment in `cad/`. Examine that
  text. The comment gives the value, why it is not confirmed, and what it
  moves.
