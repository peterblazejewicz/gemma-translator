# CLAUDE.md

This document gives the rules for all work in this repository. Obey these rules.

Write all prose in ASD-STE100 Simplified Technical English (STE). See section 8.

---

## 1. What this repository is

This repository is a fork of `google-gemma/gemma-translator`. The upstream
project is a voice translator. It operates fully offline on a Raspberry Pi. It
uses Gemma for translation, LiteRT-LM for inference, and Moonshine for voice.

This fork has a different goal. See section 3.

| Remote | URL |
| --- | --- |
| `origin` | `https://github.com/peterblazejewicz/gemma-translator.git` |
| `upstream` | `https://github.com/google-gemma/gemma-translator.git` |

---

## 2. Branches and git rules

**CAUTION: DO NOT PUSH TO A REMOTE. THE USER PUSHES. IF YOU PUSH, YOU CAN SEND
UNWANTED CHANGES TO GITHUB.**

| Branch | Purpose | Rule |
| --- | --- | --- |
| `main` | A mirror of `upstream/main` | Use it only to get upstream changes. |
| `feat/dotnet-fork` | Our development branch | All our work merges back to here. |
| `feat/*` | One unit of work | Make it from `feat/dotnet-fork`. |

Obey these rules:

- Do not commit our changes to `main`.
- Make each new branch from `feat/dotnet-fork`, not from `main`.
- Merge each completed branch back to `feat/dotnet-fork`.
- Do not push to `origin` or to `upstream`. The user does this step.
- Make a commit only when the user tells you to make a commit.

---

## 3. The goal of the fork

1. Move the core of the software from Python to C# on .NET 10 LTS. A move to
   .NET 11 is possible afterwards.
2. Replace the React user interface with Avalonia UI.
3. Avalonia must show the user interface with the Linux DRM backend. There is no
   X11, no Wayland, no browser, and no Chromium kiosk.
4. Add new STL files. The new files make a case for the new hardware.
5. Keep the function of the upstream software: speech-to-text, translation,
   text-to-speech, and full offline operation.

The DRM backend of Avalonia is the correct one for a Raspberry Pi with a DSI
display and no desktop. Use the `avalonia-docs` server to get the current
Avalonia API before you write the startup code. See section 6.1.

**The change to C# removes our Python. It does not remove all Python.**
`litert-lm` is a pip package, and `start.sh:45` starts it as a CLI. Thus the
venv, `setup.sh`, and `download_model.sh` stay on the Raspberry Pi.

`deploy-pi.sh` must keep `python3-venv` and `python3-pip`. What leaves is
`moonshine-voice`, `numpy`, `sounddevice`, `soundfile`, and
`backend/server.py`.

### 3.1 The Avalonia constraints

These facts come from the `avalonia-docs` server, not from memory. They give
the structure of the C# code.

**IMPORTANT: this section needs Avalonia 12.x.** Avalonia 11.3 does not have
`DrmOutputOptions.Orientation`. The `avalonia-docs` server answers from 12.x.
Because of this, the user selected 12.x on 2026-08-08.

**The lifetime is different on the target.** The desktop software uses
`IClassicDesktopStyleApplicationLifetime` and makes windows. The Raspberry Pi
has no window manager. Avalonia uses `ISingleViewApplicationLifetime` there,
which gives one view that fills the display.

Put all the user interface in a `MainView` UserControl. Host that control in
`MainWindow` on the Windows development host, and in `MainSingleView` on the
Raspberry Pi. This is the mechanism that makes section 7 possible: the same
user interface operates on the two machines.

**The DRM startup.** Add the `Avalonia.LinuxFramebuffer` package. Then start
the software with `StartLinuxDrm`:

```csharp
return builder.StartLinuxDrm(args, card: null, options: new DrmOutputOptions
{
    Scaling = 1.0,
});
```

Notes:

- `card: null` lets Avalonia find the card. Give `/dev/dri/card1` to select
  one card.
