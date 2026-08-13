# JNI crash-signal entry points are resolved by their exported C names.
-keep class ai.ansight.runtime.AndroidCrashSignalBridge {
    native <methods>;
}
