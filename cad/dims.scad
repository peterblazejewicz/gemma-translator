// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

include <tolerances.scad>

// ===========================================================================
// Dimensions of the flat console enclosure. All values are millimetres.
// ===========================================================================
//
// COORDINATES. x is the width, y is the depth, and z is the height. The rear
// of the appliance is at negative y. The floor of the enclosure is at z = 0.
// The rubber feet are below z = 0.

// --- shell ------------------------------------------------------------------
W            = 152;      // full width
WALL         = 3;        // side wall, rear wall, floor, and deck thickness
DECK_Z       = 52;       // top of the deck
CEIL         = DECK_Z - WALL;   // 49, top of the walls, underside of the deck
FLOOR_T      = WALL;     // 3
REAR_Y       = -116;     // outer face of the rear wall
// FRONT_EDGE_Y is in the push-to-talk section, because it is DERIVED there.
// The nut of a push-to-talk switch hangs below the deck, between the cover
// glass of the display module and this wall, and the wall must clear it.
//
// CAUTION: THIS RADIUS IS NOT A DECISION ABOUT HOW THE ENCLOSURE LOOKS. The
// rear wall is flat for abs(x) <= W/2 - CORNER_R_REAR. The boards stand toward
// the wall at negative x, thus the ethernet tunnel goes to x -68.862. A radius
// of 10 stops the flat at 66 and puts the tunnel in the round corner. A radius
// of 6 puts the flat at 70. The assert below PI_PORT_FACE_Y holds this.
CORNER_R_REAR  = 6;
CORNER_R_FRONT = 8;
FEET_H       = 6;        // rubber feet, below the floor

// --- speaker well -----------------------------------------------------------
// The speakerphone is a cylinder. These two values are MEASURED: 121 mm
// diameter and 32 mm high. The Speak2 40 datasheet gives 120 and 33. A
// measurement of the part in your hand is better than a datasheet, thus the
// measured values stay.
//
// The 32 mm INCLUDES the three rubber feet, thus PAD_T is a part of SPEAK2_H
// and it is not added to it.
SPEAK2_D     = 121;
SPEAK2_H     = 32;
// The well is a tube that stands past the front edge and rounds off the body.
// CAUTION: the centre is at 47.5. At 40 the tube comes to y = -25.5, thus the
// bay is 87.5 mm deep and the display module is 91.46 mm deep: the module does
// not go in, at any position. At 47.5 the bay is 94.5 mm deep and the module
// goes in. The two asserts below BAY_FRONT_Y hold the depth and the two gaps,
// thus no comment here holds a number that a change that comes after can make
// incorrect.
WELL_Y       = 47.5;     // centre of the well
WELL_RI      = SPEAK2_D / 2 + 1.5;    // 62.0, bore. 1.5 mm around the puck.
WELL_RO      = WELL_RI + 4;           // 66.0, outer face of the 4 mm tube
WELL_GAP_DEG = 25.2;     // opening in the tube at the rear, for the cable
// The top of the speakerphone stands SPEAK2_PROUD ABOVE the deck. It is not
// flush. The buttons of the speakerphone are on its top face, at the rim: with
// a flush top a finger on a button touches the deck, and a person cannot get
// the speakerphone out of the well. The top of the deck is not flat, and that
// is permitted: the two push-to-talk switches are also above it.
//
// DERIVE the level of the well floor from this value. With a written 22.5 a
// change of SPEAK2_H moves the top of the part and no line says so.
SPEAK2_PROUD = 2.5;      // 2 to 3 mm is acceptable
WELL_FLOOR_T = 2;        // the ring that the puck stands on
DISC_TOP     = DECK_Z - SPEAK2_H + SPEAK2_PROUD;   // 22.5, top of the well floor
DISC_BOT     = DISC_TOP - WELL_FLOOR_T;   // 20.5, underside of the well floor
assert(DISC_BOT > FLOOR_T,
       "the floor of the well is below the floor of the enclosure");
// The bore of the deck must pass the speakerphone, because the part now goes
// through the deck and not only up to it.
assert(bore_r(WELL_RI) > SPEAK2_D / 2,
       "the speakerphone does not go through the bore of the deck");

// Two decisions about the shape of the well. Each one changes the part.
//
// WELL_TUBE: "tube" keeps a full tube with a small opening at the rear, thus
// the well is a closed cylinder in the enclosure. "open" lets the well and the
// bay of the electronics become one volume, which uses less material and gives
// the cooler more volume.
WELL_TUBE = "tube";
//
// WELL_FLOOR: "disc" is a full disc. A printer CANNOT make it: it is a
// horizontal span of 123 mm above the basement, and the design says to use no
// support. "ledge" is a ring on ribs, which a printer can make.
WELL_FLOOR    = "ledge";
assert(WELL_TUBE == "tube" || WELL_TUBE == "open", "WELL_TUBE: tube or open");
assert(WELL_FLOOR == "ledge" || WELL_FLOOR == "disc", "WELL_FLOOR: ledge or disc");
LEDGE_IN_R    = 41;      // inner radius of the ring. It must carry the 3 pads,
                         // which are at a radius of 45 to 50 mm.
RIB_N         = 8;       // ribs below the ring
RIB_T         = 3;
RIB_IN_R      = 35;      // the ring bridges 29 mm between two rib tops
RIB_OFFSET    = 22.5;    // 360/RIB_N/2. It keeps a rib away from the cable
                         // slot at 270 degrees, thus the slot cuts no rib.
// The three rubber feet of the puck. They are a part of SPEAK2_H, thus they
// add nothing to the stack. The value stays for ref_puck() only: the three
// feet must be on the ring of the well floor and not above its open middle,
// and the model of the part cannot show that without them.
PAD_T        = 2;        // rubber feet, at the base of the puck
// CABLE_SLOT, CABLE_SLOT_R and CABLE_SLOT_Y are with CABLE_EXIT, in the
// section for the cable of the speakerphone. The free end of that cable goes
// through the two openings one after the other, thus the two must agree, and
// they read as one only if they are together.

// The three feet are on the ring and not above its open middle. The two
// values are the radius of the nearest foot and of the farthest foot from the
// centre of the well. ref_puck() puts them at (0, -45) and at (+/-32, 38).
PAD_R        = [45, 49.65];
assert(PAD_R[0] > LEDGE_IN_R && PAD_R[1] < WELL_RI,
       "a foot of the speakerphone is not on the ring of the well floor");
