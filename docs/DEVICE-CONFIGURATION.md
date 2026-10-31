# LAN device creation and configuration transfer

The Devices page now separates new registration from editing a selected row. **New device** clears both the selection and current cell while suppressing selection events. It defaults to a Blink camera, retains the previously selected parent module when possible, and selects the sole module automatically. The editor heading says whether the operation creates a new device or edits an existing one. Saving a new camera assigns a new ID; changing an explicitly selected row edits that device.

## Export and import

Use **Export cameras / links** and **Import cameras / links** in Windows Devices. The paired browser device editor also offers these controls.

The versioned JSON contains registered LAN devices, stable device IDs, IP/MAC addresses, parent-module IDs, and Guard Post bindings (provider/system/camera IDs). It contains no passwords, Blink sessions, router credentials, browser pairing keys, footage or thumbnails. It does not provision cameras on Blink, change account settings, or transfer camera-wall browser preferences.

- **Merge by device ID** is the default. Matching IDs update existing registrations; unrelated devices and links remain. Incoming explicit links replace links for those devices. An omitted link does not unlink an existing device during merge.
- **Replace all** restores precisely the imported devices and links, removing local entries absent from the file. Windows previews the resulting device/link counts before confirmation. The browser asks for confirmation before submitting the file.
- IP/MAC collisions across different IDs are rejected rather than silently overwriting a registration. Use Replace all when restoring a full backup onto another installation whose seeded device IDs differ.
- The complete result is checked before any saved configuration is changed: supported schema/types, unique IDs and addresses, valid parent modules, and unique valid Guard Post bindings. Malformed files, unsupported versions and files larger than 2 MiB are rejected.
- Bindings retain provider/system/camera IDs even before cloud discovery, so importing does not require Blink sign-in. Those IDs must match the intended account/cameras for the restored bindings to be useful.

## Persistence and compatibility

Devices and links now share a single atomically replaced configuration document at the existing LAN registry path. Old array-based registries and their `.activity-links.json` sidecars are still read; the first save migrates the configuration into the combined format. A subsequent restart reads devices and links together. Older app versions that only understand the array format cannot read this new document. Keep an exported configuration when moving between installations.

Removing a camera also removes its persisted Guard Post binding. Import clears old reachability and historical replay results. Live monitoring remains selected; after changing observed IP/MAC addresses, restart monitoring to establish fresh tracked connections.

## Verification

The actual WinForms regression starts from a selected module, presses New, verifies the module remains selectable, saves another camera, and checks that every original device is unchanged. It also checks switching between camera and module types.

Offline tests cover export/replace/merge, stable IDs and link restoration after restart, legacy migration, invalid/conflicting imports leaving disk and memory unchanged, pairing and mutation-header requirements, and oversized requests. Browser checks exercise camera creation with a selected parent and upload an exported file through the import control. Existing offline and browser regression suites are also run.
