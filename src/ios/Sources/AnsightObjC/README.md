# AnsightObjC

Objective-C facade over the aggregate `Ansight` Swift product.

Swift apps should prefer `AnsightRuntime.shared` directly. Use `AnsightObjC`
for Objective-C hosts and bridge layers that need an `NSObject` API.

## Usage

```objc
#import <AnsightObjC/AnsightObjC-Swift.h>

NSError *error = nil;
[ANSAnsight initializeAndActivateWithDefaultOptionsAndReturnError:&error];
[ANSAnsight recordMetric:42 channel:255 error:&error];
[ANSAnsight recordEventWithLabel:@"checkout"
                            type:@"Info"
                         details:@"loaded"
                         channel:255
                           error:&error];
```

The facade also exposes host connection, custom client logs, grouped session
properties, metric streams, and visual tree provider registration.