- `DrmOutputOptions.Orientation` turns the output in software. Our display is
  native portrait, so `Rotation0` is possibly correct. Touch coordinates
  change with the orientation automatically.
- A console cursor blinks on top of the user interface. The documents give a
  `SilenceConsole` method that stops this.
- Touch operates automatically through `libinput` with DRM.
- `kmscube` makes sure that DRM operates before you start the software.
- `CompositionOptions.UseRegionDirtyRectClipping` is off by default from
  Avalonia 12.1. The documents name embedded Linux as a condition where
  `true` can help. Do a test on the Pi.
- The DRM backend makes no popup. Each flyout, tooltip, ComboBox dropdown, and
  context menu draws in the overlay layer, clipped to 720 × 1280. Make the
  settings screen and the language selector for this condition from the start.
- Supply the fonts with the software. Add static font files as
  `AvaloniaResource`, then make an `EmbeddedFontCollection`. Avalonia cannot
  use a variable font. Noto Sans CJK is usually a variable OTF, so use the
  static instances.

**Avalonia is not WPF.** Do not think that a WPF pattern operates. These are
the errors that occur most frequently:

| Do not use | Use |
| --- | --- |
| `.xaml` | `.axaml` |
| `DependencyProperty` | `StyledProperty` or `DirectProperty` |
| `Style.Triggers`, `DataTrigger` | Pseudo-classes such as `:pointerover` |
| `Style x:Key` | Style classes and selectors |
| `HierarchicalDataTemplate` | `TreeDataTemplate` |
| `pack://` URI | `avares://` URI |
| The `Visibility` enumeration | The `bool IsVisible` property |
| ReactiveUI | CommunityToolkit.Mvvm |
| `Avalonia.Diagnostics` | `AvaloniaUI.DiagnosticsSupport` |

`Directory.Build.props` sets `AvaloniaUseCompiledBindingsByDefault` to `true`.
Thus each AXAML root element must have an `x:DataType` attribute.

---

## 4. Target hardware

The target hardware is not available in the development phase. You cannot do a
test on the target device. Write the code so that it operates on the Windows
development host without the hardware.

The datasheets are in the `docs/` directory.

### 4.1 Computer

A Raspberry Pi 5 with 8 GB of RAM, the same as upstream. The system image is
Raspberry Pi OS Lite. The user confirmed the two items on 2026-08-08.

**CAUTION: PI OS LITE CAN HAVE NO CJK FONT AND NO ARABIC FONT. WITH NO FONT,
AVALONIA THROWS AT STARTUP. THE SOFTWARE MUST SUPPLY ITS OWN FONTS. SEE
SECTION 3.1.**

### 4.2 Display — Raspberry Pi Touch Display 2, 5 inch

| Property | Value |
| --- | --- |
| Resolution | 720 × 1280 pixels, native portrait |
| Display format | 24-bit RGB |
| Diagonal | 5.0 inches |
| Active area | 62.1 mm × 110.4 mm |
| Unit dimensions | 91.5 mm × 143.4 mm |
| Touch | Capacitive, five-finger multi-touch |
| Touch response time | 35 ms typical, 40 ms maximum |
| Brightness | 500 cd/m², anti-glare |
| Connections | DSI ribbon cable, and electrical supply from the GPIO header |
| Temperature range | −20 °C to +70 °C |

**IMPORTANT:** the display is native portrait at 720 × 1280. The upstream user
interface is landscape at 480 × 320. The layout of the two lanes must change.
Do not copy the upstream layout.

### 4.3 Audio — Jabra Speak2 40

| Property | Value |
| --- | --- |
| Connection | USB-C or USB-A, 80 cm cable |
| Microphones | 4 digital MEMS, beamforming |
| Microphone range | Maximum 2.3 m |
| Microphone frequency band | 150 Hz to 7000 Hz |
| Speaker | 50 mm, peak 83 dBspl at 0.5 m |
| Full duplex | Yes |
| Echo cancellation (AEC) | Yes, in the device |
| Level normalization (AGC) | Yes, for the audio that the device sends |
| Noise reduction | Yes |
| Dimensions and mass | 120 mm diameter × 33 mm, 245 g |
| Protection | IP64 |
| Temperature range | 0 °C to 40 °C |

