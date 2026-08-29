<!--
Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
SPDX-License-Identifier: Apache-2.0
-->

# The GPIO configuration of the appliance

This document tells you how to make the physical inputs of the appliance
operate. It gives three signals to Linux: the two push-to-talk buttons, and the
mains line of the UPS.

Each value below comes from a measurement on the actual hardware.

---

## 1. The hardware

| Item | Value |
| --- | --- |
| Computer | Raspberry Pi 5 Model B Rev 1.1, 16 GB |
| System | Raspberry Pi OS Trixie (Debian 13), 64-bit |
| Kernel | 6.18.39+rpt-rpi-2712 |
| Electrical supply | Geekworm X1201 UPS with two 18650 cells |

The two buttons are momentary switches with one contact each.

The enclosure has a third momentary switch that connects to the X1201 and puts
the electrical supply on and off. That function is in the hardware of the
X1201, thus this configuration gives it no GPIO and the software does not read
it.

CAUTION: `pwr_button` in `/proc/bus/input/devices` is not that switch. It is
the button of the Raspberry Pi 5, on `107d508500.gpio`. The 40-pin header is
on `pinctrl-rp1`.

---

## 2. The signals

| Name | GPIO | Header pin | Type | Where the software reads it |
| --- | --- | --- | --- | --- |
| `SPEAKER_1` | 17 | 11 | Button | `/dev/input/recorder-buttons`, key code 183 |
| `SPEAKER_2` | 27 | 13 | Button | `/dev/input/recorder-buttons`, key code 184 |
| Mains | 6 | 31 | Level | `/sys/class/power_supply/mains/online` |

The two buttons are keys, because a push is an event. The mains line is not a
key, because it stays in one condition for hours. Thus the mains line goes to
the `gpio-charger` driver, which gives a file that has the condition.

Key code 183 is `KEY_F13` and key code 184 is `KEY_F14`. No key of that group
has a function in the console of Linux, and `systemd-logind` does nothing with
it.

The X1201 supplies the mains line on GPIO6. No cable is necessary.

---

## 3. The wiring

Each button has two terminals and no polarity. One terminal goes to the GPIO
pin. The other terminal goes to a ground pin.

| Button | Terminal A | Terminal B |
| --- | --- | --- |
| `SPEAKER_1` | Header pin 11, GPIO17 | Header pin 9, ground |
| `SPEAKER_2` | Header pin 13, GPIO27 | Header pin 14, ground |

The overlay puts a pull up on GPIO17 and on GPIO27. Thus the pin is high while
the button is open, and the button makes the pin low. The overlay has the
active low flag, thus low is a key down.

---

## 4. The files

| File | Function |
| --- | --- |
| `deploy/recorder-keys-overlay.dts` | The device tree overlay that makes the three signals. |
| `deploy/99-gemma-translator.rules` | The udev rules that make a stable path for the buttons, the touchscreen, the panel, and the speakerphone. |
| `deploy/gemma-battery-guard.sh` | The low battery guard of section 9 |
| `deploy/gemma-battery-guard.service` | The unit of the guard, which operates as root |
| `deploy/gemma-translator.service` | The unit of the translator. Section 12 gives its limits. |

The fuel gauge needs no file of this project. `i2c-sensor` is an overlay of
Raspberry Pi OS and it has a `max17040` parameter.

`deploy-pi.sh` does each step of section 5 in its step 2.

The two udev rules give the buttons and the touchscreen to the account of the
service and to no other account. The rule file has the full text on that
control. Read it before you make a change to it.

---

## 5. Installation

The overlay needs the compiler of the device tree:

```bash
sudo apt-get install -y device-tree-compiler
```

Make the overlay and put it with the other overlays:

```bash
dtc -@ -I dts -O dtb -o /tmp/rk.dtbo deploy/recorder-keys-overlay.dts
sudo install -m 644 /tmp/rk.dtbo /boot/firmware/overlays/recorder-keys.dtbo
```

Put in the udev rule. It becomes active immediately, but it agrees with no
device until the overlay is in operation:

```bash
sed "s|{{USER}}|$USER|g" deploy/99-gemma-translator.rules \
  | sudo tee /etc/udev/rules.d/99-gemma-translator.rules > /dev/null
sudo udevadm control --reload
```

**CAUTION: the next step changes `config.txt`, which controls the start of the
machine. Keep a copy first.**

```bash
sudo cp /boot/firmware/config.txt /boot/firmware/config.txt.gemma-backup
```

A new installation of Raspberry Pi OS has no line for GPIO6, GPIO17 or
GPIO27. If your `config.txt` has one, remove it. One pin has one owner, and
the second driver does not start. See section 8.3.

```bash
sudo sed -i -E \
  -e '/^[[:space:]]*dtoverlay=gpio-key,.*gpio=(17|27)([,[:space:]]|$)/d' \
  -e '/^[[:space:]]*dtoverlay=gpio-charger,.*gpio=6([,[:space:]]|$)/d' \
  /boot/firmware/config.txt
```

That command removes nothing on a new installation. It touches no other
`dtoverlay` line and no `dtparam` line.

Then add two lines. The first makes the buttons and the mains line. The second
gives the fuel gauge to the driver of the kernel. See section 8.8.

```bash
printf '\n[all]\ndtoverlay=recorder-keys\n' \
  | sudo tee -a /boot/firmware/config.txt > /dev/null
printf '\n[all]\ndtoverlay=i2c-sensor,max17040\n' \
  | sudo tee -a /boot/firmware/config.txt > /dev/null
```

`[all]` goes with each line because `config.txt` has sections such as `[pi5]`.
A line after a section is for that model only.

Examine the result, then start the machine again:

```bash
grep -n '^dtoverlay' /boot/firmware/config.txt
sudo reboot
```

The firmware reads `config.txt` before the kernel starts. Thus this step needs
a start of the machine. No other step here needs one.

---

## 6. How to make sure of the installation

```bash
grep -c recorder-buttons /proc/bus/input/devices
ls -l /dev/input/recorder-buttons
pinctrl get 6,17,27
cat /sys/class/power_supply/mains/online
grep SPEAKER /proc/interrupts
```

The measured result:

```
1
lrwxrwxrwx 1 root root 6 /dev/input/recorder-buttons -> event0
 6: ip    pd | hi // GPIO6 = input
17: ip    pu | hi // GPIO17 = input
27: ip    pu | hi // GPIO27 = input
1
166:   0   0   0   0  pinctrl-rp1  27  Edge  SPEAKER_2
167:   0   0   0   0  pinctrl-rp1  17  Edge  SPEAKER_1
```

`ls -lL /dev/input/recorder-buttons` gives `cr--------`, with the account of
the service as the owner and `root` as the group.

`/proc/bus/input/devices` gives one device with `B: KEY=180000000000000`. That
value is bit 55 and bit 56 of the third word, which are key code 183 and key
code 184. Thus one device has the two keys.

The kernel counts an interrupt each time the voltage on the pin changes.
Thus one push of a button adds two counts or more: one when the button goes
down, and one when it comes up. A contact that bounces adds more counts, and
those counts do not become events.

Read the count, push the button, then read the count again. If the count
does not change, the button has no connection to that pin.

To examine the two signals together, read the input device and the file of the
electrical supply. This needs no `sudo`, because the udev rule gives the
device to the account of the service:

```bash
python3 - <<'EOF'
import struct, select, os, time
fd = os.open("/dev/input/recorder-buttons", os.O_RDONLY)
last = None
while True:
    ready = select.select([fd], [], [], 0.2)[0]
    online = open("/sys/class/power_supply/mains/online").read().strip()
    if online != last:
        print("MAINS online=%s" % online, flush=True)
        last = online
    for f in ready:
        s, us, t, c, v = struct.unpack("qqHHi", os.read(f, 24))
        if t == 1 and v in (0, 1):
            print("BUTTON %s %s" % (c, "DOWN" if v == 1 else "UP"), flush=True)
EOF
```

A push gives one `DOWN` and one `UP`. If you disconnect the USB-C of the
X1201, `online` becomes 0. If you connect it again, `online` becomes 1.

For the touchscreen and the fuel gauge:

```bash
ls -lL /dev/input/appliance-touchscreen
ls /sys/class/power_supply/
cat /sys/class/power_supply/battery/capacity
cat /sys/class/power_supply/battery/voltage_now
```

The measured result:

```
cr-------- 1 jabra-translator root 13, 74 /dev/input/appliance-touchscreen
battery  mains
95
4146250
```

`voltage_now` is in microvolts. Thus 4146250 is 4.146 V.

