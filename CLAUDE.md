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

#### The fonts are done. Do not change this work without a cause.

The software supplies 5 static Noto fonts in
`src/GemmaTranslator/Assets/Fonts/`, approximately 18 MB. The software does not
use a font of the system, thus Raspberry Pi OS Lite cannot stop it.

| Part | File |
| --- | --- |
| The collection | `Fonts/GemmaFontCollection.cs` |
| The default family and the fallbacks | `Fonts/AppFonts.cs` |
| The check at the start | `Fonts/FontCheck.cs` |

Three points that a change must keep:

1. **`FontManagerOptions.DefaultFamilyName` is the value that stops the
   error at the start.** With no value, Avalonia asks the system, and Pi OS
   Lite can give none.
2. **The fallbacks cannot do all the work.** Chinese and Japanese use the
   same Han characters at U+4E00 to U+9FFF, and the correct shape is not the
   same. A fallback gives one font for one range of characters. Thus the
   display gives the font for each area from the language of the lane. See
   `AppFonts.For`. The fallbacks are for a character of a different
   language only.
3. **Each font must be static.** A file with an `fvar` table is a variable
   font, and Avalonia does not use it. The download must come from the
   `SubsetOTF` directory of `noto-cjk`, and not from `Variable`.

**How to make sure of this work on Windows.** Windows has a font for each of
these languages, thus text on the display is not proof.
`GlyphTypeface.FamilyName` gives the font that Avalonia selected. `FontCheck`
writes it at each start. If each line gives the name of a Noto font, the
software uses no font of the system, and the Raspberry Pi gives the
same result.

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

These are rules, not a selection.

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

Git does not hold the datasheets. This section gives each value and the
document that it comes from, by the identifier of that document.

### 4.1 Computer

A Raspberry Pi 5 with 8 GB of RAM, the same as upstream. The system image is
Raspberry Pi OS Lite.

`RP-008347-DS-1` gives the dimensions. The connector section of `dims.scad`
gives each value that the enclosure uses, with the identifier adjacent to it
and a CAUTION on the two chains, which use different datums.

**CAUTION: PI OS LITE CAN HAVE NO CJK FONT AND NO ARABIC FONT. WITH NO FONT,
AVALONIA THROWS AT STARTUP. THE SOFTWARE MUST SUPPLY ITS OWN FONTS. SEE
SECTION 3.1.**

### 4.2 Display — Raspberry Pi Touch Display 2, 5 inch

`RP-010430-MM-1` gives the dimensions. Page 3 gives the physical
specification, and the display section of `dims.scad` gives each value that the
enclosure uses, with a CAUTION on the cover glass, which is the only part of
the module at full width.

| Property | Value |
| --- | --- |
| Resolution | 720 × 1280 pixels, native portrait |
| Display format | 24-bit RGB |
| Diagonal | 5.0 inches |
| Active area | 62.1 mm × 110.4 mm |
| Unit dimensions | 91.46 mm × 143.4 mm |
| Hole in the lens | 63.1 mm × 111.4 mm |
| Touch | Capacitive, five-finger multi-touch |
| Touch response time | 35 ms typical, 40 ms maximum |
| Brightness | 500 cd/m², anti-glare |
| Connections | DSI ribbon cable, and electrical supply from the GPIO header |
| Temperature range | −20 °C to +70 °C |

**IMPORTANT: the panel is native portrait, and the appliance operates in
landscape.**

| Item | Value |
| --- | --- |
| The panel | 720 × 1280, native portrait |
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

The Speak2 40 datasheet gives the values below.

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

The model uses MEASURED values, and not the datasheet. A measurement of the
part in your hand is better than a datasheet.

The part is 121 mm in diameter and 32 mm high. Its three rubber feet are 2 mm
and that height contains them. The USB-A connector of the captive cable has a
body of 16 mm × 6 mm, and each hole that this connector goes through comes from
that measured body.

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

