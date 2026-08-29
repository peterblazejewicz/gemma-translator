#!/bin/bash
# Copyright 2026 Google LLC
# Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
# This file is part of a fork of google-gemma/gemma-translator and has been
# modified. It adds the device tree overlay and the udev rule of the GPIO
# buttons and of the mains line of the UPS.

# Automated Raspberry Pi One-Command Appliance Bootstrap Script

set -e
# pipefail because this script writes files through `... | sudo tee`. Without
# it a failure on the left of a pipe is not seen, tee writes an empty file, and
# the steps that follow report success over a rule or a unit with nothing in it.
set -o pipefail

# Each failure gives its line. This script has about forty commands that can
# fail, and it changes /boot, thus a person who reads only the last line must
# still learn where it stopped.
#
# CAUTION: step 2 changes the start of the machine and step 3 makes the venv,
# which is the step that a pin of Python can stop at any time. Thus a failure
# after step 2 leaves a machine that starts with our console and has no
# translator, and the person has already been told to restart it. This says so.
on_error() {
    echo "[ERROR] deploy-pi.sh stopped at line ${1}."
    if [ "${REBOOT_NEEDED:-0}" = "1" ]; then
        echo "[ERROR] CAUTION: the start of this machine is already changed and"
        echo "[ERROR] the appliance is not complete. To go back:"
        echo "[ERROR]   sudo cp ${BOOT_DIR:-/boot/firmware}/config.txt.gemma-backup ${BOOT_DIR:-/boot/firmware}/config.txt"
        echo "[ERROR]   sudo cp ${BOOT_DIR:-/boot/firmware}/cmdline.txt.gemma-backup ${BOOT_DIR:-/boot/firmware}/cmdline.txt"
    fi
}
trap 'on_error ${LINENO}' ERR

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CURRENT_USER="$(whoami)"
CURRENT_UID="$(id -u)"
WITH_SPLASH=0
WITH_RTC_CHARGE=0

# Put one word on the ONE line of cmdline.txt, and give 0 when the file
# changed. A word of the form key=value takes the position of a key that is
# there with a different value; a word with no value goes in if it is not
# there.
#
# CAUTION: the kernel reads the first line of cmdline.txt and no other. Thus
# this reads the first line, writes the first line, and keeps the file at one
# line. A test that reads the whole file says "already there" for a word on
# line 2, which the kernel never sees.
#
# It replaces and does not append a second copy. Two words with the same key
# leave the answer to the order that the kernel reads them in, and that order
# is not a promise of the kernel: a handler that accumulates takes both.
set_cmdline_word() {
    local file="$1" word="$2"
    local current updated
    current="$(head -1 "$file")"
    updated="$(WORD="$word" awk '
        BEGIN { w = ENVIRON["WORD"]; split(w, p, "="); k = p[1]; done = 0 }
        {
            out = ""
            for (i = 1; i <= NF; i++) {
                split($i, kv, "=")
                if (kv[1] == k) { if (!done) { out = out " " w; done = 1 } }
                else { out = out " " $i }
            }
            if (!done) { out = out " " w }
            sub(/^ /, "", out)
            print out
        }' <<< "$current")"

    if [ "$updated" = "$current" ]; then
        return 1
    fi
    printf '%s\n' "$updated" | sudo tee "$file" > /dev/null
    return 0
}

for arg in "$@"; do
    case "$arg" in
        --with-splash) WITH_SPLASH=1 ;;
        --with-rtc-charge) WITH_RTC_CHARGE=1 ;;
        *)
            echo "[ERROR] ${arg} is not a known argument."
            echo "[ERROR] Usage: ./deploy-pi.sh [--with-splash] [--with-rtc-charge]"
            echo "[ERROR]"
            echo "[ERROR] --with-rtc-charge puts a charging voltage on the cell of"
            echo "[ERROR] the clock. Give it with a rechargeable cell only, which"
            echo "[ERROR] is an ML-2020 and not a CR2032 and not a LIR2032."
            exit 1
            ;;
    esac
done

echo "==========================================================="
echo "Gemma Offline Translator — Pi Automated Deployment"
echo "Project Dir: ${PROJECT_DIR}"
echo "User: ${CURRENT_USER} (UID: ${CURRENT_UID})"
echo "==========================================================="

if [ "$(uname -s)" != "Linux" ]; then
    echo "[WARNING] This script is intended for Raspberry Pi OS / Debian Linux."
    echo "[WARNING] Current OS: $(uname -s)."
fi

echo "[1/9] Installing OS dependencies..."
if command -v apt-get &> /dev/null; then
    sudo apt-get update
    # device-tree-compiler makes the overlay of step 2. i2c-tools is for the
    # fuel gauge of the UPS at address 0x36. netcat-openbsd and lsof are for
    # start.sh, and wget gets the package feed below. alsa-utils stays for
    # `aplay -l` and `arecord -l`, which name the devices when the speakerphone
    # is not heard.
    #
    # ffmpeg, pulseaudio-utils and their callers went away together:
    # backend/server.py used ffmpeg, and wpctl and pactl set a level that the
    # Speak2 40 does not take. Nothing in src/ calls any of the three.
    #
    # The Mesa packages are what the DRM backend of Avalonia loads. Note that
    # "libegl1-mesa" is not one of them: that name went away on Debian 13, and
    # a person who copies it from an older guide gets "Unable to locate
    # package". See section 8.17 of deploy/README.md.
    #
    # libinput10 is the input part of that same backend, and Pi OS Lite does
    # not give it. Without it Avalonia throws DllNotFoundException for
    # libinput.so.10 and the software stops before it draws one pixel.
    sudo apt-get install -y python3-venv python3-pip libasound2-dev \
        alsa-utils device-tree-compiler i2c-tools \
        netcat-openbsd lsof wget \
        libinput10 libgbm1 libegl1 libegl-mesa0 libgl1-mesa-dri libgles2