**CAUTION: after the fuel gauge has its driver, `i2cdetect -y 1` gives `UU` at
address `0x36` and not `36`. That is the correct result and not a fault. `UU`
says that a driver has the address. See section 8.9.**

---

## 7. Removal

```bash
sudo systemctl disable --now gemma-battery-guard.service
sudo rm -f /etc/systemd/system/gemma-battery-guard.service
sudo rm -f /usr/local/sbin/gemma-battery-guard.sh
sudo systemctl daemon-reload
sudo cp /boot/firmware/config.txt.gemma-backup /boot/firmware/config.txt
sudo rm -f /etc/udev/rules.d/99-gemma-translator.rules
sudo rm -f /boot/firmware/overlays/recorder-keys.dtbo
sudo udevadm control --reload
sudo reboot
```

**CAUTION: the first three commands remove the low battery guard. After them
no software stops the machine when the cells go low. See section 9.**

The copy of `config.txt` removes the two `dtoverlay` lines together. If you
put the membership of group `input` away, put it back:

```bash
sudo gpasswd -a "$USER" input
```

An overlay in `config.txt` becomes part of the device tree before the kernel
starts. Thus `dtoverlay -r` cannot remove it, and `dtoverlay -l` does not give
it. You must start the machine again.

If the machine does not start, put the card in a different computer. The boot
volume is FAT32, thus each computer can read it.

Put `config.txt.gemma-backup` in the position of `config.txt`. That file is on
the same volume.

---

## 9. The low battery guard

`deploy-pi.sh` installs the guard in its step 6. The script goes to
`/usr/local/sbin/gemma-battery-guard.sh` and the unit to
`/etc/systemd/system/`.

The guard stops the machine before the cells go empty. Without it the Raspberry
Pi loses its electrical supply in one moment, and the SD card can become
defective in the middle of a write.

**CAUTION: the guard replaces the shutdown of the scripts of Geekworm in
`x120x`. Those scripts stop when the driver of the kernel takes address 0x36.
See section 8.9. Do not use the two methods together.**

### 9.1 The rule

The guard reads `/sys/class/power_supply/battery/voltage_now` each 5 s. Each
value is in microvolts.

| Condition | Result |
| --- | --- |
| Less than 2000000, or more than 4400000 | Not possible, thus hold the counts |
| A fall of more than 300000 from a value that was not low | Not possible, thus hold the counts |
| The mains supplies the machine, for a maximum of 120 reads | Hold the counts |
| Below 3200000 and at 3000000 or above, with no fall of 10000 in the last 60 reads | Hold the counts, and clear them |
| Less than 3000000, for 3 reads together | Stop the machine |
| Less than 3200000, for 12 net reads | Stop the machine |
| 3250000 or more | Remove 1 from the count of the low reads |
| Between 3200000 and 3250000 | Hold the counts |
| The read gives nothing | Hold the counts |

3.20 V is the value that Geekworm uses in its own script.

**The low tier needs a FALL, and not only a level.** The guard keeps a
reference, which is the last reading that fell by 10000 microvolts or more. A
rise takes the reference up and arms nothing, because a voltage that goes up
is not a discharge. When 60 reads give no such fall, the guard holds the low
tier and stops no machine.

The cause is measured. An X1201 with an EMPTY holder still answers at I2C
0x36, still reports `present` as 1, and still gives about 3176000 microvolts,
which is below the low value. That reading moved 3750 microvolts, three steps
of the gauge, in fifty minutes. Before this rule the guard stopped the machine
about 11 minutes after each start, and the machine started again, six times.

Two properties of this rule:

- **The emergency tier is not affected.** A reading below 3000000 counts and
  stops the machine, whatever the movement gives. Cells at that voltage take
  damage while a person examines the sensor.
- **The guard holds until it sees a fall, and not the opposite.** At the start
  it has no evidence that a pack is there, thus it stops nothing. A pack that
  supplies this machine clears that in less than a minute, because a Raspberry
  Pi 5 uses whole watts and a pack below 3.2 V that carries that load falls by
  tens of millivolts each minute.

**CAUTION: this rule OVERRIDES the maximum of 120 reads in section 9.3.** A
gauge that was never seen to fall holds for all time. Thus a charger that
holds a degraded pack below 3.2 V, and a pack whose protection board has
tripped, both read low and flat, and the guard stops the machine for neither.
No cell is run flat in either case, because the mains is present. What is lost
is the clean stop before the supply goes away. A machine that turns itself off
each 11 minutes in a public place was judged the worse of the two.

### 9.2 Why the voltage, and not the charge

`capacity` gave 10 % and the register gave 46 % at the same time, while the
cells supplied a current. RCOMP keeps its default value. The voltage was correct in each
measurement.

A control that stops a machine cannot use a value with that error. `capacity`
is correct on a bench, on the mains, with no current, and it is not correct in
service, which is the one condition that is important.

### 9.3 Why the mains line is not in the test

The mains line changes many times each second when the electrical supply cannot
give sufficient current. See section 8.7 and the debounce of
`SysfsPowerMonitor`. A rule that stops at each high read of that line gives no
protection while the cells go empty.

The voltage gives the same knowledge with no such condition. A supply that
cannot hold the cells lets the voltage go down, thus the guard operates. A
supply that holds them keeps the voltage up, thus the guard does not operate.

The mains line has one function only: while it is high the guard holds its
count, for a maximum of 120 reads. This gives a charger the time to lift cells
that are low. The maximum is necessary because a mains line that is high and
incorrect must not stop the guard for all time.

### 9.4 How to make sure of the guard

```bash
systemctl is-enabled gemma-battery-guard
systemctl is-active gemma-battery-guard
journalctl -u gemma-battery-guard -n 6
```

**The correct result depends on the cells.** `deploy-pi.sh` puts the guard on
each appliance and starts it only where a person said that the holder has
cells:

| The holder | is-enabled | is-active |
| --- | --- | --- |
| Cells in it | `enabled` | `active` |
| Empty | `disabled` | `inactive` |

A machine with cells that gives `disabled` has no protection. Correct it with
the installation, and not by hand, or the next update removes it again:

```bash
GEMMA_UPS_CELLS_FITTED=1 ./deploy-pi.sh
```

That writes `/etc/gemma-translator/ups-cells-fitted`, which the installation
reads each time afterwards. Thus an update that gives no variable keeps the
protection. Only `GEMMA_UPS_CELLS_FITTED=0` removes it.

A guard that operates writes its values each 5 minutes, thus a journal with no
such line is not correct. A journal that holds `has not fallen` says that the
guard sees no pack, and it stops no machine while that stays.

**CAUTION: `systemctl start` does not make the guard operate after the next
start of the machine. Use `systemctl enable`.**

To see the guard stop the machine, **first take the USB-C out**, then move the
low value above the voltage of a full cell. The guard writes two CAUTION lines
when it has this value:

```bash
sudo systemctl set-environment GEMMA_GUARD_LOW_UV=4300000
sudo systemctl restart gemma-battery-guard
journalctl -u gemma-battery-guard -f
```

The machine stops after 12 reads, which is 60 s.

**CAUTION: THE ORDER IS NOT FREE, AND THE TEST GIVES NOTHING WITH THE MAINS
CONNECTED.** The low tier needs a fall of 10000 microvolts in 60 reads, and a
pack that a charger holds is the most quiet thing on the board. Thus the guard
holds and the machine does not stop. On the cells the pack falls, the guard
arms, and the test measures the path that an appliance really uses.

**CAUTION: DO NOT DO THIS ON AN APPLIANCE WITH AN EMPTY HOLDER.** With no
cells the X1201 gives no ride-through, thus the USB-C is the only supply and
the machine stops the moment it goes out.

The value goes away at the next start of the machine, because systemd keeps it
in memory only.

To remove it before that:

```bash
sudo systemctl unset-environment GEMMA_GUARD_LOW_UV
sudo systemctl restart gemma-battery-guard
```

The guard refuses a value that is not a number, a value that starts with 0,
and a value that is not between 2500000 and 4300000. It writes a CAUTION and
keeps 3200000.

**The journal stays, and step 5c of `deploy-pi.sh` is what makes it stay.**

Raspberry Pi OS gives `Storage=volatile` in
`/usr/lib/systemd/journald.conf.d/40-rpi-volatile-storage.conf`, to spare an
SD card. With that value the line that gives the cause of a shutdown goes away
when the machine starts again, and a person who asks why the appliance stopped
has no answer. This appliance boots from NVMe, thus the cause of that value
does not apply.

The installation writes
`/etc/systemd/journald.conf.d/99-gemma-persistent.conf` with
`Storage=persistent` and a limit of 200M.

