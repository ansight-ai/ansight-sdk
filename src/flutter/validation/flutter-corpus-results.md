# Flutter open-source corpus validation

Generated: `2026-07-28T10:07:24.508998Z`

**1/9 apps passed** dependency resolution, target analysis, and Android debug compilation.

| App | Flutter | Result | Evidence |
| --- | --- | --- | --- |
| aaronoe/FlutterCinematic | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/aaronoe__FlutterCinematic/android-debug-apk.log` |
| mhmzdev/the-holy-quran-app | 3.38.8 | FAIL | pub-get failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/mhmzdev__the-holy-quran-app/pub-get.log` |
| KarimElghamry/chillify | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/KarimElghamry__chillify/android-debug-apk.log` |
| CoderMikeHe/flutter_wechat | 3.0.5 | FAIL | pub-get failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/CoderMikeHe__flutter_wechat/pub-get.log` |
| designDo/flutter-checkio | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/designDo__flutter-checkio/android-debug-apk.log` |
| redsolver/noteless | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/redsolver__noteless/android-debug-apk.log` |
| SAGARSURI/MyMovies | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/SAGARSURI__MyMovies/android-debug-apk.log` |
| darkmoonight/Zest | stable | PASS | pub-get: 1s, analyze-target: 7s, android-debug-apk: 31s, apk-evidence: 0s |
| LonelyCpp/flutter_weather | 3.0.5 | FAIL | pub-get failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/LonelyCpp__flutter_weather/pub-get.log` |

Each app contains `lib/ansight_validation_main.dart`, which initializes and activates the native Ansight runtime, installs Flutter instrumentation, records an integration event, and then invokes the upstream application entry point.
