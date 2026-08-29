#!/bin/sh
# Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
# SPDX-License-Identifier: Apache-2.0
#
# ---------------------------------------------------------------------------
# SAFETY CONTROL. Every comment in this file is deliberately plain English,
# not the Simplified Technical English the rest of this repository uses. This
# script decides when to turn a machine off. A vague comment on a control like
# that gets deleted by the next person who tidies up.
# ---------------------------------------------------------------------------
#
# NEW FUNCTION. Upstream has no UPS, no cells, and nothing that turns the
# machine off.
#
# The low battery guard of the appliance.
#
# It reads the cell voltage of the X1201 UPS. When the voltage stays low it
# powers the machine down while the cells can still supply it. Without this the
# cells run flat, the Raspberry Pi loses power in an instant, and the SD card
# can be corrupted in the middle of a write.
#
# This replaces the shutdown in Geekworm's x120x scripts. Those read I2C
# address 0x36 directly and the kernel driver owns that address now. See
# section 8.9 of deploy/README.md.
#
# Two failures matter equally here, and they pull in opposite directions:
# powering off a healthy appliance standing in a public place is as much a
# failure as letting a dying one run until the cells cut out. Nearly every
# rule below exists because of one or the other.

set -u

VOLTAGE_FILE="/sys/class/power_supply/battery/voltage_now"
MAINS_FILE="/sys/class/power_supply/mains/online"

# Every value is in microvolts, because that is the unit of voltage_now. An
# integer is the only numeric type POSIX sh has.
#
# 3.20 V is the figure Geekworm uses in its own script. The 50 mV band between
# LOW_UV and RESET_UV is hysteresis.
#
# FLOOR and CEILING are plausibility limits, and they must be a pair. A cell
# below 2 V would have tripped the pack's protection board and the Pi could not
# be running to take the reading. A cell above 4.4 V does not exist on this
# charger. A single flipped bit in the gauge register produces both, and the
# high one is the more dangerous: without a ceiling, one absurd reading looks
# like a healthy pack and resets the protection state.
FLOOR_UV=2000000
CEILING_UV=4400000
LOW_UV=3200000

# The largest fall we will believe in one interval. A 2P 18650 pack under this
# appliance's load sags by tens of millivolts, not by hundreds. A larger fall
# from a healthy reading is a corrupt read, so it is held once and then
# believed if the next reading agrees. A real collapse costs one interval.
MAX_FALL_UV=300000

INTERVAL_S=5
LOW_READS=12
EMERGENCY_READS=3

# How long the guard will hold its counts because the mains is present.
#
# A pack that has been run flat sits below the low threshold. Plug the USB-C
# in, press the button, and without this the guard powers the machine off 60 s
# into a charge that was working. So while the mains is present the counts
# hold and the charger is given time.
#
# The hold is BOUNDED, and that bound is the safety half of this rule. A mains
# line that is stuck on - a fault, or a charger too weak to lift the pack -
# must not disable the guard for ever. After this many held reads the guard
# counts again whatever the mains says.
#
# The hold is decided per read, never from "the mains was present recently".
# The mains line chatters about once a second when the supply cannot deliver
# enough current, so a rule that latched on any recent high reading would
# never fire while the pack drained. Holding per read merely slows the count
# in that case; it does not stop it.
#
# CAUTION: the movement test below OVERRIDES this bound. A gauge never seen to
# fall holds for ever. The cost is taken knowingly: a charger holding a
# degraded pack, and a pack with a tripped protection board, both read low and
# flat and get no shutdown. Neither is being discharged, so no cell is run
# flat; what is lost is the clean stop before the mains goes. See section 9.1
# of deploy/README.md.
MAINS_HOLD_READS=120

