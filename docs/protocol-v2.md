# Ansight secure pairing protocol v2

Status: implementation contract

Protocol v2 replaces secret-bearing UDP bootstrap and plaintext WebSockets with
signed, nonce-bound discovery, pinned WSS, one-use enrollment, and per-install
client-key authentication. Implementations MUST NOT silently downgrade a v2
pairing config to protocol v1.

## Encoding and cryptographic conventions

- JSON is UTF-8. Property names are case-sensitive.
- Timestamps are UTC RFC 3339 strings. A timestamp is signed exactly as received;
  implementations must not parse and reformat it before signature verification.
- Public keys are P-256 SubjectPublicKeyInfo DER encoded with standard padded
  Base64.
- ECDSA signatures use SHA-256 and the 64-byte IEEE P1363 `r || s` encoding,
  then standard padded Base64. The algorithm identifier is `ES256-P1363`.
- Random identifiers, nonces, secrets, HMACs, and SHA-256 fingerprints use
  unpadded Base64URL.
- `tlsSpkiSha256` is the Base64URL SHA-256 digest of the TLS leaf certificate's
  SubjectPublicKeyInfo DER.
- Enrollment proofs use HMAC-SHA256 directly with the decoded 32-byte enrollment
  secret. The secret already has full entropy and is not used for another
  purpose, so no additional password KDF is applied.
- Security-sensitive comparisons use a fixed-time byte comparison.
- Canonical JSON uses the property order shown below, no insignificant
whitespace, JSON string escaping compatible with `System.Text.Json`, and
arrays in the order specified by this document. Scope arrays use `Read`,
`Write`, `Delete` order with missing values omitted.
In particular, `System.Text.Json`'s default encoder represents `+` as
`\u002B`; all implementations MUST do the same for canonical signed bytes,
including RFC 3339 timestamps containing a positive offset.

## Pairing config v2

Studio issues v2 configs by default. The config is acquired through a local QR,
an authenticated account channel, or another out-of-band trusted path. Its
self-signature proves possession of the included host key but does not, by
itself, make an untrusted config trustworthy.

```json
{
  "schema": "ansight.pairing-config.v2",
  "configId": "opaque-id",
  "appId": "com.example.app",
  "appName": "Example App",
  "issuedAt": "2026-07-13T00:00:00.0000000Z",
  "expiresAt": "2026-07-13T00:10:00.0000000Z",
  "minProtocolVersion": 2,
  "allowedTransports": ["wss"],
  "host": {
    "hostId": "base64url-sha256-host-signing-spki",
    "hostName": "Developer Mac",
    "discoveryPort": 45123,
    "hostPubKey": "base64-p256-spki",
    "hostPubKeyFingerprint": "base64url-sha256-host-signing-spki",
    "tlsPins": [
      {
        "tlsSpkiSha256": "base64url-sha256-tls-spki",
        "notBefore": "2026-07-13T00:00:00.0000000Z",
        "notAfter": "2026-08-13T00:00:00.0000000Z"
      }
    ]
  },
  "enrollment": {
    "ticketId": "base64url-random-id",
    "secret": "base64url-32-random-bytes",
    "expiresAt": "2026-07-13T00:10:00.0000000Z",
    "grantExpiresAt": "2026-08-12T00:00:00.0000000Z",
    "maxUses": 1,
    "maxScopes": ["Read"],
    "allowCritical": false
  },
  "signatureAlgorithm": "ES256-P1363",
  "signature": "base64-p1363-signature"
}
```

The config signature covers canonical JSON with this exact order and without
`signature`:

`schema`, `configId`, `appId`, `appName`, `issuedAt`, `expiresAt`,
`minProtocolVersion`, `allowedTransports`, `host`, `enrollment`,
`signatureAlgorithm`.

Within `host`, canonical order is `hostId`, `hostName`, `discoveryPort`,
`hostPubKey`, `hostPubKeyFingerprint`, `tlsPins`. Within each pin it is
`tlsSpkiSha256`, `notBefore`, `notAfter`. Pins are sorted by `notBefore`, then
`tlsSpkiSha256`. Within `enrollment`, order is `ticketId`, `secret`,
`expiresAt`, `grantExpiresAt`, `maxUses`, `maxScopes`, `allowCritical`.