The electrical supply is a Geekworm X1201 UPS. The Geekworm dimension sheet and
the Geekworm DXF give the values. The DXF is at
`https://wiki.geekworm.com/images/e/e8/X1201.dxf`, and the board section of
`dims.scad` gives each bore that it supplies.

| Property | Value |
| --- | --- |
| Board | 106.6 mm × 85 mm |
| Cells | 2 × 18650, 18.5 mm maximum diameter × 65 mm, in holders on the board |
| Charge inlet | USB-C on the X1201, 5 V, 5 A |
| Face that holds the charge inlet | An 85 mm short edge, at the end where the Pi is |
| Centreline of the charge inlet | 51.8 mm from the 106.6 mm edge that holds the LEDs |
| Width of the charge inlet | 8.94 mm, and the edge of the board is 1.54 mm behind its face |
| Position of the cells | Adjacent to the Pi, in the 48 mm zone at the other end |
| Position of the Pi | Above the 58.5 mm zone, on M2.5 5+3 spacers |
| Electrical path to the Pi | Four pogo pins, and not the 40-pin header |

The cell diameter of 18.5 mm is a maximum, thus it is the correct value for a
keep-out. The MEASURED body of the connector of the electrical supply is 6 mm
thick.

**CAUTION: A PERSON MUST NOT CONNECT AN ELECTRICAL SUPPLY TO THE USB-C OF THE
RASPBERRY PI. CHARGE THROUGH THE USB-C OF THE X1201 AND THROUGH NOTHING ELSE.**

Geekworm gives this instruction on its wiki:

```text
Do not apply power to your Raspberry Pi via the Type-C USB socket.
```

**CAUTION: THE X1201 SUPPLIES THE PI THROUGH FOUR POGO PINS THAT PUSH ON THE
BOTTOM FACE OF THE PI. THE ENCLOSURE MUST HOLD THE TWO BOARDS TOGETHER AND MUST
NOT LET THEM MOVE. A PART THAT CAN LIFT THE PI OFF THOSE PINS IS A DEFECT.**

Geekworm records that loose spacers make bad contact and cause the low voltage
warning. Geekworm also records that the board stops after 3 seconds if it
cannot find the Pi.

**IMPORTANT: the height of the Pi above the X1201 is not measured.** The name
"M2.5x5+3" gives a spacer with a body of 5 mm and a stud of 3 mm. It is not
8 mm of height. The model uses 8 mm and the Geekworm installation document
shows 5 mm. Measure the distance between the top face of the X1201 and the
bottom face of the Pi. Each cut in the rear wall moves with this value.

A rib in the enclosure keeps a person from the USB-C of the Pi. The rib is our
decision and not a Geekworm instruction. The Geekworm case cuts a hole at that
socket and prints a warning adjacent to it.

The grid of holes in the floor is the air intake. No part of the X1201 stands
out of its bottom face, thus the grid goes on the full area.

**CAUTION: A PERSON CANNOT REMOVE A CELL EASILY. THIS ITEM IS OPEN.**

A person removes the deck, then the display module, then the carrier frame with
its four screws into the side walls. Only then is the X1201 open.

The enclosure has no hatch and no other door. Do not add one without a decision
from the user. See section 12.

### 4.5 Case

The case must hold the Raspberry Pi, the 5-inch display, the X1201 UPS, and the
two cells.

The Jabra Speak2 40 goes in a circular well at the front of the case. Its top
stands **above the deck** by `SPEAK2_PROUD`, which is 2.5 mm. It is not flush.

The buttons of the Jabra are on its top face, at the rim. With a top that is
flush, a finger on a button touches the deck, and a person cannot get the unit
out of the well. The top of the deck is not flat, and that is permitted: the
two push-to-talk switches also stay above it.

The captive cable does **not** stay in the case. It goes out of a slot in the
rear wall below the USB sockets. A person makes a loop and puts the connector
in a socket from the rear.

