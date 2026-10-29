# Additive bilateral owner v2 controls

An explicit host-only bridge selection with `protocolVersion: 2` uses
the existing authenticated HMAC transport and the separate
`/operator/v2/*` contract in [BROWSER_CONTROL_V2.md](BROWSER_CONTROL_V2.md).
Omitting that option retains the v1 behavior. The browser uses separate
`/api/hub/v2/*` routes; it never downgrades v2 to v1 or sends a v1 action
through a selected v2 bridge. The legacy `generation` and `session` bridge
configuration fields remain local transport metadata, never actual native
or app session identities.

The maintained private Morphovision Controller owns NewRun, actual offered
ResumeRun, bilateral choreography, Originals, mode changes, normal Stop and
the genuine two-peer Stop barrier before Whole. The earlier selected-factory
Source13 route is not a supported current NewRun provider. This public
adapter introduces no SDK owner, command interpreter or device worker.

The page requires a New run / Resume run choice and Own / Peer / Blend mode.
Only actual owner run choices are available for Resume. Both headset rows
show their own mode and native report state. App session state alone does not
certify advancing app video. Numeric app/native generations stay decimal
strings; native executor generation and ARM native process generation have
separate fields. Opaque process epochs, native sessions, config identities,
run identities and receipts appear in Diagnostics.

An explicit action is retained in session storage before POST. A lost reply
keeps the same request ID across reload and polls only GET. Effective results
require completed effects with actual Q/W result identities. The first facade
advertises no action readiness while another action is running or unresolved;
normal Stop is a separate intent after the current action settles. Uncertain
pair selection also survives reload and needs an explicit current-status check.
No timer resets, replays or restarts an app.

Start mirror, Stop mirror and Disconnect viewer remain separate controls.
Disconnect viewer changes only the observer; refreshing the page's status
does not reconnect an explicitly disconnected observer. Connect viewer can
reattach to the same producer without dispatching a capture or product action.
Raw selected capture sources expose their canonical device ID for exact
selected-peer matching. Loading the hub starts no capture or app action.

Run `tools/Test-BrowserOwnerV2.ps1` for the strict DTO/parser, production
HTTP routes, exact-path HMAC, retained timeout/replay and real UI callback
controls. `--owner-v2-read-test <absolute-private-bridge.json> [request-UUID]`
checks capabilities, devices, status, run choices and optional original result
using GET only. It dispatches no action. `--owner-v2-fixture`, run from the
Browser project directory, serves a modeled owner on an ephemeral loopback
port for rendered desktop/mobile QA; it creates no capture or device worker.
That explicit fixture alone exposes `POST /fixture-stop` for clean shutdown.

These source/host controls and modeled-provider cross-owner checks establish
wire and UI integration. Live selected-owner adoption, real headset effects,
decoded screen mirrors and app peer video remain separate acceptance evidence.

# Two-headset browser control hub successor

## Read-only member session evidence

`--hub-session-owner <absolute-selection.json>` selects the separate read-only
Q/W session panel. It cannot be combined with `--hub-owner`, `--hub-evidence`
or `--hub-bridge`. The authenticated `GET /api/hub/session` returns member
identities and retained receipts; pair-action POSTs return 409 in this mode.
The panel has only refresh and receipt disclosure controls. Existing v1 pair
controls remain unchanged when this option is not selected.

The closed selection schema is `questms.hub.session-selection.v1`, with
`evidencePath`, 32-byte `keyHex`, `session`, exact `writerPid` and decimal
`writerBirthUtcTicks`, bounded `sourcePins`, `actors` and
`nativeProcessGenerations`. Actors use the closed member fields `pid`,
`start_ticks`, `uid`, `app_process_generation`, `process_epoch_id`. Both maps
have Q/W keys. Native generations are opaque decimal strings, never JavaScript
numbers or application generations. Numeric identities may coincide across
devices; the consumer does not impose an inequality requirement.

The selected publisher signs a `ProjectionEnvelope` containing
`questms.hub.owner-projection.v2`. Its `generation` is a Q/W application map;
`actors` and `nativeProcessGenerations` must match the selection exactly.
`currentAuthoritySupported` must be false, `observations` empty, and
`authorityStatus` exactly
`unsupported_without_authenticated_capture_monotonic_anchor`.
`retainedReceiptEvidence` may contain Q/W recorded active receipts with native
observation/expiry, receipt identity, selected session and matching app/native
generation. This is retained evidence, not current pair or frame acceptance.