assert(PAD_T < SPEAK2_H,
       "the feet of the speakerphone are not inside its measured height");

FRONT_Y      = WELL_Y + WELL_RO;   // 113.5, the front of the appliance
LENGTH       = FRONT_Y - REAR_Y;   // 229.5

// --- display ----------------------------------------------------------------
// Raspberry Pi Touch Display 2, 5 inch. RP-010430-MM-1, the physical
// specification on page 3.
DISP_Y       = -65.5;    // centre of the module
DISP_MOD     = [143.4, 91.46];   // outline of the module
DISP_MOD_R   = 5;        // corner radius of the module
LENS         = [111.4, 63.1];    // glass opening
// CAUTION: A LIP IS NOT POSSIBLE HERE. The active area is 62.1 x 110.4 and the
// opening in the lens is 63.1 x 111.4, thus the border of the lens is 0.5 mm
// at each side. With a lip of 1 the hole in the deck was 1 mm smaller than the
// active area, and the deck covered about 6 pixels at each of the four sides
// before the module moved at all.
LENS_LIP     = 0;        // the hole in the deck is the hole in the lens
// The deck must CLEAR the cover glass. The carrier carries the module and the
// deck only closes the top. The deck bears on the top face of the wall at
// CEIL, and its bottom face above the module is this much higher.
DECK_RELIEF  = 0.3;

// CAUTION: THE MODULE IS FULL WIDTH FOR 0.69 mm ONLY. The silhouette is 91.46
// wide for the first 0.69 mm below the front face of the lens, and then it is
// 72.96. Thus the perimeter of the module is bare cover glass: it stands
// 9.25 mm out of the body on one axis and 10.33 mm on the other, which the
// "9.25" of the rear view confirms. A shelf at the rear face of the body
// touches nothing at the perimeter. The four bracket tabs carry the module.
// See CARRIER_TOP.
//
// Each value below measures DOWN FROM THE FRONT FACE OF THE LENS, which lies
// on the bottom of the deck at CEIL. The written values of the page do not use
// one datum: 8.5 and 3 measure from the rear face of the body.
//
// THE LINES OF THE DRAWING ARE THE SOURCE HERE, AND NOT THE WRITTEN 14.92.
// The vector geometry of page 3 is at 1.6334 points for each millimetre, which
// 6 dimensions give to 0.1 mm. At that scale each written value agrees with
// the lines that it points to, except 14.92: its two arrow points are 15.94
// apart. The written value gives a body of 6.42 and the lines give 7.44, and
// 7.44 + 8.5 = 15.94. A body that is too thick leaves a clearance that the
// strip fills. A body that is too thin stops the deck from closing.
DISP_GLASS_T = 0.69;     // rear face of the cover glass. The side view: "0.7 lens".
DISP_BODY_T  = 7.44;     // rear face of the body
DISP_BOSS_T  = 8.5;      // a boss stands this far below the rear face of the body
DISP_BRKT_T  = 3;        // a bracket stands this far below the same face
// The rear body. The page gives no corner radius for it, thus ref_display()
// draws it with a sharp corner, which is the larger keep-out.
DISP_BODY_XY = [122.74, 72.96];

DISP_GLASS_Z = CEIL - DISP_GLASS_T;           // 48.31, rear face of the glass
DISP_UNDER_Z = CEIL - DISP_BODY_T;            // 41.56, rear face of the body
DISP_BRKT_Z  = DISP_UNDER_Z - DISP_BRKT_T;    // 38.56, bottom of a bracket
DISP_BOSS_Z  = DISP_UNDER_Z - DISP_BOSS_T;    // 33.06, bottom of a boss. This
                                              // is the lowest face of the
                                              // module.

// Plan positions, from the rear view of the same page. That view is portrait:
// the 91.46 axis is horizontal and the 143.4 axis is vertical. Here the 143.4
// axis is x and the 91.46 axis is y.
//
// The four screw bosses hold a Raspberry Pi, thus the pattern is the 58 x 49
// of the Pi. 58 is along the 143.4 axis and 49 is along the 91.46 axis.
DISP_BOSS_XY = [29, 24.5];       // 58/2, 49/2
DISP_BOSS_D  = 5;                // measured on the edge view of the drawing
// TO BE UNDERSTOOD: the 58 pair is NOT central on the 143.4 axis. The page
// puts it at -20.2 and +37.8 from the centre of the module. The values here
// keep it central, because the two ends of the module are not told apart. See
// section 4.2 of CLAUDE.md: the turn of the panel waits for the hardware.
//
// CAUTION: A PRINTED PART SEES THIS. The carrier cuts a clearance for each of
// these four bosses at the SAME pattern, thus a value that is 8.8 mm out puts
// a boss of the module on the material of the carrier. This item is open. Do
// not change DISP_BOSS_XY without a decision from the user.
//
// The four brackets are 103.7 apart along the 143.4 axis and 51 apart along
// the 91.46 axis. Each one is 17.05 x 8.6 in plan, but the long axis of the
// two brackets at one end of the module is at 90 degrees to the long axis of
// the other two. Which end gets which is the same open question. Thus the
// keep-out is the square that holds the two positions.
DISP_BRKT_XY = [51.85, 25.5];    // 103.7/2, 51/2
DISP_BRKT    = [17.05, 17.05];

// --- the M19 switch ---------------------------------------------------------
// M19 x 1 momentary switch, 22 mm bezel, 24.8 mm deep with the terminals. The
// drawing of the manufacturer gives these values. The appliance has three of
// these switches: two on the deck for the two persons, and one in the side
// wall at positive x for the electrical supply.
PTT_BEZEL_D  = 22;
PTT_BARREL_D = 19;
PTT_HOLE_D   = PTT_BARREL_D + 2 * FIT_SLIDE;   // 19.6, the M19 clearance
PTT_DEPTH    = 24.8;     // below the deck, with the screw terminals