| Item | Value |
| --- | --- |
| The name must sort after `40-` | A drop-in beats the main file, and the last drop-in wins. `Storage=` in `/etc/systemd/journald.conf` does nothing. |
| The test | The device of the root filesystem. A device that the script cannot identify counts as a card, thus the journal stays in memory. |
| `journalctl -b -1` | It gives an answer from the NEXT start of the machine. A restart of journald does not move a journal that is in memory on to the disk. |

**CAUTION: a log line must hold no part of what a person said.** The journal
now rests on a disk that survives a power cut. Section 5.4 of `CLAUDE.md` and
the swap of step 7 give the same rule for the same cause.

---

### 8.15 The software and the console do not use the same value

`cmdline.txt` has `video=DSI-1:720x1280@60,rotate=270` and the text of the
console is correct with it. See section 8.13.

The software gives `SurfaceOrientation.Rotation90`. With that value the user
interface agrees with the console, and with `Rotation270` it is inverted
against it.

**TO BE UNDERSTOOD: the two agree with each other and with no other thing.**

A person looked at the display and selected the value of the console, and this
value agrees with that one. Thus the two can be upside down together.

`cad/dims.scad` says two times that the two ends of the display module are not
told apart. Only a photograph of the module in the case, with the DSI cable in
view, closes this.

**CAUTION: the two values are 180 degrees apart. A person who makes them agree
gets an image that is upside down.** The DRM backend makes its own plane, thus
what turns the console does not apply to it.

Touch cannot give this measurement.

The two values are 180 degrees apart, and Avalonia moves the touch coordinates
with the image in the two conditions. Thus a touch operates with the correct
value and with the incorrect value. Only the image shows which value is
correct.

### 8.16 Avalonia takes the first card that opens

Avalonia opens each `/dev/dri/card[0-9]+` in the sequence that the directory
gives, and it takes the first one that opens. It does not examine the
connectors.

This machine has three cards, and the account can open each one:

```
$ ls -l /dev/dri/by-path/
platform-1f00118000.dsi-card  -> ../card0   drm-rp1-dsi   the panel
platform-1002000000.v3d-card  -> ../card1   v3d           no display
platform-axi:gpu-card         -> ../card2   vc4-drm       HDMI, no connection
```

Thus `card: null` is a chance. With card1 the software stops in the enumeration
of the resources. With card2 it stops and says:

```
Unable to find connected DRM connector
```

The number of a card comes from the sequence of the drivers, and that sequence
can change at each start. `Program.cs` gives the path of `by-path`, which holds
the address of the DSI controller. This is section 8.6 again, for a card.

### 8.17 The names of the packages of Mesa on Debian 13

The documents of Avalonia give `libegl1-mesa`. **That package is not on
trixie**, and `apt-get` stops and says that it cannot find the package. The correct
names:

```bash
sudo apt-get install -y libgbm1 libegl1 libegl-mesa0 libgl1-mesa-dri libgles2
```

`libdrm2`, `libfontconfig1` and `libicu76` come with the image. `libicu76` is
necessary, because `InvariantGlobalization` is `false`.

**CAUTION: `libinput10` does NOT come with the image. An earlier text of this
section said that it does, and that text was not correct.** The DRM backend of
Avalonia loads that library for the input. A clean installation of Raspberry Pi
OS Lite of 2026-06-18 gives `Installed: (none)` for the package, thus the
software stops at the start:

```text
System.DllNotFoundException: Unable to load shared library 'libinput.so.10'
  at Avalonia.LinuxFramebuffer.Input.LibInput.LibInputBackend.Initialize(...)
```

The panel stays black and no message comes on it, because the software stops
before it draws. `deploy-pi.sh` installs the package in its step 1.

`kmscube` gives the proof that the stack operates, before the software starts:

```
$ kmscube -D /dev/dri/by-path/platform-1f00118000.dsi-card
EGL version 1.5, vendor "Mesa Project"
OpenGL ES 3.1 Mesa 25.0.7
renderer: "V3D 7.1.7.0"   vendor: "Broadcom"
```

**The account needs no `sudo` for this, and the software needs none.** The
documents of Avalonia give `sudo ./app --drm`. The account is in group `video`,
which opens the card. Software that opens a card when no other software holds
it becomes the master of that card, and the console does not hold it.

### 8.18 The default capture device moves

`PreferredDeviceName` in `appsettings.json` gives `Jabra`. With no name the
software asks for the default device of the machine, and that gave:

```
Unable to init device Default Audio Device. Result: InvalidArgs
```

**TO BE UNDERSTOOD: the cause is not known.** The default device gave a
microphone in one measurement and gave this error in each measurement after it,
with the same card and the same sequence of the devices. No software held
the device, thus a second reader is not the cause.

What is known is the result. On one machine, with no other change:

| `PreferredDeviceName` | Reads | Result |
| --- | --- | --- |
| empty | 5 | The error above, each time |
| `Jabra` | 3 | The device opens, each time |

```
The microphone is Jabra Speak2 40 UC, USB Audio. The software asked for
16000 Hz and the machine gave 16000 Hz with 1 channel(s).
```

The name is a part of the name of the device, and not the full name. The
sequence is: the name of the settings, then the default device, then the first
device. Thus a machine with no such device continues to operate.

### 8.19 The microphone gives no sound, and no error says so

**CAUTION: the Jabra Speak2 40 gives no microphone data while its playback
interface is stopped. The software holds a playback stream open for this one
cause. Do not make the audio a capture device only.**

Measured on the appliance from a cold start, with each other condition equal:

| Off hook | Playback stream | `arecord -D hw:0,0` |
| --- | --- | --- |
| No | No | `read error: Input/output error` |
| Yes | No | `read error: Input/output error` |
| Yes | Yes | 32000 samples, largest value 9061 |
| No | Yes | 32000 samples, largest value 4561 |

The two values are the sound of the room at different moments. They show that
sound comes through, and they do not compare one level with the other.

The playback stream is the one condition, and the call state of the device is
not a condition. The device does echo cancellation in its own hardware. Thus
the microphone is behind a canceller, and that canceller needs the signal that
goes to the speaker.

**What the software sees is worse than an error.** miniaudio gives no error
for this condition. It gives buffers of the correct dimension, at the correct
rate, and each sample has the value 0:

```
The recording stopped after 2.75 s with 44032 samples at 16000 Hz. The
largest level is 0.000.
```

44032 samples for 2.75 s at 16000 Hz is the correct quantity. Thus a count of
the samples does not find this condition, and only the level does. With the
playback stream open, the same button gives 0.600.

**The correction is in the software.** `SoundFlowAudioDevice` makes a
`FullDuplexDevice` and keeps the playback interface started for the life of the
process. Thus the appliance sends no sound between two translations, and the
microphone operates.

The contents of the mixer are not the condition. The callback of the playback
device operates although the mixer holds no component. `AddComponent` and
`RemoveComponent` do not start the device and do not stop it.

**CAUTION: a read of the ALSA `default` device is not a correction.** That
read operates, and the microphone then operates for a short time only. Six
reads of `hw:0,0` one minute after it each gave the error again.

A correction that decays is worse than none. The appliance then operates one
time and subsequently records no sound, with no signal to the user.

#### The ring of the speakerphone

The software puts the device off hook while a person holds a button, and on
hook at the release. The green ring then shows each person in the room when
the microphone is live. It does not change the capture.

The device acknowledges on two channels:

```
04 02 00   the vendor mirror, at about 24 ms
02 03 00   the telephony page: Hook Switch, and the latched call-active bit
02 00 00   on hook
```

One report holds each indicator together, and it gives the full state. Thus
`02 01 00` is "off hook, and not muted" in one write, and a device that a
person muted becomes not muted when a person holds a button again.

The nodes are for root only. The rule in `deploy/99-gemma-translator.rules`
gives one node to the account of the service by owner, and it makes the name
`/dev/appliance-speakerphone`. The software opens that one name.

**CAUTION: do not give the account group `plugdev`, and do not let the software
look for the device.** Section 8.10 keeps this account out of group `input` for
the same class of cause.

A USB device supplies its own descriptor. Thus a device that a person connects
at the rear wall can say that it is the speakerphone. The rule holds the full
cause in plain English.

Put the rules in again after a change:

```bash
sed "s|{{USER}}|$USER|g" deploy/99-gemma-translator.rules \
  | sudo tee /etc/udev/rules.d/99-gemma-translator.rules > /dev/null
sudo udevadm control --reload
sudo udevadm trigger --subsystem-match=hidraw
ls -l /dev/appliance-speakerphone
```

Without the rule the software writes one line and each other function
continues. The software writes the on-hook report at the start and at SIGTERM,
thus a ring that stays green shows a stop that gave no signal.