The consumer validates the real publisher PID/birth, source hashes, HMAC,
closed/case-sensitive fields, strict numeric types and size bounds. Duplicate
properties, changed identities and unsupported authority claims are denied.
Local publisher-file freshness is distinct from native authority: native
expiry is never compared with the PC clock. Missing/invalid/stale publisher
evidence clears the panel and does not preserve stale member identities.

The Release/self-test and session-view controls use sanitized modeled member
facts, a real host process identity and authenticated files. Rendered QA uses
the production route and UI on an isolated loopback fixture host. No headset,
new pair owner, live publisher adoption, media acceptance or cleanup proof is
established by this feature. Live publisher integration needs its own selected
original-owner slot and must not alter a comparable memory workload.

The hub assigns Q and W by explicit source IDs (`q`/`w` by default; optional
`?q=<id>&w=<id>`). Screen mirrors and app peer streaming remain independent.
The main view shows both mirrors, independently aged channel states, controller
availability and bilateral requests. Raw paths, receipts and IDs are in Diagnostics.

**Start mirror** dispatches one selected native capture through the host.
**Disconnect viewer** disconnects only that observer; the producer and other
observers keep running. **Stop mirror** requests cancellation of only the selected
capture; its state stays stopping until actual child exit, both pipe EOF and
forward/jar cleanup. **Stop monitor** applies to this host. These actions never
restart headset apps or alter keepawake. The legacy `/stream` endpoint still ties
its original viewer to its native capture and supports selected capture cancellation.

Read-only `/observe/<id>` uses transient shared packet references, with at most
8 observers per source, each with a 4 MiB / 128-packet queue and at most one
4 MiB in-flight send (8 MiB payload-reference upper bound per observer). Cached
codec/keyframe GOP is at most 4 MiB and 128 packets; configuration is at most
256 KiB. Slow observer overflow cancels its delivery scope and aborts only that
WebSocket. A recent complete GOP bootstraps late observers; otherwise they wait
for the next real keyframe. No video evidence copies are made.

`--hub-owner` selects a bounded private projection selection. Its exact publisher
PID/birth, source hashes, HMAC, generation, session and opaque Q/W native actor
strings are checked. The private owner publisher joins actual completed raw
stdout/stderr hashes, EOF/reaping and corresponding accepted owner validators,
then derives frame time from native observation clocks. Publication time is only
publisher freshness. It never turns a retained receipt into a fresh frame.
The current native ABI does not expose socket-open proof; recent advancing
accepted reports say **Frames reported**, with socket state unknown. Expired
observations remain stale. Departed writers fail closed.

Readiness follows the actual selected capture lifetime and genuine producer
exit/protocol failures. The successor removes the inherited ten-second startup
estimate; there is no post-connect startup timer that can retire a healthy waiting
producer. HTTP request timeouts remain retained uncertain requests, never replay.

The private `MorphovisionBrowserCurrentOwner.py` binds only to an existing live
selected owner and supports its once-only bilateral stop request marker. It
launches no native admission worker and claims no physical cleanup. The future
`MorphovisionBrowserSelectedFactory.py` concretely constructs the authentic
GuardedAdmissionAdapter / AdmissionMaintenance in a fresh owned selected span;
its asynchronous ingress releases the HTTP lane while the single native worker
runs. Original whole-close capsules and official physical-cleanup validator must
already be source-selected. It cannot attach a second worker to current admission.
This factory is source integration, not an installed current-owner control grant.
Its immutable HTTP selection ticket remains separate from the actual native
session created during pairing. Only the sole authentic owner callback, after
both signed pair/preflight and official arm validations, may durably anchor that
native session. Later admission progress still requires the existing Start and
bilateral renewal validators. Actor/session/source changes deny subsequent
effects. The browser cannot submit a session transition. Original close uses
fixed, preselected `BROWSER_CLOSE_ONCE` capsules and their prepared registrations;
request UUIDs remain once-only journal identities. Establish remains unavailable
until this fresh selected factory/session transition is adopted.

Adoption requires a new pinned host/capture scope: gracefully stop source06,
retain its handles/leases until actual native child exit, pipe EOF, forward/jar
cleanup and host EOF/reaping; unresolved cleanup remains HELD. Start the frozen
successor on separately selected leased host/forward ports after retirement.
Do not hot edit source06, reuse its occupied forwards, restart headset apps or
infer old cleanup from process disappearance. Start Q and W once, attach both
observers, qualify actual advancing decoded headset frames, and keep app peer
status acceptance separate. The browser client needs no installation; the PC
scrcpy bridge still requires .NET, ADB and the selected scrcpy server.

