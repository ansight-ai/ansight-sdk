# SDK motion capture test apps

These Android and iOS apps demonstrate the opt-in `AnsightMotion` package.
They enable capture through builder options, while each app owns its sensor
subscriptions. No accelerometer listener starts on app launch.

## Android

App ID: `ai.ansight.motion.testapp`.

From the `ansight-sdk` repository root:

```sh
(cd src/android && ./gradlew :motion-test-app:assembleDebug)
ansight test validate test-apps/motion/android
```

The app's **Enable accelerometer** button registers its listener. The
`motion.shake-and-samples` Ansight test checks that injection before this action
records no samples, then injects a shake and a known accelerometer sequence.
It checks visible app state and the session timeline's `motion` category.

## iOS

App ID: `ai.ansight.motion.ios-testapp`.

```sh
(cd test-apps/motion/ios && xcodegen generate && \
  xcodebuild -project AnsightMotionTest.xcodeproj -scheme AnsightMotionTest \
    -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build)
ansight test validate test-apps/motion/ios
```

The `motion.ios-shake` Ansight test injects a Simulator shake, checks that the
app's UIKit responder receives it, records a known accelerometer sample, and
verifies both events in the timeline. App-controlled Core Motion sampling is
also available for device checks.

The [independent motion apps](../../../../ansight-test-apps/motion/README.md)
test sensor injection. Build and install an app before running its Ansight
workspace test.
