#!/bin/bash
# Copyright 2026 Google LLC
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

set -e

cd "$(dirname "$0")"

echo "Creating virtual environment..."
python3 -m venv venv

echo "Activating virtual environment..."
source venv/bin/activate

echo "Installing requirements..."
# SECURITY CONTROL. --require-hashes is the control. Do not remove it.
#
# It refuses any package whose bytes do not match a hash in
# backend/requirements.txt, and it fails closed: one bad hash and pip installs
# nothing at all. This appliance holds the speech of members of the public, and
# the wheels below carry the library that hears them.
#
# The other three restrict pip to PyPI, which is the one index the hashes are
# of. They keep the control usable rather than provide it: --isolated drops the
# PIP_* variables of the environment, PIP_CONFIG_FILE=/dev/null drops
# /etc/pip.conf, and --index-url names the index. Without all three pip can
# take a wheel from the piwheels mirror that Raspberry Pi OS configures, which
# rebuilds packages, and then every hash fails in a way that reads like an
# attack and is not. Section 13 of deploy/README.md gives the mechanism.
PIP_CONFIG_FILE=/dev/null pip install --isolated --require-hashes \
    --index-url https://pypi.org/simple/ -r backend/requirements.txt

echo "========================================="
echo "Setup complete!"
echo "Run ./download_model.sh to download the model."
echo "Run ./start.sh to start the appliance."
echo "========================================="
