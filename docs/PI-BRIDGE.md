# Raspberry Pi USB storage bridge — experimental design

This is a design/runbook, not a tested installation or a Blink-supported feature. No Pi or Sync Module configuration was changed.

## Proposed topology

```text
Outdoor 3 cameras → Sync Module 2 USB-A (USB host)
                            │ USB data cable
                            ▼
                  Pi Zero 2 W USB data/OTG port (USB device)
                            │
                   exFAT mass-storage image
                            │ controlled handoff
                   separate archive on Pi storage
                            │ Wi-Fi / LAN
              Windows SMB folder import / future Pi web service
```

A NAS share cannot simply be plugged into the module. The Pi must impersonate USB mass storage. The Zero 2 W has an appropriate USB data port; its PWR IN connector is not the data port. Raspberry Pi recommends the Zero family or Compute Modules for gadget work. A Pi 4/5 has power considerations and is unnecessary for this prototype. [Raspberry Pi OTG application note](https://pip-assets.raspberrypi.com/categories/685-app-notes-guides-whitepapers/documents/RP-009276-WP-1-Using%20OTG%20mode%20on%20Raspberry%20Pi%20SBCs)

Use a suitable microSD/storage device with room for OS, the complete advertised USB image and a separate archive. Provision a test image within the Sync Module's supported size range, rather than advertising the entire OS disk. Let the module format only that disposable image if required. Verify partition/volume layout afterward. Never expose the OS block device to Blink.

Check power draw, cable quality and recovery on a bench before installation. The module's ability to power a flash drive is not proof it can reliably power a Pi under Wi-Fi load. Use an electrically appropriate independent supply/data arrangement that avoids unintended backfeed; verify against the selected board and cable. A passive USB splitter does not provide safe storage sharing.

## The essential constraint

The Sync Module and Linux must not mount and modify the same exFAT filesystem concurrently. Linux's mass-storage gadget documentation explicitly forbids another process modifying its backing file; reads while the USB host can write may also be inconsistent because the host buffers data. A read-only Linux mount during Blink writes is therefore not a reliable archive method. [Linux mass-storage gadget documentation](https://docs.kernel.org/usb/mass-storage.html)

SMB must serve a **separate archive directory**, never the active image, loop mount or block device. A raw copy taken during writes is not guaranteed to be a consistent filesystem snapshot. Syncing the Pi alone does not flush the Sync Module's host-side caches.

## Controlled handoff protocol

1. Confirm the system is using Local Storage, not subscription Clip Backup. Use a disposable test image and record several test events.
2. Request **Safe Eject** in the Blink app and wait for confirmation. Stop if ejection cannot be confirmed; an idle timer is not a substitute.
3. Detach the gadget's USB presentation and release its backing file through the configured gadget interface. Verify the LUN no longer has the image open. Do not force eject an active recording volume.
4. Attach the now-exclusive image as a **read-only** loop device. If it contains a partition table, mount the appropriate partition, not the entire image as a filesystem. Inspect the actual layout with `lsblk` first. An unpartitioned filesystem image needs a different mount target.
5. Copy complete MP4s to a separate Pi archive; compare sizes/hashes and log results. Keep the original USB volume unchanged in the first prototype.
6. Unmount Linux, detach the loop device and verify it is released. Only then reassign the image to the gadget and reconnect it to the Sync Module.
7. Confirm the module recognizes the storage again and a fresh motion event records successfully. A firmware/storage rescan may require manual action; do not assume unattended reconnection works until tested.
8. Share only the independent archive over SMB using a dedicated account and LAN-only firewall rules. In DontBlink select **Import USB / Pi folder** and choose the UNC export folder. Copied recordings remain available while the image is back with Blink.

There is a recording gap during handoff. Blink documents that local clips require recognized attached storage, so this is not a lossless always-on bridge. Keep all originals until export verification and hardware recovery tests pass. Safe source deletion requires a further **exclusive writable** handoff or Blink's own deletion commands; deleting exported archive copies does not free the module image.

## Prototype acceptance criteria

- Module accepts the gadget as storage, including after reboot and reconnect.
- Repeated events produce valid MP4s; actual video and audio playback is verified.
- Safe eject produces a releasable LUN, exports remain consistent and recording resumes.
- Simulated Wi-Fi outage, Pi reboot, module reboot and unexpected power loss do not corrupt the OS or silently destroy the archive; filesystem damage is detected and reported.
- Full image stops recording visibly; separate archive growth is monitored.
- Windows can import from the authenticated SMB export; S21 can play the imported clips through DontBlink on the PC.
- Export gap duration is measured and acceptable to the user.

If uninterrupted recording is essential, use the account-based download queue while retaining ordinary USB storage, or prove a more sophisticated consistent snapshot mechanism with the host first. A simple dual mount cannot satisfy that requirement.

## Prior-art review — 31 October 2026

[BlinkPi](https://github.com/OVR92/BlinkPi) supplies a working-project reference for the gadget/export approach and a useful USB filename parser. Its concurrent read-only mounting and best-effort pre-wipe sync do not satisfy this document's exclusive-handoff and verified-archive requirements. See [the assimilation](BLINK-PRIOR-ART.md) for source-level differences and import follow-up. No Pi integration was installed during that review.

