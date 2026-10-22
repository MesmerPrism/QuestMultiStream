# Server-owned capture operations

Source candidate only. Select `--capture-owner <absolute-private-config>` with
the existing `--owned-selection`, authenticated live `--hub-bridge` and explicit
host lifetime. No listener or device acceptance is implied by offline tests.
The ordinary packet reader, fanout, WebCodecs and DecoderPolicy are unchanged.

Private config has exactly schema=`questms.capture.owner.v1`, ownerEpoch,
key and journalDirectory. Key is a host-only independently generated encoded
secret with at least 256 bits entropy, different from the private app-owner
key. JournalDirectory is an existing absolute regular private directory. QMS
holds the config and an exclusive journal writer handle for its lifetime.
Secrets never appear in URLs, Bearer headers, browser HTML, logs or readbacks.

## Closed server routes

- POST `/api/owner/capture-actions`
- GET `/api/owner/capture-actions/<canonical-D-requestId>`: original operation
- GET `/api/owner/captures/<sourceId>`: current source owner, independently joined
- GET `/api/owner/status`: actual process PID/start ticks and selected bindings

Request body <=4096 bytes; reply <=65536 bytes. POST content type is exactly
application/json. GET body is empty. Unknown/duplicate/case-aliased JSON members,
queries, browser Origin, cookies, Authorization, non-127.0.0.1 remote peer or
Host different from the exact numeric-loopback listener authority are refused.
The server API is separate from the existing browser observer/UI token routes.

Action fields: schema=`questms.capture.action.v2`, requestId, action=start|stop,
sourceId, expectedOwnerEpoch, sourceBindingSha256, expectedInvocation. Binding
is the selected source's exact ownerBindingRef.sha256; source/device joins come
from the held owned selection. Start requires expectedInvocation=null and no
active/unresolved instance. Stop requires exactly {invocationId,generation}.
Old Stop never targets a later producer under the same source name. Same ID and
same typed body deduplicate; changed body is409. No HTTP client automatically
replays a POST after timeout, missing original or a changed owner epoch.

Before dispatch, the actual authenticated app-owner status must be `starting`
or `running`, without reconciliation required, and contain this selected device.
Component display Start uses that same policy; it cannot override all-media
Stop while the app is stopping or stopped. After this awaited guard the service
rechecks cancellation, desired-running and the exact allocated invocation under
its dispatch lock. Guard rejection retains the original with affirmative
NO_DISPATCH proof and no native terminal. This phase guard alone does not bind
a delayed old Start across a later new app lifetime; exact original-goal
admission is provided by the original-target cancellation contract below.

### Stop before an original Start arrives

The same POST route also accepts the exclusive schema=`questms.capture.action.v3`
body: requestId, action=`stop`, sourceId, expectedOwnerEpoch,
sourceBindingSha256, originalStartRequestId. All IDs are canonical D UUIDs;
Stop and target Start IDs differ. `expectedInvocation` is not accepted in v3.
V2 exact-invocation Stop remains supported without changing its wire shape.

QMS first durably creates this Stop's immutable intent, then a positive
cancel-original proof joined to the exact epoch/source/device/binding/Start ID.
Only then does it acknowledge or cancel an allocated exact invocation. If the
Start has not arrived, the proof records that fact under the dispatch gate.
A later POST with that Start ID cannot launch a producer, including after a
new app lifetime starts. It retains its own failed/no-dispatch result without
replacing a different current producer. Stop for an old original never cancels
a rearmed original under the same source. IDs and proofs remain on disk; no
routine poll consumes a logical operation or creates cancellation history.

The v3 reply schema=`questms.capture.original-stop-result.v1` contains
ownerEpoch, requestId, action=`stop`, sourceId, deviceId, sourceBindingSha256,
originalStartRequestId, state=stopping|retired|unknown, actualEffectsCompleted,
retirementPending, noDispatchProven, target and receipt. `target` is the exact
actual original Start's v2 readback, or null when it has not arrived; null does
not invent an invocation, generation or native terminal. Completion requires
the retained cancellation/no-dispatch proof or actual target retirement. A
native1/missing retirement stays unknown/pending. GET by the Stop ID is the
only follow-up after a lost reply; no automatic POST retry. Missing/corrupt
proof, incomplete journal recovery or lost allocated target evidence holds
dispatch instead of inferring absence. The private coordinator sends this Stop
directly using its durable original Start IDs; an original404 is never needed
to determine its target.

Result schema=`questms.capture.result.v2`: ownerEpoch, requestId, action,
sourceId, deviceId, sourceBindingSha256, invocationId, generation, state,
actualEffectsCompleted, retirementPending, noDispatchProven, producer, terminal, receipt.
State is queued|running|streaming|stopping|retired|failed|unknown. Streaming
completion requires this invocation's validated metadata/config/key and at
least two advancing video packets. It is packet evidence, not decoded frames
or proof of useful VR content. Producer facts contain metadataValidated,
configObserved, keyObserved, packets, videoPackets, previousPtsUs, lastPtsUs,
previousPacketUtc and lastPacketUtc. Config resets the current PTS sample pair.
Current stale packet timestamps remain stale; a GET never manufactures paint,
socket or app-peer health.

