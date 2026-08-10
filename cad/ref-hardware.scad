// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

// ===========================================================================
// Models of the parts that we buy. NO PART IN THIS FILE IS PRINTED.
// ===========================================================================
//
// Each module is a simple block that gives the space that the real part needs.
// The assembly shows them with the % modifier, thus --render leaves them out
// of the output file. Use them to look for a collision only.
//
// The dimensions come from the datasheets: RP-008347-DS-1 for the Raspberry
// Pi 5, RP-010430-MM-1 for the display, the Speak2 40 datasheet, and the
// drawing and the DXF of the Geekworm X1201.

include <dims.scad>
include <util.scad>

// --- Geekworm X1201 UPS -----------------------------------------------------
// The two 18650 cells are in holders BESIDE the Pi, in the left 48 mm zone.
// The Pi rides 8 mm above the right 58.5 mm zone.
module ref_ups() {
    translate([BRD_X, BRD_Y, UPS_TOP_Z - UPS_PCB[2]]) {
        cbox(UPS_PCB);
        for (cx = CELL_X) translate([cx - BRD_X, 0, UPS_PCB[2]]) {
            cbox([20, 70, 19]);
            translate([0, 0, 1.9 + CELL_D / 2]) rotate([90, 0, 0])
                cylinder(h = CELL_L, d = CELL_D, center = true);
        }
        // 5 V in, three 5 V out, and the power switch header, on the outer edge
        for (hy = [30, 14, -2, -18, -34])
            translate([49.0, hy, UPS_PCB[2]]) cbox([8, 10, 6]);
        translate([4.8, 0, UPS_PCB[2]]) cbox([6, 50.8, 8]);    // GPIO socket
    }
    // The only charge inlet of the appliance. It is on the 85 mm edge at
    // negative x. The DXF gives the face of the connector at X 108.138 and the
    // drills for its shell at X 101.7 and 105.5, thus the body is 6.438 long.
    translate([(UPS_USB_FACE_X + brd_x(101.7)) / 2, UPS_USB_Y, UPS_TOP_Z])
        cbox([brd_x(101.7) - UPS_USB_FACE_X, UPS_USB_W, USBC_RECEPT_H]);
}

// --- Raspberry Pi 5 ---------------------------------------------------------
// The ports face the rear. The USB-C of the Pi must stay unused: the X1201
// supplies the Pi through the GPIO header.
//
// CAUTION: THE CHAIN ON THE LONG EDGE AT NEGATIVE x MEASURES FROM THE SHORT
// EDGE THAT IS OPPOSITE THE SOCKETS, WHICH IS THE FRONT OF THE APPLIANCE.
// RP-008347-DS-1 gives that chain as 11.2 / 25.8 / 39.2. Measured from the
// socket edge instead, each connector is 62.6 mm from its correct position.
// See the connector section of dims.scad; each position is a constant there,
// thus the rib that blocks the USB-C and the model of the Pi read one value.
module ref_pi() {
    translate([PI_X, PI_Y, PI_Z - PI_PCB[2] / 2]) {
        cbox(PI_PCB);
        translate([-2, 10, PI_PCB[2]]) cbox(PI_COOLER);
        translate([25.5, 0, -8.5]) cbox([5, 50.8, 8.5]);           // GPIO
        translate([-17.8, -34.5, PI_PCB[2]]) cbox([16, 21, 13.5]); // RJ45
        // RP-008347-DS-1 gives the x of the two USB stacks: the chain
        // 10.2 / 29.1 / 47 from the long edge.
        for (ux = [PI_USB3_X, PI_USB2_X])
            translate([ux - PI_X, -36.5, PI_PCB[2]])
                cbox([USB_RECEPT[0], 17, USB_RECEPT[1]]);
        translate([PI_USBC_X - PI_X, PI_USBC_Y - PI_Y, PI_PCB[2]]) cbox(PI_USBC);
        for (hy = PI_HDMI_Y)
            translate([PI_HDMI_X - PI_X, hy - PI_Y, PI_PCB[2]]) cbox(PI_HDMI);
        for (fy = PI_FPC_Y)
            translate([PI_FPC_X - PI_X, fy - PI_Y, PI_PCB[2]]) cbox(PI_FPC);
    }
}

// --- Raspberry Pi Touch Display 2, 5 inch -----------------------------------
// Face up, landscape. The carrier holds the module by its four bracket tabs,
// thus the front face of the lens is at CEIL and the deck clears it by
// DECK_RELIEF. Four shapes come below that face: the cover glass, the body,
// the four brackets, and the four screw bosses.
//
// CAUTION: THE GLASS AND THE BODY ARE DIFFERENT SHAPES. DO NOT MAKE ONE BOX OF
// THEM. The glass is the full 143.4 x 91.46 and it is 0.69 mm thick; the body
// below it is 122.74 x 72.96. One box of 143.4 x 91.46 for the full thickness
// says that a shelf at the rear face of the body carries the module at its
// perimeter, and no such material is there. See the display section of
// dims.scad.
module ref_display() {
    translate([0, DISP_Y, 0]) {
        translate([0, 0, DISP_GLASS_Z]) linear_extrude(DISP_GLASS_T)
            rrect(DISP_MOD, DISP_MOD_R);
        translate([0, 0, DISP_UNDER_Z])
            linear_extrude(DISP_GLASS_Z - DISP_UNDER_Z)
                rrect(DISP_BODY_XY, 0);
        for (bx = [-DISP_BRKT_XY[0], DISP_BRKT_XY[0]])
            for (by = [-DISP_BRKT_XY[1], DISP_BRKT_XY[1]])
                translate([bx, by, DISP_BRKT_Z])
                    cbox([DISP_BRKT[0], DISP_BRKT[1], DISP_BRKT_T + eps]);
        for (sx = [-DISP_BOSS_XY[0], DISP_BOSS_XY[0]])
            for (sy = [-DISP_BOSS_XY[1], DISP_BOSS_XY[1]])
                translate([sx, sy, DISP_BOSS_Z])
                    cylinder(h = DISP_BOSS_T + eps, d = DISP_BOSS_D);
    }
}