# The movement test, and the reason a level on its own is not enough.
#
# A deep discharge is a FALLING voltage. That is what the words mean. A
# reading that sits still is not a discharge whatever its level, and powering
# a machine off because of one protects nothing at all.
#
# This is not a theoretical case. The gauge answers with an empty holder. On
# an X1201 with no cells and the mains present the MAX17040 keeps its I2C
# address, reports present=1, and puts the voltage at about 3176000
# microvolts with a capacity of 0 - below LOW_UV, and the perfect likeness of
# a pack that is about to die. It moved 3750 microvolts, three steps of the
# ADC, in fifty minutes. A pack that is really being discharged cannot do
# that. It falls, and under this appliance's bursty load it also sags and
# recovers by tens of millivolts within seconds.
#
# Only a FALL counts. An X1201 with an empty holder still answers, and gives
# 3176000 microvolts that drifted 3750 in fifty minutes - below LOW_UV, and
# UPWARD. A test on the size of the change would re-arm on that drift about
# every two hours, for ever. See section 9.1 of deploy/README.md.
#
# MOVEMENT_UV is above the noise of three gauge steps and BELOW the tens of
# millivolts that line 53 gives for a sag under load. That is deliberate: with
# a fall-only test a sag arms the guard, which is the safe direction.
MOVEMENT_UV=10000
MOVEMENT_WINDOW_READS=60

# How often the guard says it is alive and what it reads. Silence must not be
# the signature of both a healthy appliance and a guard that went blind.
HEARTBEAT_READS=60

# How often a held condition repeats itself in the journal. A condition that
# disarms the guard must not be announced once at boot and then be silent
# forever, because silence is also what a healthy appliance produces.
REPEAT_READS=60

# A bench test cannot flatten the cells and cannot write to sysfs. So a person
# can raise the low threshold above a full cell and watch the guard work.
#
# The emergency threshold is derived, never fixed, so that this one knob moves
# both tiers together and keeps them in order. Fixing it would let an operator
# who lowers the low threshold end up with an emergency threshold ABOVE it,
# which turns the 12-read rule into a 2-read rule without saying so.
OVERRIDE="${GEMMA_GUARD_LOW_UV:-}"

log() {
    printf '%s\n' "$*"
}

# systemd reads the <N> prefix from stdout and gives the line that priority, so
# journalctl -p err finds the lines that matter without reading the rest.
alert() {
    printf '<3>%s\n' "$*"
}

# ---------------------------------------------------------------------------
# Give back the voltage in microvolts, or fail.
#
# This is the most important function in this file. In shell, a `cat` of a file
# that cannot be read produces an EMPTY STRING, and an empty string compared as
# an integer is treated as 0 by some shells and is a fatal error in others.
# Zero is below every threshold here. So the careless version of this function
# turns a run of I2C read errors into a poweroff of a perfectly healthy
# appliance standing in a public place.
#
# Every failure path must return non-zero and print nothing. The caller then
# HOLDS its counters: it does not advance them and it does not reset them. A
# reading we do not have is not evidence of a low battery, and it is not
# evidence of a healthy one either.
# ---------------------------------------------------------------------------
read_microvolts() {
    if [ ! -r "$VOLTAGE_FILE" ]; then
        return 1
    fi

    value="$(cat "$VOLTAGE_FILE" 2>/dev/null)" || return 1

    # Reject the empty string, anything that is not all digits, and anything
    # longer than seven digits. A cell voltage in microvolts never needs more
    # than seven, and a longer number makes `test` fail with "integer expected"
    # on every comparison, which sends the reading down whichever branch
    # happens to be last.
    case "$value" in
        '' | *[!0-9]* | ????????*) return 1 ;;
    esac

    printf '%s' "$value"
}

# True when the USB-C of the X1201 is supplying the machine.
#
# This fails closed, the same way read_microvolts does: a file we cannot read
# means "assume no mains", so the guard keeps protecting instead of holding.
on_mains() {
    [ -r "$MAINS_FILE" ] || return 1
    [ "$(cat "$MAINS_FILE" 2>/dev/null)" = "1" ]
}

