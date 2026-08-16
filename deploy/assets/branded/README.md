# The assets of the brand

This directory is a drop box. `deploy/assets/` holds the three assets with no
brand, which this repository owns. Put a file here with the **same name**, and
it goes in place of the one with no brand.

**One rule, and each file follows it on its own:** if the file is here, it is
used; if it is not, the file in `deploy/assets/` is used. Thus an appliance can
give its own image of the start and keep the mark with no brand, or the other
way.

Git holds no file of this directory except this one. The brand belongs to its
owner and it does not go in this repository.

## The three names

| File | Where it comes on the display | Who draws it |
| --- | --- | --- |
| `boot-splash-720x1280.png` | The machine starts | Plymouth, before the software |
| `shutdown-splash-720x1280.png` | The machine stops or starts again | Plymouth, at the stop |
| `brand-mark.svg` | The warm-up screen and the screensaver | The software |

`deploy-pi.sh --with-splash` installs the two images. The mark goes beside the
binary at each build, and the software reads it at each start.

## The two images

1. **PNG.** The script part of Plymouth reads PNG. A file of a different type
   with the name of a PNG does not draw, and the panel stays black.
2. **720 x 1280 pixels.** That is the panel, which is native portrait.
3. **CAUTION: the art in the file is 90 degrees around.** Plymouth draws before
   the `video=` of `cmdline.txt` applies, thus an image that is upright in its
   file comes on the panel on its side. To make sure, turn the file 90 degrees
   in the other direction and look at it: the art must then be upright.

The ground is black. `gemma.script` puts the image in the middle of a black
window, thus a file with a transparent ground gives the mark on black.

## The mark

1. **SVG**, and the software draws it as you authored it. It does not recolour
   the mark, it does not follow the theme with it, and it does not fit it to the
   surface.

2. **CAUTION: the letters must be outlines and not text.** A `<text>` element
   sends the reader of the SVG to the fonts of the system, and Raspberry Pi OS
   Lite carries four faces of DejaVu and little else. The fonts that the
   software supplies to its own user interface are a different font manager and
   the reader of the SVG never asks it.

   **A mark with live text looks correct on the development host and wrong on
   the appliance**, because the host has the font and the appliance does not.
   Export the mark with the text converted to outlines. Every vector tool does
   this.

3. **A proportion that is near 3:1.** The file with no brand is 250 x 84. Each
   screen sets the width and keeps the proportion, thus a file that is much
   taller for its width takes more of the screen than the design gives it.

The reader covers the static geometry of SVG 1.1: `path`, `rect`, `circle`,
`g`, `transform`, fills and strokes. It does not need a gradient, a filter, a
mask, a clip path or an external reference, and a mark that uses one has not
been tried.

## How to make sure

The software names the file that it took, at each start:

```text
The mark of the appliance comes from .../Assets/branded/brand-mark.svg
```

Read it with `journalctl -u gemma-translator -b | grep -i mark`. A file that
does not draw gives a Warning that names it, and the software starts with no
mark rather than not starting.

**The panel is the last proof and no command gives it.** `/dev/fb0` holds the
console and not the scanout of the software. Look at the display.