// THE NUT IS THE PART THAT SETS THE POSITION OF A SWITCH, AND NOT THE BEZEL.
// The nut goes on the barrel against the far face of the panel, thus it stands
// SW_NUT_L into the enclosure and it is wider than the bezel. All three
// switches are the same part, thus one set of constants.
//
// CAUTION: ref_ptt() MUST KEEP ITS NUT. Without the nut a collision test gives
// "body ^ PTT switch EMPTY" for a switch with a nut that stands 2.5 mm in the
// side wall, 44 mm3 in the cover glass and 9 mm3 in a deck boss. A model of a
// part that does not show all of that part makes each test of the part that it
// does not show give EMPTY.
SW_NUT_AF    = 22;       // across the flats, from the same drawing
SW_NUT_AC    = 25;       // across the corners. ref-hardware.scad draws this.
SW_NUT_L     = 8;        // the nut, and the space to turn it
SW_NUT_CLR   = 0.5;      // the nut to any other part
SW_NUT_KO    = SW_NUT_AC / 2 + SW_NUT_CLR;   // 13, the keep-out radius

// --- the two push-to-talk switches, on the deck ------------------------------
// EACH VALUE BELOW IS DERIVED FROM THE PART THAT IS ADJACENT TO IT. A written
// pair puts the nut into three parts at the same time.
//
// The nut hangs below the deck, in the band z CEIL - SW_NUT_L to CEIL, and
// four things are in that band: the side wall, the cover glass of the display
// module, the tube of the well, and the two deck bosses.
//
// x: as far out as the side wall permits. It must be as far out as possible,
// because the tube of the well comes in at the front and the middle of the
// enclosure is the part of the band that the tube takes.
PTT_X        = W / 2 - WALL - SW_NUT_KO;
// y: the cover glass sets the rear limit and the tube of the well sets the
// front limit. The switch goes in the middle of the two.
//
// CAUTION: THE COVER GLASS IS THE FULL 143.4 x 91.46 AND IT IS ONLY 0.69 mm
// THICK. Below it the module is 122.74 x 72.96, thus a test against the body
// of the module alone gives a rear limit that is 10 mm too far back.
PTT_Y_GLASS  = DISP_Y + DISP_MOD[1] / 2 + SW_NUT_KO;
PTT_Y_WELL   = WELL_Y - sqrt(pow(WELL_RO + SW_NUT_KO, 2) - pow(PTT_X, 2));
PTT_Y        = (PTT_Y_GLASS + PTT_Y_WELL) / 2;
assert(PTT_Y_WELL > PTT_Y_GLASS,
       "no band between the cover glass and the tube of the well for the nut");
assert(PTT_X + SW_NUT_KO <= W / 2 - WALL,
       "the nut of a push-to-talk switch is in the side wall");
assert(PTT_Y - SW_NUT_KO >= DISP_Y + DISP_MOD[1] / 2,
       "the nut of a push-to-talk switch is under the cover glass");
assert(norm([PTT_X, PTT_Y - WELL_Y]) >= WELL_RO + SW_NUT_KO,
       "the nut of a push-to-talk switch is in the tube of the well");

// THE FRONT EDGE OF THE RECTANGULAR BODY FOLLOWS THE NUT. A written 8 puts the
// inner face of the front wall at y = 5. The corridor between the cover glass
// and that face is then 24.77 mm for a nut of 26, thus NO position on the deck
// is free of all four parts. The wall moves forward until it clears the nut,
// and the shape of the body follows it.
FRONT_EDGE_Y = PTT_Y + SW_NUT_KO + SW_NUT_CLR + WALL;
assert(PTT_Y + SW_NUT_KO <= FRONT_EDGE_Y - WALL,
       "the nut of a push-to-talk switch is in the front wall");

// --- boards -----------------------------------------------------------------
// The boards sit in the middle of the bay along y, as the display does.
//
// ALONG x THEY DO NOT. The charge inlet of the X1201 is on an 85 mm edge, at
// the end that carries the Raspberry Pi. With the boards in the middle of the
// enclosure that inlet is 18.16 mm inside the side wall, and no USB-C
// connector is that long. Thus the boards stand toward the wall at negative x,
// with the face of the connector CHARGE_RECESS from the inner face of that
// wall. The display stays in the middle. Nothing says that the display and the
// boards must agree.
BAY_REAR_Y   = REAR_Y + WALL;          // -113
BAY_FRONT_Y  = WELL_Y - WELL_RO;       // -18.5
BRD_Y        = (BAY_REAR_Y + BAY_FRONT_Y) / 2;   // -65.75
assert(BAY_FRONT_Y - BAY_REAR_Y >= DISP_MOD[1] + 2,
       "the bay is not deep enough for the display module");
// The two gaps at the ends of the module are NOT the same, because the module
// is 0.25 mm in front of the middle of the bay. An assert holds each one, thus
// no comment has to carry the number.
assert((DISP_Y - DISP_MOD[1] / 2) - BAY_REAR_Y > 1,
       "the display module touches the rear wall");
assert(BAY_FRONT_Y - (DISP_Y + DISP_MOD[1] / 2) > 1,
       "the display module touches the tube of the well");
UPS_PCB      = [106.6, 85, 1.6];    // Geekworm X1201, from its drawing
UPS_TOP_Z    = 9.6;      // top face of the X1201 PCB: floor 3, spacer 5, PCB 1.6
BOARD_CLEAR  = 1;        // printed part to the edge of the X1201

CHARGE_RECESS = 3;       // inner face of the side wall to the connector face
UPS_USB_OVER  = 1.538;   // the connector goes past the 106.6 edge. DXF X 108.138
BRD_X = -(W / 2 - WALL) + CHARGE_RECESS + UPS_PCB[0] / 2 + UPS_USB_OVER;  // -15.162

// The map from the frame of the Geekworm DXF to the model. The DXF gives
// X 0 to 106.6 and Y 0 to 85. The model is a mirror of that frame in x, and it
// is opposite in y. EACH POSITION OF A BOARD COMES THROUGH THESE TWO
// FUNCTIONS, thus one change to BRD_X moves all of them together and no
// position keeps a value that a person wrote by hand.
function brd_x(X) = BRD_X + (UPS_PCB[0] / 2 - X);
function brd_y(Y) = BRD_Y + (UPS_PCB[1] / 2 - Y);

