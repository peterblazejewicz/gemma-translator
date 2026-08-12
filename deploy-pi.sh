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

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CURRENT_USER="$(whoami)"
CURRENT_UID="$(id -u)"

echo "==========================================================="
echo "Gemma Offline Translator — Pi Automated Deployment"
echo "Project Dir: ${PROJECT_DIR}"
echo "User: ${CURRENT_USER} (UID: ${CURRENT_UID})"
echo "==========================================================="

if [ "$(uname -s)" != "Linux" ]; then
    echo "[WARNING] This script is intended for Raspberry Pi OS / Debian Linux."
    echo "[WARNING] Current OS: $(uname -s)."
fi

echo "[1/10] Installing OS dependencies..."
if command -v apt-get &> /dev/null; then
    sudo apt-get update
    # device-tree-compiler makes the overlay of step 2. i2c-tools is for the
    # fuel gauge of the UPS at address 0x36.
    sudo apt-get install -y python3-venv python3-pip ffmpeg libasound2-dev pulseaudio-utils alsa-utils device-tree-compiler i2c-tools
else
    echo "[INFO] apt-get not detected. Skipping Debian package installation."
fi

# Node.js/npm preflight — the frontend build (step 3) requires Node 18+ (Vite 5).
# Debian/Raspberry Pi OS Bookworm ships Node 18 via apt, which satisfies this.
if ! command -v npm &> /dev/null; then
    if command -v apt-get &> /dev/null; then
        echo "[INFO] Node.js/npm not found. Installing via apt..."
        sudo apt-get install -y nodejs npm
    else
        echo "[ERROR] npm is required but not installed, and apt-get is unavailable."
        echo "[ERROR] Install Node.js 18+ (https://nodejs.org/) and re-run this script."
        exit 1
    fi
fi
NODE_MAJOR="$(node -v 2>/dev/null | sed 's/^v//' | cut -d. -f1)"
if [ -n "$NODE_MAJOR" ] && [ "$NODE_MAJOR" -lt 18 ] 2>/dev/null; then
    echo "[WARNING] Node.js v${NODE_MAJOR} detected, but v18+ is required by the frontend build."
    echo "[WARNING] Step 3 (npm build) may fail. Please upgrade Node.js first."
fi

echo "[2/10] Installing the GPIO overlay and the udev rule..."
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

if [ -f "$UDEV_TEMPLATE" ]; then
    sed -e "s|{{USER}}|${CURRENT_USER}|g" "$UDEV_TEMPLATE" | sudo tee "$UDEV_FILE" > /dev/null
    sudo chmod 644 "$UDEV_FILE"
    sudo udevadm control --reload
    sudo udevadm trigger --subsystem-match=input
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

echo "[3/10] Running backend environment setup..."
"${PROJECT_DIR}/setup.sh"

echo "[4/10] Installing frontend dependencies & building production UI..."
npm --prefix "${PROJECT_DIR}/frontend" install
npm --prefix "${PROJECT_DIR}/frontend" run build

echo "[5/10] Downloading LiteRT model..."
"${PROJECT_DIR}/download_model.sh"

echo "[6/10] Installing the low battery guard..."
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

if [ -f "$GUARD_SRC" ] && [ -f "$GUARD_UNIT_SRC" ]; then
    sudo install -m 755 "$GUARD_SRC" "$GUARD_DEST"
    sudo install -m 644 "$GUARD_UNIT_SRC" "$GUARD_UNIT_DEST"
    sudo systemctl daemon-reload
    sudo systemctl enable gemma-battery-guard.service
    sudo systemctl restart gemma-battery-guard.service
    echo "[INFO] The low battery guard is at ${GUARD_DEST}."
    echo "[INFO] Read it with: journalctl -u gemma-battery-guard -n 20"
else
    echo "[ERROR] deploy/ has no low battery guard. The appliance would have"
    echo "[ERROR] no protection against cells that go empty, thus this"
    echo "[ERROR] installation stops here."
    exit 1
fi

echo "[7/10] Setting the swap of the appliance to zram with no file..."
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
    printf '%s
