# Additive QMS / maintained Morphovision owner v2 contract

Concrete shared contract, 29 October 2026. v1 routes and consumers remain unchanged.
All JSON objects are closed, case-sensitive, duplicate-free. All listed fields are required;
nullable fields must be present as null. Host counters/revisions are integers 0..9007199254740991.
Numeric native/app 64-bit identities are canonical unsigned decimal strings (0..18446744073709551615);
never JSON numbers. UUID fields use canonical lower-case UUID-D. Times are UTC ISO8601.
Maximum body/reply 65536 bytes; action/selection body 4096 bytes. Device IDs/labels <=128 chars,
receipt/reason strings <=4096; inventories <=16 devices, <=64 run choices.

## Version selection / authentication

Existing host-only bridge config gains optional `protocolVersion:2` (default 1).
Requires existing `ownerEpoch` and existing HMAC token. v2 requests/replies use unchanged
X-Quest-Owner-Epoch / X-Quest-Nonce / X-Quest-Mac and existing QMS-OWNER-REQUEST-1 /
QMS-OWNER-REPLY-1 domains with exact full path. No bearer fallback or automatic version downgrade.
Explicit selection is negotiation; GET /operator/v2/capabilities confirms exact support:
`{schema:"questms.owner.capabilities.v2",ownerEpoch,protocolVersion:2,actions:["establish","normalStop","whole","mode"],modes:["own","peer","blend"]}`.
Browser equivalents use /api/hub/v2/... with existing same-origin fragment token guard.
No secrets, native command text, shell, script path or caller-supplied native identity crosses browser ingress.

## Device and run inventory

GET /operator/v2/devices ->
`{schema:"questms.owner.devices.v2",ownerEpoch,revision,observedUtc,devices:[{deviceId,label,ready,reasonCode}]}`.

GET /operator/v2/runs ->
`{schema:"questms.owner.runs.v2",ownerEpoch,revision,selectionId,observedUtc,runs:[{runId,label,resumable,reasonCode}]}`.
selectionId null when unselected. runId is an actual durable owner operational run ID, exactly 32 lower-case hex characters.
Only actual resume choices for this selected pair are listed; no derived/fabricated run.

POST /operator/v2/selection:
`{schema:"questms.owner.selection.v2",requestId,ownerEpoch,expectedRevision,members:[{role:"Q",deviceId},{role:"W",deviceId}]}`.
Two distinct actual devices; returns current v2 status. Selection remains local control metadata.
Selection request UUID retained by owner; uncertainty is resolved by current status, never automatic replay.

## Status

GET /operator/v2/status ->
`{schema:"questms.owner.status.v2",ownerEpoch,revision,observedUtc,selectionId,members,phase,activeRequestId,reconciliationRequired,readiness,peers,receipt}`.

members = [] or exact Q/W [{role,deviceId}]; selectionId null iff unselected.
phase closed: unselected|preparing|starting|running|stopping|stopped|reconciling|unknown|failed.
readiness = {canEstablish,canNormalStop,canWhole,canMode,reasonCodes:[string]}.
Unselected, reconciliationRequired, unknown or reconciling cannot advertise readiness.
peers = [] or exact Q/W entries:
`{role,deviceId,appProcessGeneration,processEpochId,nativeGeneration,nativeProcessGeneration,runtimeConfigId,nativeSession,runId,mode,nativeFreshness,receipt}`.
appProcessGeneration, nativeGeneration and nativeProcessGeneration are nullable unsigned decimal strings; nativeGeneration is actual native_executor_generation (e.g.17/19); nativeProcessGeneration is actual ARM native_process_generation (64-bit) and stays null until observed; never synthesize one from the other;
processEpochId and nativeSession are nullable bounded opaque strings (1..256 characters);
runtimeConfigId is a nullable actual config identity, exactly 64 lower-case hex characters;
runId nullable actual 32 lower-case hex operational run ID; mode null|own|peer|blend.
nativeFreshness = {state:"unknown"|"retained"|"current"|"stale",observedMonotonicNs:decimalString|null,expiresMonotonicNs:decimalString|null}.
These are owner-qualified device-local facts, not PC times. A new observedUtc publication
cannot refresh nativeFreshness. No aggregate runtimeConfigId, nativeSession, epoch or run is allowed.
Opaque epochs and sessions preserve exact actual owner values, including app-process and session.duplex tokens.
Peer entries must join members by role/deviceId. IDs and receipts render only in Diagnostics.
Status publication age is advisory and cannot replace live owner ingress guards or imply stopped.

## Product actions and retained results

POST /operator/v2/actions:
`{schema:"questms.owner.action.v2",requestId,ownerEpoch,selectionId,expectedRevision,action,runChoice,mode}`.
action closed establish|normalStop|whole|mode.
establish requires mode own|peer|blend and runChoice:
{kind:"new",runId:null} OR {kind:"resume",runId:<actual offered 32hex runId>}.
Backend allocates and durably records NewRun ID before effects; browser cannot invent it.
mode action requires mode own|peer|blend and runChoice:null.
normalStop / whole require mode:null and runChoice:null.
Whole is independently gated by actual both-peer normalStop completion; request state cannot replace barrier.
No execute boolean. Browser POST is explicit execution, after plain human action.
GET /operator/v2/actions/<requestId> reads same retained request with no redispatch.
Both return:
`{schema:"questms.owner.result.v2",requestId,ownerEpoch,selectionId,action,state,actualEffectsCompleted,peers,receipt}`.
state queued|running|effective|failed|unknown. peers uses status peer shape plus
`state:"pending"|"effective"|"failed"|"unknown"` (separate result type).
effective iff actualEffectsCompleted=true AND exactly both peers state effective;
each effective peer must carry real nonnull app/epoch/native-executor/config/session/run identities and receipt. Native process generation remains null if not actually observed; an effective action does not refresh retained native facts.
queued/running/failed/unknown require actualEffectsCompleted=false.
Any HTTP timeout retains same requestId; GET readback only, no fresh UUID retry/reset/replay.
Identical repeated POST may return retained result; conflicting same UUID must deny.
No timer-triggered reset, native restart, implicit Start, mode change or replay.

Mirror start/stop and observer disconnect remain exclusively existing capture/observer routes;
they never invoke these product actions. Screen mirror progress cannot certify app peer streaming.