The previous monitor ownership details below apply to the legacy direct route.

# Browser monitor prototype

## Isolated two-headset control hub candidate

The default page now displays two independent existing viewers. The first and
second explicitly selected sources map to Q and W respectively; verify that
host selection order before adoption. Each pane has independent decoding,
crop, Connect once and Disconnect. Loading the hub starts no native capture.
The original single viewer remains at `/viewer.html`.

`--port <1024..65535>` selects an explicit loopback listener and updates exact
origin guards. Reserve its port independently. The default remains 43118.
Never restart or change a retained source/artifact serving an active session.

`--hub-evidence <absolute JSON>` reads bounded `questms.hub.observations.v1`
observations from one operator-selected file. Device, control, and Q→W/W→Q app
video age independently. Missing, future, expired or incomplete observations
remain unknown/invalid/expired/unproven. Active recorded app video requires a
session handshake, open socket and advancing recent app frame samples. Scrcpy
images never supply these observations. All file projections are explicitly
recorded evidence; this adapter does not authenticate owner signatures.

`tools/Project-BrowserOwnerReceipts.py --selection <private JSON> --output
<new absolute JSON>` projects explicitly named completed native peer_status
receipts without device calls. Its closed selection has owner, generation,
session, challenge, apkSha256 and Q/W receipts. It joins receipt identity and
session, preserves owner observation/authority expiry, and caps currentness to
five seconds. It displays recorded peer adoption counts, without treating
them as an open socket or handshake. Raw receipt signatures and qualification
remain unverified; receipt existence is never acceptance. Native 64-bit
incarnations are not sent to JavaScript as numeric identities.

The authenticated same-origin `/api/hub/actions` handler accepts only typed
establish/disconnect, direction, generation, UUID requestId and execute. The
default prepares reviewable candidates with no effects. Unknown fields, stale
generation, conflicting replay and absent execution ownership are denied.

`--hub-bridge <absolute private JSON>` selects the closed owner transport:
endpoint (`http://127.0.0.1:<leased port>/operator`), private bearer token,
generation and session. The browser receives no bearer token, command text,
signing key or tool path. Only bilateral execution is supported. Responses
must join request/generation/session and expose actual readback. A timeout or
failed request remains retained and must not be replayed with a new UUID.
Selecting this transport is not a readiness or authority certificate.

`tools/BrowserSelectedOwnerBridge.py` supplies the matching loopback endpoint
for explicit integration on the selected owner's available command lane. It
delegates establish to the existing guarded AdmissionMaintenance factory and
disconnect to the existing owned whole-close handler. The injected owner must
retain its source/actor/lease span, current session and actual native child
ownership. The bridge keeps a durable once-only request journal and creates
no ADB command, background worker, additional renewer or signing authority.
It denies a lane already owned by admission/maintenance. An unresolved intent
remains pending; neither timeout nor missing result proves no effects.

This candidate has not been injected into the current active owner. Adoption
needs reviewed exact source/config pins, a separately available owner lane,
owned source span, leased listener, genuine executor callbacks and joined
readbacks. The current host and APK remain unchanged. Existing admission
namespaces cannot be reused for another Start; the owner selects fresh scope.
The asynchronous owner ingress returns a durable queued request and keeps
status/stop accessible while one admission/renewal worker holds native work.
A disconnect sets only the existing worker's cancellation request. It never
kills or cancels an in-flight native child. After the actual owned run returns,
the same worker rechecks source/actor/session/live guards and invokes the
selected whole-close handler under that worker's native lock, exactly once.
If admission has already returned, one close worker may own that same lock;
it performs no Start or renewal. Missing physical barriers remain unknown.

The closed bridge binding contains generation, session, Q/W opaque app/native
actor identities and exact sourcePins. Owner implementation, factory, close
handler and actor/session readers must belong to those pinned source files.
Sources and live owner joins are checked again immediately before effects.
The admission factory accepts `cancelled` and must construct the existing
AdmissionMaintenance with that callback and the guarded owner's native lock.
After existing owner receipt validation, its record callback may publish
effective admission progress; it never asserts cleanup. Queue/running/
effective/failed/unknown states remain separate from actual completion.
Restarted ingress observes retained incomplete intents as unknown and cannot
replay their effects. The browser polls authenticated request readback through
`/api/hub/actions/<UUID>` without dispatching another operation.