power_off() {
    alert "SHUTDOWN: $1 The voltage is $2 microvolts."

    # A bare `sync` blocks until every filesystem is flushed, and it cannot be
    # interrupted. The device most likely to be wedged is the failing SD card,
    # which is the exact event this guard exists to survive. Without the
    # timeout the guard can sit in uninterruptible sleep and never reach the
    # poweroff below, and the appliance drains to a hard cut instead.
    timeout 10 sync || alert "sync did not finish in 10 s. The guard goes on to the poweroff."

    poweroff_tries=$((poweroff_tries + 1))

    if systemctl poweroff; then
        return 0
    fi

    alert "systemctl poweroff did not work. This is attempt ${poweroff_tries}."

    # systemctl talks to systemd over D-Bus, and a wedged manager is correlated
    # with the collapsing supply we are watching, not independent of it. Two
    # --force flags skip D-Bus and call reboot(RB_POWER_OFF) directly. That
    # loses the session, which is acceptable. Losing the SD card is not.
    if [ "$poweroff_tries" -ge 3 ]; then
        alert "The guard powers off without systemd."
        systemctl --force --force poweroff
    fi

    return 1
}

# --- the thresholds, and the checks that keep them in order ----------------

if [ -n "$OVERRIDE" ]; then
    # Validate before the value can reach $(( )). An arithmetic expansion looks
    # a bare name up recursively, so a non-numeric override makes `set -u` kill
    # the guard before its loop begins, and Restart=always then turns that into
    # a crash loop that never reaches systemd's failed state. A leading zero is
    # read as octal by $(( )) and as decimal by `test`, which silently moves
    # the thresholds apart.
    case "$OVERRIDE" in
        0*)
            alert "CAUTION: GEMMA_GUARD_LOW_UV has a leading zero. The guard keeps ${LOW_UV}."
            ;;
        *[!0-9]* | ????????*)
            alert "CAUTION: GEMMA_GUARD_LOW_UV is not a plain number. The guard keeps ${LOW_UV}."
            ;;
        *)
            # A range, so that a person who types volts or millivolts instead
            # of microvolts gets the default and a line in the journal, rather
            # than a guard whose low path is silently switched off.
            if [ "$OVERRIDE" -lt 2500000 ] || [ "$OVERRIDE" -gt 4300000 ]; then
                alert "CAUTION: GEMMA_GUARD_LOW_UV ${OVERRIDE} is outside 2500000 to 4300000. The guard keeps ${LOW_UV}."
            else
                LOW_UV="$OVERRIDE"
                alert "CAUTION: GEMMA_GUARD_LOW_UV moved the low value to ${LOW_UV} microvolts."
                alert "CAUTION: this is for a bench test. An appliance in service must not have it."
            fi
            ;;
    esac
fi

# A value of GEMMA_GUARD_LOW_UV that failed the checks above was never
# assigned, so LOW_UV still holds the value from the top of this file. That
# means the thresholds can only be out of order if somebody edited those
# constants. If that happens the guard stops, rather than decide from numbers
# it cannot trust.

RESET_UV=$((LOW_UV + 50000))
EMERGENCY_UV=$((LOW_UV - 200000))

if [ "$FLOOR_UV" -ge "$EMERGENCY_UV" ] \
    || [ "$EMERGENCY_UV" -ge "$LOW_UV" ] \
    || [ "$LOW_UV" -ge "$RESET_UV" ] \
    || [ "$RESET_UV" -ge "$CEILING_UV" ]; then
    alert "The thresholds are out of order: floor ${FLOOR_UV}, emergency ${EMERGENCY_UV}, low ${LOW_UV}, reset ${RESET_UV}, ceiling ${CEILING_UV}."
    alert "The guard stops rather than decide from them."
    exit 1
fi

log "The low battery guard starts. The file is ${VOLTAGE_FILE}."
log "Low is ${LOW_UV} microvolts for ${LOW_READS} net reads, and a read at ${RESET_UV} or above takes one off the count."
log "Emergency is ${EMERGENCY_UV} microvolts for ${EMERGENCY_READS} reads together."
log "A reading outside ${FLOOR_UV} to ${CEILING_UV}, or a fall of more than ${MAX_FALL_UV} in one read, is not believed."
log "The low tier needs a fall of ${MOVEMENT_UV} microvolts inside ${MOVEMENT_WINDOW_READS} reads. With no fall the guard stops no machine, and the emergency tier is not affected."