// The four spacers of the X1201 are NOT symmetric on the board. The DXF puts
// them at X 3.05 and 102.7 and at Y 4.2 and 62.2. With one symmetric pair
// instead, two of the four are about 19 mm from the correct position and
// floor_vents keeps the floor clear at two positions that have no spacer.
UPS_SPACER_X = [brd_x(3.05), brd_x(102.7)];    //  35.088, -64.562
UPS_SPACER_Y = [brd_y(4.2),  brd_y(62.2)];     // -27.450, -85.450
UPS_SPACER_D = 5.6;      // the brass spacer. The part is not selected.
UPS_SPACER_H = 5;        // M2.5 x 5. The Geekworm installation page.
UPS_SCREW_CLR_D = 2.9;   // clearance in the floor for an M2.5 screw
UPS_CSK_D    = 5.0;      // countersink for the head of an M2.5 screw

// The charge inlet. It is a hole in the side wall at negative x. See
// SIDE_CHARGE.
UPS_USB_W    = 8.94;                 // DXF Y 47.33 to 56.27
UPS_USB_Y    = brd_y(51.80);         // -75.05, the centre of the inlet
UPS_USB_FACE_X = brd_x(108.138);     // -70.0, the face of the connector

// The Geekworm drawing says "Max 18.5mm". It is a maximum, thus it is the
// correct value for a keep-out.
CELL_D       = 18.5;     // 18650
// TO BE UNDERSTOOD: 65 IS NOT CONFIRMED. The Geekworm wiki gives a maximum
// cell length of 65.3 mm, and no person here has read that drawing.
CELL_L       = 65;
// The two cells, in their holders. The DXF gives X 20.3 and 39.4 for the
// silkscreen of the holders and for their pads.
CELL_X       = [brd_x(20.3), brd_x(39.4)];     // 17.838, -1.262

// CAUTION: THE PI IS AT NEGATIVE x. DO NOT MIRROR THIS.
// A person behind the appliance sees the rear wall. From that person's LEFT to
// the right the sockets are: ethernet, USB 3.0, USB 2.0. Positive x is that
// person's right, thus the Pi and its sockets are at negative x, and the cells
// are at positive x.
PI_PCB       = [56, 85, 1.4];       // Raspberry Pi 5
// The DXF gives the four bores for the Pi at X 53.7 and 102.7 and at Y 4.2 and
// 62.2, which is the 49 x 58 pattern of the Pi. It gives the edges of the Pi at
// X 50.2 to 106.2 and at Y 0.7 to 85.7. Thus the centre of the Pi is 24.9 from
// the centre of the X1201 along x, and the Pi goes 0.7 past the X1201 along y.
PI_X         = brd_x((53.7 + 102.7) / 2);      // -40.062
PI_Y         = brd_y((0.7 + 85.7) / 2);        // -66.45
// The long edge that the drawing measures the ethernet chain from. It is the
// edge at negative x, and it carries the USB-C and the two micro-HDMI.
PI_EDGE_X    = PI_X - PI_PCB[0] / 2;           // -68.062
PI_Z         = 18.3;     // centre of the PCB. M2.5 5+3 spacers over the X1201.
PI_SPACER_X  = brd_x(102.7);        // -64.562, the same bore as one X1201 foot
// The two 5+3 spacers stand in the bores of the Pi, thus they use the same two
// y values as the feet of the X1201. The DXF gives Y 4.2 and 62.2 for both.
// CAUTION: DO NOT MIRROR THIS VALUE. The mirror puts the 3.5 mm inset at the
// edge with the sockets, where the Pi has no bore.
PI_SPACER_Y  = UPS_SPACER_Y;
PI_COOLER    = [40, 40, 12.5];      // Active Cooler

// --- the connectors on the long edge of the Pi -------------------------------
// The USB-C, the two micro-HDMI and the two 22-pin FPC connectors. They are on
// the 85 mm edge at negative x, which is the edge that the ethernet chain
// measures from.
//
// CAUTION: THE CHAIN 11.2 / 25.8 / 39.2 MEASURES FROM THE SHORT EDGE THAT IS
// OPPOSITE THE SOCKETS, WHICH IS THE FRONT OF THE APPLIANCE. Measured from the
// REAR instead, each connector on this edge is 62.6 mm from its correct
// position, and the rib in front of the USB-C of the Pi stands in front of
// nothing.
//
// The vector geometry of RP-008347-DS-1 is at 2.83465 points for each
// millimetre, which is 1:1 on A4. The extension line of the "39.2" dimension
// and the left extension line of the "85" dimension are the same line,
// x = 133.57 points, and that line is the short edge at the end away from the
// sockets. It gives 39.23 for the second micro-HDMI against the written 39.2.
// A measurement of the board with a rule gives 11.5 / 25.4 / 37.7.
PI_FRONT_Y   = PI_Y + PI_PCB[1] / 2;      // -23.95, the edge without sockets
PI_USBC_X    = PI_X - 26.5;               // across the board. No source.
PI_USBC_Y    = PI_FRONT_Y - 11.2;
PI_USBC      = [7.5, 9, 3.5];
PI_HDMI_X    = PI_X - 25.5;               // across the board. No source.
PI_HDMI_Y    = [PI_FRONT_Y - 25.8, PI_FRONT_Y - 39.2];
PI_HDMI      = [6, 8, 3];
// The two 22-pin FPC connectors, which are the display and the camera. The
// same drawing gives 47.28 to 50.24 and 53.49 to 56.41 from the front edge,
// and 0.71 to 16.19 from the edge with the sockets.
PI_FPC_X     = PI_EDGE_X + (0.71 + 16.19) / 2;
PI_FPC_Y     = [PI_FRONT_Y - (47.28 + 50.24) / 2, PI_FRONT_Y - (53.49 + 56.41) / 2];
PI_FPC       = [16.19 - 0.71, 50.24 - 47.28, 3];