### 8.20 The speakerphone controls the level of its own sound

**The Jabra Speak2 40 changes the level in the device. The software of the
host needs no control of the level.** This closes the open item of section 4.3
of CLAUDE.md.

Measured on the appliance with a tone of 440 Hz that operated for 75 s:

| Item | Result |
| --- | --- |
| A push on the buttons of the device | The level changes, and a person hears it |
| `PCM Playback Volume`, numid=4 | 8 of 15 at the start, and 8 of 15 at the end |
| A read of that control each 5 s for 65 s | 8, at each read |
| `/dev/input/event2` | 198 key events, `KEY_VOLUMEUP` and `KEY_VOLUMEDOWN` |

The range of `PCM Playback Volume` is 0 to 15, and −45.00 dB to 0.00 dB.

There are two controls, and they are not the same control:

- `PCM Playback Volume` is a control of the host on the card. Nothing on the
  appliance moves it.
- The device has a control of its own, and its buttons move that one. The host
  cannot read it.

The device sends `KEY_VOLUMEUP` and `KEY_VOLUMEDOWN` to the host also. Those
are a message and not a request. The level changes although no software of the
host reads them.

**CAUTION: the display cannot show the level.** The Consumer page of the
device declares these controls, and no other:

```
Volume Increment · Volume Decrement · Mute · Play/Pause · Play · Pause · Stop
```

Each one gives a direction and it does not give a position. Thus the host
learns which button a person pushed. It does not learn where the control is,
what its limits are, or the dimension of one step.

A bar or a percent on the display is a value that the software computes from a
start that it does not know. It becomes incorrect at the first push that a
person makes while the software is stopped.

The software can show the direction of a push. It cannot show a level.

**CAUTION: do not put a gain of the software in front of this.** Two controls
that a person can move, and that do not agree with each other, are worse than
one control. Section 9 of CLAUDE.md gives a gain of the software, and that
decision comes from a time before this measurement.

### 8.21 The synthesis is more slow than the sound that it makes

A design that cuts the text in pieces, and that speaks piece N while it makes
piece N+1, has a space with no sound at each connection, if the synthesis cannot keep in front
of the speaker. This is the measurement of that.

**CAUTION: the first version of this section gave 0.7 and said that the
synthesis is more fast than the sound. That was an error of arithmetic.** It
took the length of the sound away from the time of the call, as if the call
holds the sound. The call gives the full WAV file when the synthesis is
complete, thus the time of the call IS the synthesis. The correct ratio is the
time of the call divided by the length of the sound.

The method: `warm` the language, then one GET of `/api/tts`. The time of the
call is the synthesis. The length of the sound comes from the header of the
WAV file that comes back.

**CAUTION: that method cannot be done again. `/api/tts` went away with
`backend/server.py`, and the speech part is now in the process of the user
interface.** The numbers stay because they are measurements. To repeat them,
use the log, which is the second path below and which gives the same ratio.

| Language | Characters | The call | Sound | Call ÷ sound |
| --- | --- | --- | --- | --- |
| English | 13 | 2.53 s | 1.50 s | 1.69 |
| English | 50 | 5.09 s | 3.00 s | 1.70 |
| English | 109 | 11.60 s | 6.70 s | 1.73 |
| English | 258 | 26.49 s | 15.50 s | 1.71 |
| Japanese | 10 | 3.62 s | 2.05 s | 1.76 |
| Japanese | 46 | 12.07 s | 6.92 s | 1.74 |

**The ratio is 1.69 to 1.76 at each length and in the two languages.** The
sound of the appliance is 24000 Hz, mono.

The appliance gives the same answer from a different path. The log has one
line for the synthesis and one for the sound:

| Characters | Spoke | The speaker played | Ratio |
| --- | --- | --- | --- |
| 7 | 2.72 s | 1.58 s | 1.72 |
| 24 | 7.39 s | 4.23 s | 1.75 |
| 14 | 4.46 s | 2.64 s | 1.69 |

**Thus a queue of one piece becomes empty at each connection.** Piece N+1 needs
about 1.74 times its own length of sound, and piece N gives only its own
length. The space with no sound is about 0.74 times the length of piece N+1.

The gain of the cut is the time to the first word, and it is not the total
time. A person hears the first sentence after the synthesis of that sentence
only. The cost is that space, thus a cut must come at the end of a sentence,
where a person waits.

The cost of the cut, for the same 258 characters:

| Method | The calls | Sound |
| --- | --- | --- |
| One call | 26.75 s | 15.50 s |
| Four calls | 28.69 s | 16.75 s |

The synthesis costs 1.94 s more, which is about 7 %. The sound is 1.25 s
longer, which is about 0.31 s where two pieces come together. That is the part
with no sound at the start and at the end of each WAV file, and it adds up.

**IMPORTANT: a measurement of the full operation does not give this.** The
line of the log for the exchange gives one number for the synthesis and the
sound together, and only the synthesis can become more short. A person must
hear the sound.

### 8.22 The fuel gauge goes away, and the pin-1 end of the header is the cause

A change of the Raspberry Pi board gave a charge of "unknown" on the display.
The kernel names the failure:

```text
max17040 1-0036: probe with driver max17040 failed with error -121
```

-121 is EREMOTEIO. The chip did not answer. `i2cdetect -y 1` gives an empty
bus, and not one address.

**The Raspberry Pi is not the cause, and each test of it passes.** The overlay
of section 8.8 is in `config.txt`, the module `max17040_battery` is in the
memory, the overlay makes the device `1-0036`, and `pinctrl get 2,3` gives:

```text
 2: a3    pu | hi // GPIO2 = SDA1
 3: a3    pu | hi // GPIO3 = SCL1
```

Each line is in its alternate function, each has its pull, and each is high,
which is a bus that is idle and not one that is held. `i2cdetect -F 1` gives
each function of the controller.

**The X1201 is not the cause either, and it operates.** It supplies the Pi
through the pogo pins, and GPIO6 follows the charge inlet: a person who
disconnects that inlet makes `/sys/class/power_supply/mains/online` go from 1 to
0. CAUTION: GPIO6 has a PULL-DOWN, thus a pin with no connection reads 0. A
value of 1 is proof that the X1201 drives it.

The cause is the seat of the 40-pin header, and the signals give the position:

| Signal | Pin | Condition |
| --- | --- | --- |
| SDA1, GPIO2 | 3 | no answer |
| SCL1, GPIO3 | 5 | no answer |
| The button, GPIO17 | 11 | operates |
| The button, GPIO27 | 13 | operates |
| The mains line, GPIO6 | 31 | operates |

**Each signal that fails is at the end of the header that holds pin 1, and each
signal that operates is further along it.** A connector that lifts at that one
corner keeps the pins of the middle and gives up pins 1, 3 and 5. The pogo pins
are on springs and they keep the supply through the same error, thus the
appliance starts and looks correct.

A person pushed that corner of the connector home. `0x36` then gives `UU`,
`/sys/class/power_supply/battery` comes back, and the values are those of
section 8.8: 4196250 microvolts and a capacity of 101.

Section 4.4 of CLAUDE.md says that the height of the Pi above the X1201 is not
measured, and that the name "M2.5x5+3" gives 5 mm of body and 3 mm of stud and
not 8 mm of height. A spacer of the incorrect height leaves one end of a rigid
connector short while the pogo pins still touch, which is this failure.

**The guard of the cells does not stop the appliance in this condition, and it
does not protect it either.** `read_microvolts` of `gemma-battery-guard.sh`
gives a failure for a file that it cannot read, and the caller then HOLDS its
counters. Thus a fuel gauge that goes away makes no poweroff of an appliance
that is in good health. It also makes no poweroff of one that is empty. Keep the
charge inlet connected while the gauge is silent.

---

## 10. The swap of the appliance

`deploy-pi.sh` writes `/etc/rpi/swap.conf.d/99-gemma-translator.conf` in its
step 7:

```ini
[Main]
Mechanism=zram
```

Raspberry Pi OS gives `zram+file`. zram then writes cold pages to `/var/swap`
on the SD card:

```
$ cat /sys/block/zram0/backing_dev
/dev/loop0
$ losetup -a
/dev/loop0: []: (/var/swap)
```

**CAUTION: a page can hold the speech of a person, and a page on the card stays
after the machine loses its electrical supply. No part of the software can
remove that copy: it is not in the memory of the software.**

`Mechanism=zram` gives the same quantity of swap, in the memory, thus it costs
no headroom. After the machine starts again:

```
$ cat /sys/block/zram0/backing_dev
none
$ losetup -a
$ ls -l /var/swap
ls: cannot access '/var/swap': No such file or directory
$ cat /proc/swaps
/dev/zram0    partition   2097136   0   100
```

The unit `rpi-remove-swap-file@var-swap.service` of Raspberry Pi OS removes the
file. The generator starts that unit when the mechanism does not make it
necessary.

`/sys/block/zram0/bd_stat` gives the count of the pages that went to the card.
The first write comes 180 minutes after the machine starts, and each 24 hours
after that. Thus a machine that starts again frequently can give 0, and the
condition continues to be there.

---

## 8. Measured properties

These are the results that are not easy to see, and that give an incorrect
result which looks correct.

### 8.1 The name of the input device comes from the parent node

`gpio-keys` gives the input device the `label` of the parent node.
`recorder-keys-overlay.dts` has `label = "recorder-buttons"` on that node.
Thus the device has that name, and the udev rule of section 4 finds it.

The `label` of a key node is a different property. It becomes the name of the
interrupt in `/proc/interrupts` and the name of the consumer in `gpioinfo`.
`SPEAKER_1` and `SPEAKER_2` are those names.

With no `label` on the parent node, the name of the device is the name of the
node of the device tree. The `gpio-key` overlay of Raspberry Pi OS has no
`label` on its parent node, and its `label` parameter goes on the key node.
Thus a device of that overlay has a name such as `button@11`, which is the
number of the pin in hexadecimal.

### 8.2 The stock `gpio-charger` overlay does not apply its pull

`gpio-charger.dtbo` has `pinctrl-0` and no `pinctrl-names`. Thus the pinctrl
core gives that configuration the name `0` and not `default`. The core then
does not apply it, and the pin keeps the value of the firmware.

The `gpio_pull` parameter of that overlay does nothing, and it gives no
error.

Measured: with `dtoverlay=gpio-charger,gpio=6,gpio_pull=down`, `pinctrl get 6`
gives `ip pu`. GPIO6 is in the group 0 to 8, which the firmware pulls up.

`recorder-keys-overlay.dts` has `pinctrl-names = "default"`. With that
property the pull is applied, and `pinctrl get 6` gives `ip pd`.

### 8.3 One pin has one owner

The `gpio-keys` driver and the `gpio-charger` driver get the line for the
full time of operation. No other software can then read it.

```
$ gpioget -c gpiochip0 6
gpioget: unable to request lines: Device or resource busy
```

`libgpiod`, `gpiozero` and `System.Device.Gpio` all give this result on GPIO6,
GPIO17 and GPIO27. The scripts of the manufacturer of the UPS, which read
GPIO6 with `gpiod`, do not operate with this configuration.

A driver that starts with the kernel gets the line first. A daemon that
starts subsequently is the one that gets the error.

### 8.4 RP1 has no debounce in its hardware

The pinctrl driver of RP1 has no debounce function. Thus `gpio-keys` uses a
timer in software, and the event comes after the line becomes quiet.

Each change of the voltage starts that timer again. Thus `debounce-interval`
is the quiet time after the last change. It is not the length of the bounce,
and a value larger than the bounce gives no more protection.

The time in the event moves by the same quantity.

Measured with the buttons of the appliance: 61 presses made 238 interrupts but
only 122 events. The bounce stops in less than 4 ms. The default of 5 ms
removed all of it.

The two buttons are not the same. One bounces when it goes down. The other
bounces when it comes up.

### 8.5 The parent keys of a udev rule must agree on one parent

`SUBSYSTEMS`, `DRIVERS`, `KERNELS` and `ATTRS` examine the parents of a
device. All of them in one rule must agree on the **same** parent.

The name is a property of the `inputN` device. The driver and the name of the
node are properties of its parent. Thus a rule that has `ATTRS{name}` and
`DRIVERS` together agrees with no device, the symlink does not come, and udev
gives no error.

`99-gemma-translator.rules` uses `SUBSYSTEMS`, `DRIVERS` and `KERNELS`, which
are all properties of the same parent.

### 8.6 The number of an event device changes

The number in `/dev/input/eventN` comes from the sequence of the start. A
different configuration, or a different sequence, gives a different number to
the same device.

The udev rule gives `/dev/input/recorder-buttons`, which does not change.

### 8.7 The X1201 drives GPIO6

With mains connected, a pull down on GPIO6 does not make the pin low:

```
$ sudo pinctrl set 6 ip pd ; pinctrl get 6
 6: ip    pd | hi
$ sudo pinctrl set 6 ip pn ; pinctrl get 6
 6: ip    pn | hi
```

Thus the X1201 drives the line and does not pull it through a resistor. The
pull gives the condition only when the X1201 is not in contact. The overlay
uses a pull down, thus an X1201 that is not in contact gives `online` 0.

The polarity is measured at the event layer. A disconnection of the USB-C
makes `online` become 0, and a connection makes it 1.

### 8.8 The fuel gauge

`i2cdetect -y 1` gives address `0x36`. Register `0x0C` gives `0x9700`, which
is the default value of a MAX17040. `dtparam=i2c_arm=on` must be in
`config.txt`, and it is there in the stock file.

| Register | Value | Formula |
| --- | --- | --- |
| `0x02` | Cell voltage | `raw / 16 * 1.25` mV |
| `0x04` | State of charge | `raw / 256` % |

The device gives the most significant byte first. A read with a repeated start
gives the two bytes in that sequence.

The kernel has a driver for this part. With `dtoverlay=i2c-sensor,max17040`
the values come from `/sys/class/power_supply/battery/voltage_now` and
`/sys/class/power_supply/battery/capacity`. Each account can read them, thus
the software needs no membership of group `i2c`.

The driver and the formula give the same result. One measurement of the two,
at the same charge:

| Source | Voltage | Charge |
| --- | --- | --- |
| The registers, with the formula above | 4.1463 V | 95.29 % |
| The driver, from `power_supply` | 4146250 µV | 95 |

Thus the driver removes the fraction and changes nothing else.

The MAX17040 measures one cell. The X1201 holds two cells, and the measured
4.146 V is the voltage of one of them. Thus the two cells are in parallel and
the value is the value of the group.

**TO BE UNDERSTOOD: no document of Geekworm that we have gives the connection
of the two cells. The measurement is the only source. If a different X1201
puts the two cells in series, a fuel gauge of one cell gives a charge that is
not the charge of the group.**

The value of `capacity` can be more than 100. The value of `status` is
`Unknown`, because the driver has no charger.

The mains line of section 8.7 is the signal that tells you if the charge
increases.

### 8.9 One address has one owner

This is section 8.3 again, on the I²C bus and not on a pin.

The driver of the kernel gets address `0x36` when it binds. Software that
then reads the address directly gets `Errno 16`:

```
OSError: [Errno 16] Device or resource busy
```

**CAUTION: this stops the scripts of Geekworm in `x120x`. `bat.py`, `pld.py`
and `merged.py` each read `0x36` with `smbus2`, and each one stops with that
error. `i2cdetect` gives `UU` at that address.**

Thus the two methods are not possible together. Use the driver, or use the
scripts of the vendor. Do not make a plan that needs the two.

### 8.10 Raspberry Pi OS puts the first account in group `input`

```
$ id
uid=1000(jabra-translator) ... 996(input) ...
```

The image does this, and not `deploy-pi.sh`. Group `input` reads each
`/dev/input/event*` node, because each node is `0660 root:input`. Thus the two
udev rules of section 4 give no protection while this membership is there.

To remove it:

```bash
sudo gpasswd -d "$USER" input
```

This step has a test on this hardware. After the command, the account opens
the touchscreen and the buttons, and it opens no other device:

```
/dev/input/event10   OPEN rw   Goodix Capacitive TouchScreen
/dev/input/event4    OPEN ro   recorder-buttons
/dev/input/event0    denied    QTIL Jabra Speak2 40 UC
/dev/input/event2    denied    QTIL Jabra Speak2 40 UC Consumer Control
/dev/input/event5    denied    pwr_button
/dev/input/event6    denied    vc4-hdmi-0
```

libinput 1.28.1 continues to operate. It writes a line for each device that it
cannot open, and it adds the touchscreen:

```
$ libinput debug-events
Failed to open /dev/input/event0 (Permission denied)
...
-event10  DEVICE_ADDED   Goodix Capacitive TouchScreen  seat0 default group1  cap:kt ntouches 5 calib
```

**These 9 lines are the control in operation and they are not a fault.** Each
one is a device with `root:input` as the owner and the group: the four nodes
of the Jabra Speak2 40, the button of the Raspberry Pi 5, and the four nodes
of HDMI. The appliance uses no one of them.

