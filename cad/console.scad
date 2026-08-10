// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

// ===========================================================================
// Flat console enclosure. Three printed parts: the body, the deck, and the
// carrier frame that holds the display module.
// ===========================================================================
//
//   openscad --backend=Manifold -D 'part="body"' -o body.3mf console.scad
//
// PARTS gives each value of part. The dispatcher at the end of this file and
// the message of its assert come from it.

include <dims.scad>
include <util.scad>
use     <ref-hardware.scad>

PRINTED = ["body", "deck", "carrier"];
VIEWS   = ["section", "assembly", "none"];
PARTS   = concat(PRINTED, VIEWS);

part = "assembly";

// --- plan shapes ------------------------------------------------------------

// The rectangular part of the body. o insets the shape.
module rect_2d(o = 0) {
    offset(r = -o) hull() {
        for (sx = [-1, 1]) {
            translate([sx * (W / 2 - CORNER_R_REAR), REAR_Y + CORNER_R_REAR])
                circle(r = CORNER_R_REAR);
            translate([sx * (W / 2 - CORNER_R_FRONT), FRONT_EDGE_Y - CORNER_R_FRONT])
                circle(r = CORNER_R_FRONT);
        }
    }
}

module well_2d(o = 0) { translate([0, WELL_Y]) circle(r = WELL_RO - o); }

// The outline of the appliance: the rectangle and the speaker cylinder.
module outline_2d(o = 0) { union() { rect_2d(o); well_2d(o); } }

module well_bore_2d() { translate([0, WELL_Y]) circle(r = bore_r(WELL_RI)); }

// A sector, centred on the +x axis.
module wedge_2d(a, r) {
    polygon(concat([[0, 0]],
        [for (i = [0 : 12]) [r * cos(-a / 2 + a * i / 12),
                             r * sin(-a / 2 + a * i / 12)]]));
}

// The wall of the well. The opening at the rear passes the captive cable.
module well_tube_2d() {
    translate([0, WELL_Y]) difference() {
        circle(r = WELL_RO);
        well_bore_2d_local();
        rotate(-90) wedge_2d(WELL_GAP_DEG, WELL_RO + 1);
    }
}
module well_bore_2d_local() { circle(r = bore_r(WELL_RI)); }

// The space inside the enclosure, at a level with no well floor.
module cavity_2d() {
    difference() {
        union() { rect_2d(WALL); well_bore_2d(); }
        if (WELL_TUBE == "tube") well_tube_2d();
    }
}

// The material of the well floor, in plan.
// The outer edge uses bore_r, the same value as the bore. With WELL_RI the
// ring stops 0.0094 mm short of the tube and leaves a ring of nothing.
module well_floor_2d() {
    translate([0, WELL_Y]) difference() {
        circle(r = bore_r(WELL_RI));
        if (WELL_FLOOR == "ledge") circle(r = LEDGE_IN_R);
    }
}

// The space inside the enclosure, at the level of the well floor.
module cavity_at_floor_2d() {
    difference() { cavity_2d(); well_floor_2d(); }
}

// --- features that go inside the enclosure ----------------------------------