low_count=0
emergency_count=0
poweroff_tries=0
shutdown_started=0
last_uv=""
held_reads=0
failed_reads=0
mains_held=0
heartbeat=0
static_ref_uv=""
static_reads=0

# Starts at 1, and that is the safety property. No fall has been seen, thus
# there is no evidence of a pack, thus the guard stops nothing. Safe from the
# first read and not from the 61st. A real pack clears it inside a minute.
sensor_static=1

# Say a held condition once, then again every REPEAT_READS reads. A guard that
# cannot read its sensor is a guard that protects nothing, and that must not
# look like the silence of a healthy appliance.
hold() {
    if [ "$((held_reads % REPEAT_READS))" -eq 0 ]; then
        alert "$1 The guard holds its counts and powers off no machine."
    fi
    held_reads=$((held_reads + 1))
}

# Follow the voltage, and set sensor_static from it.
#
# The reference is the last reading that moved, not the previous reading. That
# is what makes a slow drain visible: a pack that falls one millivolt per read
# leaves its reference far behind inside one window, while a gauge with no
# pack under it never leaves its reference at all. A comparison against the
# neighbour would call that same slow pack static, and disarm the guard on the
# machine that needs it.
track_movement() {
    read_uv="$1"

    if [ -z "$static_ref_uv" ]; then
        static_ref_uv="$read_uv"
        static_reads=0
        return
    fi

    # A fall, and the comparison holds the boundary. A fall of exactly
    # MOVEMENT_UV is a fall: a pack that sags by that much under load must arm
    # the guard, and a greater-than would throw that reading away.
    if [ "$read_uv" -le "$((static_ref_uv - MOVEMENT_UV))" ]; then
        static_ref_uv="$read_uv"
        static_reads=0

        if [ "$sensor_static" -eq 1 ]; then
            sensor_static=0
            log "The voltage fell to ${read_uv} microvolts. The guard watches a pack again."
        fi

        return
    fi

    # A rise takes the reference up with it, so that a charge does not leave a
    # low reference behind for the fall test to measure from. It arms nothing:
    # a voltage that goes up is not a discharge.
    if [ "$read_uv" -gt "$static_ref_uv" ]; then
        static_ref_uv="$read_uv"
    fi

    static_reads=$((static_reads + 1))

    if [ "$static_reads" -ge "$MOVEMENT_WINDOW_READS" ] && [ "$sensor_static" -eq 0 ]; then
        sensor_static=1
        alert "The voltage has not fallen by ${MOVEMENT_UV} microvolts in ${MOVEMENT_WINDOW_READS} reads. The guard stops no machine on a level alone."
    fi
}