A touch of the display then gives `TOUCH_DOWN`, `TOUCH_MOTION` and `TOUCH_UP`,
with two fingers at the same time in slot 0 and slot 1.

To put the membership back:

```bash
sudo gpasswd -a "$USER" input
```

### 8.11 A node of 0400 stops libinput

The rule for the buttons gives mode `0400` and the rule for the touchscreen
gives mode `0600`. This is not an error.

libinput opens each device that it controls read-write. Thus a node of `0400`
gives this line, and the touchscreen does nothing:

```
Failed to open /dev/input/event10 (Permission denied)
```

The measurement, with the account that owns the node:

| Node | `O_RDONLY` | `O_RDWR` |
| --- | --- | --- |
| mode `0400` | OK | Permission denied |
| mode `0600` | OK | OK |

`EvdevPushToTalk` opens the buttons read-only, thus the buttons keep `0400`.

The rule for the buttons also gives `ENV{LIBINPUT_IGNORE_DEVICE}="1"`. libinput
then does not try the device and it writes no line for it:

```
$ udevadm info /dev/input/event4 | grep LIBINPUT
E: LIBINPUT_IGNORE_DEVICE=1
```

Without this property libinput writes `Failed to open /dev/input/event4` at
each start. That line is for the one device that the software reads itself,
thus it gives an incorrect signal to a person who examines the buttons.

**CAUTION: group `input` is not the cause of this condition, and a person who
examines it can think that it is.** The touchscreen at `0400` does not operate
with the membership of that group and without it.

### 8.12 The touchscreen gives the coordinates of the panel

The touchscreen gives x from 0 to 720 and y from 0 to 1280, which is the
native portrait of the panel.

The `video=` parameter of section 8.13 does not change these values, and no
overlay in this configuration changes them. Avalonia changes them with
`DrmOutputOptions.Orientation`, with the image.

### 8.13 `video=` goes in `cmdline.txt`, and it turns the console only

The panel is native portrait, 720 × 1280. The appliance operates in landscape.

`video=` is a parameter of the command line of the kernel. It goes at the end
of the one line of `/boot/firmware/cmdline.txt`. The same text in `config.txt`
does nothing, and it gives no error:

```
$ tr -d '\0' < /proc/device-tree/chosen/bootargs | grep -o 'video=[^ ]*'
$ cat /sys/class/graphics/fbcon/rotate
0
```

With the parameter in `cmdline.txt`, the kernel reads it:

```
$ tr -d '\0' < /proc/device-tree/chosen/bootargs | grep -o 'video=[^ ]*'
video=DSI-1:720x1280@60,rotate=270
$ cat /sys/class/graphics/fbcon/rotate
1
```

**CAUTION: this parameter turns the text console and no more.** With the
parameter in operation, the connector and the frame buffer keep the dimensions
of the panel:

```
$ cat /sys/class/drm/card0/card0-DSI-1/modes
720x1280
$ cat /sys/class/graphics/fb0/virtual_size
720,1280
```

Software that writes to DRM makes its own plane. Thus it gets a surface of
720 × 1280 and it must turn the image itself. Avalonia does this with
`DrmOutputOptions.Orientation`. The two rotations do not touch each other,
because Avalonia does not read the console.

If the text on the display is in landscape and inverted, change 270 to 90.

`deploy-pi.sh` writes this parameter in its step 2, on the one line of
`cmdline.txt` and after a backup to `cmdline.txt.gemma-backup`. It writes it
one time only: a file that already gives `video=DSI-1:` with a different value
gets a warning and keeps what it has, because that value belongs to whoever put
it there.

The appliance shows the software and not the console, thus a console that turns
is an aid to a person who does maintenance.

### 8.14 The overlay of the panel selects a different DSI interface

`vc4-kms-dsi-ili9881-5inch` has a `rotation` parameter, which is the one
control of the kernel that could turn the image for DRM. To give a parameter
to this overlay, `display_auto_detect` must be 0 and `config.txt` must name
the overlay.

**CAUTION: this stops the display. The panel of this appliance is on
`dsi@110000` and the overlay selects `dsi@128000`:**

```
pwm-backlight panel_backlight@1: error -EREMOTEIO: failed to apply initial PWM state
pwm-backlight panel_backlight@1: probe with driver pwm-backlight failed with error -121
```

`/sys/class/drm` has no connector after this, and `/proc/bus/input/devices`
has no touchscreen. The machine continues to operate and SSH continues to
operate, thus a person can put `config.txt` back.

`display_auto_detect=1` selects the correct interface. Keep it at 1.

The panel is `ili9881c-dsi`. The driver gives it the name `dsi-5inch`, with 2
lanes, on `1f00118000.dsi`.

---

## 11. The images of the start and the stop

The appliance shows a mark on a black ground while the system starts and while
it stops. The software of the appliance does not make these images: the system
shows them before the process starts and after it stops.

### 11.1 The files

| File | Function |
| --- | --- |
| `deploy/assets/boot-splash-720x1280.png` | The start |
| `deploy/assets/shutdown-splash-720x1280.png` | The stop |
| `deploy/plymouth/gemma.plymouth` | The theme |
| `deploy/plymouth/gemma.script` | The script of the theme |

**CAUTION: the two images are 720 x 1280 and the art in them is 90 degrees
around.** The panel is native portrait. `video=` of `cmdline.txt` turns the
console only, and Plymouth draws before that applies. Thus an image that is
upright in its file comes on the panel on its side.

To make sure of an image, turn it 90 degrees in the other direction and look at
it. The art must then be upright.

The mark in these two files is the mark of `Assets/Branding.axaml`, which is a
placeholder. The owner puts the mark of the brand in its position. Do not put a
file of a brand in git.

**`deploy/assets/branded/` is the location for that mark.** Put a file there
with the same name as the placeholder, and `deploy-pi.sh --with-splash` takes
it in place of the placeholder. Each file is taken on its own, thus an
appliance can give its own image of the start and keep the placeholder of the
stop. `.gitignore` holds each file of that directory out of git, and the script
gives a warning for a name that the theme does not use.
`deploy/assets/branded/README.md` gives the three properties that a file needs.

### 11.2 Installation

`deploy-pi.sh --with-splash` does each step below in its step 8, and it does
none of them without that argument. The steps stay here because a person who
examines the theme, or removes it, does them one at a time.

```bash
sudo apt-get install -y plymouth plymouth-themes
sudo mkdir -p /usr/share/plymouth/themes/gemma
sudo cp deploy/plymouth/gemma.plymouth deploy/plymouth/gemma.script \
        deploy/assets/*.png /usr/share/plymouth/themes/gemma/
sudo plymouth-set-default-theme -R gemma
```

`-R` makes the initramfs again. Without it the system keeps the theme that it
had.

Then put these words in `/boot/firmware/cmdline.txt`, on the one line that is
already there:

```text
quiet splash logo.nologo vt.global_cursor_default=0 loglevel=3
```

| Word | Function |
| --- | --- |
| `quiet` | The kernel writes less. |
| `splash` | Plymouth operates. |
| `logo.nologo` | The raspberries at the top of the display go away. |
| `vt.global_cursor_default=0` | The cursor of the console does not blink. |
| `loglevel=3` | A message of the system does not come on top of the image. |

**CAUTION: `cmdline.txt` is one line. A new line in it stops the machine at the
start.** See section 8.13.

### 11.3 How to make sure of the work

**The theme is installed on the appliance and the machine started with it.**
`deploy-pi.sh --with-splash` did each step of 11.2, and these are the
measurements:

| Item | Measured |
| --- | --- |
| `plymouth-set-default-theme` | `gemma` |
| `plymouth-quit-wait.service` | `Result=success`, **29 ms** |
| `plymouth-start.service` | 161 ms |
| `plymouthd` after the start | Gone. It gave the panel back. |
| The translator | `active`, and its mark is on the panel |

**Thus the fear that section 11.3 held is not what occurs on this machine:**
Plymouth takes the DRM device, gives it back inside 30 ms, and the software
gets the panel. The limit of 30 s below never applies.

**CAUTION: the limit stays, because 29 ms is one measurement and not a
promise.** `/usr/lib/systemd/system/plymouth-quit-wait.service` of Debian gives
`TimeoutSec=0`, and 0 in systemd is INFINITY. The unit of the translator is
`After=` that service, thus with no limit a plymouth that does not stop keeps
the panel for ever, and this appliance has no keyboard. `deploy-pi.sh` writes
`/etc/systemd/system/plymouth-quit-wait.service.d/99-gemma-timeout.conf` with
`TimeoutStartSec=30` and `JobTimeoutSec=30`. Do not remove that file.

