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
| Computer | Raspberry Pi 5, 8 GB |
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
| Less than 3000000, for 3 reads together | Stop the machine |
| Less than 3200000, for 12 net reads | Stop the machine |
| 3250000 or more | Remove 1 from the count of the low reads |
| Between 3200000 and 3250000 | Hold the counts |
| The read gives nothing | Hold the counts |

3.20 V is the value that Geekworm uses in its own script.

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

The result must be `enabled` and `active`. A guard that operates writes its
values each 5 minutes, thus a journal with no such line is not correct.

**CAUTION: `systemctl start` does not make the guard operate after the next
start of the machine. Use `systemctl enable`.**

To see the guard stop the machine, move the low value above the voltage of a
full cell. The guard writes two CAUTION lines when it has this value:

```bash
sudo systemctl set-environment GEMMA_GUARD_LOW_UV=4300000
sudo systemctl restart gemma-battery-guard
journalctl -u gemma-battery-guard -f
```

The machine stops after 12 reads, which is 60 s. With the mains connected it
stops after 132 reads, which is 11 minutes, because of section 9.3.

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

**TO BE UNDERSTOOD: the journal of this machine is in memory and not on the
card.**

**Thus the line that gives the cause of a shutdown does not stay after the
machine starts again, and a person who asks why the appliance stopped has no
answer. `Storage=persistent` in `journald.conf` corrects this, and it writes
more to the card.**

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

`libinput10`, `libdrm2`, `libfontconfig1` and `libicu76` come with the image. `libicu76` is necessary, because `InvariantGlobalization` is `false`.

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

**The correction is in the software.** `SoundFlowAudioCapture` makes a
`FullDuplexDevice` and puts nothing in its mixer. Thus the playback interface
sends no sound for the life of the process, and the microphone operates.

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

`deploy-pi.sh` does not write this parameter. The appliance shows the software
and not the console, thus a console that turns is an aid to a person who does
maintenance.

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

### 11.2 Installation

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

**IMPORTANT: nobody has done these steps on the appliance.** The commands come
from the documents of Plymouth and of Raspberry Pi OS. Do each step and record
what occurs.

1. `plymouth-set-default-theme` with no argument gives the name of the theme.
   It must give `gemma`.
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