// The plan of the two boards, with clearance, through the full height of the
// bay. Nothing printed that stands on the floor can be inside it: the X1201 is
// 5 mm above the floor and a pillar that goes through it stops the assembly.
//
// THE PI IS NOT INSIDE THE X1201. It goes 0.7 past the X1201 at each end along
// y, and its sockets at the rear stand 3 mm past the edge of its own board.
// With the X1201 alone, a boss at [-66, -106] goes into the body of the
// ethernet socket by 207.58 mm3. Thus the keep-out is the two boards together.
module board_keepout() {
    translate([BRD_X, BRD_Y, -eps])
        linear_extrude(CEIL + 2 * eps)
            square([UPS_PCB[0] + 2 * BOARD_CLEAR, UPS_PCB[1] + 2 * BOARD_CLEAR],
                   center = true);
    // The x at negative x is the face of the USB-C socket and not the edge of
    // the board: that socket stands 2.25 mm off the board, and it is the part
    // of the Pi that is nearest to the side wall. PI_RIB_X uses the same value.
    pi_x0 = min(PI_EDGE_X, PI_USBC_X - PI_USBC[0] / 2) - BOARD_CLEAR;
    pi_x1 = PI_X + PI_PCB[0] / 2 + BOARD_CLEAR;
    // The keep-out goes to the inner face of the rear wall and not to the face
    // of the sockets. The space between the two belongs to the port tunnels.
    // A stop at the face of the sockets leaves 0.3 mm of the boss at
    // [-66, -106] standing in the ethernet opening, which is 22.19 mm3.
    pi_y0 = min(PI_PORT_FACE_Y - BOARD_CLEAR, REAR_Y + WALL);
    pi_y1 = PI_Y + PI_PCB[1] / 2 + BOARD_CLEAR;
    translate([(pi_x0 + pi_x1) / 2, (pi_y0 + pi_y1) / 2, -eps])
        linear_extrude(CEIL + 2 * eps)
            square([pi_x1 - pi_x0, pi_y1 - pi_y0], center = true);
}

// See BOSSES in dims.scad for the cut at negative x.
module bosses() {
    difference() {
        union() {
            for (b = BOSSES)
                translate([b[0], b[1], FLOOR_T]) cylinder(h = b[3], r = b[2]);
            for (d = DECK_BOSSES)
                translate([d[0], d[1], FLOOR_T])
                    cylinder(h = CEIL - FLOOR_T, r = DECK_BOSS_R);
        }
        board_keepout();
    }
}

// The angle of one rib. well_ribs() and under_rib() MUST use this same
// function. With two different angles the guard keeps each hole that is clear
// and cuts the foot from all eight ribs.
function rib_angle(i) = RIB_OFFSET + i * 360 / RIB_N;

// Ribs that carry the ring of the well floor. Without them the ring is a
// horizontal overhang of 20.5 mm and a printer cannot make it.
module well_ribs() {
    if (WELL_FLOOR == "ledge")
        for (i = [0 : RIB_N - 1])
            translate([0, WELL_Y, FLOOR_T]) rotate(rib_angle(i))
                translate([RIB_IN_R, -RIB_T / 2, 0])
                    cube([bore_r(WELL_RI) - RIB_IN_R, RIB_T, DISC_TOP - FLOOR_T]);
}

// A boss on each side wall at each of the four ear positions of the carrier.
// The top face at CARRIER_BOT is the ledge that the carrier lands on, and the
// bore takes a heat-set insert for the screw that holds it down.
//
// The bottom face is at 45 degrees. The boss stands on the WALL and not on the
// floor, thus nothing goes down to the level of the boards, and a horizontal
// face below it does not print with no support. The bore is at the middle of
// the boss, where the material below the top face is CARRIER_LEDGE_H -
// CARRIER_LEDGE_IN / 2, which is 7 mm against an insert of 6.
module carrier_ledges() {
    x0 = W / 2 - WALL;
    for (sx = [-1, 1]) for (sy = CARRIER_SCREW_Y)
        translate([0, sy, 0]) rotate([90, 0, 0])
            translate([0, 0, -CARRIER_LEDGE_L / 2])
                linear_extrude(CARRIER_LEDGE_L)
                    polygon([[sx * x0, CARRIER_BOT],
                             [sx * (x0 - CARRIER_LEDGE_IN), CARRIER_BOT],
                             [sx * (x0 - CARRIER_LEDGE_IN),
                              CARRIER_BOT - CARRIER_LEDGE_H + CARRIER_LEDGE_IN],
                             [sx * x0, CARRIER_BOT - CARRIER_LEDGE_H]]);
}

module port_tunnel(p) {
    translate([p[0], REAR_Y + WALL, p[1]]) rotate([-90, 0, 0])
        rtube([p[2], p[3]], PORT_R, TUNNEL_T, TUNNEL_L);
}

