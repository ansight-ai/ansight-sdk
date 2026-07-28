# Capacitor test corpus

[`capacitor-apps.json`](./capacitor-apps.json) pins 25 licensed, non-archived,
open-source Capacitor 8 applications across Angular, React, Vue, Next, Nuxt,
vanilla TypeScript, Bluetooth, maps, canvas, background audio, Firebase, and
WebView-heavy use cases.

Prepare the corpus beside this repository:

```bash
node scripts/setup-capacitor-test-apps.mjs
```

This shallow-clones every pinned commit into
`../ansight-sdk-test-apps/capacitor`, adds the local `@ansight/capacitor`
dependency, creates a per-app adapter and metadata file, and injects the
adapter into a detected web entry point. Twenty-two apps use module imports;
three legacy no-bundler apps receive the self-contained
`dist/standalone.js` bridge. Existing clones are preserved and only moved to
the pinned commit when necessary.

To install dependencies and run `cap sync` where a native project already
exists:

```bash
node scripts/setup-capacitor-test-apps.mjs --install
```

Use `--clone-only` for an untouched source corpus and `--root=/absolute/path`
to choose a different corpus root. Build `src/capacitor` before preparing the
corpus so the static adapter is current.

Verify every commit pin, license, local dependency, metadata record, and
bootstrap injection:

```bash
node scripts/validate-capacitor-test-apps.mjs
```