A socket that is near the wall and a connector in the enclosure cannot be there
together. The face of a socket is `TUNNEL_L` behind the wall, which is about
1 mm, and a USB-A connector needs about 12 mm.

Upstream has its own enclosure in `stl/`. Our enclosure is different work. Do
not change and do not remove the upstream files. See section 12.

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

The CUPID framework comes from Daniel Terhorst-North, and the documents are at
`https://cupid.dev/`. The five properties are Composable, Unix philosophy,
Predictable, Idiomatic, and Domain-based.

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

### 5.4 Do not make a function that upstream does not have

**IMPORTANT: this fork is a port. It is not new software.** If
`google-gemma/gemma-translator` does not do it, this software does not do it,
until the user says to add it.

Before you write a part that is new, do this:

1. Find the same function in the upstream code. Give the file and the line.
2. If you cannot find it, **stop and speak to the user.**

The user must agree to each new function. A subagent must not agree, and this
document must not agree. The user is explicit about a new function, and the
commit message must say that the function is new and who asked for it.

**The software must not write the audio of a person to a disk.** A class that
does it can be off by default, have a CAUTION comment, and make a good test
possible. It is not permitted:

- Upstream writes no audio to a disk. `server.py:207-219` makes the WAV
  in memory with `io.BytesIO` and sends it in the HTTP response. Each other
  `write` in that file is `self.wfile.write`, which is the socket. The one
  `open()` reads a static file to send it.
- In the European Union the speech of a person is personal data. An appliance
  in a public location cannot keep it without the agreement of the person.
  Thus such a class is not only a new function, it is against the law.

Section 5.3 says that the move removes as much as it adds. This section says
that the move adds nothing that upstream does not have.

A diagnostic that gives a **number** is not a new function: a quantity of
samples, a level, the name of a device, or a rate. Those are the same class as
a line in a log. A diagnostic that keeps the **content** of what a person said
is a new function, and it needs the agreement of the user.

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
| Not a target | macOS. |
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

**A comment on a security control is also not STE.** Write it in plain
English. A control that stops an attack must say so in words that no person
can misread: what it stops, what occurs without it, and that a person must not
remove it. STE vocabulary makes such a comment vague, and a vague comment on a
security control gets deleted by the next person who makes the code more
simple. Example: `Configuration/LiteRtOptionsValidator.cs`, the test of
`IsLoopback`.

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

This fork stays under the Apache License, Version 2.0. Section 4(c) of that
licence says to keep each copyright notice of the upstream work in a derivative
work. It does not say to add the notice of a different person to your work.

There are two headers. The test is: **does this file hold code that comes from
upstream?**

**Yes**, the file is a port of an upstream file or it replaces one. It keeps
the two copyright lines, the full Apache text, and the trailer. Add the
location of the upstream file, for example "It replaces translateText of
frontend/src/utils/api.js".

```csharp
// Copyright 2026 Google LLC
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
//
// ... the full Apache 2.0 text, 11 lines ...
//
// This file is part of a fork of google-gemma/gemma-translator and has
// been modified.
```

**No**, the file has no upstream equivalent. It gets the copyright line of its
owner and the SPDX line only.

A Google line on a file that holds no Google code says something that is not
correct. The `.scad` files of `cad/` are the example.

```csharp
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0
```

An `.axaml` file gets the same text in an XML comment before the root element.

Related files:

| File | Function |
| --- | --- |
| `LICENSE` | Apache 2.0. Do not change it. |
| `NOTICE` | The origin of the fork, what changed, and the third-party parts. |
| `CODEOWNERS` | The owners of this fork, and not the upstream maintainers. |
| `CONTRIBUTING.md` | The procedure of this fork. The Google CLA does not apply. |

---

## 11. Comments

**IMPORTANT: the upstream code has some comments only. Do not make the ported
code more dense.** Section 5.3 says that the move removes as much as it adds. A
group of comments on each line breaks that rule, and it makes the repository
read in two voices.