// --- speakerphone -----------------------------------------------------------
// SPEAK2_D diameter and SPEAK2_H high, both measured. It stands on three
// rubber feet, and its top stands SPEAK2_PROUD above the deck.
//
// The measured height INCLUDES the feet, thus the moulded body is
// SPEAK2_H - PAD_T high and it stands on them. The two lower steps are fixed
// and the top cylinder takes the remainder: a change of SPEAK2_H then changes
// the part and not the position of its bottom face.
//
// AN ASSERT IN A MODULE RUNS ONLY WHEN THAT MODULE RUNS. In ref_puck() this
// one is silent for each part that the printer makes, because an export of the
// body, of the deck or of the carrier draws no speakerphone. It is at file
// scope, thus it runs on each include of this file.
PUCK_BODY_H = SPEAK2_H - PAD_T;
assert(PUCK_BODY_H > 26, "the body of the speakerphone is shorter than its steps");

module ref_puck() {
    body_h = PUCK_BODY_H;
    translate([0, WELL_Y, DISC_TOP + PAD_T]) {
        cylinder(h = 8, d1 = 104, d2 = 96);
        translate([0, 0, 8]) cylinder(h = 18, d1 = SPEAK2_D - 6, d2 = SPEAK2_D);
        translate([0, 0, 26]) cylinder(h = body_h - 26, d = SPEAK2_D);
    }
    // The three feet, at a radius of PAD_R from the centre of the well. They
    // must be on the ring of the well floor. See PAD_R in dims.scad.
    for (p = [[-32, 38], [32, 38], [0, -45]])
        translate([p[0], WELL_Y + p[1], DISC_TOP]) cylinder(h = PAD_T, d = 8);
}

// --- push-to-talk switch ----------------------------------------------------
// Left = person 1, right = person 2. The bezel seats on the deck.
//
// CAUTION: THE NUT IS THE WIDEST PART OF THIS SWITCH. DO NOT REMOVE IT FROM
// THIS MODEL. The bezel is 22 across and the nut is 26 with its clearance,
// thus a test against the bezel finds no collision where the nut has one.
// Without the nut a test gives "body ^ PTT switch EMPTY" for a nut that stands
// 2.5 mm in the side wall, 44 mm3 in the cover glass of the display module and
// 9 mm3 in a deck boss. A model of a part that does not show all of that part
// makes each test of the part that it does not show give EMPTY.
module ref_ptt() {
    for (px = [-PTT_X, PTT_X]) translate([px, PTT_Y, 0]) {
        translate([0, 0, DECK_Z]) cylinder(h = 3, d = PTT_BEZEL_D);
        translate([0, 0, DECK_Z + 3]) cylinder(h = 2.5, d = 15);
        translate([0, 0, DECK_Z - 17]) cylinder(h = 17, d = PTT_BARREL_D);
        translate([0, 0, DECK_Z - PTT_DEPTH]) cbox([10, 10, 7.8]);
        // The nut goes on the barrel, against the bottom face of the deck.
        translate([0, 0, CEIL - SW_NUT_L]) cylinder(h = SW_NUT_L, d = SW_NUT_AC);
    }
}

// --- the switch for the electrical supply -----------------------------------
// The same part as a push-to-talk switch, on the side wall at positive x, thus
// the bezel seats on the outer face of that wall and the terminals point
// inward. See the switch section of dims.scad.
module ref_pwr_sw() {
    translate([W / 2, PWR_SW_Y, PWR_SW_Z]) rotate([0, 90, 0]) {
        cylinder(h = 3, d = PTT_BEZEL_D);
        translate([0, 0, 3]) cylinder(h = 2.5, d = 15);
        translate([0, 0, -17]) cylinder(h = 17, d = PTT_BARREL_D);
        translate([0, 0, -PTT_DEPTH]) cbox([10, 10, 7.8]);
        // The nut goes on the barrel, against the INNER face of the wall. It
        // starts at WALL and not at 0: the origin here is the outer face.
        translate([0, 0, -WALL - SW_NUT_L]) cylinder(h = SW_NUT_L, d = SW_NUT_AC);
    }
}

// --- brass spacers ----------------------------------------------------------
// M2.5 x 5 under the X1201, M2.5 5+3 between the X1201 and the Pi.
module ref_spacers() {
    for (sx = UPS_SPACER_X) for (sy = UPS_SPACER_Y)
        translate([sx, sy, FLOOR_T])
            cylinder(h = UPS_SPACER_H, d = UPS_SPACER_D);
    for (sy = PI_SPACER_Y)
        translate([PI_SPACER_X, sy, UPS_TOP_Z])
            cylinder(h = UPS_SPACER_H + 3, d = UPS_SPACER_D);
}

// --- rubber feet ------------------------------------------------------------
module ref_feet() {
    for (f = FEET)
        translate([f[0], f[1], -FEET_H]) cylinder(h = FEET_H, r = f[2]);
}

module ref_all() {
    ref_ups();
    ref_pi();
    ref_display();
    ref_puck();
    ref_ptt();
    ref_pwr_sw();
    ref_spacers();
    ref_feet();
}