Notes for the software:

- The Jabra Speak2 40 is one USB Audio Class device. It gives audio input and
  audio output together. Select it as one device, not as two devices.
- The device does echo cancellation, level normalization, and noise reduction
  in its own hardware. Do not add these functions in software.
- The microphone band stops at 7000 Hz. A capture rate of 16 kHz mono is
  sufficient for Moonshine.
- The upstream volume control calls `wpctl`, `pactl`, and `amixer`. Examine if
  the Jabra hardware buttons make this code not necessary.

**CAUTION: THE JABRA SPEAK2 40 OPERATES FROM 0 °C TO 40 °C. THIS IS THE NARROW
LIMIT OF THE SYSTEM. THE DISPLAY PERMITS −20 °C TO +70 °C. USE THE JABRA LIMIT
FOR THE CASE.**

### 4.4 Electrical supply

A powerbank or a dedicated battery supplies the system. The user did not select
the part. This item is to be understood (TBU). Speak to the user before you make
an assumption about the electrical supply.

### 4.5 Case

New STL files go in `stl/`. The case must hold the Raspberry Pi, the 5-inch
display, and the battery. The Jabra Speak2 40 is a different unit on a cable.

---

## 5. How we do the work

Opus makes the decisions and controls the work. Opus gives the work to
subagents.

| Model | Role |
| --- | --- |
| Opus | The primary driver. Opus decides and gives out the work. Opus does hard work. |
| Sonnet | Simple, mechanical, and repetitive work. |
| Fable | Answers to questions. Directions for hard topics. |

Obey these rules:

- Give the work to a maximum of 3 subagents at the same time.
- The limit of 3 does not apply to simple mechanical work.
- Give simple mechanical work to Sonnet.
- Give hard work to Opus.
- If a problem occurs, or if the topic is hard, write a clear question. Send the
  question to Fable. Fable gives an answer or a direction.
- Fable must not do the work. Fable must not be a subagent.

### 5.1 Adversarial review

An adversarial review is an inspection that tries to find the errors in the
work. The agent that does the inspection must try to break the result. The agent
must not try to agree with the result.

Do an adversarial review for these items:

- Work that is not simple.
- Each change from Python to C#.
- Each change from React to Avalonia.

These subagents do the inspection:

| Subagent | Use it for |
| --- | --- |
| `dotnet-skills:code-reviewer` | Correct operation. Readability. The architecture. The security. The speed. |
| `dotnet-skills:security-auditor` | The proxy, the static server, and the audio device access. |

### 5.2 No test project

This fork has no test project. Upstream has no tests, and this work only moves
the upstream code to C#. Keep it simple.

- Do not make a test project. Do not use the TDD cycle.
- To make sure that the code is correct, use `dotnet build`. Then operate the
  software.
- An interface is for a different platform, not for a fake. Windows and the
  Raspberry Pi get different code for the audio and for the inference. This is
  the cause for each interface in this fork.

### 5.3 The move removes as much as it adds

The C# code replaces the upstream code. It does not go on top of it. This
repository must not hold two sets of code that do the same work.

Obey these rules:

- When you move one function to C#, remove the upstream code of that same
  function in the same change. Thus the quantity of the code stays about
  equal.
- Do work in a vertical slice. A vertical slice is the full path of one
  function: the user interface, the client, and the part of the server.
- When a vertical slice is complete, remove the upstream part of that slice.
  The new C# part goes in its position.
- Do not keep upstream code to look at subsequently. Git holds the history, and
  `upstream/main` holds all the upstream software. You can get each file
  again.
- Do not remove upstream code before its C# replacement operates. A slice that
  is not complete must keep the upstream part, or the software stops.
- Tell the user which upstream code you removed, and where the new code is.

