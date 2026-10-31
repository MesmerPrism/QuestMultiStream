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


## Optional numeric Blend strength

The existing v2 action may carry one optional `blendAmountBits` JSON integer,
0..1065353216: the exact nonnegative Float32 bits for strength0..1. Positive
zero is valid; negative-zero bits, present-null, bool/string, fractional or
exponent tokens, duplicate/unknown fields and out-of-range words are refused.
Examples: .25=1048576000, .5=1056964608,1=1065353216. Omit the property entirely
for existing preset behavior (including ordinary Blend=.5); omitted request and
result shapes remain unchanged.

Explicit bits apply only to `action:"mode",mode:"blend",runChoice:null` in an
already-running selected Blend pair. Existing owner identity/revision/readiness
and durable action semantics apply. Native freshness need not be labeled
`current`: existing retained observations are not upgraded by this feature.
The owner performs fresh qualified baseline reads before a new numeric stage,
requires a common baseline amount for this common-field edit, preserves every
other per-peer mask/policy field and changes amount only. An unavailable/unequal
baseline can earn a definite failed/no-effects action request before a setter
Original exists. Existing ordinary preset completion is not retroactively gated.
Continuation after possible setter ingress preserves its actual Original/ACK;
missing retired proof causes read-only reconciliation, never another setter.

This strength is Peer camera-input amplitude shaped by Own luminance, not
uniform opacity or a spatial coverage rectangle. The one UI field is labeled
**Own-luma-gated Blend strength**. Decimal input is quantized once for the
prepared request; review shows the exact stored Float32 value. Input events
only edit the draft and clear review. Existing Review/Execute retains the
immutable bits; Execute alone sends a mutation. Mode shortcuts keep their
existing omitted preset requests. No profile/threshold/softness editor, new
controller, action, endpoint, Native ABI or APK is introduced.

Read clients opt in on the existing capability/status/result GET paths using:

```http
Accept: application/json; profile="questms.blend-amount-bits.v1"
```

Only that read representation may append `features:["blend_amount_bits_v1"]`
to capabilities. Plain old responses remain unchanged. An old backend ignoring
the header returns the old shape; numeric editing stays unavailable while core
controls work. Failed startup capability reads recover through the existing
owner refresh lane without another timer or blocking core status/mirror reads.
The unsigned header only selects a representation; signed request body bits
carry numeric intent. Existing paths/HMAC, public bearer and Origin checks stay.
No extra feature query is introduced.

Client capability support is checked before numeric review/pending persistence,
and the public bridge rechecks before private POST. A definite local unsupported
or capability-read refusal is retained in the existing bounded `v2Requests`
service-lifetime cache as `state:"failed",actualEffectsCompleted:false,peers:[]`,
with exact request identity and receipt `blend_amount_unsupported_not_dispatched`.
GET of that UUID returns the known local refusal; it does not query a nonexistent
private Original or resend. This cache is volatile; no durable denial across
public-process loss or new journal is claimed. Once private ingress accepts the
request, its existing durable action request owns reconciliation.

An opted-in status peer may include:

```json
"blendAmount": {
  "amountBits": 1048576000,
  "controlRevision": "7",
  "retiredFrameOrdinal": "42",
  "qualification": "retired_configuration",
  "receipt": "qualified per-peer receipt"
}
```

The two counters are positive canonical unsigned decimal strings. This is an
actual qualified retained retired-configuration observation joined to that
peer's current identities, never the desired value, a configured-only ACK,
PC publication age, RGB proof or video freshness. Missing/invalidated observation
means unavailable. Q and W render independently. Ordinary omitted-field request
results retain old shapes. A numeric effective result requires both matching
qualified observations plus existing completion/identity facts. A plain effective
POST reply lacking those observations remains uncertain with the same UUID/bits;
opted GET can settle it, even after the public request cache is recreated. It
must not clear numeric pending, manufacture no-effects or replay POST.

The PWA has no service worker. The new editor starts hidden/disabled, and new JS
without its HTML nodes stays core-only. Existing coherent asset publication and
ordinary reload apply; no worker cache or OS-install retry is added.
