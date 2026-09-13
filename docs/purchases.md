# Purchase diagnostics and validation

Purchase observations, bounded retention, validation, and six `purchases.*`
remote tools are part of the **base SDK**. Swift and Kotlin own the native
implementation used by React Native, Capacitor, and Flutter. .NET includes the
same contract in `Ansight.Core`, including non-mobile targets. The common
[validation cases](contracts/purchases/validation-cases.json) run against all
three core implementations.

The tools inspect purchase integrations. They do not initiate purchases,
restore access, finish transactions, consume items, acknowledge payments, or
alter the store test environment. Drive the app's normal purchase UI and record
its outcomes. These mutation and StoreKitTest scenario controls remain separate
future work; no payment service credentials are required for this suite.

## Mandatory simulator-only safeguard

All purchase reporting, product recording, reference creation, validation, remote
execution, and native purchase bridge commands check the environment in the base
SDK before doing work. StoreKit and Play adapters preflight before touching store
APIs or callback objects. Tool discovery reports `purchases_environment_not_allowed`
when unavailable; execution independently enforces the same restriction. Native
bridges preserve that error code for Objective-C, .NET, React Native, Capacitor,
and Flutter callers.

| Runtime | Access |
| --- | --- |
| Apple simulator | Allowed by native simulator target / runtime architecture. |
| Android emulator (.NET, Kotlin, Java, framework bridges) | Allowed only when the OS reports Android Emulator hardware `ranchu` or `goldfish`. |
| Any physical device, including sandbox / TestFlight / license testers | Blocked. |
| macOS / Mac Catalyst desktop, plain .NET desktop/server, other or unknown runtime | Blocked. |

There is no public configuration or remote switch to disable the check. Debug
builds, sandbox receipts, test accounts, test tracks, environment variables, and
caller-supplied observation labels cannot unlock interop. The SDK does not query a
store or read a receipt to decide whether it can run. Production-labelled
observations are rejected even on simulators.

This is a local runtime safeguard against accidental device use, not a security
boundary against a modified application or OS. Emulator recognition does not
establish a billing account's test status. The suite remains read-only and does
not initiate or complete payments.

Checks run on each operation, including before StoreKit queries and before an
async refresh imports evidence. A denied environment check clears retained
purchase evidence and invalidates outstanding imports. Direct `clear()` / `Clear()`
remains available for local cleanup, and local tool registration remains available
so discovery can explain why tools are blocked. Bridge commands, including bridge
registration and clearing, remain guarded. Pure contract tests use non-public test
constructors/factories; they do not enable a runtime override.

## What ships

| SDK | Integration |
| --- | --- |
| Swift / Objective-C | Core models and tools; StoreKit 2 refresh in `AnsightCore` (iOS 15+, macOS 12+). Objective-C uses the async `ANSPurchaseDiagnostics` facade. |
| Kotlin / Java | Core models and tools in `ansight-core-android`; optional `ansight-purchases-googleplay-android` callback adapter compiled against Play Billing 9.1.0 (Android 23+). |
| .NET / MAUI | Models and tools in `Ansight.Core`; Swift StoreKit 2 through the existing Apple binding on iOS/Mac Catalyst; binding-neutral Google Play callback helper. Plain .NET targets expose the contract but block interop without a recognized platform environment. |
| React Native | Typed `purchases` facade over native core, exported from the existing package. |
| Capacitor | Typed `purchases` export over native core, including Cordova plugin transport. Requires a native mobile runtime. |
| Flutter | `Ansight.instance.purchases` and typed observation/product inputs over native core. Requires an iOS/Android runtime. |

The optional Play adapter does not bring its own BillingClient or package the
billing library. Supply Play Billing 9.1.0 in the host application. Applications
using other billing library versions can use the base reporting API directly.

## Evidence and tools

All six tools require `read` policy and opt-in registration. Runtime and
invitation guard limits continue to apply. Every response contains
`schema: ansight.purchases.v1`, `capturedAt`, `observations`, `products`,
`droppedEvents`, and `coverage: observedOnly`.

| Tool | Arguments |
| --- | --- |
| `purchases.get_state` | Optional `productId`. |
| `purchases.query_products` | Optional `productId`; returns observed product metadata. |
| `purchases.query_transactions` | Optional `productId`; returns retained observations, not complete store history. |
| `purchases.get_entitlements` | Optional `productId`; preserves store/app/backend evidence separately. |
| `purchases.get_events` | Optional `after` cursor; returns `nextCursor` and `cursorGap`. |
| `purchases.validate` | Required `productId`, `expectedEntitled`; optional `transactionRef`, `requireVerified` (true), `requireBackendVerification` (false), `expectedDeliveryCount`, `maxAgeMilliseconds` (60000). |