while true; do
    if microvolts="$(read_microvolts)"; then
        if [ "$failed_reads" -ne 0 ]; then
            log "The guard reads ${VOLTAGE_FILE} again."
            failed_reads=0
            held_reads=0
        fi

        # A reading the guard refuses to believe must not move the reference
        # of the control that decides a poweroff.
        if [ "$microvolts" -ge "$FLOOR_UV" ] && [ "$microvolts" -le "$CEILING_UV" ]; then
            track_movement "$microvolts"
        fi

        if [ "$microvolts" -lt "$FLOOR_UV" ] || [ "$microvolts" -gt "$CEILING_UV" ]; then
            emergency_count=0
            hold "The voltage reads ${microvolts} microvolts, which these cells cannot produce."
        elif [ -n "$last_uv" ] \
            && [ "$last_uv" -ge "$LOW_UV" ] \
            && [ "$((last_uv - microvolts))" -gt "$MAX_FALL_UV" ]; then
            emergency_count=0
            hold "The voltage fell from ${last_uv} to ${microvolts} microvolts in one read, which these cells cannot do."
        elif [ "$sensor_static" -eq 1 ] \
            && [ "$microvolts" -lt "$LOW_UV" ] \
            && [ "$microvolts" -ge "$EMERGENCY_UV" ]; then
            # THE POSITION OF THIS BRANCH IS THE CONTROL. It must stay above
            # the mains branch and above every branch that can reach
            # power_off. Move it down the chain and the appliance turns itself
            # off every few minutes, for ever, on a healthy mains supply.
            #
            # EMERGENCY_UV is outside it on purpose: cells under 3.0 V take
            # damage while a person examines the sensor.
            #
            # Both counts CLEAR, where every other hold in this file only
            # holds. The others are absence of evidence; this one is a finding
            # that nothing is being discharged. A held low_count could never
            # fall again - only a read at RESET_UV or above removes one, and
            # this reading is below LOW_UV - so it would fire on the first
            # read that cleared sensor_static.
            emergency_count=0
            low_count=0
            hold "The voltage is ${microvolts} microvolts and has not fallen for ${static_reads} reads."
        elif on_mains && [ "$mains_held" -lt "$MAINS_HOLD_READS" ]; then
            emergency_count=0
            mains_held=$((mains_held + 1))

            if [ "$((mains_held % HEARTBEAT_READS))" -eq 1 ]; then
                log "The mains is present at ${microvolts} microvolts. The guard holds its count at ${low_count}, for ${mains_held} of ${MAINS_HOLD_READS} reads."
            fi
        else
            held_reads=0

            if on_mains; then
                # The hold ran out. Either the charger cannot lift this pack or
                # the mains line is stuck on. Either way the guard must act.
                if [ "$mains_held" -eq "$MAINS_HOLD_READS" ]; then
                    alert "The mains has been present for ${MAINS_HOLD_READS} reads and the voltage is still ${microvolts} microvolts. The guard counts again."
                    mains_held=$((mains_held + 1))
                fi
            else
                mains_held=0
            fi

            if [ "$microvolts" -lt "$EMERGENCY_UV" ]; then
                # Both counts advance. A cell crossing the emergency threshold
                # under a changing load has emergency_count cleared by every
                # low read, so low_count is the only count that can reach its
                # limit for that machine.
                emergency_count=$((emergency_count + 1))
                low_count=$((low_count + 1))
                alert "EMERGENCY: ${microvolts} microvolts, read ${emergency_count} of ${EMERGENCY_READS}."

                if [ "$shutdown_started" -eq 0 ] && [ "$emergency_count" -ge "$EMERGENCY_READS" ]; then
                    power_off "The voltage was under ${EMERGENCY_UV} microvolts for ${EMERGENCY_READS} reads together." "$microvolts" \
                        && shutdown_started=1
                fi
            elif [ "$microvolts" -lt "$LOW_UV" ]; then
                emergency_count=0
                low_count=$((low_count + 1))
                alert "LOW: ${microvolts} microvolts, count ${low_count} of ${LOW_READS}."

                if [ "$shutdown_started" -eq 0 ] && [ "$low_count" -ge "$LOW_READS" ]; then
                    power_off "The voltage was under ${LOW_UV} microvolts for ${LOW_READS} net reads." "$microvolts" \
                        && shutdown_started=1
                fi
            elif [ "$microvolts" -ge "$RESET_UV" ]; then
                # Take one off the count instead of clearing it. A pack near
                # the end of its charge rests above the threshold and sags
                # under an inference burst, so the reading alternates. A count
                # that clears on any healthy read never reaches its limit for
                # that machine, and the appliance runs until the cells cut out.
                emergency_count=0
                if [ "$low_count" -gt 0 ]; then
                    low_count=$((low_count - 1))
                    log "The voltage is ${microvolts} microvolts. The count goes down to ${low_count}."
                elif [ "$((heartbeat % HEARTBEAT_READS))" -eq 0 ]; then
                    log "The voltage is ${microvolts} microvolts and the count is 0."
                fi
                heartbeat=$((heartbeat + 1))
            else
                # Between LOW_UV and RESET_UV. Both counts hold, so a voltage
                # moving inside the band does not flip between two conditions.
                emergency_count=0
            fi
        fi

        last_uv="$microvolts"
    else
        emergency_count=0
        if [ "$failed_reads" -eq 0 ]; then
            held_reads=0
        fi
        failed_reads=$((failed_reads + 1))
        hold "The guard cannot read ${VOLTAGE_FILE}."
    fi

    sleep "$INTERVAL_S"
done
