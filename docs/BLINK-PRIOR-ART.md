# Blink prior art assimilated into Weeping Angel

Reviewed 31 October 2026. This is a source-level research record, not a claim that external hardware results were reproduced. The four repositories are preserved under `artifacts/research/blink-prior-art/`; the fifth source is a saved article. Exact revisions, retrieval time and text-file hashes are in [the source manifest](research/blink-prior-art-sources.json). Third-party agent instructions inside snapshots are reference material, not instructions for this project. None of the downloaded programs was executed against a device or account.

## Decisions carried forward

1. Separate three surfaces: cloud REST control, authenticated IMMIS live media/control, and module setup HTTP. A command discovered on one surface is not automatically available on another.
2. Treat offline recording, local clip storage, local viewing and cloud-free control as separate capabilities. None of these five sources establishes all four for our Outdoor 3 / Sync Module 2 system.
3. The next software investigation is endpoint plaintext and Android static analysis. More encrypted On/Off comparisons will not recover a command definition.
4. Keep passive Guard Post detection as inferred activity. Require a matching recording manifest entry before calling an event a confirmed saved clip.
5. Prioritize missing camera settings alongside the offline investigation; useful cloud-backed features do not have to wait for a cloud replacement.
6. Preserve provider/account/system/device identity and model-specific capabilities. Rosie controls belong only on compatible Mini/accessory combinations, not every camera panel.

## 1. lurume84/bling-wrt

