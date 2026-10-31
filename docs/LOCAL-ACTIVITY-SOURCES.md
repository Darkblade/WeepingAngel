# App-managed local activity sources

## FRITZ!Box direct stream

In Settings → Devices, retain the existing explicit LAN-device/Guard Post camera links. Scroll to Direct router monitoring. Stop Chrome's capture on that interface first; capture ownership is per interface, not per browser/application.

Enter router origin (default http://fritz.box), FRITZ!Box password and optional username, and interface (default1-ath0, based on the user's confirmed capture request). Start router monitoring. No browser download or capture file is created. The app opens one streaming request, reads short160-byte packet snapshots, counts original IP payload lengths from validated headers, classifies activity, and discards packet buffers. The existing32KiB experimental threshold and inferred Possible recording label apply. Raw payload is never persisted. Session state is bounded to256connections with at most4096merged TCP byte ranges per connection; only activity metadata is exposed to Guard Post.

The optional capture filter starts blank. Generate module filter produces tcp port443 with configured module addresses. Router acceptance of this filter must be verified on the actual firmware; unsupported/error responses pause monitoring rather than silently capturing unfiltered traffic. Clear the filter to troubleshoot. Both registered endpoints' IP/MAC pairs must still match in the local classifier, regardless of router filtering.

Transient connection loss/EOF clears pulses and reconnects with1,2,4,8,16,30-second capped backoff. Each connection obtains its own challenge-response session; cleanup requests Stop for the owned capture and logs out that session. Rejected credentials, router login block time, denied capture permission, unsupported capture format, and TLS verification problems pause for user action rather than repeatedly submitting passwords. Stop monitoring cancels reads/retries and requests router capture stop. Network failure can prevent confirmation of remote stop; inspect the router capture page if needed. No TLS certificate verification bypass is used.

Remember credentials is optional. The password is encrypted with Windows DPAPI in a dedicated router credential file; configuration JSON contains only router origin, username, interface, filter and preference flags. Router credentials, challenge responses and sessionIDs are not logged or sent to Guard Post. Start with app requires remembered credentials and is explicitly opt-in. Stopping monitoring leaves that startup preference unchanged; uncheck it and save to disable next-launch startup. The growing-file follower remains available as a fallback; switching sources stops the prior source.

Capture still runs continuously while this provider is enabled. The change removes Chrome, disk growth and repeated capture-start polling; it does not remove router capture overhead. Expect an unverified live-router compatibility check for login permissions, interface ID, short snapshot length and filter support.

Login follows the vendor's published PBKDF2 challenge-response, with legacy MD5 compatibility: https://fritz.support/resources/HTTP_Session-ID_EN.pdf . The capture Start/Stop request shapes come from the user's observed browser requests. They are web-interface endpoints, so firmware compatibility is not guaranteed.

## External network events (no app packet capture)

Select Use external network events in Settings → Devices. This stops the file/router source. It is a working generic input for a separate network sensor, not an automatic Blink notification subscription or native Sync Module event API. Those integrations remain unverified and are labelled accordingly. A real producer must observe the relevant traffic and submit reports. External reports are producer assertions, not packet identity verification, and are labelled External report in the activity tooltip.

The producer pairs through the existing /api/pair flow, retains its authentication cookie, and sends same-origin/private-LAN POST requests with Content-Type application/json and X-DontBlink:1. Example POST /api/local-activity/events body:

```json
{"deviceId":"REGISTERED-LAN-CAMERA-GUID","eventId":"unique-event-update-id","kind":"upload","uploadedBytes":40000,"observedUtc":"CURRENT-UTC-ISO-TIMESTAMP","source":"My network sensor"}
```

Kinds: connection, upload, end, refused. Counts are cumulative uploaded payload for the reported activity, capped256MiB. Timestamp must be within15seconds of now. Each update uses a unique eventId; retries with the same ID are deduplicated. The device must be a registered Blink camera and explicitly linked to a panel. upload above32KiB can pulse; end/refused clears it. Freshness timeouts and reduced-motion behavior remain the same. A stopped/unselected provider returns409, unpaired requests401, invalid/stale metadata400. This endpoint cannot start captures, set router addresses or accept passwords. Producer credentials remain the existing pairing credentials.

## Validation

Offline mocks match both vendor challenge-response example vectors; test segmented stream reads with short packet snapshots, identity matching/byte counting, credential rejection without retry, EOF reconnect/backoff, cancellation, capture-stop requests, and external source selection/deduplication/freshness/end events. HTTP tests verify pairing is required. Desktop tests keep capture status above Diagnostics. Browser tests cover pulse matching and stale/unavailable clearing. No agent test logs into the user's router or changes physical camera settings.

Password-only login: leave Username blank. The client reads the initial login response Users list and uses a sole advertised account or the unique generated fritzNNNN account. Explicit usernames are preserved. Ambiguous/hidden modern user lists pause without testing multiple accounts. Legacy MD5 password-only routers can still receive an empty username. This implements the vendor guidance at https://fritz.support/resources/Empfehlungen_zur_Benutzerfuhrung_bei_Anmeldung_an_FRITZBox_v1.1_DE.pdf .

Guard Post links are explicit, per LAN device, and persist across restarts. Only Garden camera was initially linked in the user configuration. Router/file detection can therefore report activity for an unlinked camera without pulsing a panel. The Windows activity status now marks such rows NOT LINKED TO GUARD POST; the LAN-device table has a Guard Post camera dropdown on each supported camera row. Choosing a camera saves immediately; Not linked clears the binding. Saved IDs remain selectable before camera discovery. The separate camera-links section has been removed. Device table, device editor, replay, router monitoring, external events and growing-file fallback each have their own container; status retains reserved space above Diagnostics. Growing-file controls are collapsed as an optional fallback. No name-based automatic reassignment or IP/MAC correction is performed.