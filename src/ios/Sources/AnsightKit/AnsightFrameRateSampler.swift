import Foundation

#if canImport(UIKit)
import QuartzCore
#endif

final class AnsightFrameRateSampler: NSObject, @unchecked Sendable {
    static var isAvailable: Bool {
        #if canImport(UIKit)
        true
        #else
        false
        #endif
    }

    private let sampleIntervalSeconds: TimeInterval
    private let sampleHandler: @Sendable (Int) -> Void

    #if canImport(UIKit)
    private var displayLink: CADisplayLink?
    private var frameCount = 0
    private var sampleStartTimestamp: CFTimeInterval = 0
    #endif

    init(
        sampleFrequencyMilliseconds: Int,
        sampleHandler: @escaping @Sendable (Int) -> Void
    ) {
        self.sampleIntervalSeconds = max(0.2, Double(sampleFrequencyMilliseconds) / 1_000.0)
        self.sampleHandler = sampleHandler
    }

    func start() {
        #if canImport(UIKit)
        Task { @MainActor in
            startOnMainActor()
        }
        #endif
    }

    func stop() {
        #if canImport(UIKit)
        Task { @MainActor in
            stopOnMainActor()
        }
        #endif
    }

    #if canImport(UIKit)
    @MainActor
    private func startOnMainActor() {
        stopOnMainActor()
        frameCount = 0
        sampleStartTimestamp = 0

        let link = CADisplayLink(target: self, selector: #selector(displayLinkDidFire(_:)))
        link.add(to: .main, forMode: .common)
        displayLink = link
    }

    @MainActor
    private func stopOnMainActor() {
        displayLink?.invalidate()
        displayLink = nil
        frameCount = 0
        sampleStartTimestamp = 0
    }

    @objc
    private func displayLinkDidFire(_ displayLink: CADisplayLink) {
        guard sampleStartTimestamp > 0 else {
            sampleStartTimestamp = displayLink.timestamp
            frameCount = 0
            return
        }

        frameCount += 1
        let elapsedSeconds = displayLink.timestamp - sampleStartTimestamp
        guard elapsedSeconds >= sampleIntervalSeconds else {
            return
        }

        let framesPerSecond = Int((Double(frameCount) / elapsedSeconds).rounded())
        frameCount = 0
        sampleStartTimestamp = displayLink.timestamp
        sampleHandler(max(0, framesPerSecond))
    }
    #endif
}