// --- rear ports -------------------------------------------------------------
// The rear is the only face with a cable. Each value is [x, z, width, height].
// The X1201 USB-C is the only charge inlet: Geekworm blocks the USB-C of the
// Pi, and a printed rib keeps a person from using it. See PI_RIB below.
// RP-008347-DS-1 gives the chain from one long edge of the board: ethernet
// 10.2, USB 3.0 29.1, USB 2.0 47, board 56. The drawing measures from the
// outer long edge, which is at negative x. PI_EDGE_X is with the Pi, above,
// thus each value below is added to it.
//
// A chain of 31.5 and 49.5 is incorrect: a 13.5 mm connector at 49.5 ends at
// 56.25, which is off the board. Section 12.4 of CLAUDE.md says the datasheet
// is the authority.
// CAUTION: EACH USB SOCKET MUST HAVE A HOLE THROUGH THE WALL. No connector can
// go into the enclosure: the inner face of the rear wall is TUNNEL_L behind the
// face of a socket, which is about 1 mm, and a USB-A connector is about 12 mm
// long. The three sockets are for a keyboard, for the speakerphone, and for a
// socket that stays free.
//
// The rule for each hole that a connector goes through:
//
//     hole = max(receptacle, body of the connector) + 2 * PLUG_BODY_CLEAR
//
// The measured connectors. A connector that no person has measured keeps its
// receptacle as the source, and the assert below says so.
USBA_PLUG    = [16, 6];        // the USB-A of the speakerphone, measured
RJ45_PLUG    = [13, 11];       // with the latch, measured
USB_RECEPT   = [13.5, 15.6];   // one USB stack of the Pi 5
PI_USB3_X    = PI_EDGE_X + 29.1;
PI_USB2_X    = PI_EDGE_X + 47;

// THE TWO USB STACKS GET ONE HOLE. This is not a selection about how the rear
// looks: at a pitch of 17.9 mm two holes of 16 + 2 * 0.5 keep 1.9 mm of wall,
// and with the clearance of a receptacle they overlap each other. A wall of
// 1 mm between two openings that a person pushes a connector into is the part
// that breaks first. The ethernet hole keeps its own opening.
PORT_RJ45    = [PI_EDGE_X + 10.2, 25.8, 16.0 + 2 * PLUG_CLEAR, 13.5 + 2 * PLUG_CLEAR];
PORT_USB     = [(PI_USB3_X + PI_USB2_X) / 2, 26.8,
                (PI_USB2_X - PI_USB3_X)
                    + max(USB_RECEPT[0], USBA_PLUG[0]) + 2 * PLUG_BODY_CLEAR,
                max(USB_RECEPT[1], USBA_PLUG[1]) + 2 * PLUG_BODY_CLEAR];
PORT_R       = 1.5;      // corner radius of a port opening

// The ethernet connector is SMALLER than its own receptacle, thus the
// receptacle sets that hole and the connector does not. This assert is the
// test that the USB holes did not have.
assert(PORT_RJ45[2] >= RJ45_PLUG[0] + 2 * PLUG_BODY_CLEAR &&
       PORT_RJ45[3] >= RJ45_PLUG[1] + 2 * PLUG_BODY_CLEAR,
       "the ethernet hole does not clear the body of its connector");
// ONE HOLE FOR TWO SOCKETS NEEDS A TEST ON ONE SOCKET, AND NOT ON THE HOLE.
// A test of PORT_USB[2] >= USBA_PLUG[0] + 2 * PLUG_BODY_CLEAR is 34.9 >= 17.
// That is the expression that makes PORT_USB[2], with the pitch of 17.9 mm
// added to it, tested against itself without the pitch. It is correct for each
// value of PORT_USB, thus it can find no error.
//
// A person puts a connector in ONE socket. The share of the hole that this
// connector gets is the distance from the centre of the OUTER socket to the
// near edge of the hole; the connector in the other socket has the same share
// at the other end. The test below uses the socket positions and the hole, and
// not the expression that made the hole. Thus a PORT_USB that a person writes
// by hand makes it give an error.
USB_SHARE    = min(PI_USB3_X - (PORT_USB[0] - PORT_USB[2] / 2),
                   (PORT_USB[0] + PORT_USB[2] / 2) - PI_USB2_X);      // 8.5
assert(USB_SHARE >= USBA_PLUG[0] / 2 + PLUG_BODY_CLEAR,
       "an outer USB socket does not clear the body of a USB-A connector");
assert(USB_SHARE >= USB_RECEPT[0] / 2 + PLUG_CLEAR,
       "an outer USB socket does not clear its own receptacle");
assert(PORT_USB[3] >= USBA_PLUG[1] + 2 * PLUG_BODY_CLEAR,
       "the USB hole is not high enough for the body of a USB-A connector");
// The wall between the ethernet hole and the USB hole. It is the material that
// is nearest to a hole in the rear wall, and a person loads it each time.
RJ45_TO_USB  = (PORT_USB[0] - PORT_USB[2] / 2)
               - (PORT_RJ45[0] + PORT_RJ45[2] / 2);      // 1.4
assert(RJ45_TO_USB > 1, "the USB hole breaks into the ethernet hole");
TUNNEL_T     = 2;        // wall of a printed port tunnel
// The tunnel of the USB sockets stops below the display module. The top of its
// wall is at 37.1 and the rear face of the module is at 41.56, thus the cut
// removes nothing. Keep it: it follows the module, thus a module that comes
// down again cannot get a tunnel through it in silence. With the rear face at
// 37.5 the tunnel stood 0.99 mm3 in the module.
TUNNEL_TOP   = DISP_UNDER_Z - 0.3;
// The tunnel closes the space between the rear wall and the face of the
// socket, thus a plug cannot go into the enclosure adjacent to the socket. The
// length is DERIVED from the board position: a written 18 mm puts the tunnel
// through the PCB of the Pi. The side view of RP-008347-DS-1 gives 3 mm from
// the edge of the board to the face of the ethernet socket.
PI_PORT_FACE_Y = PI_Y - (PI_PCB[1] / 2 + 3);   // outer face of the rear sockets
TUNNEL_L     = PI_PORT_FACE_Y - (REAR_Y + WALL);

// The ethernet hole is the hole that is nearest to a corner. See CORNER_R_REAR.
assert(abs(PORT_RJ45[0]) + PORT_RJ45[2] / 2 + TUNNEL_T <= W / 2 - CORNER_R_REAR,
       "the ethernet tunnel is not in the flat part of the rear wall");