Use the CUPID properties to make the decision. See section 5.1.

**Remove a comment that:**

- Tells .NET, C#, or Avalonia to a person who writes .NET. That person knows
  the ecosystem. A comment that explains `AddHttpClient` or `[ObservableProperty]`
  is not idiomatic, because it does not trust its reader.
- Says again what the line below it says. This makes a second source of truth,
  and the two then disagree. Example: a comment that says `IHttpClientFactory`
  controls the life of the handler is incorrect when a singleton view model
  holds that client.

**Keep a comment that records what the code cannot show:**

- A measured property of a machine that is not ours. Example: the server reads
  `Content-Length` only. You cannot see this in the code, and the code looks
  incorrect without it.
- A decision, its cost, and what it stops. Example: the lifetime of the handler
  is infinite on purpose.
- A trap that looks like a defect and is not.
- A number that a person computed. Example: 105 pixels is 9 mm on this panel.

The test: **remove the comment. If a competent .NET developer can then get the
same knowledge from the code, the comment was noise. If that knowledge is gone,
keep the comment.**

An XML documentation comment on a public member is idiomatic .NET, thus
`<summary>` stays. But a `<remarks>` element of ten lines is usually prose that
belongs in a document, and not adjacent to the code.

---

## 12. CAD and OpenSCAD

This section covers the OpenSCAD source of the enclosure in `cad/`: the tool
that makes the parts, the rules of the source, and how to make sure of a
change.

Section 3 item 4 and section 4.5 give the goal: files for a printer that make a
case for the new hardware. The printer makes three parts: the body, the deck,
and the carrier frame that holds the display module.

Upstream supplies `.stl` binaries and no CAD source, thus the `.scad` source
has no upstream equivalent. See section 10 for the header that it gets.

### 12.1 The OpenSCAD software

**CAUTION: DO NOT PUT THE LOCATION OF OPENSCAD IN A FILE THAT GIT HOLDS. THE
LOCATION IS A PROPERTY OF THE DEVELOPMENT HOST. IT IS NOT A PROPERTY OF THIS
SOFTWARE.**

Start OpenSCAD with the `OPENSCAD` variable of the environment. Set the value
in `.claude/settings.local.json`, which git ignores:

```json
{ "env": { "OPENSCAD": "<the full path of openscad.exe>" } }
```

If the variable is empty, stop and speak to the user. Do not look for
OpenSCAD, and do not put its location in this document.

Do not start a bare `openscad`. That name can find the 2021.01 release, which
does not have the Manifold engine.

| Item | Value |
| --- | --- |
| Version on the development host | 2025.09.07 |
| Engine | `--backend=Manifold` |

Give `--backend=Manifold` to each command. Manifold is 10 to 100 times more
fast than CGAL for a boolean operation. The parts of this case have many
boolean operations in each other.

### 12.2 Traps that give an incorrect result that looks correct

Each trap below gives an incorrect result that looks like correct work.

1. **PowerShell does not wait.** On Windows `openscad.exe` is GUI software.
   PowerShell starts it and continues immediately. Thus a test of the output
   file says "not found" although the command is correct.

   Send the output through a pipe, for example `| Out-String`. Then PowerShell
   waits.

2. **`--render` needs the `=` character.** With `--render file.scad` OpenSCAD
   reads the name of the file as the value of `--render`. It then writes its
   help text and does no work. Write `--render=cgal`.

3. **Manifold does not tell you about a bad mesh.** For two cubes that touch at
   one edge only, Manifold writes `Status: NoError`. For the same source, CGAL
   writes `WARNING: Object may not be a valid 2-manifold and may need repair!`

   Thus `Status: NoError` from Manifold is not proof. A `Genus` value less than
   0 is a signal of a problem. See section 12.5.

