import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

struct ResolvedConnectionRequest {
    let document: ParsedPairingDocument
    let source: HostConnectionSource
}
