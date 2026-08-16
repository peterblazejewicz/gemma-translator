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
# modified. The Vite server of the user interface went away, and the Avalonia
# software came in.

# The startup of the appliance. It starts two processes:
#
#   litert-lm            the model, which speaks the OpenAI protocol
#   publish/GemmaTranslator --drm   the user interface, on the panel
#
# The Vite dev server is gone with frontend/. The user interface is the
# Avalonia software now, and it needs no browser and no web server.
#
# backend/server.py is gone also. The speech-to-text part and the
# text-to-speech part call the Moonshine library in the process of the user
# interface, thus there is no third process and nothing listens on port 3000.
#
# --prod and -p do nothing. The systemd unit gives --prod, and this script
# takes it so that an old unit does not stop the appliance.

set -e

for arg in "$@"; do
    case "$arg" in
        --prod|-p) ;;
        *) echo "[start.sh] WARNING: ${arg} is not a known argument." ;;
    esac
done

export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"

# Unbuffered Python stdout so litert-lm logs reach journald immediately
# (block-buffered prints previously hid request logs and hampered debugging)
export PYTHONUNBUFFERED=1

# Kill existing processes only if NOT running under systemd
# (systemd handles process lifecycle; killing ports here causes crash loops)
if [ -z "$INVOCATION_ID" ]; then
    lsof -ti:9379 | xargs kill -9 2>/dev/null || true
fi

LITERT_PORT=9379
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# SECURITY CONTROL. Do not remove --host 127.0.0.1.
#
# litert-lm serve listens on 0.0.0.0:9379 by default. This appliance sits on a
# network, so without this flag any machine that can reach it can send prompts
# to the model and read the answers, with no authentication. LiteRtOptions-
# Validator restricts the client the same way; this is the server half of it.
LITERT_CMD="${PROJECT_DIR}/venv/bin/litert-lm serve --host 127.0.0.1"

# The Moonshine library is in the venv, because it comes in a wheel of Python.
# The venv itself gives the directory: the name holds the version of Python and
# this script must not build that path. With no value the software looks for
# the package on its own, and this makes it look for nothing.
if [ -z "${GEMMA_Speech__LibraryDirectory:-}" ]; then
    MOONSHINE_DIR="$("${PROJECT_DIR}/venv/bin/python3" -c \
        'import os, moonshine_voice; print(os.path.dirname(moonshine_voice.__file__))' \
        2>/dev/null || true)"

    if [ -n "$MOONSHINE_DIR" ]; then
        export GEMMA_Speech__LibraryDirectory="$MOONSHINE_DIR"
        echo "[start.sh] Moonshine: ${MOONSHINE_DIR}"
    else
        echo "[start.sh] The venv does not hold moonshine_voice. The software will look for it."
    fi
fi

# The user interface is the Avalonia software. --drm makes it draw on the panel
# with no window manager and no browser. deploy-pi.sh puts the files here.
UI_CMD="${PROJECT_DIR}/publish/GemmaTranslator --drm"

CLEANING_UP=0
cleanup() {
    [ "$CLEANING_UP" -eq 1 ] && return
    CLEANING_UP=1
    echo "[start.sh] Shutting down..."
    # Kill only tracked child processes, not the entire process group
    for pid in $LITERT_PID $UI_PID; do
        [ -n "$pid" ] && kill "$pid" 2>/dev/null || true
    done
    wait 2>/dev/null || true
    echo "[start.sh] All processes stopped."
}
trap cleanup EXIT TERM INT

# The user interface must be there before anything starts. A missing binary
# gives a black panel with no cause on it, and the appliance has no console.
if [ ! -x "${PROJECT_DIR}/publish/GemmaTranslator" ]; then
    echo "[start.sh] ERROR: ${PROJECT_DIR}/publish/GemmaTranslator is not there."
    echo "[start.sh] Run deploy-pi.sh, or publish it by hand:"
    echo "[start.sh]   dotnet publish src/GemmaTranslator -c Release -r linux-arm64 -o publish"
    exit 1
fi

# MEASURED: nothing here sets the level of the sound. The host control stays
# where it is while the Speak2 40 moves its own level in the device, thus a
# call to wpctl or amixer changes nothing that a person hears. Do not add a
# gain in software either: two controls that a person can move, and that do not
# agree, are worse than the one on the speakerphone. See section 8.20 of
# deploy/README.md.

# Start litert-lm in background
echo "[start.sh] Starting litert-lm..."
$LITERT_CMD &
LITERT_PID=$!

# Wait for litert-lm to be ready (max 60s). The probe gives the address and not
# the name "localhost", which also holds ::1: the server binds 127.0.0.1 only,
# thus a probe that reaches ::1 finds nothing there.
echo "[start.sh] Waiting for litert-lm on port ${LITERT_PORT}..."
LITERT_READY=0
for i in $(seq 1 60); do
    if nc -z 127.0.0.1 "${LITERT_PORT}" 2>/dev/null; then
        echo "[start.sh] litert-lm ready after ${i}s."
        LITERT_READY=1
        break
    fi
    # Check if process died
    if ! kill -0 $LITERT_PID 2>/dev/null; then
        echo "[start.sh] litert-lm process died. Exiting."
        exit 1
    fi
    sleep 1
done

# Without this, 60 seconds with no port gives the same lines as a good start,
# minus one, and the user interface comes on the panel with a translation that
# can never answer. A person then reads a defect of the translator.
if [ "$LITERT_READY" -eq 0 ]; then
    echo "[start.sh] ERROR: litert-lm did not open ${LITERT_PORT} in 60s."
    exit 1
fi

# Start the user interface on the panel.
echo "[start.sh] Starting the user interface..."
$UI_CMD &
UI_PID=$!

echo "[start.sh] All services running."
echo "[start.sh] LiteRT-LM PID: $LITERT_PID"
echo "[start.sh] User interface PID: $UI_PID"

# Wait for any child to exit (then the trap will clean up the others)
wait -n
echo "[start.sh] A child process exited. Shutting down."
exit 1