4. **A flat part gets no test at all.** A part that is one `linear_extrude` of
   a 2D shape stays a `PolySet`, and OpenSCAD sends it to no geometry engine.
   Then `--summary geometry` writes NO status line and CGAL gives NO warning.

   That looks the same as a part that passed, but nothing was tested. `render()`
   does not correct this; version 2025.09.07 removes it for a `PolySet`. A
   boolean operation makes the engine do the work. `console.scad` has a `gate()`
   module that puts each part in an `intersection()` with a large cube. Do not
   remove it.

   Proof that it operates: the deck gives a `Genus` value, which is the count
   of its holes. See section 12.5.

5. **A rounded rectangle can be an EMPTY 2D shape.** `square()` with a side of
   0 makes no shape, and `offset()` of no shape is no shape. Thus a fully
   circular end, where the radius is one half of the smaller side, makes no
   shape at all, and the `difference()` that uses it removes nothing.

   The model then gives `Status: NoError` and the wall has no slot. The
   `rrect` of `util.scad` keeps a side of `eps` for this. Do not remove that
   `max()`.

6. **A hole in ONE wall has the colour of that wall.** Look down at the
   enclosure and you see the floor through the bay. Thus a hole 0.5 mm from a
   wall and a hole that goes into that wall look the same.

   Put the part in a slab of 1 mm with `intersection()`, give each part its
   own colour, and look at the slab.

7. **A model of a part that we buy must show all of that part.** A test of a
   collision against a model that has no nut finds no collision where the nut
   has one, and the row for that part then reads EMPTY.

   The M19 switch is the example. Its bezel is 22 mm across and its nut is 25.
   A model with the bezel, the barrel and the terminals, and no nut, hides a
   nut that stands in a side wall, in the cover glass of the display module,
   and in the two deck bosses.

   Before you agree with an EMPTY row, count the parts of that model against
   the document of the manufacturer.

8. **A test that reads a constant of the model tests nothing.** A row that
   takes its keep-out from the same variable that made the geometry moves with
   that variable, thus it always gives a correct result.

   A test must hold the value of the document. `DISP_BOSS_XY` is the example:
   it is an open item of the display section of `dims.scad`.

9. **The window of OpenSCAD keeps a cache.** It can show an error for a file
   that the command line makes with no error. Use `Design`, then
   `Flush Caches`.

### 12.3 Files and directories

| Directory | Contents | Git |
| --- | --- | --- |
| `cad/` | The `.scad` source. | Git holds it. |
| `stl/console/` | Our files for the printer. | Git holds it. |
| `notes/docs/` | The datasheets. | Git ignores it. |
| `notes/build/` | Images, output files, and the notes of the work. | Git ignores it. |

**IMPORTANT: git ignores `notes/`.** A person who clones this repository gets
the dimensions from section 4 and from the comments of the `.scad` source, and
from no other location.

Put each value and the identifier of its document in one of those two
locations. Do not point at a file of `notes/`: a reader cannot follow the
pointer.

`cad/` holds these five files, and no more: `console.scad`, which has the three
printed parts, the views, and the dispatcher; `dims.scad`, which has each
dimension of the enclosure; `ref-hardware.scad`, which has the models of the
parts that we buy; `tolerances.scad`, which has the fits, the clearances, and
the facet count; and `util.scad`, which has the helpers.

The `.stl` files at the top of `stl/` are the enclosure of upstream. They are
not our work. Do not change them and do not remove them.

`console.scad` selects one printed part with `-D 'part="body"'`,
`-D 'part="deck"'` or `-D 'part="carrier"'`. `PARTS` at the top of that file
gives the values of `part`, and the message of the assert at the end comes from
it.

Write a 3MF file for the printer. Write an STL file as an alternative.

### 12.4 The rules of the source

- Put each dimension in a variable at the top of the file, or in
  `cad/tolerances.scad`. Do not put a number in the body of a module.

