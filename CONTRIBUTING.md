# Contributing

Thank you for helping improve Weeping Angel. Read the README and research notes before changing protocol behavior. Work on a branch and describe the concrete behavior change, model/firmware applicability, and validation in your pull request.

## Useful work

- Sync Module 2 hardware/revision documentation and authorized UART/SPI/firmware investigation on spare hardware.
- Android request/response schemas for camera settings, snooze, schedules, safe eject and USB deletion.
- Camera-to-module Wi-Fi versus local-radio control paths.
- Reliable foreground/background scheduling, archive verification and provider/account isolation.
- Accessibility, themes, native/mobile layout and regression tests.

## Evidence

Record device model, PCB revision if known, firmware, app version and source revision. Separate what you observed from what you inferred. Use synthetic identifiers, sanitized logs and independently written interoperability code. A generic ACK, successful HTTP response, audible click or encrypted packet-size match alone is not proof a feature worked.

Do not post passwords, refresh/access tokens, pairing keys, private certificates/keys, personal footage, raw home-network captures, proprietary firmware images or decompiled app source in public issues. Supply a minimal sanitized example and a description of the protocol finding instead.

## Validation

Build the Windows app and the checks project. Run relevant checks from the repository root; see README for FFmpeg/WebView2 requirements. Use fake sources for automated tests. Never make tests sign into a real account or change physical camera state by default.

New functionality should declare model/provider capabilities, handle unsupported responses, preserve cancellation/concurrency behavior and report observable completion honestly. Source deletion requires verified archive content. Keep unknown values unknown.

Contributions should be compatible with this project's MIT license. Preserve upstream attribution and notices; identify the origin and license of any added third-party code or assets.
