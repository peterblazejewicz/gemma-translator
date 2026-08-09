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
- `DrmOutputOptions.Orientation` turns the output in software. The panel is
  native portrait and the appliance operates in landscape, thus the value is
  `Rotation90`. Touch coordinates change with the orientation automatically.
  See section 4.2.
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

### 3.2 The code patterns

These are rules, not a selection. The user confirmed them on 2026-08-08.

| Item | Rule |
| --- | --- |
| The pattern | MVVM. Each view gets its data from a view model. |
| The MVVM library | `CommunityToolkit.Mvvm`. Do not use ReactiveUI. |
| Dependency injection | `Microsoft.Extensions.DependencyInjection`. |
| The settings | `IConfiguration`, from `appsettings.json` and the environment. |
| The log | `ILogger<T>` from the container. |

Obey these rules:

- Do not make a service or a view model with `new` in a view. Get it from the
  container.
- Add each service in `ServiceRegistration.cs`. This is the one location.
- Give each dependency to the constructor.
- Use `ObservableObject` and the `[ObservableProperty]` attribute for a
  property that the display shows. Use `[RelayCommand]` for an operation.
- Do not put logic in the code behind an AXAML file. It goes in the view
  model.
- The software uses the container only. It does not use the generic host,
  because Avalonia has its own lifetime.

An interface goes in front of each part that is not the same on Windows and on
the Raspberry Pi. See section 5.2.

Obey these rules for the settings and for the log:

- Do not read a value with `Environment.GetEnvironmentVariable` or from a file
  that you open. Get `IConfiguration` from the container.
- A variable of the environment has the prefix `GEMMA_`, and two low lines for
  a level, for example `GEMMA_Logging__LogLevel__Default`. The systemd unit
  changes a value with this method and touches no file.
- `appsettings.json` is the location of the settings of the appliance. The
  display has no keyboard, thus a person cannot type a value. See section 4.2.
- Do not use `Console.WriteLine`. Get `ILogger<T>` from the container.
- Write each log message with the `[LoggerMessage]` attribute. It makes the
  code, it makes no garbage, and it does no work if the level is off.
- The console output goes to the journal of systemd on the Raspberry Pi. On
  Windows a WinExe has no console, thus the debug output is the one that you
  see in the IDE.

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

**IMPORTANT: the panel is native portrait, and the appliance operates in
landscape.** The user made this decision on 2026-08-08.

| Item | Value |
| --- | --- |
| The panel | 720 × 1280, native portrait. The datasheet, page 3. |
| Upstream | 480 × 320, landscape. `style.css:68` says "hardcoded 480x320 landscape". |
| The appliance | 1280 × 720, landscape. `SurfaceOrientation.Rotation90`. |

Avalonia turns the output in software with an offscreen framebuffer and a
shader. It adjusts the touch coordinates automatically.

The correct value is `Rotation90` or `Rotation270`. Which one is correct
depends on the side that the DSI cable goes out, and you cannot know it before
the hardware is here. If the display shows the user interface upside down,
change the one value in `Program.cs`.

Landscape keeps the upstream proportions, thus the layout is a move and not new
work. The upstream heights of the 320 pixels, and the heights here:

- The response area: 232 upstream, the remainder here, 72.5 % of the height.
- The two lanes: 60 upstream, 135 here, 18.75 % of the height.
- The visualizer: 28 upstream, 63 here, 8.75 % of the height.

**CAUTION: THE CASE MUST HOLD THE PANEL ON ITS SIDE, WITH THE LONG EDGE
HORIZONTAL. THE STL FILES IN `stl/` MUST AGREE WITH THIS DECISION.**

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
| `cupid-reviewer` | The five CUPID properties. **Use it in each adversarial review.** |
| `dotnet-skills:code-reviewer` | Correct operation. Readability. The architecture. The security. The speed. |
| `dotnet-skills:security-auditor` | The proxy, the static server, and the audio device access. |

