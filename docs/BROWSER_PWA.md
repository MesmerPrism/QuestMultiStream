# Installable browser controls

The browser root is the existing Quest Multi Stream control hub, packaged with
its existing artwork and a root-scoped standalone web app manifest. It has no
service worker, offline shell, mutation queue, credential cache or saved media.
Installation does not start a capture, select devices, replay an Original or
grant control. The existing authenticated operator and owner contracts apply.

## Install and reconnect

Use Edge or Chrome on Windows, or Chrome on Android, and choose **Install app**
in the browser menu when available. The page also offers an Install button when
the browser supplies an install prompt. On iPhone, use Safari's Share menu,
**Add to Home Screen**, **Open as Web App** if offered, then **Add**. iPhone does
not provide Chromium's install-prompt event.

The manifest ID, scope and start URL are `/`, with no token or device identities.
A freshly opened installed window can have separate session storage. Use
**Connect operator link** and paste the current link for the same host address.
The form masks and clears the pasted link, hands its fragment to the existing
hub authentication bootstrap, then the hub removes the fragment from the URL.
Credentials stay in that window's existing session storage; closing the app may
require the link again. A replacement link reloads the same hub and preserves
its existing unresolved Original request storage. Link submission itself sends
no mutation. A host restart may invalidate a formerly valid operator link.

A fresh unauthenticated window shows Disconnected and connection/install help;
controls stay hidden until a link is supplied. While running, a network warning
says current owner status is unverified. Owner readback remains authoritative;
returning online does not dispatch an action. If the host cannot be reached on
a cold launch, the browser's own network error is expected: there is no cached
application claiming connectivity. Browser HTTP cache behavior is separate from
an offline app cache; authentication/API/media are never cached by a PWA worker.

## Secure phone origin

Installation requires HTTPS trusted by the device, or localhost/loopback on the
same device. A phone's localhost is the phone, not the PC. Plain LAN HTTP is not
an installable secure origin. Use one stable trusted HTTPS host name and port
for both browser authentication and installed relaunch; a different origin has
different storage and installation identity. Do not put an operator token in a
manifest, bookmark, static file, screenshot, access log or persistent cache.

## Isolated local QA

Reserve an independent exact fixture port through your machine coordinator.
Do not use the live owner or mirror ports. This source-only modeled fixture
uses the production routes and UI; it creates no device/capture worker:

```powershell
dotnet build src/QuestMultiStream.Browser --nologo
$env:QMS_FIXTURE_PORT='<reserved-independent-port>'
Set-Location src/QuestMultiStream.Browser
dotnet bin/Debug/net10.0/QuestMultiStream.Browser.dll --owner-v2-fixture
```

Open the printed loopback origin without a fragment to test a fresh app launch.
The fixture's public synthetic `#token=fixture` fragment can be supplied through
Connect operator link to view modeled controls. It is not a live credential.
Only fixture mode exposes `POST /fixture-stop`; use that endpoint to stop the
owned fixture, prove process exit and release its exact port. Remove the
process-local `QMS_FIXTURE_PORT` environment variable afterward.

Run `node --test src/QuestMultiStream.Browser/wwwroot/*.test.mjs` for actual
bootstrap callbacks, same-origin link validation, fresh-window authentication,
one-shot user-triggered install prompting and network behavior, alongside
existing Original retention and independent mirror controls. Run the existing
`tools/Test-BrowserOwnerV2.ps1` for modeled authenticated production HTTP routes.
Desktop responsive emulation checks layout only; it is not phone installation
or Safari acceptance.

## Physical phone checks

On Windows, Android Chrome and iPhone Safari, verify the trusted HTTPS origin,
manifest/icon, install, icon launch, fresh operator-link connection, close and
relaunch with honest authentication behavior. Rotate the phones and test touch
controls without unintended submissions or horizontal page overflow. Interrupt
network and host availability: no stale success, saved identities, action queue
or mutation replay may appear. Restore connectivity and verify GET-only status
recovery. Real media decoding and background/resume behavior need physical
phone checks; platform codec support is not established by desktop emulation.

