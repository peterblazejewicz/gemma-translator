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
| `deploy/99-gemma-translator.rules` | The two udev rules that make a stable path for the buttons and for the touchscreen. |

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
sudo cp /boot/firmware/config.txt.gemma-backup /boot/firmware/config.txt
sudo rm -f /etc/udev/rules.d/99-gemma-translator.rules
sudo rm -f /boot/firmware/overlays/recorder-keys.dtbo
sudo udevadm control --reload
sudo reboot
```

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

**CAUTION: this step has no test on this hardware.**

Avalonia reads the touchscreen through libinput. libinput is not installed on
this machine, and the software of this fork has not started on it.

Thus nobody has seen that the touchscreen continues to operate after this
command. Do this step when the software first starts on the appliance, and
prepare to put the membership back:

```bash
sudo gpasswd -a "$USER" input
```