Terminal facts contain localChildExitCode, stdoutEof, stderrEof,
nativeChildExitCode, nativeStdoutEof, nativeStderrEof, remoteAckMatched,
resourcesRetired and receipt. Only exact joined terminal identities are exposed.
Native1, missing ACK, missing actual EOF or missing retirement proof stays
unknown/busy. A known pre-dispatch cancellation may report absent with explicit
typed noDispatchProven evidence and null terminal; it never fabricates native0 or ACK.
The decision comes from this invocation's in-memory dispatch gate, never a
failed/missing-file probe. A create-new NO_DISPATCH.json retains the exact
immutable instance join before reporting absence. Storage/proof failure stays
unknown/pending even in dynamic original readback and retains the writer lock.

Current schema=`questms.capture.current.v2`: ownerEpoch, sourceId, deviceId,
sourceBindingSha256, originalStartRequestId, desiredState and operation (that original Start
readback, or null if this process has no current/pending original). desiredState
is running|stopped|unknown; explicit Stop persists stopped before cancellation.
It cannot
replace an original-operation lookup. Status schema=`questms.capture.owner-status.v1`
contains ownerEpoch, processId, processStartUtcTicks and sources, each containing
sourceId, deviceId and sourceBindingSha256.

## Separate MAC audience and replay window

Use X-Quest-Owner-Epoch, X-Quest-Nonce and X-Quest-Mac only. Authorization is
forbidden. Canonical request input is UTF8:

`QMS-CAPTURE-REQUEST-1\n<METHOD>\n<path>\n<epoch>\n<nonce>\n<lowercase-body-sha256>`

Reply input uses `QMS-CAPTURE-REPLY-1` and decimal HTTP status instead of METHOD.
No trailing newline. HMAC-SHA256 key is UTF8 config key. Verify epoch/nonce and
fixed-time reply MAC before parsing even error replies. Use no redirects/proxy.
The private app-owner QMS-OWNER audience remains separately selected and uses
its existing random128-bit nonce/persistent SQLite policy.

Capture nonce is32 lowercase hexadecimal characters: high64 bits are actual
Unix milliseconds, low64 bits are fresh random entropy. The MAC binds all128
bits. Capture accepts age <=30000ms and future skew <=2000ms. A lock reserves
each authenticated nonce before parse/dispatch. Recent RAM holds <=8192 entries;
only timestamps already outside the authenticated age window are removed.
Exhaustion denies new dispatch until the window clears, rather than evicting
still-valid replay fences. This is a concurrent-window throughput bound, not a
total poll count or service lifetime limit. Backward server clock refuses new
dispatch until its last observed UTC milliseconds are recovered; no synthetic
clock advancement is used. The private client never changes timestamp to bypass
that refusal. Missing/stale/future/duplicate/cross-audience MACs fail closed.

AUTH_EPOCH_USED.json is create-new before live transport use. One epoch is one
QMS process incarnation; restarting with the same config cannot clear a still
fresh replay cache. Re-admission needs an explicit new epoch/private journal and
reconciliation of old retained originals/remote ownership. This candidate does
not reconnect/adopt surviving Android producers across QMS restart. A replacement
owner or404 original is uncertainty, not permission to replay Start.

## Durable ownership and lifecycle

Each original directory records create-new INTENT.json and INSTANCE.json before
any possible launch/cancellation. INSTANCE binds original Start ID and allocated
invocation/generation immutably. RESULT.json is a bounded atomic readback;
DISPATCH.json precedes possible child launch. Disk records are flushed before
dispatch. Completed history stays on disk; only selected instances and at most8
pending Stop controls per instance stay in RAM. Finished Stops leave that queue.
Routine packet/read progress produces no journal event per packet/poll.

Storage failure holds new Start dispatch and retains original uncertainty.
Recovery never auto-dispatches an old intent. Unresolved original instances
remain unknown and block source replacement. Exact original/instance/result
joins are checked on reads. Partial/incomplete admission fails closed pending
explicit reconciliation. This is local durability, not a signed native receipt.

No media task is awaited by the admission lock. Each display owns its Stop and
drain; a blocked Q retirement does not prevent W Stop. Browser loss only releases
observers. QMS host release still waits on actual owned retirement. Browser
component controls use this same handler when capture-owner is selected.

The private product service must persist an app+selected-display composition
intent before app effects, then call these server routes from its independent
worker. Its immutable desired/fence permit is checked before each possible
display Start; closing a page cannot prevent startup. Native renewal/rollover
does not issue display Start again. Explicit overall Stop first latches desired
Stopped, dispatches exact per-display Stops independently, and performs normal
app Stop without waiting indefinitely behind a display drain. Overall completion
requires all required scopes; partial/unknown state must stay truthful.

`--capture-owner-self-test` exercises actual service/transport with modeled
effects, including original joins/rearm, pre-dispatch Stop, native1/recovery,
hostile auth and8300 reads beyond the total recent-cache size. Existing
`--candidate-self-test --fixture-python <python>` also composes the actual
SelectedCaptureInvocation/OwnedDisplayProducer with modeled real Python children.
Neither opens listeners or proves Android retirement/useful capture.