- Write `$fn = $preview ? 32 : 180;`. The quantity of facets changes the
  dimension of a large hole, thus this value is not only cosmetic. For a large
  hole, use the larger radius `r / cos(180/$fn)`.

- Write `eps = 0.01`. Make each cut longer than the material by `eps`. Two
  faces at the same location make geometry with no thickness, and a slicer
  refuses it.

- Use BOSL2 (`cuboid(rounding=)`, `attach()`, `screw_hole()`). Do not write a
  chain of `hull()` or `minkowski()` if BOSL2 has a module for the shape.
  BOSL2 is in the library directory of OpenSCAD on the development host, thus
  `include <BOSL2/std.scad>` finds it and no variable of the environment is
  necessary.

- Show a part that we buy with the `%` modifier. Then `--render=cgal` does not
  put it in the output. `ref-hardware.scad` holds these models. Give
  `convexity = 10` to an import that has holes.

- The datasheets are the authority, and a model of a board from a library is
  not. Put the identifier of the document in a comment adjacent to the value.
  The four identifiers are `RP-008347-DS-1`, `RP-010430-MM-1`, the Speak2 40
  datasheet, and the Geekworm X1201 DXF. See section 11.

### 12.5 How to make sure of the work

**IMPORTANT: do not tell the user that a change is complete before you look at
it.**

Section 5.2 says that this fork has no test project. For CAD work an image is
the equivalent of `dotnet build`.

**An image alone is not proof.** It shows the shape that you looked for and it
hides the shape that you did not. After each change, do these steps:

1. Write a PNG image of the ISO view, the top view, the front view, and a view
   of a cut that `difference()` makes.
2. Read each image.
3. Compare each dimension with the datasheet of its part.
4. Make each of the three parts and compare the `Genus` value.
5. Then speak to the user.

```powershell
& $env:OPENSCAD --backend=Manifold -o notes/build/iso.png --imgsize=1200,900 `
    --camera=0,0,0,55,0,25,600 --colorscheme=Tomorrow --render=cgal `
    cad/console.scad | Out-String
& $env:OPENSCAD --backend=Manifold -D 'part="body"' -o notes/build/body.3mf `
    --summary geometry cad/console.scad | Out-String
```

The geometry gate: `body` gives `Genus: 138`, `deck` gives 6, and `carrier`
gives 13, each with `Status: NoError`. A different value says that the geometry
changed; if you did not intend the change, remove it. A value less than 0 is a
signal of a bad mesh.

Before a file goes to the printer, make the same part again with
`--backend=CGAL`. CGAL must give no warning about a 2-manifold. Manifold alone
is not sufficient. See section 12.2 item 3.

### 12.6 What is not decided

Two decisions of section 4 apply to each part:

- The panel operates in landscape. The case must hold the panel on its side,
  with the long edge horizontal. See section 4.2.

- The Jabra Speak2 40 goes in a well at the front of the case. Its top is
  2.5 mm above the deck, and its captive cable goes out of the rear wall. See
  section 4.5.

These items are open:

- The printer and the material are not selected. `cad/tolerances.scad` gives
  values for a printer that we do not know. Do a test print of one small part
  and measure it before the full enclosure goes to the printer.

- The four bosses that hold the carrier frame have no bore. `carrier_ledges()`
  makes the boss and the carrier has a clearance hole above it, but nothing
  cuts the bore for the insert. The two deck bosses have their bore:
  `INSERT_BORE_D` 4.2 mm and `INSERT_DEPTH` 6 mm for an M3 heat-set insert.

- `DISP_BOSS_XY` keeps the pattern of the four screw bosses central on the long
  axis of the display module, and `RP-010430-MM-1` does not. The carrier cuts
  its clearance at this pattern, thus a value that is out puts a boss of the
  module on the material of the carrier.

- Each other open value is a `TO BE UNDERSTOOD` comment in `cad/`. Examine that
  text. The comment gives the value, why it is not confirmed, and what it
  moves.