`plymouth-set-default-theme` is in `/usr/sbin`, thus a shell that is not a
login shell needs the full path.

1. `sudo /usr/sbin/plymouth-set-default-theme` with no argument gives the name
   of the theme. It must give `gemma`.
2. Start the machine again and look at the display. The mark must be upright
   and in the middle.
3. `sudo plymouthd; sudo plymouth --show-splash` shows the image with no start
   of the machine.

   **CAUTION: do this step through SSH, and not on the console of the
   appliance.** `plymouthd` takes the DRM device. While it holds that device
   the software of the appliance cannot get the panel, thus the display keeps
   the image and the translator does not come. There is no keyboard on the
   appliance to correct that condition.

   `sudo plymouth --quit` is necessary and it is not optional. If the panel
   keeps the image after it:

   ```bash
   sudo systemctl stop plymouth-quit-wait.service
   sudo plymouth --quit
   sudo systemctl restart gemma-translator
   ```
4. `sudo systemctl poweroff` shows the image of the stop.

If the mark is on its side, the images are not correct: turn each one 90
degrees and put them in the theme again. If the display stays black, examine
`journalctl -b -u plymouth-start` and make sure that `splash` is in
`/proc/cmdline`.

### 11.4 Before the disk of the appliance gets encryption

**CAUTION: `gemma.script` has no `display_password` callback.** The appliance
has no keyboard and no password now, thus it needs none.

A disk with encryption asks for a password at the start. Plymouth calls
`display_password`, it finds nothing, and the machine stays at the image with
no prompt and no cause, for ever. Put that callback in the theme before the
disk gets encryption, and not after.

### 11.5 The mark of the software, and the one rule for the three assets

`deploy/assets/` holds three assets with no brand and `deploy/assets/branded/`
takes the same three with the brand of the owner. **One rule covers all three:
if the branded file is there it is used, and if it is not the file with no brand
is used. Each file follows the rule on its own.**

| File | Who draws it | How it arrives |
| --- | --- | --- |
| `boot-splash-720x1280.png` | Plymouth | `deploy-pi.sh --with-splash`, step 8 |
| `shutdown-splash-720x1280.png` | Plymouth | `deploy-pi.sh --with-splash`, step 8 |
| `brand-mark.svg` | The software | The publish of step 4 puts it beside the binary |

The two images go to `/usr/share/plymouth/themes/gemma/`. The mark goes to
`publish/Assets/` and `publish/Assets/branded/`, and the software reads it at
each start. Thus **a person can put a mark on an appliance that operates and
restart it, with no build**:

```bash
cp brand-mark.svg ~/develop/gemma-translator/publish/Assets/branded/
sudo systemctl restart gemma-translator
journalctl -u gemma-translator -b | grep -i mark
```

The software draws the mark as the designer authored it. It does not recolour
it and it does not follow the theme with it.

**CAUTION: the letters of a mark must be outlines and not text.** A `<text>`
element sends the reader of the SVG to the fonts of the system, and this machine
carries four faces of DejaVu. The fonts that the software supplies to its own
user interface are a different font manager and the reader never asks it. A mark
with live text looks correct on the development host, which has the font, and
wrong here. `deploy/assets/branded/README.md` gives each property that a file
needs.

---

## 12. The limits of the unit of the translator

`deploy/gemma-translator.service` is the unit of the appliance. `deploy-pi.sh`
puts the account, the project directory and the uid in it in its step 8.

Two lines of that unit keep the speech of a person off the SD card and away
from the other processes of the account. The unit gives the full text on each
one. Read it before you change it.

| Line | What it stops |
| --- | --- |
| `Environment=DOTNET_EnableDiagnostics=0` | A process of the same account cannot order a full dump of the memory through the diagnostic socket of .NET in `/tmp`. |
| `LimitCORE=0` | A fault in native code does not write a core file that holds the recording, the transcripts and the sound of the answer. |

The speech part operates in the process of the user interface now. Thus one
dump or one core file holds all of it, and not the words alone.

**CAUTION: `LimitCORE=0` stops a core file only.** `core(5)` says that the
limit "is not enforced for core dumps that are piped to a program". Thus
`systemd-coredump` and `apport` make that path again. Do not install one of
them on the appliance.

The other limits are the same group as the limits of the guard of section 9.
`ProtectSystem=strict` makes the file system read-only, and `ReadWritePaths=`
gives back the four paths that the appliance writes:

| Path | What writes it |
| --- | --- |
| `~/.config` | The settings of a person, in `gemma-translator/user-settings.json`. |
| `~/.cache` | The models of Moonshine, and the cache of Hugging Face of `litert-lm`. |
| `~/.local` | The data and the state of the packages of Python, through platformdirs. |
| `/tmp` | A temporary file of .NET or of Python. |

The project directory is not in that group, because the software writes nothing
in it. Thus `publish/`, `venv/` and the scripts are read-only while the
appliance operates.

`PrivateDevices=`, `SystemCallFilter=` and `RestrictRealtime=` are not in the
unit. The panel, the buttons, the speakerphone and the audio thread need what
each of those three takes away. The unit gives the cause for each one.

### 12.1 How to make sure of the limits

**These steps are done on the appliance.** `systemd-analyze verify` gives no
line, thus the unit is well formed, and `systemd-analyze security` gives
**6.5 MEDIUM**. The one item that it names and that this project has not taken
is `UMask=`, which it scores at 0.1: a file that the software makes is readable
by each account of the machine. The software writes `user-settings.json` and
nothing else, and that file holds an accent colour and a language. Take `UMask=`
if the software ever writes a file that holds the speech of a person, and that
is a change that section 5.4 of `CLAUDE.md` does not permit on its own.

The steps below give the same result again.

```bash
systemd-analyze verify /etc/systemd/system/gemma-translator.service
systemd-analyze security gemma-translator.service
systemctl show -p LimitCore -p Environment gemma-translator.service
```

Do the test on the file in `/etc/systemd/system/`, which `deploy-pi.sh` writes.
The file in git holds `{{USER}}` and `{{PROJECT_DIR}}`, and those are not
paths.

Then make sure that the appliance still operates: the panel shows the user
interface, the two buttons record, the speakerphone speaks, and a change in the
settings screen stays after `systemctl restart gemma-translator`. The last one
is the test of `ReadWritePaths=`. The paths in that line are
`/home/<account>/...`, because `%h` gives `/root` in a system unit. An account
with a home in a different place keeps its display and loses that file, and the
journal then gives "The software did not write ...".

---

## 13. The pins of Python and their hashes

`backend/requirements.txt` holds 41 pinned packages, and each one carries the
sha256 of its wheel. `setup.sh` installs with `--require-hashes`, thus a
package that does not match its hash does not go in, and the installation
stops with a message about tampering.

### 13.1 The index is part of the control

**CAUTION: Raspberry Pi OS does not go to PyPI by default.** The image ships
`/etc/pip.conf` with this line:

```text
[global]
extra-index-url=https://www.piwheels.org/simple
```

piwheels is a service that **builds its own wheel** of a package for the
Raspberry Pi. It does not serve the file that PyPI has. The two carry the same
version and different bytes, thus a hash of the one does not match the other.

A measurement of this repository shows the effect. `anyio 4.14.1` is a wheel of
pure Python, and it is not the same file on the two services:

| Source | sha256 of `anyio-4.14.1-py3-none-any.whl` |
| --- | --- |
| PyPI | `4e5533c5b8ff0a24f5d7a176cbe6877129cd183893f66b537f8f227d10527d72` |
| piwheels | `7e463996095e5923f5a6d201ede676b3f107b77f73c0980f577f333b6b2871b1` |

Thus `setup.sh` gives two things, and both are necessary:

```bash
PIP_CONFIG_FILE=/dev/null pip install --require-hashes \
    --index-url https://pypi.org/simple/ -r backend/requirements.txt
```

- `PIP_CONFIG_FILE=/dev/null` makes pip read no configuration file, thus the
  `extra-index-url` of the image does not apply.
- `--index-url` names PyPI as the one index.

With neither, each hash fails and the message reads like an attack. It is not
one.

**CAUTION: `PIP_CONFIG_FILE=/dev/null` operates on Linux only.** pip stops at
the configuration file when the value is the null device of the machine, and on
Windows that name is `nul`. `setup.sh` is a script of Linux, thus this is
correct where it stands, and a person who uses the same line on the development
host does not get the same effect.

### 13.2 How to make the file again

A change of a version needs the hash of each new wheel. Do this on the
Raspberry Pi **and** on the Windows host, because ten of the packages ship a
different wheel for each platform:

```bash
grep -oE '^[A-Za-z0-9._-]+==[0-9A-Za-z.]+' backend/requirements.txt > pins.txt
PIP_CONFIG_FILE=/dev/null pip download --no-deps \
    --index-url https://pypi.org/simple/ --dest wheels -r pins.txt
sha256sum wheels/*.whl
```

Put one `--hash=sha256:` line below each pin. A package with a wheel for each
platform gets two, and pip takes the one that matches the file it downloads.

The ten with two hashes are `cffi`, `charset-normalizer`, `hf-xet`,
`litert-lm-api`, `moonshine-voice`, `numpy`, `protobuf`, `PyYAML`,
`sounddevice`, and `tomli`. The other 31 wheels are pure Python and are one
file on the two platforms.

`colorama` is in the file with a marker of the platform, because `click` and
`tqdm` ask for it on Windows and the appliance never installs it.
`--require-hashes` needs each package of the graph, and not the ones that this
project names.

### 13.3 How to make sure

```bash
PIP_CONFIG_FILE=/dev/null pip install --require-hashes \
    --index-url https://pypi.org/simple/ --dry-run --ignore-installed \
    -r backend/requirements.txt
```

It gives `Would install ...` with 40 packages on the Raspberry Pi and 41 on
Windows, and it gives 0. A hash that does not agree names its package and gives
the value that it expected and the value that it got.

---

## 14. The cell of the real time clock

The Raspberry Pi 5 holds the time while it has no power, and it needs a cell on
the J5 connector to do it. **This appliance needs that more than a machine on a
desk does: it operates offline, thus no NTP corrects the clock in the field, and
the screensaver puts the time on the panel.** With no cell each start begins at
the last time that the file system recorded.

### 14.1 The cell

| Item | Value |
| --- | --- |
| The cell | Panasonic **ML-2020**, which Raspberry Pi sells as RPI-23926 |
| The chemistry | Lithium-manganese, and it takes a charge |
| The window of the charge | 2.8 V to 3.2 V |
| Nominal | 3.0 V, 45 mAh, 20 mm x 2.0 mm |

**CAUTION: THE CHARGER IS OFF UNTIL A PERSON PUTS IT ON, AND THE CHEMISTRY OF
THE CELL DECIDES WHETHER THAT IS SAFE.**

| Cell | The charger |
| --- | --- |
| ML-2020, ML2032 | Correct. It takes a charge. |
| **CR2032** | **NEVER.** A primary cell of lithium leaks, opens or bursts. |
| **LIR2032** | **NEVER.** Lithium-ion, and a different chemistry. |

Raspberry Pi names the second and the third as wrong for this connector. The
default of the firmware is a charger that is off, thus a machine that gets no
argument is safe with any of the three.

### 14.2 How to put the charger on

```bash
./deploy-pi.sh --with-rtc-charge
```

It writes one line and the machine must start again:

```ini
dtparam=rtc_bbat_vchg=3000000
```

3.0 V sits in the middle of the window of the ML-2020. The overlays README of
the firmware gives the parameter: "Set the RTC backup battery charging voltage
in microvolts. If set to 0 or not specified, the trickle charger is disabled.
(2712 only, default 0)".

**CAUTION: `charging_voltage_max` gives 4400000 and that is not a value for
this cell.** It is the range of the driver, which serves other chemistries.
4.4 V corrodes an ML-2020.

### 14.3 How to make sure

```bash
grep . /sys/class/rtc/rtc0/charging_voltage /sys/class/rtc/rtc0/battery_voltage
timedatectl | grep -iE "RTC time|Local time"
```

Measured on this appliance:

| Moment | `charging_voltage` | `battery_voltage` |
| --- | --- | --- |
| Before the line | 0 | 2 720 510 |
| A few minutes after the restart | 3 000 000 | 2 889 740 |
| Later | 3 000 000 | 2 903 416 |

A cell that is not there reads near 0. `RTC time` is in UTC and `Local time` is
the time of the place, thus the two differ by the offset and that is correct.

**The last proof needs a full loss of power, and no command gives it.** Note the
time, stop the machine, take the mains AND the cells of the X1201 away, wait,
and give the power back. Read `timedatectl` before NTP corrects anything. A cell
that holds gives the correct time; a cell that is empty gives a date near the
epoch. Wait a day after 14.2 first, so that the cell is full.

---

## 15. The fan of the Active Cooler on GPIO12

**CAUTION: THIS SECTION IS FOR A BOARD WHOSE 4-PIN FAN SOCKET IS GONE.** The
socket of this appliance was damaged and a person removed it. On a Raspberry Pi
5 that still has its socket, do not do this: the overlay takes the fan away
from the pin of the socket, and the fan of that socket then gets no control.

### 15.1 The wiring

| Wire | Goes to | What it is |
| --- | --- | --- |
| Red | 5 V of the X1201 | the supply |
| Black | Ground of the X1201 | the return, and the reference of the two signals |
| Blue | GPIO12 | the PWM that the Raspberry Pi drives |
| Yellow | GPIO13 | the tachometer that the fan drives |

The ground must land. The two signals have no return path without it, and the
X1201 shares its ground with the Raspberry Pi through the pogo pins.

**The colours came from a measurement and not from a document.** The product
brief `RP-008188-DS-2` gives "5 V DC supplied via four-pin fan header on
Raspberry Pi 5. Pulse width modulation control with tachometer. 1.09 CFM.
8000 RPM +/- 15%", and it gives no pinout. To tell the two signals apart, make
each pin an input and see which one moves:

```bash
sudo pinctrl set 12 ip pu
sudo pinctrl set 13 ip pu
gpiomon -c gpiochip0 -b pull-up -e both 12 13
```

The line that toggles is the tachometer. Both pins stay inputs, thus no two
outputs can meet.

### 15.2 Why the fan stops when a person connects the blue wire

GPIO12 has a pull-down and no function until an overlay gives it one:

```text
12: no    pd | -- // GPIO12 = none
```

The PWM input of a 4-wire fan has a pull-up on the fan, thus a wire that is
loose gives 100 %. A pin that is held low gives 0 %, and the fan stops. This is
not a fault of the fan and not of the wiring.

### 15.3 How to put it on

```bash
./deploy-pi.sh --with-gpio-fan
sudo reboot
```

The argument is not optional. Without it the installation leaves the fan alone,
because the overlay is wrong for a board with a socket. See section 15.1.

**CAUTION: DO NOT TEST THIS OVERLAY WITH THE `dtoverlay` COMMAND.**
`rp1_pwm_remove` of kernel 6.18.34 reads a null pointer when a runtime overlay
that started the RP1 PWM goes away:

```text
Unable to handle kernel NULL pointer dereference at virtual address 0000000000000008
pc : rp1_pwm_remove+0x1c/0x48
```

The `dtoverlay` process then stays in state D, it holds the locks of the driver
core, each later overlay operation waits for ever, and only a restart of the
machine clears it. An overlay of `config.txt` never takes that path, because
the firmware puts it in the device tree and nothing removes it.

To measure the fan without an overlay, use `/sys/class/pwm/` directly.
**`pwmchip0` is the regulator of the touchscreen. Do not write to it.** The PWM
of the RP1 is the chip whose device is `1f00098000.pwm`.

### 15.4 How to make sure

```bash
cat /sys/class/thermal/cooling_device0/type     # pwm-fan
cat /sys/class/thermal/cooling_device0/cur_state
pinctrl get 12                                  # 12: a0 pn | lo // GPIO12 = PWM0_CHAN0
```

The fan gives 2 pulses for each revolution, thus the speed is the count of the
rising edges in one second, multiplied by 30:

```bash
timeout 2 gpiomon -c gpiochip0 -b pull-up -e rising 13 | wc -l   # x 15 gives RPM
```

A state of 0 with 0 RPM below 50 degrees is correct and it is not a fault. The
appliance is then silent, which the microphone needs. To see the governor
operate, load the four cores and read the three values again.

The measurement of this appliance:

| Condition | Temperature | State | RPM |
| --- | --- | --- | --- |
| Idle | 45.2 C | 0 | 0 |
| Load | 54.0 C | 1 | 2520 |
| Load | 59.0 C | 2 | 4185 |
| Load | 61.1 C | 2 | 4350 |
| After the load | 51.8 C | 1 | 2715 |

**The appliance is not silent at idle with the software running.** The
translator holds the machine at about 53 degrees, which is above the first
trip, thus the fan turns at about 2670 RPM. That is far below the 8750 RPM of a
fan with no control, and it is not zero. `dtparam=fan_temp0=58000` in
`config.txt` buys silence and gives away some margin. Measure the transcription
before and after such a change.