Validation MUST reject an incorrect computed host-key fingerprint, expiry,
future `issuedAt` beyond five minutes of clock skew, lifetimes longer than the
implementation's configured maximum, algorithms other than those above,
`minProtocolVersion` below 2, any transport other than `wss`, no currently valid
TLS pin, `maxUses` other than 1, or an invalid enrollment secret length.

After successful enrollment the secret MUST be deleted. Remembered profiles
retain only host identity/pins, the client private key reference, and grant
metadata. A host-key change requires an old-key cross-signature or explicit
re-pairing.

## Secret-free UDP bootstrap

The client sends `CONNECT_INIT_V2`. No bearer or enrollment secret is sent.

```json
{
  "type": "CONNECT_INIT_V2",
  "ver": 2,
  "requestId": "base64url-16-random-bytes",
  "configId": "opaque-id",
  "appId": "com.example.app",
  "clientNonce": "base64url-32-random-bytes",
  "supportedVersions": [2],
  "supportedTransports": ["wss"]
}
```

Canonical init property order is exactly the order above.

Studio replies with a short-lived signed offer:

```json
{
  "type": "CONNECT_OFFER_V2",
  "ver": 2,
  "requestId": "echoed-request-id",
  "configId": "echoed-config-id",
  "appId": "echoed-app-id",
  "clientNonce": "echoed-client-nonce",
  "hostNonce": "base64url-32-random-bytes",
  "hostId": "paired-host-id",
  "selectedVersion": 2,
  "selectedTransport": "wss",
  "webSocketPort": 45124,
  "webSocketPath": "/ws/v2/opaque-offer-id",
  "tlsSpkiSha256": "base64url-sha256-tls-spki",
  "expiresAt": "2026-07-13T00:00:10.0000000Z",
  "signatureAlgorithm": "ES256-P1363",
  "signature": "base64-p1363-signature"
}
```

Canonical offer property order is exactly the order above without `signature`.
The signed bytes are:

```text
ANSIGHT-CONNECT-OFFER-V2\n
<canonical CONNECT_INIT_V2>\n
<canonical CONNECT_OFFER_V2 without signature>
```

The client verifies every echoed field, both nonce lengths, offer expiry, host
ID, selected version/transport, signature against the trusted host key, and that
the offered TLS pin is currently valid and listed in the signed config. UDP
source address is routing information, not identity.

## Pinned WSS

The client opens only the offered `wss://` endpoint and validates the leaf SPKI
digest against both the signed config and signed offer. Normal certificate
validity and Server Authentication EKU checks also apply. A pin mismatch, TLS
failure, invalid offer, or v2 authentication failure MUST NOT fall back to v1.

The WSS upgrade contains no token in its URL or headers. Studio rate-limits
pending offers and permits only one live connection for an offer. Application
messages other than the authentication messages below are rejected until
`AUTH_OK_V2` has been sent.

## Authentication challenge

Immediately after WSS opens, Studio sends:

```json
{
  "type": "AUTH_CHALLENGE_V2",
  "ver": 2,
  "authSessionId": "base64url-16-random-bytes",
  "requestId": "bootstrap-request-id",
  "configId": "config-id",
  "appId": "app-id",
  "clientNonce": "bootstrap-client-nonce",
  "hostNonce": "bootstrap-host-nonce",
  "serverChallenge": "base64url-32-random-bytes",
  "expiresAt": "2026-07-13T00:00:20.0000000Z"
}
```

Canonical challenge order is exactly the order above.

## First-use enrollment

The client generates a per-install P-256 signing key. Its private key should be
non-exportable and stored through Keychain/Secure Enclave, Android Keystore, or
the platform credential protector.

```json
{
  "type": "AUTH_ENROLL_V2",
  "ver": 2,
  "authSessionId": "auth-session-id",
  "ticketId": "enrollment-ticket-id",
  "clientKeyId": "base64url-sha256-client-spki",
  "clientPublicKey": "base64-p256-spki",
  "requestedScopes": ["Read"],
  "requestCritical": false,
  "proofAlgorithm": "HMAC-SHA256",
  "proof": "base64url-hmac"
}
```

The enrollment proof input is canonical JSON with this exact order:

```json
{
  "context": "ANSIGHT-AUTH-ENROLL-V2",
  "configSignatureSha256": "base64url-sha256-decoded-config-signature",
  "requestId": "request-id",
  "clientNonce": "client-nonce",
  "hostNonce": "host-nonce",
  "tlsSpkiSha256": "tls-pin",
  "authSessionId": "auth-session-id",
  "serverChallenge": "server-challenge",
  "ticketId": "ticket-id",
  "clientKeyId": "client-key-id",
  "clientPublicKey": "client-public-key",
  "requestedScopes": ["Read"],
  "requestCritical": false
}
```

Studio verifies the proof in fixed time and atomically consumes the ticket. The
effective grant is the intersection of ticket maximums, requested scopes, local
Studio approval, the app's compiled tool set, and its SDK ToolGuard.

## Reconnect proof

For reconnect, the client responds to the same challenge with:

```json
{
  "type": "AUTH_PROVE_V2",
  "ver": 2,
  "authSessionId": "auth-session-id",
  "grantId": "grant-id",
  "clientKeyId": "client-key-id",
  "signatureAlgorithm": "ES256-P1363",
  "signature": "base64-client-signature"
}
```

The client signature input is canonical JSON in this exact order:

```json
{
  "context": "ANSIGHT-AUTH-PROVE-V2",
  "requestId": "request-id",
  "clientNonce": "client-nonce",
  "hostNonce": "host-nonce",
  "tlsSpkiSha256": "tls-pin",
  "authSessionId": "auth-session-id",
  "serverChallenge": "server-challenge",
  "grantId": "grant-id",
  "clientKeyId": "client-key-id"
}
```

Studio verifies the registered grant is not expired or revoked and verifies the
signature against its registered client public key.

## Authentication result and grant

Successful enrollment or reconnect returns:

```json
{
  "type": "AUTH_OK_V2",
  "ver": 2,
  "sessionId": "transport-owned-session-id",
  "grant": {
    "grantId": "grant-id",
    "hostId": "host-id",
    "configId": "config-id",
    "appId": "app-id",
    "clientKeyId": "client-key-id",
    "allowedScopes": ["Read"],
    "allowCritical": false,
    "issuedAt": "2026-07-13T00:00:00.0000000Z",
    "expiresAt": "2026-08-12T00:00:00.0000000Z",
    "signatureAlgorithm": "ES256-P1363",
    "signature": "base64-host-signature"
  }
}
```

Grant canonical order excludes `signature` and is `grantId`, `hostId`,
`configId`, `appId`, `clientKeyId`, `allowedScopes`, `allowCritical`, `issuedAt`,
`expiresAt`, `signatureAlgorithm`. The grant signature uses the paired host key.
Studio remains authoritative and does not trust grant fields supplied by a
client. The client verifies the signature before persisting grant metadata.

Failures use `AUTH_ERROR_V2` with `ver`, `code`, `message`, and `retryable`.
Messages received before auth, expired challenges, duplicate tickets, revoked
grants, key mismatches, and duplicate auth attempts fail closed.

After `AUTH_OK_V2`, the existing session protocol runs inside the authenticated
TLS connection. Studio assigns the session identity; caller-provided session IDs
cannot switch the authenticated session. TLS supplies record confidentiality,
integrity, ordering, and replay protection. Mutating tool request IDs are cached
for the connection lifetime so duplicate requests are not executed twice.

## Authorization

Effective tool permission is:

```text
compiled tools ∩ SDK ToolGuard ∩ authenticated grant ∩ Studio session approval
```

Tool discovery and execution are unavailable before authentication. Write,
Delete, invocation, reflection, filesystem, database, secure-storage, and other
critical operations require explicit opt-in. Release builds default to remote
tools disallowed; aggregate SDK setup defaults to disabled or Read-only rather
than FullAccess.

## Legacy v1

- Protocol v1 is enabled only by an explicit `AllowInsecureV1` development
  option and a v1 pairing document.
- New v2 configs cannot negotiate `ws` or version 1.
- V2 clients never downgrade after any v2 failure.
- Studio labels v1 sessions insecure and restricts them to loopback/read-only
  where possible.
- Existing v1 cached credentials require re-pairing and are deleted after
  migration.