else
    echo "[INFO] apt-get not detected. Skipping Debian package installation."
fi

# The .NET SDK makes the user interface. Node.js is gone with frontend/.
#
# The package feed of Microsoft gives arm64 for .NET 10 and gives x64 only for
# .NET 8 and .NET 9. Thus this step is possible on this machine and it was not
# possible with an earlier version.
if ! command -v dotnet &> /dev/null; then
    echo "[INFO] The .NET SDK is not there. Installing .NET 10 for linux-arm64..."
    if [ -f /etc/os-release ] && command -v apt-get &> /dev/null; then
        # In a subshell, because /etc/os-release sets a group of names and this
        # script needs two of them only. Raspberry Pi OS 64-bit gives
        # ID=debian and VERSION_ID="13", which is the path that the feed uses.
        OS_ID="$(. /etc/os-release && printf '%s' "$ID")"
        OS_VER="$(. /etc/os-release && printf '%s' "$VERSION_ID")"
        MS_PROD_DEB="$(mktemp --suffix=.deb)"
        MS_FEED="https://packages.microsoft.com/config/${OS_ID}/${OS_VER}/packages-microsoft-prod.deb"

        # SECURITY CONTROL. Do not remove this hash and do not make it optional.
        #
        # dpkg -i runs the maintainer scripts of this package as root, and the
        # package installs the signing key and the address of the feed that the
        # SDK below comes from. It is the trust root of about 1 GB that follows
        # it. A machine that takes a different file here has an attacker's key
        # in apt, and each signature that apt tests after that point agrees.
        #
        # HTTPS alone does not close this. The appliance is built on whatever
        # network is at hand, and a network that intercepts TLS with a root
        # certificate that the machine already trusts gives a wget that
        # succeeds and a dpkg that runs somebody else's code. The hash comes
        # from this repository, thus it is trust that the network cannot reach.
        #
        # The value is of the file for Debian 13 and it comes from
        # https://packages.microsoft.com/config/debian/13/FILE_MANIFEST
        # A machine that is not Debian 13 gets no hash from this project, thus
        # it does not install from the feed and the block below tells a person
        # what to do by hand. Microsoft makes this file again from time to
        # time: when the hash stops agreeing, read FILE_MANIFEST, look at what
        # changed, and put the new value here on purpose.
        MS_PROD_SHA256=""
        if [ "$OS_ID" = "debian" ] && [ "$OS_VER" = "13" ]; then
            MS_PROD_SHA256="d0c2f69250c6ce0d4c6220b142f999d039a3c560af7f980b943687d106ca8e38"
        fi

        # Each command is in the condition and not in the body. In the body one
        # failure of apt ends this script with the raw message of apt, and the
        # block below, which gives a person the way out, never comes on the
        # display.
        if [ -z "$MS_PROD_SHA256" ]; then
            echo "[WARNING] This project holds no hash of the feed for ${OS_ID} ${OS_VER}."
        elif wget -q "$MS_FEED" -O "$MS_PROD_DEB" \
           && echo "${MS_PROD_SHA256}  ${MS_PROD_DEB}" | sha256sum --check --status \
           && sudo dpkg -i "$MS_PROD_DEB" \
           && sudo apt-get update \
           && sudo apt-get install -y dotnet-sdk-10.0; then
            echo "[INFO] The .NET SDK is in."
        else
            echo "[WARNING] The feed gave no dotnet-sdk-10.0 for ${OS_ID} ${OS_VER}."
            echo "[WARNING] If the download was good, the hash did not agree."
        fi
        rm -f "$MS_PROD_DEB"
    fi
fi

# CAUTION: the feed of Microsoft comes in at the same priority as the archive of
# Debian, which carries dotnet-sdk-8.0 and dotnet-runtime-8.0 of its own. With
# no rule, a later `apt upgrade` takes a package of .NET from whichever of the
# two gives the higher number, and the origin does not enter that decision. This
# keeps the feed for the packages of .NET and for no other package.
#
# It is outside the block above on purpose: a machine that got .NET before this
# rule existed needs the rule, and that machine does not enter the install.
if [ -f /etc/apt/sources.list.d/microsoft-prod.list ] && [ -d /etc/apt/preferences.d ]; then
    printf '%s\n' \
        '# Gemma Translator appliance.' \
        '# The feed of Microsoft gives the packages of .NET and nothing else.' \
        'Package: *' \
        'Pin: origin "packages.microsoft.com"' \
        'Pin-Priority: -10' \
        '' \
        'Package: dotnet* aspnetcore* netstandard*' \
        'Pin: origin "packages.microsoft.com"' \
        'Pin-Priority: 500' \
        | sudo tee /etc/apt/preferences.d/99-gemma-microsoft > /dev/null
    sudo chmod 644 /etc/apt/preferences.d/99-gemma-microsoft
    echo "[INFO] apt: the feed of Microsoft gives the packages of .NET only."
fi

DOTNET_MAJOR=""
if command -v dotnet &> /dev/null; then
    DOTNET_MAJOR="$(dotnet --version 2>/dev/null | cut -d. -f1)"
fi

# CAUTION: .NET 9 and below cannot build this project, which targets net10.0.
# An older SDK in PATH is the same condition as no SDK at all, thus it stops
# here and not at step 4, which is after the changes to /boot.
if [ -z "$DOTNET_MAJOR" ] || [ "$DOTNET_MAJOR" -lt 10 ] 2>/dev/null; then
    if [ -z "$DOTNET_MAJOR" ]; then
        echo "[ERROR] The .NET SDK is not there, thus step 4 cannot publish."
    else
        echo "[ERROR] .NET v${DOTNET_MAJOR} is installed and this needs v10."
    fi
    echo "[ERROR] Install it by hand and run this script again:"
    echo "[ERROR]   wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh"
    echo "[ERROR]   chmod +x dotnet-install.sh && ./dotnet-install.sh --channel 10.0"
    echo "[ERROR] Then put ~/.dotnet in PATH and set DOTNET_ROOT to it. CAUTION:"
    echo "[ERROR] the unit of step 9 gives a PATH that does not hold ~/.dotnet."
    exit 1