The browser client needs no installation. This scrcpy route still needs a PC
bridge with .NET, ADB and the pinned scrcpy server. Removing the PC bridge
requires a separate authenticated headset-hosted media/control transport;
ordinary browser APIs cannot launch ADB or directly decode an arbitrary app
peer socket. LAN reachability alone proves no pairing or media flow.

Validation: `tools/Test-BrowserMonitor.ps1` plus
`python tools/Test-BrowserSelectedOwnerBridge.py`. Bridge tests model the
selected owner and verify authentication, identity, busy lane, durable replay
and uncertain outcomes. A genuinely blocked modeled native Start exercises
concurrent status and stop ingress, one close dispatch and unresolved physical
barriers; these tests do not qualify device effects.

Visibility recovery clears the decoded image and waits for a current keyframe
on the same capture. An active blank view now identifies that recovery state,
rather than suggesting that no source is selected. Bounded client counters show
received video packets/keyframes, their arrival ages and the actual drop reason.
These distinguish no received packet, hidden decoding, keyframe wait and rejected
nonadvancing timestamps; they do not prove a network or headset cause.

Explicit codec-configuration packets establish a new decoder timestamp epoch.
Visibility changes alone retain timestamp guards. Within each configuration,
nonadvancing packets still drop, deltas cannot establish a fresh decoder, and
only a real decoded current frame restores the live label. There is no automatic
native capture restart, retry, control write or IDR request.

The pinned server profile has control=false, so no current same-capture control
route can request an IDR. When recovery remains unresolved, an operator may
select Disconnect, require actual native EOF/reaping and forward/jar cleanup,
then separately select Connect once. A stale document or absent host PID does
not substitute for that retirement evidence. Original source05/capture evidence
stays unchanged; packet counters cannot retrospectively identify its failure.

For the standalone scrcpy raw capture, authenticated `GET /api/captures` adds
`rawRead` on the existing status row (null for other producer paths). This is a
constant-size host observation: stage/outcome, bytes consumed in that stage,
expected stage bytes, total reader wire bytes, metadata completion, complete
config/key/delta counts, first/last host packet receipt UTC and the last complete
non-config packet's opaque masked PTS as an exact invariant decimal string/null.
Counters saturate rather than wrap.
Wire bytes include metadata and packet headers/bodies, but exclude the dummy
byte already consumed by startup. No packet payloads are retained by these facts.

Metadata readiness alone leaves all packet counters zero. A `packet-header`
stage with zero bytes means no bytes of the next packet have been consumed;
nonzero header bytes or an incomplete `packet-body` identify partial reads.
Only a fully read valid packet increments one config/key/delta count. Outcome
`reading`, `ready`, `eof`, `cancelled` or `failed` describes the reader; cancellation
and failure preserve the last stage and partial byte position. No next read has
begun while a ready snapshot remains at metadata or a completed packet body.
The existing video-ready, stop/close-observed, capture-failed, child-wait HELD
and terminal evidence details also carry `rawRead`, including normal EOF.
These facts do not prove fanout delivery, decoding, encoder progress or server
retirement. Host UTC is receipt time only; PTS is never compared with the host
clock. Stop ordering, guards, control=false and actual retirement requirements
remain unchanged. In-memory fixtures cover fragmentation, partial cancellation,
truncated input and complete packet accounting; they do not qualify a headset.

Run `dotnet run --project src/QuestMultiStream.Browser` and open the printed localhost URL. Without a selection, the host performs no ADB operations. A local video file tests source/crop controls; explicit window sharing is a manual fallback. The viewer needs a WebCodecs-capable browser, no client installation. The PC host needs .NET 10, ADB and the matching scrcpy 3.3.4 server.

The direct route reads H.264 scrcpy packets into WebCodecs over a localhost WebSocket. It reuses the Core display/crop launch profile and the app's eye crop/black fallback constants. The stereo blend is unaligned; it does not reproduce the desktop alignment estimator. Crops in the viewer change only drawing. No audio or input control socket is created.

Live capture is a separate operator selection: `--selection <absolute private JSON>`. The closed HostSelection schema requires explicit enable/deadline, pinned absolute ADB/server/Agent Board/PowerShell paths and SHA256 values, and selected sources with exact serial/canonical/boot/Quest lease, unique local forward port, display ID and optional server crop. Keep machine paths and identities outside tracked source. The host checks current lease and canonical/boot before capture, locks source tools, pushes a unique server jar, uses a unique scrcpy socket and a new-only ADB forward, and holds actual subprocess ownership until exit and both pipe EOF. It never kills an app, starts an APK, changes the daemon, wakes a device, retries capture or accepts browser shell commands.