// --- the path of the cable of the speakerphone -------------------------------
// A socket that is near the wall and a connector inside the enclosure cannot
// be there together. Thus the captive cable goes OUT of the well through
// CABLE_SLOT in the floor of the well, along the bay, and OUT of the enclosure
// through CABLE_EXIT in the rear wall. A person then makes a loop outside and
// puts the connector in the USB 2.0 socket from outside.
//
// CAUTION: THE TWO OPENINGS MUST PASS THE USB-A CONNECTOR, NOT ONLY THE CABLE.
// The cable is captive at the speakerphone, thus the free end is the end that
// a person threads, and it carries the connector. A slot of 16 x 8 for a
// connector of 16 x 6 passes the bare cable and not the connector: it is short
// by 3.69 mm3, or by 95.63 mm3 turned 90 degrees. THE TWO OPENINGS COME FROM
// ONE SOURCE. A change to USBA_PLUG moves the two.
//
// The two openings are 20 x 10, which is 2 mm around a connector of 16 x 6.
PLUG_PASS      = 2;      // around the body of a connector that a person threads
CABLE_PASS     = [USBA_PLUG[0] + 2 * PLUG_PASS, USBA_PLUG[1] + 2 * PLUG_PASS];

// In the rear wall. It is 20 along x and 10 along z, because a USB-A connector
// is wider than it is high.
CABLE_EXIT_X   = PI_USB2_X;      // -21.062, below the USB 2.0 pair
CABLE_EXIT_Z   = 9.5;            // centre
CABLE_EXIT     = CABLE_PASS;
CABLE_EXIT_R   = 5;              // fully round ends
// The chamfer is not decoration. The cable bears on the edge of the slot, and
// the edge of a printed layer cuts an insulator.
CABLE_EXIT_CHM = 1;              // chamfer on the two faces of the wall

// In the floor of the well, at a radius of 54 mm. The third rubber pad of the
// speakerphone is at a radius of 45 mm on the same radial line, thus a slot at
// 47 mm puts the puck on two pads and one hole. At 54 mm the slot is clear of
// the pad and it is still below the cable, which comes out of the groove at
// the base of the puck at a radius of 47 to 55 mm.
CABLE_SLOT     = CABLE_PASS;
CABLE_SLOT_R   = 3;
CABLE_SLOT_Y   = WELL_Y - 54;

// A TEST OF THE WIDTH AND THE HEIGHT IS NOT SUFFICIENT. The corner of the body
// of the connector must be in the circular corner of the opening. At 16 x 8
// with a radius of 3 the two ends of the slot are fully round, thus a corner
// of a 16 x 6 body stands outside the arc although the slot is as wide as the
// connector and 2 mm higher.
function corner_ok(size, r, body) =
    let (c = [size[0] / 2 - r, size[1] / 2 - r], p = [body[0] / 2, body[1] / 2])
    norm([max(0, p[0] - c[0]), max(0, p[1] - c[1])]) <= r;
assert(corner_ok(CABLE_SLOT, CABLE_SLOT_R, USBA_PLUG),
       "the slot in the floor of the well cannot pass a USB-A connector");
assert(corner_ok(CABLE_EXIT, CABLE_EXIT_R, USBA_PLUG),
       "the slot in the rear wall cannot pass a USB-A connector");

// The slot goes to z 15.5 and the bottom of the USB tunnel is at 16.5.
assert(CABLE_EXIT_Z + CABLE_EXIT[1] / 2 + CABLE_EXIT_CHM
       <= PORT_USB[1] - PORT_USB[3] / 2 - TUNNEL_T,
       "the slot for the cable breaks into the USB tunnel");
assert(abs(CABLE_EXIT_X) + CABLE_EXIT[0] / 2 + CABLE_EXIT_CHM
       <= W / 2 - CORNER_R_REAR,
       "the slot for the cable is not in the flat part of the rear wall");
// The slot in the floor of the well must be on the ring and not over its open
// middle, and it must not break into the wall of the bore.
assert(norm([CABLE_SLOT[0] / 2, WELL_Y - CABLE_SLOT_Y + CABLE_SLOT[1] / 2])
       <= WELL_RI &&
       norm([CABLE_SLOT[0] / 2, WELL_Y - CABLE_SLOT_Y - CABLE_SLOT[1] / 2])
       >= LEDGE_IN_R,
       "the slot for the cable is not on the ring of the floor of the well");

// --- the charge inlet, in the side wall at negative x ------------------------
// The inlet is on an 85 mm edge of the X1201, thus a hole in the rear wall
// cannot be in front of it. The value is [y, z, width along y, height].
//
// The 9 comes from a measurement: the body of the connector of the electrical
// supply is 6 mm thick, and the hole gives 1.5 mm at each side.
//
// TO BE UNDERSTOOD: THE 16 IS AN ESTIMATE. No person has measured the
// width of that same moulded body. A body 6 mm thick is usually 10 to 12 wide,
// which asks for a hole of 13 to 15, thus 16 is large and it is safe. Make
// it smaller only with a measurement. The face of the socket is 6 mm inside
// the outer face of the wall: 3 mm of wall and 3 mm of CHARGE_RECESS. If the
// hole is too small a person cannot charge the appliance, thus this is a hard
// failure and not an appearance.
PORT_CHARGE_SIZE = [16, 9];
assert(PORT_CHARGE_SIZE[0] >= UPS_USB_W + 2 * PLUG_CLEAR &&
       PORT_CHARGE_SIZE[1] >= USBC_RECEPT_H + 2 * PLUG_CLEAR,
       "the charge hole is smaller than the receptacle with its clearance");
SIDE_CHARGE  = [UPS_USB_Y,
                UPS_TOP_Z + USBC_RECEPT_H / 2,
                PORT_CHARGE_SIZE[0],
                PORT_CHARGE_SIZE[1]];
// The tunnel closes the space between the wall and the face of the connector,
// as the tunnels of the rear wall do. Thus a plug cannot go into the enclosure
// beside the connector.
CHARGE_TUNNEL_L = CHARGE_RECESS;