#### The CUPID framework

The user selected the CUPID framework on 2026-08-09. It comes from Daniel
Terhorst-North, and the documents are at `https://cupid.dev/`. The five properties
are Composable, Unix philosophy, Predictable, Idiomatic, and Domain-based.

`cupid-reviewer` is a permanent role in each adversarial review. Its definition
is in `.claude/agents/cupid-reviewer.md`.

**IMPORTANT: a CUPID property is a centred set, and it is not a rule.** A rule
gives compliance or no compliance. A property gives a direction of travel. Thus
a result must give the direction and the first step, and not a judgement.

Two properties do not agree with the conditions of this fork. Do not correct
this without a decision from the user:

- **Predictable** usually gets its strength from tests. Section 5.2 permits no
  test project. Thus clarity and the log do this work.
- **Domain-based** prefers a directory tree of the domain. The `Services`,
  `ViewModels`, and `Views` tree is a tree of technical types, which each .NET
  developer expects. The idiom and the domain fight here.

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

**IMPORTANT: the rule of this section changed on 2026-08-09. The rule before it said
to copy the Google header to each new file. That rule was incorrect.** A file
that you write is not the work of Google, and a Google line alone on it says
that Google owns it.

This fork stays under the Apache License, Version 2.0. Section 4(c) of that
licence says to keep each copyright notice of the upstream work in a derivative
work. It does not say to add the notice of a different person to your work.

The user selected one header for each file on 2026-08-09:

```csharp
// Copyright 2026 Google LLC
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
//
// ... the full Apache 2.0 text, 11 lines ...
//
// This file is part of a fork of google-gemma/gemma-translator and has
// been modified.
```

The two lines go on each file, also on a file that has no upstream equivalent.
The fork is a derivative work in full. This gives a small quantity of more
attribution on some files, and it removes each judgement about one file. It
does not say that your work is the work of Google.

An `.axaml` file gets the same text in an XML comment before the root element.

If a file is a port of one upstream file, add the location to the text, for
example "It replaces translateText of frontend/src/utils/api.js".

Related files:

| File | Function |
| --- | --- |
| `LICENSE` | Apache 2.0. Do not change it. |
| `NOTICE` | The origin of the fork, what changed, and the third-party parts. |
| `CODEOWNERS` | The owners of this fork, and not the upstream maintainers. |
| `CONTRIBUTING.md` | The procedure of this fork. The Google CLA does not apply. |

---

## 11. Comments

**IMPORTANT: the upstream code has few comments. Do not make the ported code
more dense.** Section 5.3 says that the move removes as much as it adds. A
block of comments on each line breaks that rule, and it makes the repository
read in two voices.

Use the CUPID properties to make the decision. See section 5.1.

**Remove a comment that:**

- Tells .NET, C#, or Avalonia to a person who writes .NET. That person knows
  the ecosystem. A comment that explains `AddHttpClient` or `[ObservableProperty]`
  is not idiomatic, because it does not trust its reader.
- Says again what the line below it says. This makes a second source of truth,
  and the two then disagree. A comment of this class was **already incorrect**
  before the first commit: it said that `IHttpClientFactory` controls the life
  of the handler, and a singleton view model made that false.

**Keep a comment that records what the code cannot show:**

- A measured property of a machine that is not ours, with the date of the test.
  Example: the server reads `Content-Length` only. You cannot see this in the
  code, and the code looks incorrect without it.
- A decision, its cost, and what it stops. Example: the lifetime of the handler
  is infinite on purpose.
- A trap that looks like a defect and is not.
- A number that a person computed. Example: 105 pixels is 9 mm on this panel.

The test: **remove the comment. If a competent .NET developer can then get the
same knowledge from the code, the comment was noise. If that knowledge is gone,
keep the comment.**

An XML documentation comment on a public member is idiomatic .NET, thus
`<summary>` stays. But a `<remarks>` block of ten lines is usually prose that
belongs in a document, and not beside the code.