Reads query the bounded cache; they do not refresh or repair the application.
Refresh StoreKit from app activation/purchase callbacks, or use the local SDK
`refreshStoreKit` API before investigating. Other adapters report from the app's
existing callbacks. An absent cached product means **not observed**, not an
invalid product. A successful StoreKit lookup records explicit unavailable IDs.

Retain at most 256 observations and 256 products. Event cursors are local to the
current diagnostics lifetime; clear evidence when switching app accounts.
The SDK runtime buffer-clear operation also clears its shared purchase diagnostics. An
old cursor beyond the current sequence is rejected. The event gap flag reports
when a cursor predates retained history. Products are the latest recorded
metadata for an ID, with their own observation timestamp. Unknown offer pricing
stays absent rather than choosing a subscription offer automatically.

Observation sources are `store`, `app`, and `backend`. Backend evidence is
reported by the application; this tool is not an independent server audit.
`verification` is `verified`, `unverified`, or `unknown`. Keep `finished`,
`acknowledged`, and `consumed` distinct. `deliveryCount` is the application's
cumulative delivery count for the purchase being checked, not a count of SDK
callbacks. Product types are `consumable`, `nonConsumable`, `subscription`,
`nonRenewingSubscription`, and `unknown`. All times are Unix milliseconds.

Use `createTransactionReference` / `CreateTransactionReference` to create an
opaque SHA-256 reference from a provider identifier. Its random salt is local to
the diagnostics instance and rotates on clear. Native adapters never export raw
transaction IDs, purchase tokens, receipts, signed payloads, or account tokens.
Only the declared model fields are retained. Cross-process/cross-instance
references are not comparable. Apple observations imported into .NET retain the
Swift instance's reference.

## Validation behavior

Supply `transactionRef` when validating one specific purchase; observations of
other transactions then cannot satisfy its checks. Without it the check is
product-scoped.

Validation compares the newest observation per source, using arrival order to
break timestamp ties. Store and app evidence are required. Missing, future, or
stale evidence is inconclusive. Freshness bounds must be 1–86400000 milliseconds.

For positive access expectations, the store must report a purchased state and,
by default, verified evidence. Google Play's client `PURCHASED` state alone is
not verification: report a sanitized backend observation and request
`requireBackendVerification: true`. This adds a backend check and uses backend
verification while retaining the separate store-state check.

A consumable's store purchase state can be checked without claiming a durable
store entitlement. Assert `expectedDeliveryCount` against the app's ledger.
Non-renewing subscription expiry remains app-specific; store entitlement may
remain unknown. A verified transaction can therefore still produce an
inconclusive entitlement report for this product type.

Store expiry/revocation affects the store check. StoreKit grace periods preserve
access through the separately verified `gracePeriodExpiresAt`; an unknown grace
period bound is inconclusive. Billing retry without grace does not grant access. App/backend entitlement fields
remain the reported actual decision, so a missed app revocation is visible.
A mismatch or explicit verification rejection fails; missing required evidence
is inconclusive. The overall report fails if any check fails, otherwise it is
inconclusive if any check is inconclusive. Only all passing checks pass overall.
A completed validation returns a successful tool envelope even if its report
fails. Invalid arguments produce `purchases_invalid_arguments`.

Use the existing UI tools after validation to verify the purchased feature.
Neither this suite nor the app's “premium” flag proves visible behavior by itself.

## .NET / MAUI

```csharp
using Ansight.Purchases;

// Add to your existing Ansight options builder.
builder.WithPurchaseTools();

// Apple: use StoreKit 2 through the native Swift core. Call on activation
// and after your normal purchase/update callback completes.
await PurchaseStoreKit.RefreshAsync(["premium.monthly"]);

PurchaseDiagnostics.Shared.Record(new PurchaseObservation(
    "premium.monthly", "app", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    Entitled: myEntitlementService.HasPremium,
    DeliveryCount: myDeliveryLedger.GrantCount));
```

For Android, forward the existing binding's purchase fields to
`PurchaseGooglePlay.Record(productIds, purchaseState, acknowledged, suspended,
productType)`. It deliberately accepts primitive fields so the base SDK does not
force a particular .NET Play Billing binding dependency. Record the backend's
verification/account-binding decision as a separate `backend` observation.