References: [MDN installability](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps/Guides/Making_PWAs_installable),
[Apple Home Screen web apps](https://support.apple.com/en-lb/guide/iphone/iphea86e5236/27/ios/27).

## Optional direct HTTPS listener

The existing ASP.NET host can expose the same browser app on one explicit
private IPv4 HTTPS listener. Omit `--https-selection` to keep the existing
loopback-only HTTP behavior. The option does not start another controller or
proxy, change the bridge, open a firewall, install trust, or configure a router.
Do not enable it on a running accepted owner by replacing files underneath it;
qualify and adopt the new source and ordinary owner prerequisites first.

Prepare a private JSON selection under ignored `local/`, with these fields:

| Field | Value |
| --- | --- |
| `listenAddress` | One exact private IPv4 of this PC; loopback is also supported for isolated QA |
| `port` | One reserved port from 1024 through 65535 |
| `origin` | Exact `https://<certificate-name>:<port>` with no trailing slash, path, query, fragment or credential |
| `certificatePath` | Absolute private path to a PKCS12/PFX server certificate with its private key |
| `passwordFile` | Absolute private path to a UTF-8 file containing the PFX password; one final newline is ignored |

Keep the selection, PFX and password file private and outside tracked source;
restrict them to the host operator. The selected certificate must cover the
exact origin DNS name or IP address in its Subject Alternative Name and be valid
for TLS server authentication. Its chain must already be trusted by each phone.
Normal certificate-loading and actual TLS errors remain failures. Windows uses
normal temporary private-key import because Schannel cannot serve an ephemeral
private key; disposal releases that import, with no `PersistKeySet` or trust-store
installation. Certificate/trust provisioning is separate from app installation.

Start a separately qualified host with its existing owner/bridge selections,
its original HTTP `--port`, and `--https-selection <absolute-private-json>`.
The console prints a distinct HTTPS operator link using the existing ephemeral
browser token. Give the phone that HTTPS link through your normal private
operator handoff; do not save it in source or an installation URL. DNS must
resolve the certificate name to the exact private PC address. The phone and PC
must have private-network connectivity; configure only the selected private
listener's network access deliberately. Do not use wildcard/public listeners,
router port forwarding, disabled certificate checks or plain LAN HTTP.

The direct TLS connection, exact local address/port and exact selected Host
choose the browser authority. Mutation and WebSocket requests still need the
existing bearer token and that exact Origin; a loopback Origin cannot cross the
HTTPS listener. Forwarded Host/Proto headers are ignored, with no proxy trust.
The private `/api/owner` path is denied on HTTPS even for a client on this PC;
its original numeric-loopback Host/HMAC contract remains on HTTP. Mirrors use
`wss://` under HTTPS and `ws://` under loopback HTTP. Existing API, bridge,
Original request, capture and media contracts remain unchanged.

Tomorrow, first open the HTTPS address in each physical phone's browser and
verify a valid trusted connection with no certificate warning. If using a
private CA, provision only the deliberately chosen CA through the platform's
normal trust process; iPhone may require explicit full trust for a manually
installed root. Never install the server private key on a phone. Then connect
with the current HTTPS operator link, install the web app using the platform
steps above, and test icon relaunch, fresh-link connection, background/resume,
rotation, network loss and honest status recovery. Do not assume a phone trusts
the PC development certificate. Phone trust, installation and actual mirror
codec support remain physical-device acceptance work.

For source validation, `--https-self-test` checks selection and direct-transport
rules without opening sockets. With two explicit independently reserved loopback
ports, `--https-self-test <http-port> <https-port>` also exercises real Kestrel
TLS and static assets, production v2 auth/Origin boundaries, Host rejection,
private-owner isolation and a small authenticated WSS fixture. Its generated
CA exists only in memory and the test client's custom trust policy; normal
chain/name validation stays enabled. It proves rejection of an untrusted chain
and SAN mismatch without modifying OS trust. The fixture creates no device,
owner or media worker and shuts both listeners down before returning.

References: [Kestrel HTTPS endpoints](https://learn.microsoft.com/en-gb/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0),
[.NET TLS trust configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices).