fi

echo "[2/9] Installing the GPIO overlay and the udev rule..."
# The two push-to-talk buttons on GPIO17 and GPIO27, and the mains line of the
# X1201 UPS on GPIO6. See deploy/recorder-keys-overlay.dts.
BOOT_DIR="/boot/firmware"
OVERLAY_SRC="${PROJECT_DIR}/deploy/recorder-keys-overlay.dts"
UDEV_TEMPLATE="${PROJECT_DIR}/deploy/99-gemma-translator.rules"
UDEV_FILE="/etc/udev/rules.d/99-gemma-translator.rules"
REBOOT_NEEDED=0

if [ -f "$OVERLAY_SRC" ] && [ -d "${BOOT_DIR}/overlays" ]; then
    TMP_DTBO="$(mktemp)"
    dtc -@ -I dts -O dtb -o "$TMP_DTBO" "$OVERLAY_SRC"

    # Install the overlay only if it changed. The value tells the user if a
    # restart is necessary, and a restart of this appliance is not free.
    if ! sudo cmp -s "$TMP_DTBO" "${BOOT_DIR}/overlays/recorder-keys.dtbo"; then
        sudo install -m 644 "$TMP_DTBO" "${BOOT_DIR}/overlays/recorder-keys.dtbo"
        echo "[INFO] Installed ${BOOT_DIR}/overlays/recorder-keys.dtbo"
        REBOOT_NEEDED=1
    else
        echo "[INFO] recorder-keys.dtbo is current."
    fi
    rm -f "$TMP_DTBO"

    CONFIG_TXT="${BOOT_DIR}/config.txt"

    # CAUTION: config.txt controls the start of the machine. Keep a copy
    # before the first change. The boot volume is FAT32, thus a person can
    # correct this file from a different machine if the Raspberry Pi does not
    # start.
    if [ ! -f "${CONFIG_TXT}.gemma-backup" ]; then
        sudo cp "$CONFIG_TXT" "${CONFIG_TXT}.gemma-backup"
        echo "[INFO] Backup: ${CONFIG_TXT}.gemma-backup"
    fi

    # One pin has one owner. A line for GPIO6, GPIO17 or GPIO27 from an
    # earlier configuration must go, or our overlay does not bind and the log
    # of the kernel gives "error -16". A new installation of Raspberry Pi OS
    # has no such line, and this removes nothing.
    sudo sed -i -E \
        -e '/^[[:space:]]*dtoverlay=gpio-key,.*gpio=(17|27)([,[:space:]]|$)/d' \
        -e '/^[[:space:]]*dtoverlay=gpio-charger,.*gpio=6([,[:space:]]|$)/d' \
        "$CONFIG_TXT"

    if ! grep -qE '^[[:space:]]*dtoverlay=recorder-keys[[:space:]]*$' "$CONFIG_TXT"; then
        # CAUTION: [all] goes with the line. config.txt has sections such as
        # [pi5] and [cm5], and each line after a section is for that model
        # only. This file ends with [all] today, but a person can add a
        # section at the end. Then a line that we put at the end goes in that
        # section, and the overlay does not load on this machine.
        printf '\n[all]\ndtoverlay=recorder-keys\n' | sudo tee -a "$CONFIG_TXT" > /dev/null
        echo "[INFO] Added dtoverlay=recorder-keys to config.txt"
        REBOOT_NEEDED=1
    else
        echo "[INFO] config.txt already has dtoverlay=recorder-keys."
    fi

    # The fuel gauge of the X1201, through the driver of the kernel. This is a
    # stock overlay of Raspberry Pi OS and it needs no file from this project.
    #
    # CAUTION: the driver then owns address 0x36 of bus 1. A program that reads
    # that address directly gets "Device or resource busy". The scripts of
    # Geekworm in x120x read it directly, thus they stop after this line.
    if ! grep -qE '^[[:space:]]*dtoverlay=i2c-sensor,max17040[[:space:]]*$' "$CONFIG_TXT"; then
        printf '\n[all]\ndtoverlay=i2c-sensor,max17040\n' | sudo tee -a "$CONFIG_TXT" > /dev/null
        echo "[INFO] Added dtoverlay=i2c-sensor,max17040 to config.txt"
        echo "[WARNING] After the next start, the driver owns I2C address 0x36."
        echo "[WARNING] The scripts in x120x read that address directly and will"
        echo "[WARNING] stop with 'Device or resource busy'. If one of them does"
        echo "[WARNING] your low battery shutdown, put a replacement in place"
        echo "[WARNING] first. See section 8.9 of deploy/README.md."
        REBOOT_NEEDED=1
    else
        echo "[INFO] config.txt already has dtoverlay=i2c-sensor,max17040."
    fi
else
    echo "[INFO] ${BOOT_DIR}/overlays not found. Skipping the GPIO overlay."
fi

