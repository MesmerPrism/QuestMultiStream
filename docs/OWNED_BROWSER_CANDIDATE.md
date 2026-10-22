# Owned display and live owner source candidate

This optional source candidate extends the existing browser monitor. It is not
a release, a running service, current media authority or a live device result.
Keep it inert until the product owner selects the exact reviewed candidate.
An initial product baseline runs without display producers or browser previews.

The live panel exposes **Request Stop** for a fresh authenticated selected pair,
including while Start or renewal is pending or its outcome is unknown. This is
the existing closed `disconnect` intent, not a claim that `readiness.canStop`
is true or cleanup has completed. The service still owns serialized dispatch,
desired-state fencing and all native guards. Start stays disabled while an
original is unresolved. Missing disconnect capability, stale/unavailable status
or a changed pair prevents the browser from submitting that Stop intent.

Before any action POST the browser persists its original request ID. A bounded
list retains both a prior unknown Start and the new human Stop through reload;
the old single-record storage format migrates on read. A denied or lost POST
does not clear either original or trigger a retry. Read-only GET of each exact
original removes only that original on a definitive failed or actually effective
reply. Browser storage failure prevents submission. This changes no owner wire
schema, authentication, readiness semantics or native completion requirements.

The existing QMS HTTP/WebSocket metadata, H.264 packet envelope, bounded
CaptureFanout and WebCodecs DecoderPolicy remain the browser media path. An
independently authored MIT RMANVID1v1 reader adapts the documented owned wire.
Quest and Hostess producer implementations remain separately licensed and
out of process. Nothing here relicenses their implementation.

## Selection and process boundary

Use `--owned-selection <absolute-private-json>` together with `--hub-bridge`
and explicit `--host-end`. It is exclusive with the existing scrcpy
`--selection` and offline video fixture. The strict private JSON shape is:

```json
{
  "schema": "questms.display.selection.v1",
  "python": {"path":"<python-executable>","bytes":1,"sha256":"<sha256>"},
  "script": {"path":"<hostess-entrypoint>","bytes":1,"sha256":"<sha256>"},
  "outputDirectory": "<existing-private-output-root>",
  "sources": [{"sourceId":"q","deviceId":"<opaque-owner-device-id>",
    "label":"Headset Q","ownerBindingRef":{"path":"<immutable-binding>",
    "bytes":1,"sha256":"<sha256>"}}]
}
```

These are placeholders, not runnable identities. Each source has a distinct
opaque device ID. Source start requires the same device in the authenticated
current pair. Pair selection and display admission are serialized; changing
the pair while a display is active or unresolved is rejected. The Hostess
binding still owns immediate exact device/boot/process/source/expiry guards.

One exact pinned child is launched per display with arguments `-B`, the exact
script path and fixed mode `display-stream-qms-v1`. First stdin NDJSON record:
`schema=questms.display.start.v1`, sourceId, invocationId, generation, stopNonce,
ownerBindingRef and outputDirectory. The last is a new per-invocation directory.
The first four identities are opaque strings. Python, entrypoint and binding
bytes are held open against replacement throughout the owned operation.

Child stdout contains only RMANVID1v1 bytes. Stderr is concurrently drained.
Display Stop sends one stdin record with schema `questms.display.stop.v1` and
the same sourceId/invocationId/generation/stopNonce. The media read is not
cancelled to pretend that the producer ended. The exact remote control owner
must close its blocked write, retire resources and return its nonce-joined
terminal proof. QMS drains real stdout/stderr and waits for the local child.

Create-new `TERMINAL.json` has schema `questms.display.terminal.v1`, those four
identities, state, nativeChildExitCode, nativeStdoutEof, nativeStderrEof,
remoteAckMatched, resourcesRetired and receipt. Retired requires native exit0,
both EOF, matching ACK/resources and local child exit0/full EOF. QMS also writes
its own create-new `QMS_CHILD.json` linking the exact terminal bytes to local
EOF/exit evidence. Native exit1, a foreign nonce or missing proof stays unknown;
the source remains busy and blocks rearm/host release. There is no force kill,
generic shell, media replay, fixed remote filename or automatic retry.

The reader admits only version1 H.264/reserved0, bounded dimensions/metadata,
known config/key flags, exact packet EOF, safe source PTS and config followed
by a genuine keyframe. It preserves payload and PTS in the existing 12-byte
browser packet format. A packet must fit the existing observer queue budget;
configuration has the existing smaller bound. A source failure does not cancel
the other source. Viewer loss only releases that observer.