'         '# Gemma Translator appliance.'         '#'         '# zram only, with no file. The default is zram+file, which writes cold'         '# pages to /var/swap on the SD card. A page can hold the speech of a'         '# person, and a page on the card stays after the machine stops.'         '[Main]'         'Mechanism=zram'         | sudo tee "$SWAP_CONF" > /dev/null
    sudo chmod 644 "$SWAP_CONF"
    sudo systemctl daemon-reload

    # The generator makes the unit again at a daemon-reload, but a zram device
    # that is in operation keeps the file that it has. The machine must start
    # again to remove it.
    if [ "$(cat /sys/block/zram0/backing_dev 2>/dev/null)" != "none" ]; then
        REBOOT_NEEDED=1
    fi

    echo "[INFO] swap: ${SWAP_CONF}"
else
    echo "[INFO] This machine has no /etc/rpi, thus it does not use rpi-swap."
    echo "[INFO] Examine the swap yourself: a file on the card can keep speech."
fi

echo "[8/10] Registering systemd service..."
SERVICE_FILE="/etc/systemd/system/gemma-translator.service"
TEMPLATE_FILE="${PROJECT_DIR}/deploy/gemma-translator.service"

if [ -f "$TEMPLATE_FILE" ]; then
    sed -e "s|{{USER}}|${CURRENT_USER}|g" \
        -e "s|{{PROJECT_DIR}}|${PROJECT_DIR}|g" \
        -e "s|{{UID}}|${CURRENT_UID}|g" \
        "$TEMPLATE_FILE" | sudo tee "$SERVICE_FILE" > /dev/null
    sudo chmod 644 "$SERVICE_FILE"

    sudo systemctl daemon-reload
    sudo systemctl enable gemma-translator.service
    sudo systemctl restart gemma-translator.service

    echo "[9/10] Configuring GUI kiosk autostart..."
    LXSESSION_DIR="/home/${CURRENT_USER}/.config/lxsession/rpd-x"
    AUTOSTART_FILE="${LXSESSION_DIR}/autostart"
    if [ -d "$LXSESSION_DIR" ] || [ -f "/etc/xdg/lxsession/rpd-x/autostart" ]; then
        mkdir -p "$LXSESSION_DIR"
        if [ -f "/etc/xdg/lxsession/rpd-x/autostart" ] && [ ! -f "$AUTOSTART_FILE" ]; then
            cp /etc/xdg/lxsession/rpd-x/autostart "$AUTOSTART_FILE"
        fi
        sed -i '/@chromium/d' "$AUTOSTART_FILE" 2>/dev/null || true
        echo '@chromium --password-store=basic --kiosk --noerrdialogs --disable-infobars --disable-session-crashed-bubble --disable-features=TranslateUI --check-for-update-interval=31536000 --remote-debugging-port=9222 --remote-allow-origins=* --ozone-platform=x11 --use-fake-ui-for-media-stream --autoplay-policy=no-user-gesture-required --allow-insecure-localhost http://localhost:3000' >> "$AUTOSTART_FILE"
        echo "[INFO] Kiosk autostart configured in ${AUTOSTART_FILE} -> http://localhost:3000"
    else
        echo "[INFO] LXDE rpd-x session not detected. Skipping LXDE autostart config."
    fi

    echo "[10/10] Systemd service configured and started."
    systemctl status --no-pager gemma-translator.service || true
else
    echo "[ERROR] Template file ${TEMPLATE_FILE} not found."
    exit 1
fi

echo "==========================================================="
echo "Deployment complete! Appliance running at http://localhost:3000"
echo "==========================================================="

if [ "$REBOOT_NEEDED" = "1" ]; then
    echo
    echo "IMPORTANT: the GPIO overlay changed. The two buttons and the mains"
    echo "IMPORTANT: line do not operate before this machine starts again."
    echo
    echo "    sudo reboot"
    echo
    echo "Then make sure of these four:"
    echo "    grep -c recorder-buttons /proc/bus/input/devices  # 1"
    echo "    ls -l /dev/input/recorder-buttons                 # the symlink"
    echo "    pinctrl get 6,17,27                               # 6 is pd, 17 and 27 are pu"
    echo "    cat /sys/class/power_supply/mains/online          # 1 with mains"
fi
