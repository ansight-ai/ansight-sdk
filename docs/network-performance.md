# Passive network performance

Apple native network capture retains the final `URLSessionTaskTransactionMetrics`
in the optional `performance` object of `ansight.network-request.v1`. This applies
to HTTP(S) requests observed by the native interceptor, including framework apps
that use the Apple URL Loading System. It requires a rebuilt SDK/native bridge
and a new capture; older records and other collectors may omit the object.

## Integration

The measurements are included from SDK 1.8.1 when native Apple network capture
is enabled. For .NET MAUI, update `Ansight.Maui` and its transitive
`Ansight.Native.Apple.Binding` package together, then rebuild and reinstall the
app. Keep `.WithNetworkCapture(...)` enabled; there is no separate performance
switch. `.WithConnectivityCapture()` records the complementary OS connectivity
observations. Request and response body content can remain disabled while byte
counts and timings are retained.

The native collector sends the same request envelope to live host sessions and
offline capture storage. Framework collectors using Dart, JavaScript, or .NET
`SocketsHttpHandler` do not acquire Apple transaction measurements merely by
upgrading the package. Use an Apple URL Loading System-backed request for an
integration check and verify `source: "apple.nsurlprotocol"` plus `performance`
in the new session's network records.

## Measurements

| Field | Measurement |
| --- | --- |
| `timeToFirstByteMilliseconds` | Request start to first response byte; includes upload and server processing, excludes DNS/connection setup. |
| `dnsMilliseconds` | Domain lookup start to end. |
| `connectMilliseconds` | Connection start to end, including TLS when used. |
| `tlsMilliseconds` | Secure connection start to end. |
| `uploadDurationMilliseconds` | Request start to request end, including headers. |
| `downloadDurationMilliseconds` | First response byte to response end, including headers. |
| `uploadedBytes` | Body bytes actually sent. |
| `downloadedBytes` | Body bytes actually received before decompression. |
| `reusedConnection` | Whether the final transaction reused its connection. |

Only the final transaction is measured here; prior redirects/retries are excluded.
Cache/local fetches do not emit performance. Missing or invalid phases stay absent,
including unfinished responses and DNS/TLS phases on reused connections. The
existing `includeBodySizes` option also controls these byte counts. Collecting
timings does not enable body capture or issue additional network requests.

The shared replay shows the last 60 seconds of completed requests at the playhead,
grouped by server. Latency p50/p95 uses nearest-rank percentiles of available
measurements. Transfer rates are `bodyBytes * 8 / milliseconds / 1000` Mbps,
calculated only for positive measured bytes and durations. Server summaries show
the median of these per-transfer averages, not aggregate traffic or connection
capacity. Headers are excluded from byte counts but included in the corresponding
transfer interval. Zero-byte transfers have no displayed rate.

Total request durations remain separate because their observation boundary varies
between integrations. Do not infer latency or throughput from `Content-Length`,
retained body bytes, the OS link-quality category, or total request duration.

Native measurement reference: [Apple URLSessionTaskTransactionMetrics](https://developer.apple.com/documentation/foundation/urlsessiontasktransactionmetrics).
