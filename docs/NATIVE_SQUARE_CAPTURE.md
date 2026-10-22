# Experimental native square capture source

The browser supports two distinct capture modes per selected headset:

- `raw-two-lens`: the existing distorted binocular display mirror.
- `square-left`: a coherent 1280 × 1280 LEFT image from the selected Meta native
  capture service, encoded on the headset. This is a separate capture source;
  the browser's left-eye presentation crop does not produce it.

Choose a capture mode, then explicitly Start. Stop the original capture and
wait for actual retirement before selecting another mode. Closing or reloading
an observer does not stop/restart its producer. Each headset retains one source
ID and one capture owner across its available choices. Switching one headset
does not replace the other headset's producer or the bilateral app session.

## Host selection

`--native-capture <absolute-private-json>` supplements the existing
`--owned-selection`, durable `--capture-owner` journal and authenticated
`--hub-bridge` service. The owned/native consumer is experimental and requires
that real app-owner service to be selected and available; it is not a standalone
native browser launcher. Do not invent a bridge selection or substitute a
modeled test owner for a live one. The public scrcpy `--selection` baseline
remains a standalone browser path; the separate Quest native helper also has
its own command-line entry independent of QMS.

Live hosts require an explicit `--host-end`. An offline fixture cannot select
live captures. The versioned native selection shape is:

```json
{
  "schema": "questms.native-square.selection.v1",
  "transport": "<complete existing HostSelection object>",
  "helper": {"path":"<selected external Quest helper>","bytes":1,"sha256":"<exact digest>"},
  "entryClass": "io.github.mesmerprism.rustyquest.nativecapture.NativeBrowserCapture",
  "globalNativeCaptureAuthorityAcknowledged": true,
  "driverSha256": "<exact selected default Driver APK digest>",
  "driverProfiles": {"q":"<optional exact source-specific Driver digest>"}
}
```

The placeholders above are explanatory, not executable configuration. Private
tools, targets, boot IDs, leases, ports and evidence belong in the local
selection. Canonical targets must be distinct. The transport must match the
native target's serial/canonical/boot/lease. An owned source must join the held,
hash-verified `rusty.hostess.display_stream.owner_binding.v1` target and ADB tool;
source-label equality is insufficient. Other binding schemas are not inferred.
The existing authenticated app-owner admission still applies in owned mode.

The separately licensed Quest helper stays out of the MIT browser source. It
uses genuine shell attribution, the published typed existing-service Binder,
normal privacy defaults, its own encoder Surface and a nonempty session ID.
The exact installed Driver APK and runtime interface are checked before Start.
There is no APK install, app launch, custom compositor shader or Camera2 input.

Native Stop is global and asynchronous, so this route requires an operator-owned
sole native capture scope on every selected headset and an explicit
`globalNativeCaptureAuthorityAcknowledged:true`. The host rejects omitted/false
acknowledgment before live tool or device checks. Coordinate other native
capture clients before granting it; the exact Quest lease is also checked. A session label and resource lease
are not an OS capture-session fence. Client retirement includes the original
Stop nonce, encoder/Surface/worker cleanup, real process exit and both pipe EOF.
It does not claim a native-server Surface-release acknowledgement. Failed or
unknown retirement holds the source and prevents replacement. Stage ownership
is tracked before process launch. Streaming periodically rechecks its exact
device, boot, lease and ADB daemon.

## API and media

`GET /api/sources` adds `captureModes`. `GET /api/captures` reports the actual
original's `captureMode`. Start accepts `{requestId,captureMode}`; omission
preserves the raw default. Stop remains mode-free and targets the original.
The durable owner uses action v4 to bind the mode into its original request;
legacy v2 raw actions and v3 cancel-original Stop remain compatible. A changed
mode cannot reuse an original request ID or replace an unresolved invocation.

Both modes reuse the existing H.264 fanout, observer and WebCodecs decoder.
Square metadata is `captureMode:square-left, frameLayout:mono`; raw is
`captureMode:raw-two-lens, frameLayout:stereo-sbs`. Mono disables the stereo eye
crop/blend heuristics and preserves the square image's aspect ratio.

The tested Windows `adb shell` client expands newline bytes on stdout. The
Quest helper therefore sends ASCII base64 over that process boundary; the host
decodes it before the unchanged RMANVID1 reader. Compressed AVC payload and
timestamps are preserved. Diagnostics use the separate stderr pipe. This adds
transport overhead but avoids corrupting compressed binary data.

The square image's absolute angular FOV and equivalence to every official
Cinematic setting remain uncalibrated. Browser capture is independent of
actual Q-to-W/W-to-Q app video and cannot supply its health or acceptance.

## Build and source-only checks

Build this MIT host with .NET 10 and test its inert controls:

```powershell
dotnet build src/QuestMultiStream.Browser/QuestMultiStream.Browser.csproj
dotnet run --project src/QuestMultiStream.Browser --no-build -- --self-test
dotnet run --project src/QuestMultiStream.Browser --no-build -- --native-square-self-test
dotnet run --project src/QuestMultiStream.Browser --no-build -- --capture-owner-self-test
dotnet run --project src/QuestMultiStream.Browser --no-build -- --candidate-self-test --fixture-python <absolute-python-executable>
node --test src/QuestMultiStream.Browser/wwwroot/*.test.mjs
```

The external AGPL Quest helper is published separately in
[rusty-quest/apps/native-browser-capture-android](https://github.com/MesmerPrism/rusty-quest/tree/main/apps/native-browser-capture-android).
Its build instructions produce `target/native-browser-capture-android/native-browser-capture.jar`;
set `helper.path`, byte count and SHA256 to those exact locally built bytes.
No helper binary or platform implementation is bundled here. Select the installed
Driver hash and complete private transport using the helper's documented firmware
constraints; never replace placeholders with an inferred device identity.

For an operator who already has the real authenticated app-owner service and
its reviewed selections, the combined invocation is:

```powershell
dotnet run --project src/QuestMultiStream.Browser -- --native-capture <absolute-native-selection> --owned-selection <absolute-owned-selection> --capture-owner <absolute-durable-owner-selection> --hub-bridge <absolute-app-owner-selection> --host-end <UTC-deadline> --port <port>
```

These are required existing service/configuration dependencies, not generated
sample credentials. See [durable capture-owner setup](CAPTURE_OWNER_API.md) and
[owned bridge configuration](OWNED_BROWSER_CANDIDATE.md). The same selected
journal survives a host crash; unresolved originals block a new Start without
replaying the old one. Do not select a fresh journal to bypass uncertainty.
The external script is
[Hostess tools/display_stream_qms.py](https://github.com/MesmerPrism/rusty-hostess/blob/main/tools/display_stream_qms.py),
with its [adapter contract](https://github.com/MesmerPrism/rusty-hostess/blob/main/docs/OWNED_DISPLAY_ADAPTER.md).
These AGPL scripts are configurable pinned processes rather than MIT source
dependencies. No source-only test command requires this live service.

October 17 historical evidence covered two simultaneous square sources, a
single-target square/raw/square transition, browser reload/Stop and native
Base64 framing. That evidence belongs to the original selected source/artifacts.
Publication changes, rebuilt helpers and different firmware require their own
runtime qualification. Host protocol/lifecycle tests and source publication
do not qualify a new headset run, app-media session, native-server retirement
or official Cinematic/FOV equivalence.
