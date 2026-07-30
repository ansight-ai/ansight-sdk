# Flutter open-source corpus validation

Generated: `2026-07-30T06:24:51.552326Z`

**18/25 apps passed** dependency resolution, target analysis, and Android debug compilation.

| App | Flutter | Result | Evidence |
| --- | --- | --- | --- |
| Anxcye/anx-reader | 3.38.8 | PASS | pub-get: 4s, analyze-target: 16s, android-debug-apk: 1051s, apk-evidence: 0s |
| gskinnerTeam/flutter-wonderous-app | stable | PASS | pub-get: 1s, analyze-target: 5s, android-debug-apk: 244s, apk-evidence: 0s |
| gskinnerTeam/flutter-folio | 3.0.5 | PASS | pub-get: 5s, analyze-target: 4s, android-debug-apk: 113s, apk-evidence: 0s |
| yubo725/flutter-osc | 3.0.5 | PASS | pub-get: 2s, analyze-target: 2s, android-debug-apk: 16s, apk-evidence: 0s |
| X-Wei/flutter_catalog | stable | PASS | pub-get: 3s, code-generation: 20s, analyze-target: 11s, android-debug-apk: 61s, apk-evidence: 0s |
| asjqkkkk/flutter-todos | 3.0.5 | PASS | pub-get: 3s, analyze-target: 4s, android-debug-apk: 125s, apk-evidence: 0s |
| mkobuolys/flutter-design-patterns | stable | PASS | pub-get: 0s, analyze-target: 2s, android-debug-apk: 23s, apk-evidence: 0s |
| AimesSoft/NipaPlay-Reload | 3.41.9 | PASS | pub-get: 3s, analyze-target: 15s, android-debug-apk: 66s, apk-evidence: 0s |
| bizz84/layout-demo-flutter | stable | PASS | pub-get: 4s, analyze-target: 8s, android-debug-apk: 175s, apk-evidence: 0s |
| RIP-Comm/sossoldi | 3.38.8 | PASS | pub-get: 2s, analyze-target: 93s, android-debug-apk: 705s, apk-evidence: 0s |
| abuanwar072/Welcome-Login-Signup-Page-Flutter | 3.19.6 | PASS | pub-get: 3s, analyze-target: 7s, android-debug-apk: 21s, apk-evidence: 0s |
| marchellodev/sharik | 3.0.5 | PASS | pub-get: 4s, analyze-target: 2s, android-debug-apk: 21s, apk-evidence: 0s |
| guozhigq/flutter_v2ex | stable | PASS | pub-get: 1s, analyze-target: 11s, android-debug-apk: 33s, apk-evidence: 0s |
| darkmoonight/Rain | stable | PASS | pub-get: 2s, analyze-target: 10s, android-debug-apk: 224s, apk-evidence: 0s |
| aaronoe/FlutterCinematic | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/aaronoe__FlutterCinematic/android-debug-apk.log` |
| mhmzdev/the-holy-quran-app | 3.38.8 | FAIL | pub-get failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/mhmzdev__the-holy-quran-app/pub-get.log` |
| KarimElghamry/chillify | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/KarimElghamry__chillify/android-debug-apk.log` |
| CoderMikeHe/flutter_wechat | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/CoderMikeHe__flutter_wechat/android-debug-apk.log` |
| designDo/flutter-checkio | 3.0.5 | PASS | pub-get: 23s, analyze-target: 42s, android-debug-apk: 127s, apk-evidence: 0s |
| bizz84/movie_app_state_management_flutter | 3.0.5 | PASS | pub-get: 7s, workspace-pub-get: 4s, workspace-code-generation: 45s, analyze-target: 27s, android-debug-apk: 81s, apk-evidence: 0s |
| bimsina/notes-app | 3.0.5 | PASS | pub-get: 11s, analyze-target: 8s, android-debug-apk: 80s, apk-evidence: 0s |
| redsolver/noteless | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/redsolver__noteless/android-debug-apk.log` |
| SAGARSURI/MyMovies | 3.0.5 | FAIL | android-debug-apk failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/SAGARSURI__MyMovies/android-debug-apk.log` |
| darkmoonight/Zest | stable | PASS | pub-get: 8s, analyze-target: 107s, android-debug-apk: 62s, apk-evidence: 0s |
| LonelyCpp/flutter_weather | 3.0.5 | FAIL | pub-get failed; `/Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/validation/logs/LonelyCpp__flutter_weather/pub-get.log` |

Each app contains `lib/ansight_validation_main.dart`, which initializes and activates the native Ansight runtime, installs Flutter instrumentation, records an integration event, and then invokes the upstream application entry point.