// --- the switch for the electrical supply ------------------------------------
// The same M19 x 1 switch as the two push-to-talk switches: bezel 22, thread
// M19 x 1, barrel 19, length 24.8, and 25 across the corners of the hex nut.
// It drives the PSW header of the X1201, which is a momentary input for the
// electrical supply. The Geekworm drawing says to push the button two times,
// one immediately after the other, to stop the UPS.
//
// IT GOES IN THE SIDE WALL AT POSITIVE x. That wall is the only one with the
// 24.8 mm of clear depth that the switch needs. The rear wall has 5 mm to the
// edge of the X1201 and the wall at negative x has 4.94 mm to the edge of the
// Pi. This wall has 34.86 mm, and the PSW header is on the edge of the board
// that is adjacent to it.
PWR_SW_Y     = BRD_Y + UPS_PCB[1] / 2 - 17.3;   // -40.55, opposite the PSW header
// THE NUT IS THE PART THAT SETS THIS VALUE, AND NOT THE BEZEL. The nut is
// SW_NUT_AC across against 22 for the bezel, thus it stands 2 mm higher on each
// side. At 24 the nut goes to z 37 and the carrier is at 35.56, thus the nut
// stands 1.44 mm in the carrier. At 22 the nut goes to 35 and the bezel to 33.
PWR_SW_Z     = 22;       // centre. The floor is at 3 and the nut comes to 9.
assert(W / 2 - WALL - PTT_DEPTH > BRD_X + UPS_PCB[0] / 2 + BOARD_CLEAR,
       "the switch of the electrical supply goes into the X1201");
assert(PWR_SW_Z - SW_NUT_KO > FLOOR_T,
       "the nut of the switch of the electrical supply is in the floor");

PI_RIB_CLEAR = 1;        // rib to the nearest part that we buy
// The rib that blocks the USB-C of the Pi. It stands between the side wall and
// that socket, thus it must clear two parts: the PCB of the X1201 and the body
// of the socket. The keep-out comes from the two parts. A part that moves then
// moves the rib, and it cannot go through the rib in silence.
PI_RIB_X     = min(BRD_X - UPS_PCB[0] / 2,
                   PI_USBC_X - PI_USBC[0] / 2) - PI_RIB_CLEAR;
PI_RIB_Y     = PI_USBC_Y;
PI_RIB_T     = 3;
assert(PI_RIB_X > -(W / 2 - WALL),
       "no space for the rib in front of the USB-C socket of the Pi");

// --- fasteners and bosses ---------------------------------------------------
BOSS_R_MID    = 6.5;
SCREW_BORE_R  = 1.5;     // pilot bore. A heat-set insert needs a larger bore.
SCREW_CLEAR_R = 1.6;     // clearance in the floor
// [x, y, radius, height, takes a screw]
//
// CAUTION: NOTHING IN THE BAY REACHES THE DECK, AND THAT IS DELIBERATE. The
// display module is 143.4 x 91.46 in a space of 146 x 95. It covers the full
// bay from z = 41.56 up, and its brackets and its screw bosses come down to
// 38.56 and to 33.06. A pillar that goes to the deck in that footprint goes
// through the module.
//
// A boss that stops at the rear face of the BODY of the module touches
// nothing, because the perimeter of the module is bare cover glass. The
// carrier carries the module. The one boss here is in the basement below the
// well, where the module is not. It holds the ring of the well floor between
// two ribs.
//
// CAUTION: THE POSITION IS DERIVED. A WRITTEN VALUE PUTS THE BOSS ON THE WALL
// OF THE BORE. At y = WELL_Y + 55.5 the outer edge of the boss is at a radius
// of 62.0, which is the bore: the lap is a clearance of 0.0094 mm, and a
// printer makes a joint from it, or an open line, and no line of the model
// says which. The value below keeps 1.5 mm of air, and it follows the bore.
//
// The deck screws are in DECK_BOSSES, in front of the module.
BOSS_MID_CLEAR = 1.5;    // boss to the wall of the bore
BOSS_MID_R     = WELL_RI - BOSS_R_MID - BOSS_MID_CLEAR;   // 54.0
BOSSES = [
    [  0, WELL_Y + BOSS_MID_R, BOSS_R_MID, DISC_BOT - FLOOR_T, false]
];

// The deck attaches with a screw from above into a heat-set insert. The module
// carries the deck over the bay; these two bosses hold it down at the front.
//
// EACH POSITION IS DERIVED. A pair that comes from the SWITCH BARREL, which is
// 19.6 mm, puts the nut of the switch 9.30 mm3 in the boss, because the nut is
// 26. A boss is a full nut away from the switch, and the asserts below hold
// the three clearances that it needs.
DECK_BOSS_R    = 5;
DECK_BOSS_CLR  = 1;      // boss to a part that is next to it
// y: as far back as the cover glass of the display module permits.
DECK_BOSS_Y    = DISP_Y + DISP_MOD[1] / 2 + DECK_BOSS_R + DECK_BOSS_CLR;
// x: a nut and a boss in from the switch. The switch is at the side wall, thus
// the boss goes inboard of it and not outboard.
DECK_BOSS_X    = PTT_X - (SW_NUT_KO + DECK_BOSS_R + DECK_BOSS_CLR);
DECK_BOSSES    = [[-DECK_BOSS_X, DECK_BOSS_Y], [DECK_BOSS_X, DECK_BOSS_Y]];
assert(norm([DECK_BOSS_X - PTT_X, DECK_BOSS_Y - PTT_Y])
       >= SW_NUT_KO + DECK_BOSS_R,
       "a deck boss is in the nut of a push-to-talk switch");
assert(norm([DECK_BOSS_X, DECK_BOSS_Y - WELL_Y]) >= WELL_RI + DECK_BOSS_R,
       "a deck boss is in the bore of the well");
assert(DECK_BOSS_Y - DECK_BOSS_R >= DISP_Y + DISP_MOD[1] / 2,
       "a deck boss is under the cover glass of the display module");
assert(DECK_BOSS_Y + DECK_BOSS_R <= FRONT_EDGE_Y - WALL,
       "a deck boss is in the front wall");
assert(abs(DECK_BOSS_X) + DECK_BOSS_R <= W / 2 - WALL,
       "a deck boss is in the side wall");
INSERT_BORE_D = 4.2;     // M3 heat-set insert
INSERT_DEPTH  = 6;
DECK_SCREW_D  = 3.4;     // M3 clearance in the deck
DECK_CSK_D    = 6.4;     // countersunk head, flush with the top of the deck

