# Weeping Angel: next tests and local-operation research

## Research update — 31 October 2026

The [five-project assimilation and missing-feature map](BLINK-PRIOR-ART.md) supersedes the historical packet-test queue below. Update Image, physical motion, internet isolation, repeated On/Off and module-reconnect captures have since been performed. Prioritize bounded decoded IMMIS observation, Android protocol analysis, and reversible configuration comparisons; see that document for evidence and acceptance criteria.

## Current feature boundaries

Implemented: persisted refresh-token restore; thumbnail age when Blink reports its timestamp; camera motion controls; persistent camera order; service/system selection; system arm/disarm; reported module status, firmware and last update; camera details; supported-model night vision; archive transfer diagnostics; Guard Post live recording to the configured archive.

Placeholders: RTSP/ONVIF adapters, notification snooze, camera provisioning/removal, scheduling, Wi-Fi changes, safe eject, zone editors and unverified camera-setting writes. Known settings are displayed read-only where available. Missing data remains unknown. Provider selection establishes UI scope; multiple active provider/account adapters are still future work.

## Verification status

User verified App regression, Archive observability and Guard Post recording on 30 October 2026. Next pending test: Update Image packet capture.

## Ordered tests

1. **App regression:** reopen the updated build and verify session restoration without SMS. Dismiss camera menus repeatedly. Drag cameras, restart and verify order. Test themes on Recording overlays, Guard Post and Services & systems.
2. **Archive observability:** select Detailed, then Trace. Copy a small batch. Verify total/processed/succeeded/failed/skipped counts, current bytes, elapsed time and ETA. ETA is unavailable until a clip completes and is approximate. Verify the destination MP4 plays before any original is removed. Repeat with a previously archived clip and a clip removed through the Blink app.
3. **Guard Post recording:** open Garden camera live view; start recording for 20–30 seconds; stop recording while leaving live view playing. Check the saved path and play the resulting file in the network archive. Repeat by stopping live view during recording. Verify audio, overlays, finalization and a clear error if the share is unavailable. Two-view limits still apply. Offline automated tests validate the recording backend, and the user has now verified real Blink/network-share behavior.
4. **Next packet capture — Update Image:** capture lan and ath0 simultaneously. Leave 30 seconds idle, update only Garden camera, note request and confirmation times, then wait 60 seconds before stopping. Keep the official app closed and pause automatic archive work. Preserve the original capture files and resulting JPEG. Repeat the same camera once, then Lower Door. This distinguishes repeatable image traffic from background connections more clearly than changing all eight at once.
5. **Motion-generated video:** with the intended system armed and one camera enabled, record an idle baseline, trigger a known brief movement, wait for the clip to appear, and archive the original MP4. Record trigger/upload/archive times, filename, size and SHA-256. Compare the camera, module and cloud traffic with the Update Image capture.
6. **System scope:** toggle one selected system to Disarmed, then Armed; confirm in the official app and restore the previous state. Check camera enable flags separately: system arming and individual camera motion settings are different controls.
7. **Settings verification:** use one camera and change one reversible setting at a time in Blink. Preserve before/after configuration and timestamps, then restore the original value. Start with night vision, clip duration and sensitivity. Verify snooze expiry and notification behavior before implementing it; snooze must not accidentally disable recording.
8. **Offline boundary:** after establishing a working baseline, use Fritz Box internet blocking while keeping LAN access. First isolate the module; separately test isolation of both module and cameras. Disable phone cellular fallback. Test motion-to-USB recording, fresh image, live view and controls individually; restore internet after each condition. Record exactly which operations continue. Existing cached images are not proof of fresh offline operation.
9. **Provider architecture:** test two synthetic providers with overlapping device IDs, separate system scopes and unsupported capabilities. Keep credentials, commands and saved layouts scoped to provider/account. Add actual RTSP/ONVIF adapters only after these boundaries are verified.

## What captures can establish

The observed module session uses TLS 1.2 with ECDHE/RSA and AES-GCM. Matching encrypted packet lengths or byte positions do not reveal command fields, camera IDs or reusable On/Off commands. Replaying an encrypted record into another TLS session is not a substitute for implementing its authenticated protocol. Cameras also contacted cloud endpoints after module reconnection. No direct camera IP traffic during motion toggles does not exclude a separate radio link.

Continue with non-invasive captures and controlled internet isolation. No soldering, wiring, factory reset or camera removal is required. Keep lan captures: they have shown traffic missing from ath0. Do not guess Fritz Box DTrace parameters; use documented controls only.
