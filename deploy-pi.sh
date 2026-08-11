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

echo "[1/8] Installing OS dependencies..."
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

echo "[2/8] Installing the GPIO overlay and the udev rule..."
# The two push-to-talk buttons on GPIO17 and GPIO27, and the mains line of the
# X1201 UPS on GPIO6. See deploy/recorder-keys-overlay.dts.
BOOT_DIR="/boot/firmware"
OVERLAY_SRC="${PROJECT_DIR}/deploy/recorder-keys-overlay.dts"
UDEV_TEMPLATE="${PROJECT_DIR}/deploy/99-gemma-translator.rules"
UDEV_FILE="/etc/udev/rules.d/99-gemma-translator.rules"
REBOOT_NEEDED=0

if [ -f "$OVERLAY_SRC" ] && [ -d "${BOOT_DIR}/overlays" ]; then
    dtc -@ -I dts -O dtb -o /tmp/recorder-keys.dtbo "$OVERLAY_SRC"

    # Install the overlay only if it changed. The value tells the user if a
    # restart is necessary, and a restart of this appliance is not free.
    if ! sudo cmp -s /tmp/recorder-keys.dtbo "${BOOT_DIR}/overlays/recorder-keys.dtbo"; then
        sudo install -m 644 /tmp/recorder-keys.dtbo "${BOOT_DIR}/overlays/recorder-keys.dtbo"
        echo "[INFO] Installed ${BOOT_DIR}/overlays/recorder-keys.dtbo"
        REBOOT_NEEDED=1
    else
        echo "[INFO] recorder-keys.dtbo is current."
    fi
    rm -f /tmp/recorder-keys.dtbo

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
else
    echo "[INFO] ${BOOT_DIR}/overlays not found. Skipping the GPIO overlay."
fi

if [ -f "$UDEV_TEMPLATE" ]; then
    sed -e "s|{{USER}}|${CURRENT_USER}|g" "$UDEV_TEMPLATE" | sudo tee "$UDEV_FILE" > /dev/null
    sudo chmod 644 "$UDEV_FILE"
    sudo udevadm control --reload
    sudo udevadm trigger --subsystem-match=input
    echo "[INFO] udev rule: ${UDEV_FILE}"
fi

echo "[3/8] Running backend environment setup..."
"${PROJECT_DIR}/setup.sh"

echo "[4/8] Installing frontend dependencies & building production UI..."
npm --prefix "${PROJECT_DIR}/frontend" install
npm --prefix "${PROJECT_DIR}/frontend" run build

echo "[5/8] Downloading LiteRT model..."
"${PROJECT_DIR}/download_model.sh"

echo "[6/8] Registering systemd service..."
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

    echo "[7/8] Configuring GUI kiosk autostart..."
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

    echo "[8/8] Systemd service configured and started."
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
