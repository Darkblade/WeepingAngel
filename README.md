# Weeping Angel

Experimental Windows camera application with a mobile web interface, a Guard Post camera wall, local/network clip archiving, and Blink protocol research.

**Blink controls and fresh capture currently require Blink cloud access. This is not a cloud-free replacement for Blink.** Local archive playback/import and passive LAN activity monitoring are separate capabilities.

## What works

- Windows desktop, paired mobile browser interface, and Guard Post camera wall.
- Blink OAuth refresh-token restoration using Windows DPAPI storage.
- Camera snapshots, motion controls, supported-model night vision, and system arm/disarm.
- USB clip archiving to a local or network folder, with verification before source removal and foreground interaction priority.
- Archive filters, previews, playback and multi-selection deletion.
- Experimental live view, recording to the archive, overlays and push-to-talk.
- LAN camera registration, explicit Guard Post bindings, and configuration export/import.
- FRITZ!Box capture monitoring, inferred upload alerts, rolling test logs and bounded decoded live-session metadata.
- On-demand video capture requests for recognized camera families. Command completion does not prove the clip reached USB or the archive; verify the actual destination.

Some controls remain placeholders or model-specific. RTSP/ONVIF adapters, safe eject, USB bulk deletion, provisioning, zones, snooze and scheduling are not yet complete. Saved credentials are optional; refresh tokens and device identity are encrypted for the current Windows user. No cloud credentials are included in this repository.

## Build and run

Requirements: Windows 11, .NET 10 SDK, and Microsoft Edge WebView2 Runtime. Network restore retrieves the declared NuGet dependencies.

```powershell
dotnet restore WeepingAngel.csproj
dotnet build WeepingAngel.csproj
dotnet run --project WeepingAngel.csproj
```

FFmpeg is optional for archive browsing, but required for live conversion/recording, overlays and the full regression suite. Install a Windows FFmpeg build separately and set its executable path before starting the application:

```powershell
$env:DONTBLINK_FFMPEG = 'C:\Tools\ffmpeg\bin\ffmpeg.exe'
dotnet run --project WeepingAngel.csproj
```

FFmpeg binaries are not distributed in this source repository. Review the license of the build you obtain. The application also searches for `tools\ffmpeg\bin\ffmpeg.exe` beside or above its executable.

The `Sources/Blink` client derives from [bvdcode/Blink.NET](https://github.com/bvdcode/Blink.NET), with interoperability updates. The legacy `DontBlink` namespaces, environment variables and application-data folder remain for compatibility.

## Getting started

1. Sign in to Blink in Windows and complete 2FA when requested. Session restoration can avoid a fresh SMS on subsequent launches, but revocation/server policy may require sign-in again.
2. Choose an archive folder. Review the Auto Archive setting: its workflow verifies a copied recording and then removes the USB original. Keep it off while exploring if you want originals retained on USB.
3. Open Home to load the camera roster. Use the camera menu for settings, image updates and clip requests. Live sessions are limited and battery-camera live viewing is on demand.
4. Configure Phone Access to browse from another device on a trusted LAN. Use the displayed pairing code. Keep the interface private; do not expose its ports to the internet. Optional HTTPS setup is available in the app.
5. Configure your actual LAN IP/MAC addresses and parent modules in Devices. The seeded entries are synthetic examples. Registering an address does not provision a camera on Blink. Guard Post links use provider/system/camera IDs, not names alone.
6. Export the camera/link configuration before moving between installations. Merge retains unrelated IDs; Replace all restores the imported inventory. See [configuration transfer](docs/DEVICE-CONFIGURATION.md).

Application settings and credentials stay under the current user's local application-data folder. The default archive is the Windows Videos folder; a local or UNC destination can be selected. Do not submit these private files to issues.

## Tests

Tests use synthetic camera/relay fixtures and do not require a real Blink login. The full suite requires FFmpeg; browser checks also require WebView2 and an interactive Windows desktop session.

```powershell
dotnet run --project Tests/DontBlink.Checks.csproj
dotnet run --project Tests/DontBlink.Checks.csproj -- --device-table-check
dotnet run --project Tests/DontBlink.Checks.csproj -- --web-ui-check
```

The LAN samples and regression fixtures use synthetic addresses. No packet captures, home-camera footage, credentials, downloaded research repositories or prebuilt application binaries are included.

## Research and contributions

We welcome .NET/UI work, protocol analysis, model/firmware testing and hardware research on devices you own. Particularly useful contributions are Sync Module 2 firmware/UART/SPI expertise, local radio/control-path analysis, verified camera settings, and safe eject/bulk-delete semantics.

- [Prior-art review and missing-feature map](docs/BLINK-PRIOR-ART.md)
- [Capture requests and decoded live diagnostics](docs/CAMERA-CAPTURE-AND-LIVE-DIAGNOSTICS.md)
- [Live test logs](docs/LIVE-TEST-SESSIONS.md)
- [Local activity sources](docs/LOCAL-ACTIVITY-SOURCES.md)
- [Archive operation priority](docs/ARCHIVE-PRIORITY.md)
- [Experimental Pi storage bridge design](docs/PI-BRIDGE.md)
- [Contributing](CONTRIBUTING.md)

A camera-to-module upload alert is labelled **Possible recording**, not confirmed motion or proof of a saved clip. TLS packet sizes do not reveal reusable command bytes. Results are firmware/model dependent; distinguish source observations, local tests and hypotheses.

## Buy me a coffee

If Weeping Angel is useful to you and you would like to support its development, you can send a coffee donation via **PayPal: darkblade@darkblade.de**.

## License and credits

Original project code is available under the [MIT License](LICENSE.md). Third-party components retain their own licenses; see [credits and notices](THIRD-PARTY-NOTICES.md), the Blink.NET license, security notices and font license.

This is an independent community project, not affiliated with Blink, Amazon or AVM.
