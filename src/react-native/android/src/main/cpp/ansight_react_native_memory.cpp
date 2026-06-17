#include <jni.h>
#include <jsi/instrumentation.h>
#include <jsi/jsi.h>

#include <cstdint>
#include <exception>
#include <initializer_list>
#include <string>
#include <unordered_map>

namespace {
jlong heapValue(
    const std::unordered_map<std::string, int64_t>& heapInfo,
    std::initializer_list<const char*> keys) {
  for (const auto* key : keys) {
    const auto iterator = heapInfo.find(key);
    if (iterator != heapInfo.end() && iterator->second > 0) {
      return static_cast<jlong>(iterator->second);
    }
  }
  return 0;
}
} // namespace

extern "C" JNIEXPORT jlongArray JNICALL
Java_ai_ansight_reactnative_ReactNativeMemoryNative_readHeapInfo(
    JNIEnv* env,
    jobject,
    jlong runtimePointer) {
  jlong values[2] = {0, 0};

  if (runtimePointer != 0) {
    try {
      auto* runtime =
          reinterpret_cast<facebook::jsi::Runtime*>(runtimePointer);
      const auto heapInfo = runtime->instrumentation().getHeapInfo(false);
      values[0] = heapValue(
          heapInfo,
          {"hermes_allocatedBytes", "allocatedBytes", "usedJSHeapSize"});
      values[1] = heapValue(
          heapInfo,
          {"hermes_heapSize", "heapSize", "totalJSHeapSize"});
    } catch (const std::exception&) {
      values[0] = 0;
      values[1] = 0;
    } catch (...) {
      values[0] = 0;
      values[1] = 0;
    }
  }

  jlongArray result = env->NewLongArray(2);
  if (result != nullptr) {
    env->SetLongArrayRegion(result, 0, 2, values);
  }
  return result;
}