// The clear path of a connector: the hole in the wall and the tunnel behind it.
module port_bore(p) {
    translate([p[0], REAR_Y - eps, p[1]]) rotate([-90, 0, 0])
        linear_extrude(WALL + TUNNEL_L + 2 * eps) rrect([p[2], p[3]], PORT_R);
}

// The cut at TUNNEL_TOP follows the display module. See TUNNEL_TOP in
// dims.scad: with the module lower, a full wall above the USB tunnel goes
// into it.
//
// THE BORES COME OUT OF THE UNION AND NOT OUT OF ONE TUBE. The ethernet hole
// and the USB hole are RJ45_TO_USB apart, which is 1.4 mm, and each tube has a
// wall of 2 mm. Thus the two walls touch, and each one goes 0.6 mm into the
// opening of the other. A tube on its own cannot see this.
module port_tunnels() {
    intersection() {
        difference() {
            union() {
                port_tunnel(PORT_RJ45);
                port_tunnel(PORT_USB);
            }
            port_bore(PORT_RJ45);
            port_bore(PORT_USB);
        }
        translate([-W, REAR_Y - 1, -1]) cube([2 * W, 20, TUNNEL_TOP + 1]);
    }
}

// The tunnel of the charge inlet. It is the same part as port_tunnel, on the
// other axis: the hole is in the side wall at negative x, not in the rear wall.
module charge_tunnel() {
    translate([-(W / 2 - WALL), SIDE_CHARGE[0], SIDE_CHARGE[1]])
        rotate([0, 90, 0])
            rtube([SIDE_CHARGE[3], SIDE_CHARGE[2]], PORT_R, TUNNEL_T,
                  CHARGE_TUNNEL_L);
}

// A rib in front of the Pi's own USB-C socket. Nobody must apply power to that
// socket. Geekworm's wiki says "Do not apply power to your Raspberry Pi via the
// Type-C USB socket": the X1201's own USB-C is the only charge inlet, and the
// X1201 feeds the Pi through pogo pins. Geekworm does not publish what breaks
// if you power both, and its own case does NOT block the port - it cuts a hole
// there and prints a warning beside it. Blocking it is our decision, and it is
// the safer one for an appliance a stranger can plug a charger into. Do not
// delete this rib to save material or to make the part simpler.
//
// It stands on the floor and joins the side wall at negative x, so it prints
// with no support. It stops at CARRIER_BOT: the socket is at z 19 to 22.5, so
// nothing is necessary higher, and the carrier closes the space above that
// level. At DISP_BRKT_Z the rib stands 0.16 mm3 in the rear corner of the
// carrier.
module pi_usbc_rib() {
    translate([-(W / 2 - WALL), PI_RIB_Y - PI_RIB_T / 2, FLOOR_T])
        cube([(W / 2 - WALL) + PI_RIB_X, PI_RIB_T, CARRIER_BOT - FLOOR_T]);
}

// --- holes ------------------------------------------------------------------

module rear_port(p) {
    translate([p[0], REAR_Y + WALL + eps, p[1]]) rotate([90, 0, 0])
        linear_extrude(WALL + 2 * eps) rrect([p[2], p[3]], PORT_R);
}

module rear_ports() {
    rear_port(PORT_RJ45);
    rear_port(PORT_USB);
}