// --- perforations -----------------------------------------------------------
// The side grid is the exhaust path of the cooler. The floor grid is the one
// intake. No part of the X1201 stands out of its bottom face, thus the grid
// goes over the full area below the boards.
SIDE_HOLE_R  = 3.4;
SIDE_PITCH   = [11, 11.5];
SIDE_CENTRE  = [BRD_Y, 26];  // [y, z] of the middle hole
SIDE_NX      = 4;            // -SIDE_NX to +SIDE_NX along y
SIDE_NZ      = 1;

FLOOR_HOLE_R = 3.5;
FLOOR_PITCH  = 13;
// Vent below the basement of the well. The speakerphone sends its sound UP
// through the grille on its top face, thus these holes are for air and for a
// spilt liquid, and not for the sound.
FLOOR_VENT_R = 54.5;

// --- the carrier frame ------------------------------------------------------
// THE THIRD PRINTED PART. The module has two patterns of holes on its rear
// face and they are not the same thing: four circular bosses at 58 x 49, which
// are the pattern of a Raspberry Pi, and four bracket tabs at 103.7 x 51,
// which are for an enclosure.
//
// The frame goes behind the module and takes ALL FOUR bracket tabs. It is
// possible because at the level of the tabs the module is NOT 143.4 wide: from
// 41.56 down the module is its body of 122.74 x 72.96, and below 38.56 there
// is nothing but the tabs and the four circular bosses. Thus the frame has the
// full bay at CARRIER_TOP.
//
// The frame replaces a rebate at the top of the wall. A rebate carries the
// module on its cover glass, which is 0.69 mm thick.
CARRIER_T     = 3;
CARRIER_TOP   = DISP_BRKT_Z;              // 38.56, the bottom face of a tab
CARRIER_BOT   = CARRIER_TOP - CARRIER_T;  // 35.56
CARRIER_CLEAR = FIT_SLIDE;                // to the inner face of a side wall
CARRIER_X     = W / 2 - WALL - CARRIER_CLEAR;         // 72.7
CARRIER_EDGE  = 6;       // material past the centre of a screw
CARRIER_Y     = [DISP_Y - DISP_BRKT_XY[1] - CARRIER_EDGE,
                 DISP_Y + DISP_BRKT_XY[1] + CARRIER_EDGE];    // -97, -34
CARRIER_R     = 3;
// The opening in the middle passes the air and the FPC cable, and it takes out
// material that does nothing.
CARRIER_WIN   = [90, 40];
CARRIER_SCREW_D    = UPS_SCREW_CLR_D;   // 2.9, M2.5 clearance
// TO BE UNDERSTOOD: THE SCREW INTO A TAB IS NOT KNOWN. RP-010430-MM-1 gives no
// diameter for the hole in a bracket tab. The position of each tab is known;
// the screw is not.
CARRIER_BOSS_CLR_D = 8;  // clearance for a circular boss of the module

// How the frame attaches: a boss on each side wall at each of the four
// positions, with a heat-set insert, and a screw from above.
//
// CAUTION: THE LEDGE IS THE TOP FACE OF THESE FOUR BOSSES AND IT IS NOT A
// CONTINUOUS SHELF. A shelf at CARRIER_BOT along the two walls stops the frame
// from the bay: the frame is as wide as the bay, thus a shelf that stands in
// from the wall stops it. Four bosses stop nothing above their top face, thus
// the frame goes straight down on to them.
CARRIER_LEDGE_IN = 6;    // how far a boss goes in from the inner face of a wall
CARRIER_LEDGE_H  = 10;   // 45 degrees below it, thus it prints with no support
CARRIER_LEDGE_L  = 12;   // along y
CARRIER_SCREW_X  = W / 2 - WALL - CARRIER_LEDGE_IN / 2;    // 70
// EACH EAR SITS ON A COLUMN OF THE SIDE GRID. A boss is 12 long and the
// columns are 11 apart, thus an ear between two columns blocks two of them and
// an ear on a column blocks one. The ears are 2 columns and 0 columns from the
// middle: the front pair must stay behind the nut of the switch of the
// electrical supply, which comes back to y -53.55.
CARRIER_SCREW_Y  = [SIDE_CENTRE[0] - 2 * SIDE_PITCH[0], SIDE_CENTRE[0]];

// The three clearances below the frame. Each one is a measured result and not
// a value that a person selected.
PI_USB_TOP_Z    = PI_Z + PI_PCB[2] / 2 + USB_RECEPT[1];    // 34.6
CARRIER_USB_GAP = CARRIER_BOT - PI_USB_TOP_Z;              // 0.96
// 0.96 mm ABOVE THE USB SOCKETS IS THE TIGHTEST CLEARANCE IN THE ENCLOSURE. It
// becomes 3.96 mm if the Pi is 5 mm above the X1201 and not 8, and section 4.4
// of CLAUDE.md records that this height is not measured. Thus a change of
// PI_Z, of the height of a socket, or of the thickness of the frame comes out
// here first.
assert(CARRIER_USB_GAP > 0.5,
       "the carrier stands on the USB sockets of the Pi");
// The nut of the switch of the electrical supply is SW_NUT_AC across and it is
// on the same wall as the two ears at positive x.
assert(PWR_SW_Z + SW_NUT_KO < CARRIER_BOT,
       "the nut of the switch of the electrical supply goes into the carrier");
// A tunnel of a socket goes to z 37.1, which is in the thickness of the frame.
// The tunnels are at y REAR_Y + WALL to PI_PORT_FACE_Y.
assert(CARRIER_Y[0] > PI_PORT_FACE_Y + 1,
       "the carrier is above a tunnel of a socket");
// The frame must not lap the body of the module: it comes to the tabs, and
// the body is 3 mm above them.
assert(CARRIER_X < DISP_MOD[0] / 2 + 2,
       "the carrier is wider than the bay");

// --- feet -------------------------------------------------------------------
FEET = [[-58, BRD_Y - 34.5, 10], [58, BRD_Y - 34.5, 10],
        [-36, WELL_Y + 38, 9], [36, WELL_Y + 38, 9]];

// --- finish -----------------------------------------------------------------
// The colour of the filament. It changes no dimension.
FILAMENT   = "#ffd100";
REF_COLOUR = "#2a2b2d";   // the parts that we buy, in the assembly view