# The charger of the cell of the real time clock, with --with-rtc-charge only.
#
# CAUTION: THIS LINE PUTS A CHARGING VOLTAGE ON THE CELL OF THE J5 CONNECTOR.
# GIVE THE ARGUMENT ONLY WITH A CELL THAT TAKES A CHARGE.
#
# The cell of this appliance is a Panasonic ML-2020, which Raspberry Pi sells
# as RPI-23926. ML is lithium-manganese and it is rechargeable. Its window is
# 2.8 V to 3.2 V, thus 3.0 V sits in the middle of it.
#
# A CR2032 is a primary cell of lithium. It does not take a charge, and a
# charge makes it leak, open or burst. A LIR2032 is lithium-ion and its
# chemistry is not this one either. Raspberry Pi names both as wrong for this
# connector. **A person who fits one of those must not give this argument.**
#
# CAUTION: /sys/class/rtc/rtc0/charging_voltage_max gives 4400000, and that
# number is the range of the driver and not a value for this cell. 4.4 V on an
# ML-2020 corrodes it.
#
# The default of the firmware is 0, which is the charger off. Thus a machine
# with no argument charges nothing, and a cell of the wrong kind is safe.
if [ "$WITH_RTC_CHARGE" = "1" ]; then
    CONFIG_TXT="${BOOT_DIR}/config.txt"
    RTC_SYSFS="/sys/class/rtc/rtc0/charging_voltage"

    if [ ! -e "$RTC_SYSFS" ]; then
        echo "[ERROR] This machine gives no ${RTC_SYSFS}, thus it has no charger"
        echo "[ERROR] of a backup cell. The parameter is for the 2712 of the"
        echo "[ERROR] Raspberry Pi 5 only."
        exit 1
    fi

    if [ ! -f "$CONFIG_TXT" ]; then
        echo "[ERROR] ${CONFIG_TXT} is not there."
        exit 1
    fi

    if grep -qE '^[[:space:]]*dtparam=rtc_bbat_vchg=' "$CONFIG_TXT"; then
        echo "[INFO] config.txt already gives the charge of the clock cell."
    else
        printf '\n[all]\ndtparam=rtc_bbat_vchg=3000000\n' | sudo tee -a "$CONFIG_TXT" > /dev/null
        echo "[INFO] Added dtparam=rtc_bbat_vchg=3000000 to config.txt, which is"
        echo "[INFO] 3.0 V, in the window of 2.8 V to 3.2 V of the ML-2020."
        REBOOT_NEEDED=1
    fi

    echo "[INFO] The cell of the clock reads $(cat /sys/class/rtc/rtc0/battery_voltage 2>/dev/null) microvolts."
    echo "[INFO] The charger reads $(cat "$RTC_SYSFS" 2>/dev/null) microvolts, and it"
    echo "[INFO] takes the new value after the next start of this machine."
fi

# The console of the panel, which is native portrait. This turns the console
# only: the software turns its own output, because a program that draws through
# DRM gets the panel as it is. See section 8.13 of deploy/README.md.
#
# CAUTION: the value here and SurfaceOrientation.Rotation90 in Program.cs are
# 180 degrees apart, and that is correct. A person who makes the two the same
# gets an image that is upside down. See section 8.15.
CMDLINE_TXT="${BOOT_DIR}/cmdline.txt"
CMDLINE_VIDEO="video=DSI-1:720x1280@60,rotate=270"

if [ -f "$CMDLINE_TXT" ]; then
    # CAUTION: cmdline.txt is ONE line. The kernel reads the first line and
    # nothing else. A command that appends a word puts a second line in this
    # file, and then the machine does not start. Each edit below stays on the
    # one line, and the backup is the way back.
    if [ ! -f "${CMDLINE_TXT}.gemma-backup" ]; then
        sudo cp "$CMDLINE_TXT" "${CMDLINE_TXT}.gemma-backup"
        echo "[INFO] Backup: ${CMDLINE_TXT}.gemma-backup"
    fi

    # head -1 and not the whole file: the kernel reads the first line only, thus
    # a word on line 2 is not there as far as the machine is concerned.
    if head -1 "$CMDLINE_TXT" | grep -qF -- "$CMDLINE_VIDEO"; then
        echo "[INFO] cmdline.txt already gives the mode of the panel."
    elif head -1 "$CMDLINE_TXT" | grep -qF -- "video=DSI-1:"; then
        # A test of the "video=DSI-1:" part alone takes any value after it. A
        # machine that got rotate=90 from an earlier attempt would then keep it
        # for ever, and the image on the panel is upside down. This script does
        # not correct the line, because the value belongs to whoever put it
        # there.
        echo "[WARNING] cmdline.txt gives a mode of the panel and not this one:"
        echo "[WARNING]   ${CMDLINE_VIDEO}"
        echo "[WARNING] The panel can be upside down. See section 8.15 of"
        echo "[WARNING] deploy/README.md, and correct the line by hand."
    elif set_cmdline_word "$CMDLINE_TXT" "$CMDLINE_VIDEO"; then
        echo "[INFO] Added ${CMDLINE_VIDEO} to cmdline.txt"
        REBOOT_NEEDED=1
    fi
else
    echo "[INFO] ${CMDLINE_TXT} is not there. Skipping the mode of the panel."
fi

if [ -f "$UDEV_TEMPLATE" ]; then
    sed -e "s|{{USER}}|${CURRENT_USER}|g" "$UDEV_TEMPLATE" | sudo tee "$UDEV_FILE" > /dev/null
    sudo chmod 644 "$UDEV_FILE"
    sudo udevadm control --reload
    sudo udevadm trigger --subsystem-match=input
    # The speakerphone is a hidraw node, thus a trigger of "input" alone does
    # not apply its rule and /dev/appliance-speakerphone is not made.
    sudo udevadm trigger --subsystem-match=hidraw
    # The panel is a drm node, and the same applies. Without this line
    # /dev/dri/appliance-panel is not made, and Program.cs throws at the start:
    # it takes that path and it does not look for a card on its own, because
    # this machine has three and Avalonia would take the first one that opens.
    sudo udevadm trigger --subsystem-match=drm
    echo "[INFO] udev rule: ${UDEV_FILE}"

    # The two rules above give this account the buttons and the touchscreen by
    # owner, thus it needs no membership of group "input". That group reads
    # each /dev/input/event* node, because each node is 0660 root:input. This
    # includes the microphone of the Jabra, the button of the Raspberry Pi, and
    # a USB keyboard that a person connects after this installation.
    #
    # Raspberry Pi OS puts the first account of the machine in that group. Thus
    # this step removes a membership that the image gives, and not one that
    # this project gives. See section 8.10 of deploy/README.md.
    if id -nG "$CURRENT_USER" | tr ' ' '\n' | grep -qx input; then
        sudo gpasswd -d "$CURRENT_USER" input
        echo "[INFO] Removed ${CURRENT_USER} from group input."
        echo "[INFO] A session that is open keeps the group of its start. The"
        echo "[INFO] change is complete after the next start of the machine."
        REBOOT_NEEDED=1
    else
        echo "[INFO] ${CURRENT_USER} is not in group input."
    fi
