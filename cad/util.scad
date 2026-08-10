// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

include <tolerances.scad>

// A rectangle with the same radius at each corner, centred on the origin.
//
// CAUTION: THE max() IS NOT DECORATION. square() with a side of 0 makes NO
// shape, and offset() of no shape is no shape. Thus a fully round end, where
// r is half of the smaller side, gives an EMPTY 2D shape, and a difference()
// that uses it removes nothing at all, in silence: the model gives Status
// NoError and the wall has no slot. The guard keeps a side of eps, thus the
// shape is 0.01 mm larger on that axis and it is not empty.
module rrect(size, r) {
    w = size[0]; h = size[1];
    rr = min(r, w / 2, h / 2);
    offset(r = rr) square([max(w - 2 * rr, eps), max(h - 2 * rr, eps)],
                          center = true);
}

// A box with a vertical rounded edge, centred on x and y, from z = 0 up.
module rbox(size, r) {
    linear_extrude(size[2]) rrect([size[0], size[1]], r);
}

// A tube with a rectangular section. The wall goes outward from the opening.
module rtube(inner, r, wall, h) {
    linear_extrude(h) difference() {
        rrect([inner[0] + 2 * wall, inner[1] + 2 * wall], r + wall);
        rrect(inner, r);
    }
}

// A plain cube, centred on x and y, from z = 0 up.
module cbox(size) {
    translate([0, 0, size[2] / 2]) cube(size, center = true);
}