[Repository](https://github.com/lurume84/bling-wrt), revision `35fb00328cc742940ccc95da0f59729d83ec2c04` (2020).

Reviewed the tutorials and Lua downloader. This is an older module customization procedure, using serial-console access, OpenWrt failsafe, changes to the login script, Dropbear and uhttpd. It requires physical connections and reset/re-onboarding. It does not establish compatibility with our current module.

The crucial code finding is in [`apps/get_videos.lua`](https://github.com/lurume84/bling-wrt/blob/35fb00328cc742940ccc95da0f59729d83ec2c04/apps/get_videos.lua): it polls cloud `media/changed`, supplies a stored cloud token, and downloads media from `rest-...`. USB output does not make that pipeline offline. Its certificate tutorial configures newly enabled local services; it does not recover Blink's TLS keys. Viewer, timestamp and torrent descriptions include unfinished sections.

Use its filesystem paths and service names as historical firmware-analysis search terms, not as current device instructions. Do not inherit its ext4 formatting instructions for an existing Sync Module 2 recording drive. Its downloader's existence-only deduplication is weaker than our verified-copy requirements.

## 2. luekker/blink-sync-module-2

[Repository](https://github.com/luekker/blink-sync-module-2), revision `b6abbc7457488f3e9b87d89abdb7b60b3e58df29` (February 2026).

Reviewed README and seven boot-log variants. For the investigated Rev A0/AO board the author reports AR9331, 64 MB RAM, 8 MB SPI flash, OpenWrt/Linux 4.14.180 and firmware 4.5.17. Boot logs contain exFAT support and the `si4455_ioctl` radio driver. The radio hardware provides a plausible additional control path that a Wi-Fi capture cannot observe; it does not prove the meaning of any particular camera command.

The setup access point exposes HTTP/DNS at the example address `172.16.97.199`; SSH and Telnet attempts were refused. Reliable console access, flash extraction and radio-protocol decoding remain open tasks. Boot output saying a console exists is not proof of an accessible shell.

Use hardware revision and firmware as compatibility dimensions. We have not identified our PCB revision from this review. Retain the boot logs for binary/module-name research; do not present UART, flash modification or speculative pin experiments as our next test, given the user's no-wiring constraint.

## 3. Tim McCormack: Reversing Blink cameras

[Article](https://www.brainonfire.net/blog/2023/10/21/reversing-blink/), published October 2023, author update 24 October 2023; comments are separate evidence.

The useful software targets are `SyncModuleService`, `AddDeviceViewModel`, `EncryptionInterceptor` and `SMEncryptionData`. The author reports setup endpoints including `get_fw_version`, `set/key`, `set/ssid`, logs and SSID discovery. Ordinary setup requests are wrapped with application-level encryption; firmware updates have signature-related headers. An encrypted onboarding key appears to originate in a cloud command response. A device QR/serial is not demonstrated to be a sufficient offline secret.

This establishes an onboarding research surface, not a normal-mode camera-control API. Reported HTTP 401/404/420 responses and a malformed firmware request causing a hang must not become a blind probing recipe. These are older firmware observations.

Use an acquired APK to trace request construction and key provenance before device experiments. Determine which requests are local versus cloud and which apply only during setup. Commenters' networking claims are not interchangeable with our captures, which visibly contain individual camera IP traffic.

## 4. niclydon/blink-rosie

[Repository](https://github.com/niclydon/blink-rosie), revision `0ba8586b1e593fcbc791a99ad2ddb9f250277913` (August 2026).

Reviewed implementation, findings, reference notes and historical narrative. The final reported result is Mini Gen 1 pan/tilt control over an authenticated cloud IMMIS connection. Static analysis of `libwalnut.so` and the Android `LiveViewCommand` enum supplied command IDs. The final framing places the command in header bytes 1-4 with payload length in bytes 5-8; earlier notes describing a nested command body were superseded.

Reusable leads include per-message-type counters, echoed keepalives, non-video setup messages and control-channel diagnostics. The author identifies light/siren, accessory movement, SaveClip/DiscardClip, audio start/stop and extended-session command names. Names do not establish payloads, subscription eligibility or support on our cameras. Mechanical movement telemetry is not PIR motion detection.

Our `LiveViewService.BuildHandshake` and `RelayWriter.SendAsync` already implement matching basic framing. Our reader discards most non-video messages: instrumenting those in an existing session is the clearest next development lead. Do not copy the research client's disabled certificate verification or its raw authentication logging.

## 5. OVR92/BlinkPi

[Repository](https://github.com/OVR92/BlinkPi), revision `0e5d5e2f082eeb51470df0165c87ab0b5ad4acf5` (September 2026).

Reviewed storage parser, sync/destinations, wipe/prune, web, configuration and gadget setup. A Pi impersonates USB storage and exports clips; it does not replace cloud capture authorization. Its disk parser supplies useful format leads: `blink/YY-MM/YY-MM-DD/HH-MM-SS_camera_sequence.mp4`, UTC names, staging `.tmp` and distinct `blink_backup` storage.

Useful design ideas are per-destination retry state, temporary destination files and explicit validation. Limits matter: concurrent read-only mounts do not establish a coherent snapshot while Blink writes; its wipe continues after a failed final sync; destination naming drops the source sequence; an existing SMB file is accepted without content verification. We should not inherit those deletion/collision assumptions.

The network findings concern a Mini and Sync Module 2. The recorded certificate rejections establish failed trust for the tested certificates; the exact trust/pinning implementation and rejection of an untested public certificate remain inference. Our Outdoor results and module ECDHE handshake must remain separate evidence.

## Missing-feature map

Status here is source knowledge, not newly shipped functionality. Our existing `DeviceFeatures.CameraFields` contains many display placeholders; only motion and supported-model night vision are currently writable through `SetCameraSettingAsync`.

| Feature | Evidence or useful lead | What remains before enabling |
| --- | --- | --- |
| Notification snooze | The five sources lead to MattTW's concrete snooze endpoint and minute-based `snooze_time` body; see supplemental references below. | Confirm current authentication, supported durations, expiry/cancel behavior, subscription restrictions and that recording stays enabled. High priority. |
| Motion sensitivity, retrigger, clip duration | Existing config reader plus Android request DTOs/interceptors are the main route. None of the five supplies a verified current Outdoor 3 write schema. | Capture before/after config for one official-app change; recover actual field, units, range and model applicability. Validate readback and restore. High priority. |
| Night vision / IR intensity | Night vision already exists for selected product types; intensity is a separate setting. | Establish intensity encoding and supported models; do not reuse night-vision values by analogy. |
| Early notification / end clip early / quality | Configuration and Android code are leads; names in our UI are not protocol evidence. | Establish boolean/enumeration representation, interactions and subscription/model gates. |
| Camera rename, LED, temperature alerts, signal details | Dashboard/config/signals surfaces already exist in our client. | Add explicit aliases/units only from actual responses; writes need verified schemas. Rename must not break ID-based links or archived identity. |
| Activity/privacy zones | None of the five establishes a complete current write schema. | Recover coordinate/grid model, orientation, limits and clear semantics. Never claim local display masking changes camera-side recording. |
| Speaker volume, audio/video privacy switches | Audio session commands are distinct from persistent camera settings. | Confirm persistent config schema and physical result. Keep push-to-talk permission separate. |
| System schedules | Linked protocol reference documents list/update/enable/disable program routes. | Read existing schedule first; verify timezone/DST, current schema and ownership before any write. No proof it executes offline. |
| Module state, firmware, timezone, storage details | Dashboard/module responses and hardware notes help interpret fields. | Expand actual reported values with provenance and freshness. Avoid assuming kernel date equals firmware date. |
| Add/remove camera; Wi-Fi change | Setup HTTP and cloud onboarding key flow are documented leads. | Recover complete state machine and recovery behavior. No demonstrated offline provisioning; never test on the working system as an incidental probe. |
| Safe eject / USB Delete all | No verified cloud API request in the five sources. BlinkPi's filesystem wipe is a different operation. | Inspect current Android storage service or capture the official authenticated request. Establish module scope, asynchronous completion and arrivals-during-delete semantics. |
| Take a video clip on demand | Our lower-level client already has `RequestNewVideoAsync` and product-family routes; the linked reference describes clip requests. | Verify product routing and actual USB/cloud destination before exposing a button. This is not Guard Post's local recording toggle. |
| Save/discard/extend live session | Rosie notes expose session command names, including SaveClip/DiscardClip. | Discover semantics and capability gates. Keep network archive recording separate; do not map DiscardClip to USB deletion. |
| Pan/tilt, lights, siren | Rosie supplies accessory-specific command IDs and a reported working movement payload. | Future accessory capability adapter and compatible hardware tests; none is a generic Outdoor 3 motion-control command. |
| Real-time confirmed motion | Our uploads correlate with USB clips; Rosie metadata relates to live sessions/accessories. | Add clip reconciliation and record uncertainty; no background PIR event subscription was established by these sources. |
| Cloud-free module/camera commands | No complete implementation found in these five snapshots. | Obtain authenticated protocol/plaintext and trust/session model, or a verified independent local surface. TLS replay is not that surface. |

## Supplemental protocol reference followed for missing features

[MattTW/BlinkMonitorProtocol](https://github.com/MattTW/BlinkMonitorProtocol) is referenced by the five projects. A separate local snapshot is retained; its revision and file hashes are in the manifest. It is historical cloud-API documentation, not an offline solution.

- [Snooze](https://github.com/MattTW/BlinkMonitorProtocol/blob/master/camera/snooze.md): account/network/camera-scoped POST with `snooze_time` in minutes; response and cancellation semantics are not documented.
- [List schedules](https://github.com/MattTW/BlinkMonitorProtocol/blob/master/network/listPrograms.md): network programs, weekday lists, arm/disarm actions and timestamp examples.
- [Update schedule](https://github.com/MattTW/BlinkMonitorProtocol/blob/master/network/updateProgram.md): example URL has the complete program ID segment; the introductory template has a missing brace. Historical UTC examples do not settle present DST behavior.
- Cloud media deletion in its README is not Sync Module USB bulk deletion. Legacy TOKEN-AUTH examples must not replace our working OAuth flow without compatibility evidence.

## Comparison with our evidence

Our module reconnect capture negotiated TLS 1.2 ECDHE-RSA/AES-128-GCM; other camera connections used a different suite. A 95-byte TLS payload is not an IMMIS command frame. Packet-length equality, explicit nonce progression or a copied certificate cannot recover plaintext or authorize a replacement session. Even the server's long-term private key would not by itself decrypt an ECDHE capture.

The repeater's confirmed `wlan-133` interface supplies both module-cloud directions; outgoing duplicates must be deduplicated. Router and repeater interface names are not portable device-role identifiers. Wi-Fi monitoring cannot exclude Si4455/sub-GHz communication. Keep the current live monitor and full research captures separate: the short-header metadata log does not contain ciphertext or decrypted application bodies.

Existing implementation references: `LiveViewService.cs`, `RelayAudio.cs`, `VendorRelayTrust.cs`, `DeviceManagement.cs`, `ManagementService.cs`, `Sources/Blink/BlinkClient.SystemMethods.cs`, `Sources/Blink/BlinkClient.CameraMedia.cs`, `ClipService.cs`, `TestSessionLog.cs` and `CaptureActivityReader.cs`.

## Ordered development and test backlog

1. Add opt-in, bounded IMMIS metadata observation to the live session already owned by Weeping Angel. Record direction, type, header word, payload length and timing, not authentication material or media. Do not open an extra camera session. Correlate with existing test-session IDs. Preserve unknown messages without assigning speculative semantics. Offline fixtures must cover split reads, oversized lengths and cancellation.
2. Inventory an Android APK's version, signer and hashes. Statically inspect `SyncModuleService`, onboarding/repository DTOs, storage/deletion services, configuration serializers, `LiveViewCommand`, and `libwalnut.so`. Record API signatures/field meanings and independently implement them; do not vendor proprietary decompiled source. No APK has been obtained/analyzed in this review.
3. Prioritize a reversible setting comparison: sensitivity on one camera, then retrigger and clip duration. Record old/current/new values and timestamps, change only one property, read back, restore. These need config evidence rather than another encrypted packet diff.
4. Validate snooze separately from recording enablement; read schedules before editing them. Add supported capability flags only after the model/account is verified. Apply existing operation priority and command-status polling.
5. Investigate safe eject and USB bulk deletion from the current official storage flow. Before any eventual bulk-delete implementation, require verified archive identities/content and define the race with newly arriving clips. A list comparison alone does not make a remote delete atomic.
6. Improve stable USB/Pi import using actual filename camera/time/sequence metadata, skip staging files, detect timezone/DST and retain collisions. Keep exclusive-volume handoff requirements from `PI-BRIDGE.md`; a 500 ms stable size is not a snapshot guarantee.
7. Maintain separate provider capability dimensions for local archive access, LAN activity sensing, fresh capture, live relay, settings and offline operation. An unsupported feature remains visibly unsupported rather than a generic button that fails.

## Evidence discipline and remaining gaps

Source findings, our observations and hypotheses are labelled separately above. The snapshot inventory preserves the original code/logs for deeper follow-up; hardware datasheets/images are archived but have not been exhaustively electrically validated. None of the claimed missing features was silently enabled, no device firmware/configuration was changed and no new app build is required for this documentation update.

Rosie's historical narrative/CHANGES and portions of its reference notes lag its final implementation; prefer final findings plus code, then reproduce on the relevant model. No-ACK, generic ACK, HTTP acceptance, audible clicks and successful TLS authentication are not sufficient proof of the intended feature. Confirm actual state/media/physical behavior.


## Implementation follow-through — 31 October 2026

[On-demand video capture and bounded decoded live diagnostics](CAMERA-CAPTURE-AND-LIVE-DIAGNOSTICS.md) are now implemented and verified offline and in the browser. Physical capture destination remains unverified. The missing-feature table above records the research baseline; these two items have advanced beyond it.