// The slot below the USB 2.0 pair, for the captive cable of the speakerphone.
// The chamfer at each face of the wall keeps the edge of a printed layer off
// the insulation of the cable. See CABLE_EXIT in dims.scad.
module cable_exit() {
    big = [CABLE_EXIT[0] + 2 * CABLE_EXIT_CHM, CABLE_EXIT[1] + 2 * CABLE_EXIT_CHM];
    translate([CABLE_EXIT_X, REAR_Y + WALL, CABLE_EXIT_Z]) rotate([90, 0, 0]) {
        translate([0, 0, -eps]) linear_extrude(WALL + 2 * eps)
            rrect(CABLE_EXIT, CABLE_EXIT_R);
        hull() {   // the chamfer at the inner face of the wall
            translate([0, 0, -eps]) linear_extrude(eps)
                rrect(big, CABLE_EXIT_R + CABLE_EXIT_CHM);
            translate([0, 0, CABLE_EXIT_CHM - eps]) linear_extrude(eps)
                rrect(CABLE_EXIT, CABLE_EXIT_R);
        }
        hull() {   // the chamfer at the outer face
            translate([0, 0, WALL]) linear_extrude(eps)
                rrect(big, CABLE_EXIT_R + CABLE_EXIT_CHM);
            translate([0, 0, WALL - CABLE_EXIT_CHM - eps]) linear_extrude(eps)
                rrect(CABLE_EXIT, CABLE_EXIT_R);
        }
    }
}

// The hole for the switch of the electrical supply, through the side wall at
// positive x. See the switch section of dims.scad.
module pwr_switch_hole() {
    translate([W / 2 - WALL - eps, PWR_SW_Y, PWR_SW_Z]) rotate([0, 90, 0])
        cylinder(h = WALL + 2 * eps, r = bore_r(PTT_HOLE_D / 2));
}

// The hole for the charge inlet, through the side wall at negative x.
module charge_hole() {
    translate([-(W / 2 + eps), SIDE_CHARGE[0], SIDE_CHARGE[1]])
        rotate([0, 90, 0])
            linear_extrude(WALL + 2 * eps)
                rrect([SIDE_CHARGE[3], SIDE_CHARGE[2]], PORT_R);
}

// A hole that breaks into the charge hole or into the wall of its tunnel gives
// no air and it makes the opening larger than the plug. The charge hole is in
// the wall at negative x only, thus the two walls are not the same. The
// keep-out comes from the feature, as near_boss and near_foot do in
// floor_vents: a hole that moves or that becomes larger moves this keep-out
// with it.
function near_charge(sx, hy, hz) = sx < 0 &&
    abs(hy - SIDE_CHARGE[0]) < SIDE_CHARGE[2] / 2 + TUNNEL_T + SIDE_HOLE_R + 1 &&
    abs(hz - SIDE_CHARGE[1]) < SIDE_CHARGE[3] / 2 + TUNNEL_T + SIDE_HOLE_R + 1;

// The rib in front of the USB-C socket of the Pi stands on the inner face of
// the wall at negative x. A hole behind it opens on to the edge of the rib, and
// the rib then divides the opening into two parts. The keep-out comes from the
// rib.
function behind_rib(sx, hy) = sx < 0 &&
    abs(hy - PI_RIB_Y) < PI_RIB_T / 2 + SIDE_HOLE_R;

// The bezel of the switch of the electrical supply seats on the outer face of
// the wall at positive x. A hole below that bezel gives it no seat, and a hole
// that is nearer breaks into the hole for the barrel. The keep-out comes from
// the bezel, as near_charge comes from the charge hole.
function near_pwr_sw(sx, hy, hz) = sx > 0 &&
    sqrt((hy - PWR_SW_Y) * (hy - PWR_SW_Y) + (hz - PWR_SW_Z) * (hz - PWR_SW_Z))
        < PTT_BEZEL_D / 2 + SIDE_HOLE_R + 1;

// A boss of the carrier stands on the inner face of each side wall. A hole
// behind it opens into the boss, and a hole that laps the top face takes away
// the ledge that the carrier lands on. The keep-out comes from the boss, thus
// a boss that moves or that becomes larger takes its keep-out with it.
function behind_ledge(hy, hz) = max([for (sy = CARRIER_SCREW_Y)
    abs(hy - sy) < CARRIER_LEDGE_L / 2 + SIDE_HOLE_R &&
    abs(hz - (CARRIER_BOT - CARRIER_LEDGE_H / 2))
        < CARRIER_LEDGE_H / 2 + SIDE_HOLE_R ? 1 : 0]) == 1;

