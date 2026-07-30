# Ansight Capacitor harness

This app validates the public `@ansight/capacitor` surface on Android and iOS.
It includes 54 feature-level checks derived from the React Native SDK contract:
options and lifecycle, telemetry aliases and retention, capture, custom
properties, JavaScript tools, DOM inspection, artifacts, native files,
preferences, SQLite, secure-storage expectations, host capabilities, pairing,
and the complete live-session lifecycle.

## Run

```bash
npm install
npm run run:android
# or
npm run run:ios
```

`npm run sync` builds the web app, creates either missing native project, and
runs `cap sync`. Use Node 22+, Java 21, Android SDK 36, and Xcode 26+.

The safe suite avoids host/session mutations. Scan a Studio enrollment QR once
before running the configured suite. Every destructive connection check
reconnects before it finishes, so the app remains visible in Ansight Studio.

For unattended local validation, add an ignored
`public/ansight-autorun.json` containing `{"suite":"safe"}` or
`{"suite":"configured"}` before building.

The app seeds data/cache files, `ansight.harness.*` preference keys, and a
SQLite database containing `harness_orders` and `harness_events`. It also
publishes `harness.validation.expectations`, `harness.sdk.surface`, and state
tools for host-driven validation. Results can be exported as
`ai.ansight.capacitor-harness.results.v2` JSON.