Example: the endpoint, the model, and the key are one small slice.
`LiteRtOptions` replaces the three text fields of `SettingsOverlay.jsx`, thus
the three text fields go away in the same change. The other fields of that file
stay until their C# replacement operates.

---

## 6. Skills and servers you must use

### 6.1 The Avalonia MCP server

**IMPORTANT: use the `avalonia-docs` server first for all Avalonia content.**

Query this server before you write Avalonia code, before you change Avalonia
code, and before you answer a question about Avalonia. Make sure of the syntax,
the language, and the functions of the framework. Do not write Avalonia code
from memory.

| Item | Value |
| --- | --- |
| Name | `avalonia-docs` |
| URL | `https://docs-mcp.avaloniaui.net/mcp` |
| Protocol | HTTP, remote |
| Licence | Free. No licence is necessary. |
| Configuration | `.mcp.json` at the repository root |

These are the 8 tools of the server:

| Tool | Use it for |
| --- | --- |
| `search_avalonia_docs` | To examine the full documents, the APIs, and the tutorials. |
| `lookup_avalonia_api` | One class, one property, one method, or one event. |
| `get_avalonia_expert_rules` | The Avalonia guidelines. |
| `migrate_diagnostics` | The Developer Tools package. |
| `analyze_wpf_project` | To examine a WPF project. |
| `migrate_to_xpf` | The XPF cross-platform steps. |
| `migrate_to_avalonia` | The steps to native Avalonia. |
| `lookup_wpf_to_avalonia_mapping` | The WPF-to-Avalonia tables. |

Use `context7` for .NET and for the other libraries. Use `avalonia-docs` for
Avalonia. If the two servers disagree, `avalonia-docs` is correct for Avalonia.

### 6.2 Skills

These skills are installed. Use them.

| Skill | Use it for |
| --- | --- |
| `dotnet-skills:spec` | A specification before the C# code. |
| `dotnet-skills:plan` | Small tasks that you can make sure of. |
| `dotnet-skills:build` | Small steps with `dotnet build`. |
| `dotnet-skills:review` | The five-axis inspection of C# changes. |
| `dotnet-skills:deprecation-and-migration` | The change from Python to C#. |
| `dotnet-skills:frontend-ui-engineering-avalonia` | Avalonia user interface work. |
| `avalonia-dev:review` | The Avalonia structure, the tokens, and the themes. |
| `asd-ste100-dictionary` | All prose. See section 8. |
| `context7` | Current documents for .NET and the other libraries. |

---

## 7. Development environment

| Item | Value |
| --- | --- |
| Development host | Windows 11 |
| Appliance target | Linux on ARM64 (Raspberry Pi OS Lite) |
| Second target | Windows x64. The software must fully operate here. |
| Not a target | macOS. The user confirmed this on 2026-08-08. |
| .NET SDK | 10.0.302 |

**IMPORTANT:** the upstream Python and bash stack does not operate on the
Windows host. `start.sh`, `deploy-pi.sh`, `os.getuid()`, `wpctl`, `pactl`,
`amixer`, and `lsof` are Linux only. Do not try to start the upstream software
on Windows.

The new C# code must operate on the Windows host. Keep the Linux-only parts
behind an interface, so that each platform can put in its own code. See
section 5.2.

The LiteRT-LM native library is not the same on the two platforms. Windows uses
`litert-lm.dll` from the `win_amd64` wheel. The Raspberry Pi uses
`liblitert-lm.so` from the `manylinux aarch64` wheel.

The software must operate fully on Windows, and there is no fake. Thus make
sure of the Windows native library as one of the first steps. If it does not
operate, tell the user immediately.

---

## 8. Language of the documents

Write all prose in ASD-STE100 Simplified Technical English. This applies to:

- Documents, README files, guides, and architecture decision records.
- Code comments and XML documentation comments.
- Commit messages and pull request text.
- Your replies to the user.

Do not apply STE to code, identifiers, command names, or log strings.

Obey this procedure:

1. Use the `asd-ste100-dictionary` skill before you write prose.
2. Write the text.
3. Do a check with the checker of the skill.
4. Correct each error. Use the lookup tool of the skill for each word that is
   not clear.
5. Do the check again.
6. Tell the user what the checker found and what you changed.

The two scripts come with the skill. This repository has no `scripts/`
directory. On this development host the scripts are here:

```bash
python ~/.claude/skills/asd-ste100-dictionary/scripts/ste_check.py CLAUDE.md
python ~/.claude/skills/asd-ste100-dictionary/scripts/lookup.py utilize
```

The command is `python`, not `python3`. The Windows host has no `python3`. The
skill documents show `python3`, which is correct on Linux only.

The checker gives exit code 1 if it finds an error. It reads the dictionary
from `~/.claude/ste/dictionary.json`. If you do not have that file, the skill
tells you how to make it.

Keep the technical names in `.ste-technical-names.txt` at the repository root.
The checker reads this file. Do not add a word only to remove a checker result.

Do not tell the user that a text is "STE-conformant". Tell the user that the
text obeys the rules and the vocabulary that the checker examines.

---

## 9. The upstream software

This section records what the C# code must replace.

| Part | File | Function |
| --- | --- | --- |
| User interface | `frontend/src/TranslatorApp.jsx` | The response area, the two lanes, and the visualizer. |
| One lane | `frontend/src/components/LanguageLane.jsx` | One person. The two lanes are adjacent to each other in a strip of 60 pixels, not two half screens. |
| API client | `frontend/src/utils/api.js` | The STT, translation, and TTS calls. |
| Audio capture | `frontend/src/hooks/useAudioRecorder.js` | 16 kHz mono Float32 capture. |
| Server | `backend/server.py` | Static files, `/proxy`, `/api/stt`, `/api/tts`, `/api/volume`. |
| Settings | `frontend/src/components/SettingsOverlay.jsx` | The endpoint, the model, the API key, and the volume. |
| Startup | `start.sh`, `deploy-pi.sh` | Three processes, and the systemd unit. |

`litert-lm serve` is not in this table. It is a Python CLI and it stays on the
device. See section 3.

The plan deletes `/api/volume` (`backend/server.py:263-381`), which
`SettingsOverlay.jsx:42,61` calls. It starts `wpctl`, `pactl`, and `amixer`,
which are Linux only, and Pi OS Lite has no PipeWire.

Use software gain in the C# software. This is cross-platform, and a test can
use a fake.

The three text fields of `SettingsOverlay.jsx` become `LiteRtOptions` in the
`LiteRt` section of `appsettings.json`. See section 3.2. The key is not in that
file. It comes from `GEMMA_LiteRt__ApiKey`, because the repository holds
`appsettings.json`.

The upstream `useProxy` value has no C# equivalent. The browser sent each
request through `/proxy` in `server.py` to keep the same origin. C# has no
browser and no same-origin rule, thus the software speaks to the endpoint
directly.

The flow is simple. The user holds a button. The software records the
microphone.

The software sends the audio to Moonshine. Gemma translates the text. Moonshine
speaks the result in the other language.

### 9.1 The set of languages

The set of languages is in four locations. A change must touch all four
locations. If you miss one location, the software falls back to English without
an error.

| Location | File |
| --- | --- |
| `AVAILABLE_LANGUAGES` | `frontend/src/TranslatorApp.jsx:34` |
| `SUPPORTED_STT_LANGS` | `backend/server.py:37` |
| `TTS_LANG_MAP` | `backend/server.py:47` |
| `TTS_VOICE_MAP` | `backend/server.py:57` |

The current languages are Arabic, English, Spanish, Japanese, Chinese, and
Korean. The C# code must hold this set in one location only.

---

## 10. Licence headers

Each source file in the upstream project has an Apache 2.0 header. This
repository has a `CODEOWNERS` file and a contributor licence agreement (CLA)
procedure.

Add the same header to each new source file. Copy the text from
`backend/server.py`, lines 1 to 13.