function side_vent_ok(sx, hy, hz) =
    !near_charge(sx, hy, hz) && !behind_rib(sx, hy) &&
    !near_pwr_sw(sx, hy, hz) && !behind_ledge(hy, hz);

module side_vents() {
    for (sx = [-1, 1])
        for (iy = [-SIDE_NX : SIDE_NX])
            for (iz = [-SIDE_NZ : SIDE_NZ]) {
                hy = SIDE_CENTRE[0] + iy * SIDE_PITCH[0];
                hz = SIDE_CENTRE[1] + iz * SIDE_PITCH[1];
                if (side_vent_ok(sx, hy, hz))
                    translate([sx * (W / 2 - WALL / 2), hy, hz])
                        rotate([0, 90, 0])
                            cylinder(h = WALL + 2 * eps, r = SIDE_HOLE_R,
                                     center = true);
            }
}

// True if the hole at (hx, hy) is below a rib of the well floor.
function under_rib(hx, hy) =
    WELL_FLOOR != "ledge" ? false :
    let (dx = hx, dy = hy - WELL_Y, rr = sqrt(dx * dx + dy * dy))
    rr < RIB_IN_R - 2 || rr > WELL_RI + 2 ? false :
    let (a = atan2(dy, dx))
    max([for (i = [0 : RIB_N - 1])
            let (d = abs(((a - rib_angle(i) + 540) % 360) - 180))
            d < 12 ? 1 : 0]) == 1;

// Each keep-out comes from the feature itself. With a number written here, a
// larger boss or a spacer that moves cuts through it in silence.
function near_boss(hx, hy) = max([for (b = BOSSES)
    sqrt((hx - b[0]) * (hx - b[0]) + (hy - b[1]) * (hy - b[1]))
        < b[2] + FLOOR_HOLE_R + 2 ? 1 : 0]) == 1;

// The countersink is the largest of the three circles at a foot of the X1201,
// thus it gives the keep-out. The 4 mm is the floor between the two holes.
function near_foot(hx, hy) = max([for (sx = UPS_SPACER_X) for (sy = UPS_SPACER_Y)
    sqrt((hx - sx) * (hx - sx) + (hy - sy) * (hy - sy))
        < UPS_CSK_D / 2 + FLOOR_HOLE_R + 4 ? 1 : 0]) == 1;

// One test for one hole of the floor grid. It is a function, thus a test can
// count the holes and measure them without the mesh.
function floor_vent_ok(hx, hy) =
    (abs(hx) <= 60 && abs(hy - BRD_Y) <= 37
     || sqrt(hx * hx + (hy - WELL_Y) * (hy - WELL_Y)) <= FLOOR_VENT_R)
    && !near_boss(hx, hy) && !near_foot(hx, hy)
    && !under_rib(hx, hy);

// The floor grid is the one intake for the air below the boards, and the
// downfire vent below the basement of the speaker. It keeps clear of each boss
// and of the four spacer feet of the X1201.
module floor_vents() {
    for (ix = [-5 : 5]) for (iy = [-8 : 8]) {
        hx = ix * FLOOR_PITCH;
        hy = -(iy * FLOOR_PITCH - 2);
        if (floor_vent_ok(hx, hy))
            translate([hx, hy, -eps])
                cylinder(h = FLOOR_T + 2 * eps, r = FLOOR_HOLE_R);
    }
}

// A screw goes up through the floor into each of the four spacers that carry
// the X1201. The head goes in a countersink on the bottom face: the feet are
// 6 mm high, thus a head that stands out does not lift the enclosure, but a
// countersink is the better work.
module ups_screw_bores() {
    for (sx = UPS_SPACER_X) for (sy = UPS_SPACER_Y) translate([sx, sy, -eps]) {
        cylinder(h = FLOOR_T + 2 * eps, d = UPS_SCREW_CLR_D);
        cylinder(h = (UPS_CSK_D - UPS_SCREW_CLR_D) / 2 + eps,
                 d1 = UPS_CSK_D, d2 = UPS_SCREW_CLR_D);
    }
}

