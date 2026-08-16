<img width="960" height="540" src="https://storage.googleapis.com/experiments-uploads/gemma-translator/gemma-translator.gif" />

# Gemma Translator

This repo was built with the assistance of [Google Antigravity](https://antigravity.google/) and includes code to run an on-device, fully offline voice translator powered by [Gemma 4](https://ai.google.dev/gemma/docs/core) and [LiteRT-LM](https://github.com/google-ai-edge/LiteRT-lm). This fork replaces the web frontend with an Avalonia user interface on .NET, which draws on the panel of the appliance with no browser and no window manager. The speech-to-text part and the text-to-speech part call the Moonshine library in that same process, thus there is no API server. Text-to-speech is powered by [Moonshine](https://github.com/moonshine-ai/moonshine).

https://github.com/user-attachments/assets/343072ce-dc78-44a7-a783-99312845cabe

## Features

- **On-Device Inference**: Uses LiteRT-LM to run the `gemma4-e2b` model entirely locally. No internet required after setup.
- **Voice Interface**: Captures microphone audio, processes it, and sends it to the local model.
- **No browser and no window manager**: the Avalonia user interface draws on the panel through the DRM backend of Linux.
- **Unified Startup**: One script starts the model server and the user interface. The speech is in the process of the user interface.

## Prerequisites

The appliance needs nothing on it before the deployment. `deploy-pi.sh`
installs each item below.

- Raspberry Pi OS Lite, 64-bit, Debian 13 (trixie)
- Python 3.13, which that image gives
- The .NET SDK 10 for `linux-arm64`, which step 1 installs from the package feed of Microsoft
- A network, for the deployment only. The appliance operates offline afterwards.

The development host is Windows 11 with the .NET SDK 10. macOS is not a target,
and `setup.sh`, `start.sh` and `deploy-pi.sh` are for Linux.

## Required Hardware

`deploy/README.md` gives each measured property and the document that it comes
from.

- **Compute**: Raspberry Pi 5 with 16 GB RAM
- **Display**: Raspberry Pi Touch Display 2, 5 inch. 720 × 1280 native portrait, and the appliance operates in landscape.
- **Audio**: Jabra Speak2 40. One USB Audio Class device that gives the microphone and the speaker together.
- **Electrical supply**: Geekworm X1201 UPS with two 18650 cells
- **Buttons**: two push-to-talk buttons on GPIO17 and GPIO27

<img width="3024" height="1672" src="https://storage.googleapis.com/experiments-uploads/gemma-translator/gemma-translator-cad.gif" />

## Deployment on the Raspberry Pi

This is the one path. `deploy-pi.sh` is safe to run again: the steps that
change the machine — the overlay, `config.txt`, `cmdline.txt`, the udev rules,
the swap — test for their own work first and change nothing that is already
correct. The steps that only make files — the packages, the venv, the publish,
the guard, the unit — do their work each time.

1. **Flash Raspberry Pi OS Lite**, 64-bit. Give the machine a network and an
   account, and connect to it.

2. **Get the software.**
   ```bash
   mkdir -p ~/develop && cd ~/develop
   git clone -b feat/dotnet-fork https://github.com/peterblazejewicz/gemma-translator.git
   cd gemma-translator
   ```

3. **Run the deployment.** It takes some tens of minutes: the model alone is
   about 2.6 GB, and it is kept two times while it goes in.
   ```bash
   ./deploy-pi.sh
   ```

4. **Start the machine again**, which the script asks for when the overlay or
   `cmdline.txt` changed. The two buttons and the panel do not operate before
   it does, and the unit is enabled and not started until then.
   ```bash
   sudo reboot
   ```

The appliance now comes up on its own at each start.

What `deploy-pi.sh` installs, in nine steps:

| Step | What it does |
| --- | --- |
| 1 | The packages of Debian, and the .NET SDK 10 from the feed of Microsoft |
| 2 | The device tree overlay of the two buttons and of the mains line, the udev rules, and the mode of the panel in `cmdline.txt` |
| 3 | The Python venv, which carries `litert-lm` and the Moonshine library |
| 4 | The publish of the Avalonia user interface for `linux-arm64` |
| 5 | The `gemma4-e2b` model, from Hugging Face into LiteRT-LM |
| 6 | The low battery guard, which stops the machine before the cells go empty |
| 7 | The swap, as zram with no file, so that no page of speech reaches the card |
| 8 | The images of the start and the stop, with `--with-splash` only |
| 9 | The systemd unit of the appliance |

Give `--with-splash` to add the theme of Plymouth. It is not there by default:
Plymouth takes the DRM device, thus a theme that does not give it back keeps
the panel and the software never comes on the display. Nobody has done that
step on the appliance. Section 11 of `deploy/README.md` gives the steps to make
sure of it, and the script prints the way back.

Each step but 3, 4 and 5 needs `sudo`. Section 12 of `deploy/README.md` gives
the limits that the unit puts on the software, and why each one is there, and
section 13 gives the pins of Python and their hashes.

Step 9 starts the appliance only when it can operate: the panel must be at
`/dev/dri/appliance-panel` and the account must be in group `video`. Without
either, the unit goes in and stays stopped, with the cause on the display,
because a unit that cannot start writes to the card every five seconds and
shows nothing.

### About the disk

**Give the card 16 GB or more.** The deployment needs about 7 GB free, and the
appliance then takes about 8 GB after a person presses a button one time:

| Item | Size | When |
| --- | --- | --- |
| The .NET SDK | about 650 MB | Step 1 |
| The venv | about 370 MB | Step 3 |
| The cache of NuGet | some hundreds of MB | Step 4 |
| The model in the cache of Hugging Face | about 2.6 GB | Step 5 |
| The model in LiteRT-LM | about 2.6 GB | Step 5 |
| The models of Moonshine | about 1.4 GB | The first press of a button |

The two copies of the model are both kept: `download_model.sh` gets the file
from Hugging Face and then imports it, and it makes sure of 6 GB before it
starts. The cache of Hugging Face can go after the import, and it is the way
back if the import must run again.

## Running it by hand

`start.sh` starts the model server and then the user interface, and it stops
both when either one stops. The systemd unit calls this same script.

```bash
./start.sh
```

`--prod` and `-p` do nothing. The systemd unit gives `--prod`, and `start.sh`
takes it so that an old unit does not stop the appliance.

The user interface draws on the panel of the appliance. It is not a page, thus
there is no address for it. One server operates, and it takes connections from
this machine only:

- **LiteRT-LM**: `http://127.0.0.1:9379`

To read what the appliance does:

```bash
systemctl status gemma-translator
journalctl -u gemma-translator -b
journalctl -u gemma-battery-guard -n 20
```

## Project Structure

- `src/GemmaTranslator/` - the Avalonia user interface on .NET 10. The speech-to-text part and the text-to-speech part are in it.
- `backend/` - `requirements.txt` only. It pins `litert-lm`, and `moonshine-voice` for the native speech library and the models that the C# software loads. Each pin carries the hash of its file on PyPI.
- `deploy/` - the systemd units, the udev rules, the device tree overlay, the low battery guard, the theme of Plymouth, and `README.md`, which gives each measured property of the hardware.
- `stl/` - STL files for 3D printing the hardware case.
- `cad/` - the OpenSCAD source of the enclosure of this fork.
- `setup.sh` - makes the Python venv and installs the pins with their hashes.
- `download_model.sh` - Fetches the required LiteRT model.
- `start.sh` - starts the model server and the user interface.
- `deploy-pi.sh` - One-command Raspberry Pi automated deployment script.

## How a person operates it

The appliance has two lanes, one for each person, and no keyboard. There are
two ways to give it a command.

**The two buttons.** One button belongs to one lane. A person holds the button
of their lane, speaks, and releases it. The software then transcribes the
speech, translates it, and speaks the result in the language of the other lane.
A press that is more short than `Audio:MinimumPressMilliseconds` gives nothing,
and a press that goes past `Audio:MaximumRecordingSeconds` stops on its own.

The overlay of the kernel gives the two buttons the codes `KEY_F13` and
`KEY_F14`, and the software reads them at `/dev/input/recorder-buttons`.

**The touchscreen.** Each lane has a drum of languages that a person turns, and
the display has a button for the settings. The languages are Arabic, English,
Spanish, Japanese, Chinese, and Korean.

On the Windows development host there are no GPIO buttons, thus `Z` records for
lane 1 and `X` records for lane 2. `F13` and `F14` operate there also, which is
what a programmable keyboard sends.

### Credits
Made by a small team at [Google Creative Lab](https://github.com/googlecreativelab):
- [Alan Yam](https://github.com/alanvww)
- [Shashwath Santosh](https://x.com/shashwth)
- [Dan Motzenbecker](https://github.com/dmotz)

## Disclaimer

This is not an officially supported Google product. This project is not
eligible for the [Google Open Source Software Vulnerability Rewards
Program](https://bughunters.google.com/open-source-security).