fi

# The console of the panel goes away.
#
# MEASURED: Plymouth gives the panel back when it stops, and the software draws
# its first frame about 7 seconds later, because it starts litert-lm and waits
# for the port. In that window the login of tty1 is on the display, and a person
# sees text on an appliance that shows no text anywhere else.
#
# The appliance has no keyboard, thus a login on the panel serves nobody. With
# no getty the display stays black until the warm-up screen comes.
#
# CAUTION: linger comes first and it is not optional. The session of that same
# getty is what made /run/user/1000, which the unit gives to the software as
# XDG_RUNTIME_DIR. With the getty gone and no linger, that directory is not
# there. Nothing reads it today - a measurement of the running software gives
# no open file below /run/user - and a variable that names a directory that
# does not exist is a trap for whoever adds the next library.
if command -v loginctl &> /dev/null; then
    if [ "$(loginctl show-user "$CURRENT_USER" --property=Linger --value 2>/dev/null)" != "yes" ]; then
        sudo loginctl enable-linger "$CURRENT_USER"
        echo "[INFO] linger is on for ${CURRENT_USER}, thus /run/user/$(id -u) is made at each start."
    fi
fi

if [ "$(systemctl is-enabled getty@tty1.service 2>/dev/null)" = "enabled" ]; then
    sudo systemctl disable --now getty@tty1.service
    echo "[INFO] The login of tty1 is off. The panel shows no console."
fi

echo "[3/9] Making the Python environment..."
"${PROJECT_DIR}/setup.sh"

echo "[4/9] Publishing the user interface..."
# The Avalonia software replaces the React user interface. It goes in publish/,
# which start.sh starts with --drm.
#
# CAUTION: the runtime identifier must be linux-arm64. A publish with no
# identifier gives a binary that this machine cannot start.
dotnet publish "${PROJECT_DIR}/src/GemmaTranslator" \
    -c Release \
    -r linux-arm64 \
    --self-contained false \
    -o "${PROJECT_DIR}/publish"

echo "[5/9] Downloading LiteRT model..."
"${PROJECT_DIR}/download_model.sh"

# The models of the speech are not in the step above. That one gives the model
# of the translation to litert-lm, and this one fills the cache that
# libmoonshine.so reads. An appliance with one and not the other starts, shows
# the user interface, and hears nothing.
echo "[5b/9] Downloading the models of the speech..."
"${PROJECT_DIR}/download_speech_models.sh"

echo "[6/9] Installing the low battery guard..."
# The guard stops the machine when the voltage of the cells stays low. Without
# it, the cells go empty, the Raspberry Pi loses its electrical supply in one
# moment, and the SD card can become defective in the middle of a write.
#
# The guard operates as root, because systemctl poweroff needs that privilege.
# It is not part of the translator on purpose: the moments that the guard is
# necessary are the moments that the translator does not operate.
GUARD_SRC="${PROJECT_DIR}/deploy/gemma-battery-guard.sh"
GUARD_DEST="/usr/local/sbin/gemma-battery-guard.sh"
GUARD_UNIT_SRC="${PROJECT_DIR}/deploy/gemma-battery-guard.service"
GUARD_UNIT_DEST="/etc/systemd/system/gemma-battery-guard.service"

# Whether cells are in the holder is a fact a person knows when they build the
# appliance, and no reading from the board can replace it. An X1201 with an
# empty holder still answers on I2C, still reports present=1, and still gives
# a voltage below the low threshold. A guard that is enabled by default then
# powers the machine off every few minutes on a mains supply that never
# failed. The files always go on, so that they are there when cells arrive.
# Only the unit waits for a person to say that the cells are in.
#
# The answer is kept ON THE MACHINE. An appliance gets a software update many
# times after a person puts the cells in, and an update that runs without the
# variable must not take the protection away from cells that are there. Thus
# the marker file gives the default, the variable can change it, and only an
# explicit 0 removes the protection.
CELLS_MARKER="/etc/gemma-translator/ups-cells-fitted"

if [ -e "$CELLS_MARKER" ]; then
    GEMMA_UPS_CELLS_FITTED="${GEMMA_UPS_CELLS_FITTED:-1}"
else
    GEMMA_UPS_CELLS_FITTED="${GEMMA_UPS_CELLS_FITTED:-0}"
fi

# A value that is not one of these comes from a person who believes the guard
# operates. Stop, because the alternative is an appliance with no protection
# and an [INFO] line that agrees with them.
case "$(printf '%s' "$GEMMA_UPS_CELLS_FITTED" | tr 'A-Z' 'a-z')" in
    1 | true | yes | y) CELLS_FITTED=1 ;;
    0 | false | no | n) CELLS_FITTED=0 ;;
    *)
        echo "[ERROR] GEMMA_UPS_CELLS_FITTED is \"${GEMMA_UPS_CELLS_FITTED}\"."
        echo "[ERROR] Use 1 or 0. This value decides if the cells of the X1201"
        echo "[ERROR] get protection from a deep discharge, thus this script"
        echo "[ERROR] does not guess what you meant."
        exit 1
        ;;
