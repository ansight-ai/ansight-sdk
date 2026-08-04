#import <Foundation/Foundation.h>
#import <React/RCTBridge+Private.h>
#import <React/RCTBridge.h>
#import <jsi/instrumentation.h>
#import <jsi/jsi.h>

#include <algorithm>
#include <atomic>
#include <exception>
#include <functional>
#include <initializer_list>
#include <string>
#include <unordered_map>

namespace {
int64_t heapValue(
    const std::unordered_map<std::string, int64_t>& heapInfo,
    std::initializer_list<const char*> keys) {
  for (const auto* key : keys) {
    const auto iterator = heapInfo.find(key);
    if (iterator != heapInfo.end() && iterator->second > 0) {
      return iterator->second;
    }
  }
  return 0;
}
} // namespace

@interface AnsightReactNativeMemorySampler : NSObject
- (void)attachToBridge:(RCTBridge *)bridge;
- (NSNumber *_Nullable)jsHeapUsedBytes;
- (NSNumber *_Nullable)jsHeapTotalBytes;
@end

@implementation AnsightReactNativeMemorySampler {
  __weak RCTBridge *_bridge;
  std::atomic<bool> _refreshScheduled;
  std::atomic<bool> _hasSample;
  std::atomic<int64_t> _jsHeapUsedBytes;
  std::atomic<int64_t> _jsHeapTotalBytes;
}

- (instancetype)init
{
  if (self = [super init]) {
    _refreshScheduled.store(false);
    _hasSample.store(false);
    _jsHeapUsedBytes.store(0);
    _jsHeapTotalBytes.store(0);
  }
  return self;
}

- (void)attachToBridge:(RCTBridge *)bridge
{
  _bridge = bridge;
  [self requestRefresh];
}

- (NSNumber *)jsHeapUsedBytes
{
  [self requestRefresh];
  int64_t value = _jsHeapUsedBytes.load();
  return _hasSample.load() && value > 0 ? @(value) : nil;
}

- (NSNumber *)jsHeapTotalBytes
{
  [self requestRefresh];
  int64_t value = _jsHeapTotalBytes.load();
  return _hasSample.load() && value > 0 ? @(value) : nil;
}

- (void)requestRefresh
{
  RCTBridge *bridge = [_bridge batchedBridge] ?: _bridge;
  if (!bridge) {
    return;
  }

  bool expected = false;
  if (!_refreshScheduled.compare_exchange_strong(expected, true)) {
    return;
  }

  __weak AnsightReactNativeMemorySampler *weakSelf = self;
  __weak RCTBridge *weakBridge = bridge;
  [bridge dispatchBlock:^{
    AnsightReactNativeMemorySampler *strongSelf = weakSelf;
    RCTBridge *strongBridge = weakBridge;
    if (!strongSelf || !strongBridge) {
      if (strongSelf) {
        strongSelf->_refreshScheduled.store(false);
      }
      return;
    }

    @try {
      try {
        // RCTBridgeProxy implements both dispatchBlock:queue: and runtime, so
        // this works with the New Architecture without relying on the legacy
        // RCTCxxBridge-only invokeAsync: selector.
        RCTCxxBridge *cxxBridge = (RCTCxxBridge *)strongBridge;
        auto *runtime = reinterpret_cast<facebook::jsi::Runtime *>(cxxBridge.runtime);
        if (runtime) {
          const auto heapInfo = runtime->instrumentation().getHeapInfo(false);
          int64_t used = heapValue(
              heapInfo,
              {"hermes_allocatedBytes", "allocatedBytes", "usedJSHeapSize"});
          int64_t total = heapValue(
              heapInfo,
              {"hermes_heapSize", "heapSize", "totalJSHeapSize"});
          if (used > 0) {
            strongSelf->_jsHeapUsedBytes.store(used);
            strongSelf->_jsHeapTotalBytes.store(std::max(total, used));
            strongSelf->_hasSample.store(true);
          }
        }
      } catch (const std::exception &) {
      } catch (...) {
      }
    } @catch (NSException *) {
    }

    strongSelf->_refreshScheduled.store(false);
  } queue:RCTJSThread];
}

@end