Live integration is pending independent review and a fresh root-selected device phase. A socket EOF is not a current frame or successful encoder qualification. Prototype network access is localhost only; remote viewers require an explicit authenticated HTTPS deployment design. Localhost origin and a random URL-fragment token protect source routes; do not publish that token.

Run `pwsh -File tools/Test-BrowserMonitor.ps1` for the actual packet parser and browser crop/codec controls. Packet format is pinned to scrcpy 3.3.4 upstream `doc/develop.md` and `app/src/demuxer.c`; changing versions requires protocol review.

Offline fixture: `--fixture <absolute MP4>` adds an explicitly labeled synthetic-test button and a token-protected, range-capable file endpoint. This option is rejected with live selection. The host never exposes an arbitrary file path endpoint.

The live schema also requires the exact shared ADB daemon PID/start UTC ticks and an existing absolute ignored evidence directory. Agent Board membership must be active and unexpired. Readiness retries only TCP/dummy-byte setup for the same server child within ten seconds, never a second server launch. Unknown child retirement keeps the invocation and source handles held beyond that diagnostic deadline. CreateNew stage receipts precede effects. Server stdout/stderr drain concurrently through EOF, retaining at most 1 MiB each plus full-stream byte count/hash and explicit truncation. Cleanup attempts are independent. The selected local forward port must separately be reserved by the operator before live selection.

RM-026 proposal: browser monitoring belongs to QuestMultiStream; implemented source-only direct H.264 bridge and offline viewer. Remaining acceptance: exact reviewed/pinned host selection, per-headset/forward-port leases, actual matching scrcpy server packet stream, fresh decoded frames/crop/source/retirement evidence. Two tabs can monitor independent selected sources. Multi-pane composition, remote authenticated HTTPS and desktop alignment parity are follow-ups, not claims of this prototype.
Capture deadline must fit the actual selected lease expiry. A same-span guard checks revocation/canonical/boot/daemon every30 seconds while packets flow; guard failure cancels this transport, with no relaunch. Retained stage evidence errors do not skip owned retirement or independent cleanup. An actual nonzero server child exit fails the terminal result. Tool config is accepted only through explicit CLI arguments, not ambient ASP.NET configuration.

Successor decoding keeps the same native capture while a tab is hidden: it drops video packets, clears the old image, and on visibility or queue pressure resets only the browser decoder and waits for a new keyframe. No server or socket restart is automatic. Frame age is age since local decode, with transport delay unknown. Disconnect observes an actual WebSocket close and closes the video transport; native ownership remains until actual child exit and both pipe EOF.

For a bounded listener use `--host-end <UTC ISO time>` at or after the capture deadline and inside the reserved host-port lease. At that end new captures are denied and the listener exits only when all capture invocations have completed their actual retirement/cleanup. A held unknown child keeps the same host alive; its port lease must remain supervised/renewed until actual retirement. Lease expiry is not a process shutdown certificate. The original bilateral run decoded advancing 1344x704 video and left/right crops; background decoder backlog and delayed Disconnect were genuine failures. Live custom-crop and successor hide/show recovery await separate qualification.

An explicitly selected `CaptureLifetime: "until-operator-stop"` with `DeadlineUtc: null` keeps a capture running until Disconnect, monitor Stop, transport failure or actual guard revocation. It requires `--host-end until-operator-stop`; omitted/unknown/conflicting modes are not an indefinite live grant. Existing bounded configurations keep their deadlines. Both bounded host arguments and JSON deadlines require ISO UTC (`Z` or `+00:00`); locale dates are rejected. PowerShell launchers must parse plans with `ConvertFrom-Json -DateKind String` and retain string arguments through `ProcessStartInfo.ArgumentList`.

The authenticated, same-origin **Stop monitor** button requests cancellation of this host's captures and listener only. It does not stop headset apps, blending or keepawake. The response is queued, not proof of completion: the host waits for every owned capture's actual child exit, both pipe EOF and cleanup before listener shutdown. Ctrl+C uses the same owner request rather than the default immediate ASP.NET shutdown. Never force-stop a host with unresolved children. A late stop does not reclassify deadline expiry as successful capture. An until-stop host still needs a real port-lease heartbeat supervisor and held source/static/artifact/config inputs through actual exit; this option does not itself reserve or renew resources.