// The bore stops 2 mm below the top of the boss. A bore through the top makes
// a hole in the deck seat and the screw then holds nothing.
module screw_bores() {
    for (b = BOSSES) if (b[4]) {
        translate([b[0], b[1], FLOOR_T]) cylinder(h = b[3] - 2, r = SCREW_BORE_R);
        translate([b[0], b[1], -eps]) cylinder(h = FLOOR_T + 2 * eps, r = SCREW_CLEAR_R);
    }
    // Bore for the heat-set insert, open at the top of the boss.
    for (d = DECK_BOSSES)
        translate([d[0], d[1], CEIL - INSERT_DEPTH])
            cylinder(h = INSERT_DEPTH + eps, d = INSERT_BORE_D);
}

// The captive cable of the speakerphone goes through the well floor here. The
// is necessary also with the ring: the cable comes out of the groove at the
// base of the puck, at a radius of 47 to 55 mm, which is above the ring and
// not above the open middle.
module cable_slot() {
    translate([0, CABLE_SLOT_Y, DISC_BOT - eps])
        linear_extrude(DISC_TOP - DISC_BOT + 2 * eps)
            rrect(CABLE_SLOT, CABLE_SLOT_R);
}

// --- the three printed parts ------------------------------------------------

module body() {
    difference() {
        union() {
            difference() {
                linear_extrude(CEIL) outline_2d();
                translate([0, 0, FLOOR_T])
                    linear_extrude(DISC_BOT - FLOOR_T) cavity_2d();
                translate([0, 0, DISC_BOT])
                    linear_extrude(DISC_TOP - DISC_BOT) cavity_at_floor_2d();
                translate([0, 0, DISC_TOP])
                    linear_extrude(CEIL - DISC_TOP + eps) cavity_2d();
            }
            bosses();
            well_ribs();
            carrier_ledges();
            port_tunnels();
            charge_tunnel();
            pi_usbc_rib();
        }
        rear_ports();
        cable_exit();
        charge_hole();
        pwr_switch_hole();
        side_vents();
        floor_vents();
        screw_bores();
        ups_screw_bores();
        cable_slot();
    }
}

module deck() {
    difference() {
        translate([0, 0, CEIL]) linear_extrude(WALL) difference() {
            outline_2d();
            translate([0, DISP_Y])
                rrect([LENS[0] - 2 * LENS_LIP, LENS[1] - 2 * LENS_LIP], 3.5);
            translate([0, WELL_Y]) circle(r = bore_r(WELL_RI));
            for (px = [-PTT_X, PTT_X])
                translate([px, PTT_Y]) circle(r = bore_r(PTT_HOLE_D / 2));
        }
        // The relief above the module. The deck bears on the top face of the
        // wall at CEIL, and above the module it stays DECK_RELIEF higher.
        // With the bottom face of the deck and the front face of the cover
        // glass at CEIL together, the deck presses on a strip of bare glass
        // 15.18 mm wide with nothing below it. The carrier carries the module,
        // thus the deck must not touch it at all.
        translate([0, DISP_Y, CEIL - eps])
            linear_extrude(DECK_RELIEF + eps)
                rrect(DISP_MOD + [1.2, 1.2], DISP_MOD_R + 0.6);
        // Screw into the insert below, with a head that is flush with the top.
        for (d = DECK_BOSSES) translate([d[0], d[1], 0]) {
            translate([0, 0, CEIL - eps])
                cylinder(h = WALL + 2 * eps, d = DECK_SCREW_D);
            translate([0, 0, DECK_Z - (DECK_CSK_D - DECK_SCREW_D) / 2])
                cylinder(h = (DECK_CSK_D - DECK_SCREW_D) / 2 + eps,
                         d1 = DECK_SCREW_D, d2 = DECK_CSK_D);
        }
    }
}