The requested [Swift/.NET binding generator](https://github.com/justinwojo/swift-dotnet-bindings)
and its [StoreKit packages](https://github.com/justinwojo/swift-dotnet-packages)
require .NET 10 Apple targets; Ansight currently targets .NET 9. This implementation
uses the existing `Ansight.Native.Apple.Binding` bridge to call the same Swift
core rather than introducing a .NET major-version migration. The bridge exposes
`purchaseCommand:completion:` and returns sanitized JSON. Refresh timeout or
cancellation does not cancel StoreKit work already in flight.

## Swift

```swift
import AnsightCore

// After initializing the runtime:
try AnsightRuntime.shared.registerPurchaseTools()
try await PurchaseStoreKit.refresh(productIds: ["premium.monthly"])
var observation = PurchaseObservation(productId: "premium.monthly", source: "app")
observation.entitled = entitlementService.hasPremium
try PurchaseDiagnostics.shared.record(observation)
```

StoreKit reads product metadata and the latest transaction per supplied ID,
including verification, expiry and revocation. It does not own transaction
updates or finish transactions; forward lifecycle changes through refresh calls.
It does not retrieve a complete transaction history. StoreKit local Xcode and
sandbox infrastructure are different test lanes; a local run does not prove
App Store server notification delivery.

## Kotlin / Java

```kotlin
import ai.ansight.runtime.purchases.*
import ai.ansight.purchases.googleplay.GooglePlayPurchaseObserver

builder.withPurchaseTools()
val observer = GooglePlayPurchaseObserver()
// In your existing PurchasesUpdatedListener:
purchases.forEach { observer.record(it, productType = "subscription") }
PurchaseDiagnostics.shared.record(PurchaseObservation(
    productId = "premium.monthly", source = "app", entitled = entitlementService.hasPremium
))
```

`GooglePlayPurchaseObserver.recordProduct` accepts an optional selected
subscription offer index. When selected it records the final pricing phase;
this is not a full offer/pricing-phase catalog. No offer is automatically chosen
for subscriptions. Suspended purchases report no current store access, even when
the purchase state remains `PURCHASED`. The adapter reports environment and
verification as unknown until the app supplies authoritative backend evidence.

## React Native / Capacitor

```typescript
// React Native: import { purchases } from '@ansight/react-native';
import { purchases } from '@ansight/capacitor';

await purchases.register(); // after SDK initialization
await purchases.refreshStoreKit(['premium.monthly']); // Apple only
await purchases.record({
  productId: 'premium.monthly', source: 'app', entitled: true, deliveryCount: 1,
});
const report = await purchases.validate({
  productId: 'premium.monthly', expectedEntitled: true, expectedDeliveryCount: 1,
});
```

On Android, report store observations from the purchase library's callbacks and
backend verification separately. `refreshStoreKit` rejects on Android. The thin
facades do not contain a second validation engine or claim a browser billing
implementation.

## Flutter

```dart
await Ansight.instance.purchases.register();
await Ansight.instance.purchases.record(const PurchaseObservation(
  productId: 'premium.monthly', source: 'app', entitled: true,
));
final report = await Ansight.instance.purchases.validate(
  productId: 'premium.monthly', expectedEntitled: true,
);
```

Forward results from the app's existing purchase plugin into these typed inputs.
StoreKit refresh is also available on iOS. Account switching should clear both
native diagnostics and any separate application-owned diagnostic instance.

## Verification

The shared fixture suite covers verification rejection, unknown/missing/stale
sources, pending purchases, backend checks, duplicate delivery, wrong transaction
references, grace-period deadlines, expiry and failed app revocation. Each core
also checks its actual output against the generated result schema. Additional
tests cover bounded retention, opaque references, passive reads, and clearing
while StoreKit refresh is in flight. Facade tests cover native delegation and
error propagation. Guard tests cover blocked local and remote entry points,
bridge preflight, production evidence rejection, unknown runtimes, and environment
changes that invalidate retained evidence.

Regenerate the native/.NET result schemas after changing
`docs/contracts/purchases/result.schema.json`:

```sh
python3 scripts/generate-purchase-schemas.py
dotnet test src/dotnet/tests/Ansight.UnitTests/Ansight.UnitTests.csproj -f net9.0 --filter FullyQualifiedName~Purchase
ANSIGHT_ALLOW_REMOTE_TOOLS=true swift test --package-path src/ios --filter PurchaseDiagnosticsTests
# From src/android:
./gradlew :ansight-core:testDebugUnitTest :ansight-purchases-googleplay:assembleRelease
```

These unit fixtures and package builds do not establish real store sandbox,
Family Sharing, cross-device restore, or server-notification acceptance. The
StoreKit adapter reads the latest transaction for each product, rather than an
aggregate entitlement across all family members or a complete transaction history.