esac

if [ -f "$GUARD_SRC" ] && [ -f "$GUARD_UNIT_SRC" ]; then
    sudo install -m 755 "$GUARD_SRC" "$GUARD_DEST"
    sudo install -m 644 "$GUARD_UNIT_SRC" "$GUARD_UNIT_DEST"
    sudo systemctl daemon-reload

    if [ "$CELLS_FITTED" = "1" ]; then
        sudo mkdir -p "$(dirname "$CELLS_MARKER")"
        sudo touch "$CELLS_MARKER"
        sudo systemctl enable gemma-battery-guard.service
        sudo systemctl restart gemma-battery-guard.service
        echo "[INFO] The low battery guard is at ${GUARD_DEST} and it operates."
        echo "[INFO] Read it with: journalctl -u gemma-battery-guard -n 20"
    else
        # This is the repair path for an appliance that is already in the
        # poweroff loop, thus it must not fail quietly. A disable that did not
        # work, with a line below that says it did, leaves the machine free to
        # turn itself off in the middle of the steps that follow. The errors
        # stay on the display for the same reason.
        sudo rm -f "$CELLS_MARKER"
        sudo systemctl disable gemma-battery-guard.service || true
        sudo systemctl stop gemma-battery-guard.service || true

        if systemctl is-active --quiet gemma-battery-guard.service; then
            echo "[ERROR] The low battery guard still operates and this script"
            echo "[ERROR] could not stop it. It can turn the machine off in the"
            echo "[ERROR] steps below, and this installation would not finish."
            echo "[ERROR] Stop it by hand, then run this script again."
            exit 1
        fi

        echo "[CAUTION] The low battery guard is at ${GUARD_DEST} and it does"
        echo "[CAUTION] NOT operate. Nothing protects a cell of the X1201 from"
        echo "[CAUTION] a deep discharge, and nothing stops this machine before"
        echo "[CAUTION] the supply goes away."
        echo "[CAUTION] With cells in the holder, run this script again with"
        echo "[CAUTION] GEMMA_UPS_CELLS_FITTED=1 in the environment."
    fi
else
    echo "[ERROR] deploy/ has no low battery guard. The appliance would have"
    echo "[ERROR] no protection against cells that go empty, thus this"
    echo "[ERROR] installation stops here."
    exit 1
fi

echo "[7/9] Setting the swap of the appliance to zram with no file..."
# The default mechanism of rpi-swap is zram+file. zram then writes cold pages
# to /var/swap on the SD card. A page can hold the speech of a person, and a
# page on the card stays after the machine loses its electrical supply. No part
# of the software can remove that copy.
#
# zram with no file keeps the same quantity of swap in the memory, thus it
# costs no headroom on each board.
SWAP_CONF_DIR="/etc/rpi/swap.conf.d"
SWAP_CONF="${SWAP_CONF_DIR}/99-gemma-translator.conf"

if [ -d /etc/rpi ]; then
    sudo mkdir -p "$SWAP_CONF_DIR"
    printf '%s\n' \
        '# Gemma Translator appliance.' \
        '#' \
        '# zram only, with no file. The default is zram+file, which writes cold' \
        '# pages to /var/swap on the SD card. A page can hold the speech of a' \
        '# person, and a page on the card stays after the machine stops.' \
        '[Main]' \
        'Mechanism=zram' \
        | sudo tee "$SWAP_CONF" > /dev/null
    sudo chmod 644 "$SWAP_CONF"
    sudo systemctl daemon-reload

    # The generator makes the unit again at a daemon-reload, but a swap that is
    # in operation stays as it is. The machine must start again to remove it.
    #
    # The test is on the type that swapon gives, and not on
    # /sys/block/zram0/backing_dev. That file holds the writeback device of
    # zram, which is a different mechanism: a machine with a live /var/swap
    # still reads "none" there, thus the old test believed the file was gone
    # while it was still taking pages. It also read empty on a machine with no
    # zram at all, and empty is not "none", thus each deployment of such a
    # machine asked for a restart that nothing needed.
    if swapon --show=NAME,TYPE --noheadings 2>/dev/null | grep -qw file; then
        echo "[INFO] A swap file is in operation. It goes at the next start."
        REBOOT_NEEDED=1
    fi

    echo "[INFO] swap: ${SWAP_CONF}"
else
    echo "[INFO] This machine has no /etc/rpi, thus it does not use rpi-swap."
    echo "[INFO] Examine the swap yourself: a file on the card can keep speech."
fi