// The carrier frame. It hangs the display module on its four bracket tabs and
// it lands on the four bosses of the side walls. See the carrier section of
// dims.scad.
//
// A person assembles the appliance in these steps: the frame goes in and the
// four screws hold it down; the module comes down on to it and four screws go
// up into its tabs; then the deck closes the top and it touches the module
// nowhere.
module carrier() {
    translate([0, (CARRIER_Y[0] + CARRIER_Y[1]) / 2, CARRIER_BOT])
        linear_extrude(CARRIER_T) difference() {
            rrect([2 * CARRIER_X, CARRIER_Y[1] - CARRIER_Y[0]], CARRIER_R);
            translate([0, DISP_Y - (CARRIER_Y[0] + CARRIER_Y[1]) / 2]) {
                rrect(CARRIER_WIN, 5);
                for (bx = [-DISP_BRKT_XY[0], DISP_BRKT_XY[0]])
                    for (by = [-DISP_BRKT_XY[1], DISP_BRKT_XY[1]])
                        translate([bx, by]) circle(d = CARRIER_SCREW_D);
                for (sx = [-DISP_BOSS_XY[0], DISP_BOSS_XY[0]])
                    for (sy = [-DISP_BOSS_XY[1], DISP_BOSS_XY[1]])
                        translate([sx, sy]) circle(d = CARRIER_BOSS_CLR_D);
            }
            for (sx = [-CARRIER_SCREW_X, CARRIER_SCREW_X])
                for (sy = CARRIER_SCREW_Y)
                    translate([sx, sy - (CARRIER_Y[0] + CARRIER_Y[1]) / 2])
                        circle(d = DECK_SCREW_D);
        }
}

// --- output -----------------------------------------------------------------

module printed_parts() { body(); deck(); carrier(); }
module all_printed()   { color(FILAMENT) printed_parts(); }

// Keeps the half at negative x.
module cut_half() {
    intersection() {
        children();
        translate([-300, -300, -100]) cube([300, 600, 300]);
    }
}

// A cut on the plane x = 0. It shows the stack of levels: the floor, the
// basement, the well floor, the puck, the boards, and the deck.
//
// Each colour gets its own cut. A colour does not go through a boolean
// operation: the result of the operation takes one colour only, thus a cut of
// the two groups together makes the parts that we buy the same colour as the
// filament.
module section_yz() {
    color(FILAMENT)   cut_half() printed_parts();
    color(REF_COLOUR) cut_half() ref_all();
}

// CAUTION: this intersection is a safety check, not decoration. Do not remove
// it to make the model more simple.
//
// The deck is one extrusion of a 2D shape. OpenSCAD
// keeps such a part as a PolySet and sends it to no geometry engine. Then
// `--summary geometry` writes NO status line and CGAL gives NO warning, which
// looks exactly the same as a part that passed the test of section 12.5 of
// CLAUDE.md -- but nothing was tested. A boolean operation makes the engine do
// the work. The cube is larger than the part, thus the shape does not change.
// render() does NOT do this; OpenSCAD 2025.09.07 removes it for a PolySet.
module gate() {
    intersection() {
        children();
        cube(1000, center = true);
    }
}

// The message comes from PARTS, thus a value that the dispatcher accepts and
// the message does not give is not possible.
function is_part(p) = len([for (n = PARTS) if (n == p) 1]) > 0;
assert(is_part(part), str("part must be one of ", PARTS, ". It is: ", part));

if (part == "body")           color(FILAMENT) gate() body();
else if (part == "deck")      color(FILAMENT) gate() deck();
else if (part == "carrier")   color(FILAMENT) gate() carrier();
else if (part == "section")   section_yz();
else if (part == "assembly")  { all_printed(); %ref_all(); }
// "none" draws nothing. A test file includes this file with part="none", then
// it calls one module and puts it in an intersection with another, to measure
// a collision.
