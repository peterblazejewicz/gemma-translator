// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

// ===========================================================================
// Fits, clearances, and the facet count. All values are millimetres.
// ===========================================================================

// Overshoot on each cut. Two faces at the same location make geometry with no
// thickness, and a slicer refuses it. See section 12.4 of CLAUDE.md.
eps = 0.01;

// The facet count changes the dimension of a large hole, thus it is not only
// cosmetic. The well bore holds a 120 mm puck with 1.5 mm around it.
$fn = $preview ? 32 : 180;

// A polygon of $fn sides inside a circle of radius r has a smaller width than
// 2r. Use this radius for a large hole, so that the part that goes in it fits.
function bore_r(r) = r / cos(180 / $fn);

// Clearance between a printed part and a part that we buy.
FIT_SLIDE   = 0.30;   // a part that a person puts in and takes out
FIT_PRESS   = 0.10;   // a part that stays

// More space around a RECEPTACLE of the rear wall.
//
// CAUTION: THIS VALUE ALONE DOES NOT MAKE A HOLE THAT A PERSON CAN USE. It is
// a clearance around the receptacle, and the moulded body of a connector is
// larger than the receptacle it goes into. With this value alone the USB hole
// is 15.5 mm against a measured USB-A connector of 16 mm, and no person can
// connect anything to the appliance. Each hole must clear the larger of the
// two, with PLUG_BODY_CLEAR around it.
PLUG_CLEAR  = 1.0;

// More space around the BODY of a connector. The one hole for the two USB
// stacks sets this value: at a pitch of 17.9 mm two holes of 16 + 2 * 1.0
// overlap, and no wall is between them.
PLUG_BODY_CLEAR = 0.5;

// The charge inlet of the X1201 is a USB-C receptacle. The DXF of the board
// gives its position and its width, and it gives NO height.
//
// TO BE UNDERSTOOD: this value is an estimate. Measure a real USB-C
// receptacle. It sets the z of the centre of the charge hole. The size of that
// hole is PORT_CHARGE_SIZE in dims.scad.
USBC_RECEPT_H = 3.2;         // height of the receptacle above the board

// TO BE UNDERSTOOD: these values are for a printer that is not selected. Do a
// test print of one small part and measure it before the full enclosure goes
// to the printer.