Decoded progress restores the current H.264 label after stale-age or keyframe waiting, only for an open current live transport without an error. Late output cannot overwrite terminal error/closure truth. Registered controls invoke the actual browser callbacks using synthetic packets, and real child argv/production date-denial paths under German locale; these are source/host controls, not device or visual blend qualification.

### Host turnover and typed action CLI

`--hub-action <absolute JSON>` runs one bounded `HubAction` through the same
`HubActionHandler` as `/api/hub/actions`, then emits its readback and exits.
It accepts only optional `--hub-evidence`, `--hub-owner` and `--hub-bridge`
selections. It creates no monitor listener or capture. The selected owner keeps
durable request identity across CLI processes; a lost reply remains uncertain.

The standing supervisor also admits two explicit predecessor dispositions via
the pinned `tools/BrowserHubPredecessor.ps1` source. Existing retained-supervisor
v1 admission remains available. `questms.browser_hub.normal_retirement.v1`
requires `historicalOutcomesPreserved: true`, pinned `hostEof`, `hostRetirement`,
`portsReleased` and nonempty `sourcePins`. Its receipts must join actual host
PID/birth/exit, OS wait, both EOF, capture retirement and held-input checks.
All three prior lease IDs must be absent from the live active Board inventory.
EOF, retirement and release receipts must also join the exact proposal SHA,
supervisor PID/birth and host PID/birth. Released lease IDs/resources join the
retirement's held lease inventory; selected source pins join its held-input
inventory. Both prior host and supervisor exact processes must be absent.
The standing proposal must hold its executable as an artifact and select
exactly the listener and two forward-port leases.

`questms.browser_hub.postboot_disposition.v1` requires
`historicalOutcomesPreserved: true`, `originalExitCertified: false`,
`originalCleanupCertified: false`, exact `currentHostBootUtc`, earlier
`previousHostObservationUtc`, nonempty `processes` (`pid`, string `birthTicks`),
`previousPorts`, `sourcePins`, and `headsetBootChanges` (`canonical`,
`previousBoot`, `currentBoot`). Live host reboot, prior exact process absence,
unoccupied prior ports and each selected headset's changed boot must match.
This establishes a new host scope; original process exit and remote cleanup
stay unknown. Current headset/daemon/lease/source guards still run before
capture. A root-selected disposition is an input, never an inferred certificate.

`Test-BrowserHubTurnover.ps1` reserves three test ports and launches two actual
offline production hosts in succession. It exercises the production stop
endpoint, actual exit/EOF/reaping, disposed raw writers, exclusive reopen,
actual CLI/HTTP preparation parity. These
controls qualify host turnover only; they contain no headset capture or app
pair effects. Full prepare/connect/configure/physical-stop/next-session browser
operation still requires the selected app owner's authentic lifecycle backend.

`Test-BrowserHubStandingTurnover.ps1` executes two complete production standing
supervisors and consumes their actual emitted receipts for successor admission.
It intentionally supplies an invalid daemon identity so the real browser child
rejects before any platform command. Actual nonzero child exit, both pipe EOF,
supervisor exit, owned lease release, successor launch and mixed-scope/lease/
source denials are qualified. This is failed/unstarted host turnover, with no
physical capture or headset effect qualification.

### Status qualification and command selection

Successful selected projection reads derive typed `SourceOwnerValidated`
provenance after MAC, publisher PID/birth, selected sources, session and actor
checks. A static observation file cannot obtain that provenance by copying an
owner label. Device/control rows identify validated observations at report time.

Native signed authority expiry, last native frame adoption time, advancing
counters and current socket knowledge remain independent. Older accepted reports
with advancing frames display `advancing-reports` and their actual frame age;
they do not assert an instantaneous live connection. Expired authority remains
stale, a frozen counter remains unconfirmed, and absent socket evidence remains
unknown. Headset screen mirror decoding is a separate capture status.

The status API reports `commandTransportSelected`, `requestActions` and
`executionVerified` separately. Selecting a closed authenticated transport does
not certify that effects are ready or complete: the original owner rechecks its
live guards at dispatch. A stop response without completed effects is shown as a
pending request with physical closure unconfirmed. The current-owner controller
supports cancellation requests only; repeated fresh establishment requires the
owner constructor and lifecycle preparation described above.

Routine ASP.NET request and static-file Information logs are filtered at the
category level. Warnings/errors, the private startup viewer URL, application
capture failures and all owned capture/control/native lifecycle journals remain.
Standing owners must drain stdout/stderr with immediate file visibility (for
example a one-byte FileStream buffer) so the private startup URL can be consumed
without depending on later request logging to flush a parent buffer.