echo "[8/9] The images of the start and the stop..."
# The theme of Plymouth is not part of the appliance by default. It takes the
# DRM device, thus a theme that does not give it back keeps the panel and the
# software never comes on the display. See section 11.3 of deploy/README.md.
#
# This step comes before the unit on purpose: it can change cmdline.txt, and
# step 9 reads REBOOT_NEEDED to decide what to say.
if [ "$WITH_SPLASH" = "1" ]; then
    PLYMOUTH_SRC="${PROJECT_DIR}/deploy/plymouth"
    SPLASH_SRC="${PROJECT_DIR}/deploy/assets"
    BRANDED_SRC="${SPLASH_SRC}/branded"
    THEME_DEST="/usr/share/plymouth/themes/gemma"

    # These two names come from gemma.script, which calls Image() with each one
    # and with no other. A name that changes in one location and not in the
    # other gives a theme that draws nothing.
    SPLASH_IMAGES="boot-splash-720x1280.png shutdown-splash-720x1280.png"

    # branded/ is the drop box of each asset of the brand and not of the two
    # images only. brand-mark.svg belongs there and the software reads it from
    # the publish, thus this step must not call it a name that nobody uses.
    KNOWN_NAMES="${SPLASH_IMAGES} brand-mark.svg README.md"

    if [ ! -f "${PLYMOUTH_SRC}/gemma.plymouth" ]; then
        echo "[ERROR] ${PLYMOUTH_SRC} has no theme."
        exit 1
    fi

    # Each image is named before anything goes in. plymouth-set-default-theme
    # makes gemma the default, and a theme with an image that is not there
    # gives a black panel at the next start. A failure must come before that
    # line, and not after it.
    #
    # branded/ comes first. It holds the mark of the owner of the appliance,
    # which git does not carry, and deploy/assets/ holds the placeholder that
    # this repository owns. Each file is taken on its own, thus an appliance
    # can give its own image of the start and keep the placeholder of the stop.
    for image in $SPLASH_IMAGES; do
        if [ -f "${BRANDED_SRC}/${image}" ]; then
            echo "[INFO] ${image}: the file of the brand."
        elif [ -f "${SPLASH_SRC}/${image}" ]; then
            echo "[INFO] ${image}: the placeholder of this repository."
        else
            echo "[ERROR] ${image} is in neither ${BRANDED_SRC} nor"
            echo "[ERROR] ${SPLASH_SRC}. The theme cannot draw without it."
            exit 1
        fi
    done

    # A file in branded/ with a name that nothing reads draws nothing, and a
    # person who put it there believes that it does.
    if [ -d "$BRANDED_SRC" ]; then
        for file in "${BRANDED_SRC}"/*; do
            [ -f "$file" ] || continue
            name="$(basename "$file")"
            case " ${KNOWN_NAMES} " in
                *" ${name} "*) ;;
                *)
                    echo "[WARNING] ${name} is in branded/ and nothing reads that"
                    echo "[WARNING] name. Give it one of: ${SPLASH_IMAGES} brand-mark.svg"
                    ;;
            esac
        done
    fi

    sudo apt-get install -y plymouth plymouth-themes
    sudo mkdir -p "$THEME_DEST"
    sudo install -m 644 "${PLYMOUTH_SRC}/gemma.plymouth" \
                        "${PLYMOUTH_SRC}/gemma.script" "$THEME_DEST/"

    for image in $SPLASH_IMAGES; do
        if [ -f "${BRANDED_SRC}/${image}" ]; then
            sudo install -m 644 "${BRANDED_SRC}/${image}" "${THEME_DEST}/${image}"
        else
            sudo install -m 644 "${SPLASH_SRC}/${image}" "${THEME_DEST}/${image}"
        fi
    done

    sudo plymouth-set-default-theme -R gemma

    # An earlier form of this step gave --ignore-serial-consoles to the five
    # units of systemd that start plymouthd. That cannot work and the words of
    # cmdline.txt below replace it: the plymouthd that draws the image of the
    # start runs from the INITRAMFS, one second into the start, and no unit and
    # no file of /etc exists at that moment. These files are ours, thus this
    # takes them away.
    for unit in plymouth-start plymouth-reboot plymouth-poweroff \
                plymouth-halt plymouth-kexec; do
        STALE="/etc/systemd/system/${unit}.service.d/99-gemma-panel.conf"
        if [ -f "$STALE" ]; then
            sudo rm -f "$STALE"
            sudo rmdir "$(dirname "$STALE")" 2>/dev/null || true
            echo "[INFO] Removed ${STALE}, which never applied."
        fi
    done

    # SECURITY OF THE FUNCTION, not of the data: this bounds the wait.
    #
    # plymouth-quit-wait.service of Debian gives `plymouth --wait` with
    # TimeoutSec=0, and 0 in systemd is INFINITY and not zero. Type=oneshot
    # takes the start timeout away as well. The unit of the translator is
    # After= that service, thus a plymouth that does not stop keeps the panel
    # and the translator never starts - not late, never. The appliance has no
    # keyboard to correct that.
    #
    # 30 s is longer than any start that a person has seen, and it is short
    # enough that a person in front of the appliance waits and does not go away.
    sudo mkdir -p /etc/systemd/system/plymouth-quit-wait.service.d
    printf '%s\n' \
        '# Gemma Translator appliance.' \
        '# The unit of the translator waits for this one. Without a limit, a' \
        '# plymouth that does not stop holds the panel for ever.' \
        '[Unit]' \
        'JobTimeoutSec=30' \
        '[Service]' \
        'TimeoutStartSec=30' \
        | sudo tee /etc/systemd/system/plymouth-quit-wait.service.d/99-gemma-timeout.conf > /dev/null
    sudo chmod 644 /etc/systemd/system/plymouth-quit-wait.service.d/99-gemma-timeout.conf
    sudo systemctl daemon-reload

    # loglevel=4 or vt.global_cursor_default=1 from an earlier configuration
    # must give way, and not stand beside the value that this appliance needs.
    # set_cmdline_word replaces the value of a key that is there.
    # plymouth.ignore-serial-consoles IS WHAT PUTS THE IMAGE ON THE PANEL.
    #
    # MEASURED with plymouth.debug: cmdline.txt gives console=serial0,115200,
    # which the Raspberry Pi 5 resolves to /dev/ttyAMA10. plymouthd finds that
    # console, decides that this machine is a serial terminal, and writes:
    #
    #   serial consoles detected, managing them with details forced
    #   creating devices for (renderer type: 4294967295) (terminal: /dev/ttyAMA10)
    #   adding text display for terminal /dev/ttyAMA10
    #
    # 4294967295 is PLY_RENDERER_TYPE_NONE. plymouthd never opens /dev/dri, it
    # draws text, and no error says so: the panel keeps the console and the
    # image never comes.
    #
    # CAUTION: this must be a word of the kernel and not an argument of a unit
    # of systemd. The plymouthd of the start comes from the initramfs at
    # 00:00:01, which is before the root file system, thus /etc reaches it
    # never. plymouthd reads this word itself. It also covers the four units of
    # the stop, thus one word does the work of five files.
    #
    # The serial console stays. It is the one way in when the panel and the
    # network are both gone, and this only tells plymouthd to pass over it.
    if [ -f "$CMDLINE_TXT" ]; then
        for word in quiet splash logo.nologo vt.global_cursor_default=0 \
                    loglevel=3 plymouth.ignore-serial-consoles; do
            if set_cmdline_word "$CMDLINE_TXT" "$word"; then
                echo "[INFO] cmdline.txt: ${word}"
                REBOOT_NEEDED=1
            fi
        done
    fi
    echo "[INFO] The theme is at ${THEME_DEST}."
    echo "[INFO]"
    echo "[INFO] CAUTION: nobody has done this on the appliance. Plymouth takes"
    echo "[INFO] the DRM device, and the appliance has no keyboard to give it"
    echo "[INFO] back. If the panel keeps the image and the translator does not"
    echo "[INFO] come, do this THROUGH SSH:"
    echo "[INFO]     sudo systemctl stop plymouth-quit-wait.service"
    echo "[INFO]     sudo plymouth --quit"
    echo "[INFO]     sudo systemctl restart gemma-translator"
    echo "[INFO] If the panel stays with the image, take the word splash out of"
    echo "[INFO] ${CMDLINE_TXT} and start the machine again."
else
    echo "[INFO] The theme of Plymouth is not installed."
    echo "[INFO] Give --with-splash to install it."
fi

echo "[9/9] Registering systemd service..."
# [9/10] of the upstream script was a Chromium kiosk on http://localhost:3000.
# There is no browser on this appliance and nothing listens on that port: the
# user interface is the Avalonia software on the DRM backend, and the speech
# part is in that same process. Thus this script has nine steps.
SERVICE_FILE="/etc/systemd/system/gemma-translator.service"
TEMPLATE_FILE="${PROJECT_DIR}/deploy/gemma-translator.service"

if [ ! -f "$TEMPLATE_FILE" ]; then
    echo "[ERROR] Template file ${TEMPLATE_FILE} not found."
    exit 1
fi

sed -e "s|{{USER}}|${CURRENT_USER}|g" \
    -e "s|{{PROJECT_DIR}}|${PROJECT_DIR}|g" \
    -e "s|{{UID}}|${CURRENT_UID}|g" \
    "$TEMPLATE_FILE" | sudo tee "$SERVICE_FILE" > /dev/null
sudo chmod 644 "$SERVICE_FILE"

sudo systemctl daemon-reload
sudo systemctl enable gemma-translator.service

# The two conditions that the software cannot start without. Program.cs throws
# when the panel is not there, and it needs group video to open the node, which
# stays root:video by the decision in 99-gemma-translator.rules. Neither comes
# back on its own: a start into either one gives Restart=always a loop of five
# seconds that writes to the card and shows nothing on the display.
#
# The two buttons are NOT in this test. EvdevPushToTalk writes a line in the log
# and goes on when /dev/input/recorder-buttons is not there, thus the software
# operates with the touchscreen and no button. That is a machine that a person
# can use, and it is not a cause to hold the start.
CAN_START=1

if [ ! -e /dev/dri/appliance-panel ]; then
    echo "[WARNING] /dev/dri/appliance-panel is not there."
    echo "[WARNING] The panel comes from the rule in 99-gemma-translator.rules."
    CAN_START=0
fi

if ! id -nG "$CURRENT_USER" | tr ' ' '\n' | grep -qx video; then
    echo "[WARNING] ${CURRENT_USER} is not in group video, thus it cannot open"
    echo "[WARNING] the panel. Add it with: sudo gpasswd -a ${CURRENT_USER} video"
    CAN_START=0
fi

if [ "$CAN_START" = "0" ] || [ "$REBOOT_NEEDED" = "1" ]; then
    echo "[INFO] The unit is enabled and it is not started."
    echo "[INFO] This machine must start again first. See the note below."
    REBOOT_NEEDED=1
else
    sudo systemctl restart gemma-translator.service

    # CAUTION: Type=simple means that systemctl restart gives no opinion about
    # the software. It returns when the fork succeeds, thus a status here gives
    # "active" for a process that throws one moment later. This waits and then
    # asks again, because a deployment that says "complete" over a unit in a
    # loop is worse than one that stops.
    sleep 10
    if systemctl is-active --quiet gemma-translator.service; then
        echo "[INFO] The translator is running."
    else
        echo "[ERROR] The translator did not stay up. Read it with:"
        echo "[ERROR]   journalctl -u gemma-translator -b --no-pager | tail -40"
        exit 1
    fi
fi

echo "==========================================================="
if [ "$REBOOT_NEEDED" = "1" ]; then
    echo "Deployment complete. This machine must start again."
else
    echo "Deployment complete. The user interface draws on the panel."
fi
echo "==========================================================="

if [ "$REBOOT_NEEDED" = "1" ]; then
    echo
    echo "IMPORTANT: the translator is enabled and it is not started. The two"
    echo "IMPORTANT: buttons, the mains line and the panel do not operate"
    echo "IMPORTANT: before this machine starts again."
    echo
    echo "    sudo reboot"
    echo
    echo "Then make sure of these six:"
    echo "    systemctl is-active gemma-translator             # active"
    echo "    grep -c recorder-buttons /proc/bus/input/devices  # 1"
    echo "    ls -l /dev/input/recorder-buttons                 # the symlink"
    echo "    ls -l /dev/dri/appliance-panel                    # the symlink"
    echo "    pinctrl get 6,17,27                               # 6 is pd, 17 and 27 are pu"
    echo "    cat /sys/class/power_supply/mains/online          # 1 with mains"
    echo
    echo "The panel is the last proof and no command gives it. Look at it."
fi
