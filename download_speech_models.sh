#!/bin/bash
# Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
# SPDX-License-Identifier: Apache-2.0

set -e

cd "$(dirname "$0")"

if [ ! -f "venv/bin/activate" ]; then
    echo "Virtual environment not found. Please run ./setup.sh first."
    exit 1
fi
# shellcheck disable=SC1091
source venv/bin/activate

MODELS_CS="src/GemmaTranslator/Services/Speech/SpeechModels.cs"
CACHE_ROOT="${HOME}/.cache/moonshine_voice/download.moonshine.ai"

# The C# code calls libmoonshine.so directly and it only READS this cache.
# Nothing else fills it: upstream let backend/server.py pull each model on the
# first use through the Python package, and the port deleted that file. Thus a
# clean appliance has no speech at all, and the message is "the load of the
# transcriber gave error -1".
#
# CAUTION: the two tags below belong to the set of languages, which
# Languages.cs and SpeechModels.cs also hold. See section 9.1 of CLAUDE.md.
# The paths of the models are NOT here: the check at the end reads them from
# SpeechModels.cs, thus that file stays the one source of truth for them.
LANGUAGES="ar:ar_msa en:en_us es:es_es ja:ja_jp zh:zh_hans ko:ko_kr"

# The six models and the assets of the text-to-speech part measure about 1.4 GB.
REQUIRED_GB=3
AVAIL_GB="$(df -Pk "$HOME" | awk 'NR==2 { print int($4 / 1024 / 1024) }')"
if [ -n "$AVAIL_GB" ] && [ "$AVAIL_GB" -lt "$REQUIRED_GB" ]; then
    echo "Error: only ${AVAIL_GB}GB free on the filesystem holding $HOME."
    echo "At least ${REQUIRED_GB}GB is necessary for the models of the speech."
    exit 1
fi

echo "Downloading the Moonshine models of ${LANGUAGES// /, }..."
echo "The package gives each path and each architecture. Compare them with"
echo "the table of ${MODELS_CS}."

LANGUAGES="$LANGUAGES" python -u - <<'PY'
import os

from moonshine_voice.download import (
    download_g2p_assets,
    download_tts_assets,
    get_model_for_language,
)

for pair in os.environ["LANGUAGES"].split():
    stt, tts = pair.split(":")
    print(f"=== {stt} / {tts} ===", flush=True)
    path, arch = get_model_for_language(stt)
    print(f"  STT  path={path}  arch={arch}", flush=True)
    download_tts_assets(tts, show_progress=False)
    download_g2p_assets(tts, show_progress=False)
PY

# The paths come out of the C# table, thus a language that moves there and not
# here gives a failure at this step and not at the first exchange of a person.
echo
echo "Checking each directory that ${MODELS_CS} names..."
missing=0
while read -r model_dir; do
    if [ -d "${CACHE_ROOT}/${model_dir}" ] \
       && [ "$(find "${CACHE_ROOT}/${model_dir}" -maxdepth 1 -type f | wc -l)" -gt 0 ]; then
        echo "  OK      ${model_dir}"
    else
        echo "  MISSING ${model_dir}"
        missing=1
    fi
done < <(grep -oE '"model/[^"]+"' "$MODELS_CS" | tr -d '"' | sort -u)

if [ "$missing" -ne 0 ]; then
    echo
    echo "Error: a model that ${MODELS_CS} names is not in the cache."
    echo "The software gives Moonshine error -1 for that language."
    exit 1
fi

echo "  OK      tts ($(find "${CACHE_ROOT}/tts" -type f | wc -l) files)"
echo "The models of the speech are complete."
