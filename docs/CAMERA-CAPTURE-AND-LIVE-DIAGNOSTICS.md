# Camera capture requests and decoded live diagnostics

Implemented 31 October 2026 from the [prior-art review](BLINK-PRIOR-ART.md). These features still use the signed-in Blink session.

## Request video clip

Open a camera's `…` menu in Windows Home, mobile Home, or the Guard Post camera wall and choose **Request video clip**. Known Blink product families `catalina`, `owl`, and `lotus` use their existing camera-specific clip endpoints. Unknown models/providers remain unsupported.

The action asks Blink to capture a video without walking in front of the camera. It is separate from the Guard Post live-view Record button, which saves the live stream to the configured archive. This action does not start a live stream, change the camera's motion setting, or directly create an archive file.

The app waits for Blink's returned command to report successful completion. HTTP 200 without a usable command ID is insufficient. Completion is not proof that the clip is on USB, in cloud storage or in the network archive. Refresh USB clips to check the result; existing Auto Archive behavior remains in effect. Account, subscription, product and firmware may affect whether capture succeeds and its destination. No real-device capture was performed during implementation.

Requests are validated against the current camera roster and selected system. Duplicate in-flight requests for the same camera are rejected. A live-view reservation on the module blocks competing capture. Foreground operation priority is preserved. A failed, timed-out or disconnected request is not automatically retried: check for a clip before requesting another, because an already-sent command may still finish.

Windows logs requested/completed/failed outcomes. An active research test session also records action markers. The mobile and Guard Post actions update their notification text without rebuilding the thumbnails. Mobile requests disable only their own camera's capture action; other actions retain their existing behavior.

First device check: use Garden camera, close its live view, request one clip, and inspect Diagnostics plus the USB manifest. Note the clip's timestamp and actual destination. If it fails, retain the reported error and avoid repeated clicks. This validates the camera/firmware behavior the offline tests cannot establish.

## Decoded live protocol diagnostics

Choose **Trace** in Windows Diagnostics, then open one normal live view. The app observes the IMMIS messages already decoded by its own authenticated TLS connection. This adds no extra camera connection, router capture or firmware change.

Entries contain camera/system IDs, direction, message type, header word, payload length and elapsed session time. The header word can be a command ID, counter or another value depending on message type; it is deliberately not labelled a universal sequence number. Unknown message types remain unknown. Reported Stop/configuration names remain provisional interpretations.

At most 128 non-media frame entries are emitted per live session. Video/audio frames are counted for the end-of-session summary rather than logged individually. Detailed or Trace shows that summary, including frame/byte counts and suppressed-entry count. No authentication header, relay URL, serial, token, message payload, video or microphone bytes are included.

If a live research test session is active, the bounded frame metadata is also appended as `immis_frame` records to its rolling JSONL log. Capture that alongside action markers for timing comparisons. This observes the app-to-relay session; it does not decrypt the separate module-cloud or camera-module connections.

## Verification

Offline checks cover camera routing, tracked versus untracked command completion, duplicate requests, failure/cancellation cleanup, provider/model/system validation, module live conflicts, paired HTTP access, required mutation headers and signed-out behavior. Relay replay verifies decoded metadata arrives without logging media/authentication bytes. Diagnostics checks cover bounded output, unknown types and summary totals.

Actual Chromium/WebView2 checks exercise both new web menus and verify requests leave the existing cards/images intact. The full existing offline regression suite and browser UI suite passed. Build warnings are existing missing source-control/XML-documentation metadata. Physical capture behavior remains to be verified by the user.

Compatibility fix: the camera overview now falls back from missing product_type to Blink's type field and normalizes the family value. The prior parser left all eight user cameras Unknown, disabling their capture menus. Regression checks use this real homescreen shape. Known legacy white/xt/xt2 families also use the default camera route; completely missing families remain unsupported. Refresh Home after installing the fix to replace previously cached Unknown values.