The pair selection/session belongs to the service, not an individual native
runtime generation. Renewal/rollover appears through current phase,
readiness reasonCodes and native actor/runtime fields. It must not change the
user selection or producer source ID. The panel has no renewal command and
does not restart display producers when native fields change. Display children
continue until an explicit display/monitor Stop and actual retirement; closing
a browser tab does not cancel them. The service independently owns native
renewal/rollover and its original-result reconciliation.

## Current owner wire

The existing strict `questms.owner.action.v1` body and HubOwnerReadback fields
are unchanged. Start/Stop UI labels use establish/disconnect wire values.
Generation/session bind the selected control pair; actual native runtime and
actor generations remain separate in current status. The owner persists the
original request before effects. GET `/operator/<requestId>` observes it after
timeout/reload and never replays POST. The QMS retained task also prevents
same-process retries after a lost reply. Service restart durability belongs
to the existing private owner journal, not this in-memory HTTP client.

Optional `ownerEpoch` in HubBridgeSelection selects required authenticated live
mode. Legacy bridge selections continue to use their previous contract, and
cannot claim live status. Live mode adds GET `/operator/status`, GET
`/operator/devices`, and no-effect POST `/operator/selection`; strict DTOs live
in OwnerLiveStatus.cs. Their browser-facing paths are `/api/hub/current`,
`/api/hub/devices` and `/api/hub/selection`. Owner actor/session joins, current
expiry, monotone revision, distinct devices and unresolved-operation denial
remain independent of transport success.

Live mode never sends Authorization or the configured secret on the wire.
Its detached request headers are
X-Quest-Owner-Epoch, X-Quest-Nonce (fresh 128-bit lowercase hex) and X-Quest-Mac.
MAC uses HMAC-SHA256 with the UTF8 configured host-only token as key; configure
at least256 bits entropy. Canonical UTF8 request input is
`QMS-OWNER-REQUEST-1\n<METHOD>\n<path>\n<epoch>\n<nonce>\n<lowercase-body-sha256>`.
Reply input is `QMS-OWNER-REPLY-1\n<decimal-status>\n<path>\n<epoch>\n<nonce>\n<lowercase-body-sha256>`.
GET hashes an empty body. No trailing newline. Verify exact epoch/nonce and
fixed-time MAC before parsing. The owner rejects reused transport nonces,
stale selections and conflicting logical request IDs; results retain original
logical identity. A fresh GET nonce does not create another mutation.
Missing/duplicate/wrong headers, duplicate/case-aliased JSON, source/native
identity drift and stale status fail closed. Responses are bounded64KiB.

Legacy selections without ownerEpoch retain their previous Authorization:
Bearer contract and do not support authenticated live status. Sending the live
MAC key as a Bearer token would let a replacement server occupying the port
forge replies, so the two authentication modes are exclusive.

Only the QMS backend contacts the private loopback owner. No owner secret is
passed to the browser. Browser source/observer token handling and exact origin
checks stay separate. OPTIONS or local network permission grants no authority.
Current displays and decoded frames cannot fabricate app peer traffic. The
panel shows each independently, and only two current native directions can
produce an overall active result.

## Offline qualification and limits

The optional server-owned original capture API and its separate MAC audience
are specified in [CAPTURE_OWNER_API.md](CAPTURE_OWNER_API.md). Selecting it routes
component UI capture actions through the same durable owner handler. Product
all-media composition remains an independent private service responsibility.

`dotnet <browser-dll> --candidate-self-test` runs production reader, fanout,
terminal verifier and in-memory HTTP client controls without a listener.
Add `--fixture-python <exact-python-executable>` to run modeled producer child
processes through the production process adapter. These test two independent
media pumps, actual stdin Stop, real local EOF/exit and native1 marker rejection;
they do not prove ADB stdin propagation or remote Android retirement.

Run the existing `wwwroot/*.test.mjs` Node fixtures, including live-owner and
observer-reload controls. They invoke actual panel/viewer callback paths with
modeled DOM/network, not a rendered browser. Rendered desktop/mobile, actual
owner service, two headset live feeds, remote retirement and normal app
Stop/Start remain separate product qualification after owner adoption and the
baseline without previews. No listeners, browser, device or publication actions
are part of these offline checks.
