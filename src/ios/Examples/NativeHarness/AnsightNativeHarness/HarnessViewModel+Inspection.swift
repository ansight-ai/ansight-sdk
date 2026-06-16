import Ansight
import Foundation

extension HarnessViewModel {
    var seededDataText: String {
        """
        seededAtUtc=\(seededAtUtc)
        preferencePrefix=\(HarnessConstants.preferencePrefix)
        preferenceKeys=\(HarnessConstants.preferencePrefix)mode,\(HarnessConstants.preferencePrefix)lastSeededAtUtc,\(HarnessConstants.preferencePrefix)launchCount
        fileRoot=documents
        filePath=ansight-harness/hello.txt
        databasePath=ansight-harness/sample.sqlite
        databaseTables=harness_events,harness_orders,harness_inventory,harness_navigation_events
        databaseRowCount=\(databaseRowCount)
        secureStorageService=\(HarnessConstants.secureStorageService)
        secureStorageKey=\(HarnessConstants.secureStorageKey)
        customTools=harness.state.snapshot,harness.reflection_roots.list,harness.reflection_root.inspect
        """
    }

    var debugText: String {
        """
        initialized=\(snapshot.initialized)
        active=\(snapshot.active)
        sessionOpen=\(snapshot.sessionOpen)
        metricsRecorded=\(snapshot.metricsRecorded)
        eventsRecorded=\(snapshot.eventsRecorded)
        registeredTools=\(snapshot.registeredTools)
        executableTools=\(snapshot.executableTools)
        connectionState=\(snapshot.hostConnectionStatus.connectionState.rawValue)
        connectionSummary=\(snapshot.hostConnectionStatus.summaryMessage)
        screenCaptureActive=\(snapshot.screenCaptureActive)
        screenFramesCaptured=\(snapshot.screenFramesCaptured)
        screenFramesSent=\(snapshot.screenFramesSent)
        lastScreenCaptureMessage=\(snapshot.lastScreenCaptureMessage ?? "<none>")
        frameRateCaptureActive=\(snapshot.frameRateCaptureActive)
        lastFrameRate=\(snapshot.lastFrameRate.map(String.init) ?? "<none>")
        touchCaptureEnabled=\(snapshot.touchCaptureEnabled)
        touchCaptureActive=\(snapshot.touchCaptureActive)
        touchCaptureStreamingActive=\(snapshot.touchCaptureStreamingActive)
        touchesCaptured=\(snapshot.touchesCaptured)
        touchesSent=\(snapshot.touchesSent)
        lastTouchCaptureMessage=\(snapshot.lastTouchCaptureMessage ?? "<none>")
        lifecycleState=\(snapshot.lifecycleState.rawValue)
        currentScreen=\(snapshot.currentScreen?.name ?? "<none>")
        toolDiscoveryEnabled=\(snapshot.toolDiscoveryEnabled)
        toolExecutionEnabled=\(snapshot.toolExecutionEnabled)
        embeddedDeveloperPairingAvailable=\(snapshot.embeddedDeveloperPairingAvailable)
        bundledHarnessPairingAvailable=\(HarnessBundledPairingConfig.json != nil)
        bundledHarnessPairingSource=\(HarnessBundledPairingConfig.sourceDescription)
        bundledHarnessPairingHasHostDiscovery=\(HarnessBundledPairingConfig.hasHostDiscovery)
        detectedBundledTools=\(snapshot.detectedBundledTools.joined(separator: ","))
        lastPairingConfigId=\(snapshot.lastPairingConfigId ?? "<none>")
        resolvedHostAddress=\(snapshot.resolvedHostAddress ?? "<none>")
        sessionMessage=\(snapshot.sessionMessage ?? "<none>")
        selectedTab=\(selectedTab.rawValue)
        activeModal=\(activeModal)
        flyoutSelection=\(flyoutSelection)
        pushDepth=\(pushDepth)
        sceneMaterial=\(sceneMaterial)
        sceneRotationEnabled=\(sceneRotationEnabled)
        sceneSpinSpeed=\(sceneSpinSpeed)
        selectedSceneNode=\(selectedSceneNode)
        keyboardText=\(keyboardText.isEmpty ? "<empty>" : keyboardText)
        pickerValue=\(pickerValue)
        expeditedBilling=\(expeditedBilling)
        quantity=\(Int(quantity))
        databaseRowCount=\(databaseRowCount)
        navigationEvents=\(navigationEvents.joined(separator: " | "))
        """
    }

    var reflectionRootsText: String {
        guard case .array(let roots) = inspectionStore.rootsJSON() else {
            return "<none>"
        }

        return roots.compactMap { root -> String? in
            guard case .object(let object) = root,
                  case .string(let id)? = object["rootId"],
                  case .string(let name)? = object["name"]
            else {
                return nil
            }

            return "\(id) - \(name)"
        }
        .joined(separator: "\n")
    }

    func refresh() {
        snapshot = AnsightRuntime.shared.snapshot()
        syncInspectionState()
    }

    func syncInspectionState() {
        let state = HarnessInspectionState(
            connectionMessage: connectionMessage,
            selectedTab: selectedTab,
            keyboardText: keyboardText,
            pickerValue: pickerValue,
            expeditedBilling: expeditedBilling,
            quantity: Int(quantity),
            activeModal: activeModal,
            flyoutSelection: flyoutSelection,
            pushDepth: pushDepth,
            sceneMaterial: sceneMaterial,
            sceneRotationEnabled: sceneRotationEnabled,
            sceneSpinSpeed: sceneSpinSpeed,
            selectedSceneNode: selectedSceneNode,
            seededAtUtc: seededAtUtc,
            documentsRoot: harnessDirectoryURL()?.path ?? "<unresolved>",
            databasePath: databaseURL()?.path ?? "<unresolved>",
            databaseRowCount: databaseRowCount,
            navigationEvents: navigationEvents,
            runtimeSnapshot: snapshot
        )
        inspectionStore.update(state)
    }
}
