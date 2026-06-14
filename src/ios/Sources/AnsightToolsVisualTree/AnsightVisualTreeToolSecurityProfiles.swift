import AnsightKit
import Foundation

public enum AnsightVisualTreeToolSecurityProfiles {
    public static let getVisualTree = AnsightToolSecurity(
        level: .high,
        summary: "Inspects the active UI hierarchy and can reveal visible text, accessibility labels, and layout metadata.",
        implications: [
            AnsightToolSecurityImplications.inspectsUi,
            AnsightToolSecurityImplications.metadataDisclosure,
        ]
    )

    public static let getScreenshot = AnsightToolSecurity(
        level: .high,
        summary: "Captures and exports an image of the active app scene.",
        implications: [
            AnsightToolSecurityImplications.capturesScreenshots,
            AnsightToolSecurityImplications.exportsData,
            AnsightToolSecurityImplications.usesBinaryTransfer,
        ]
    )

    public static let inspectNode = AnsightToolSecurity(
        level: .high,
        summary: "Inspects a single UI node and can reveal visible text, accessibility labels, and implementation details.",
        implications: [
            AnsightToolSecurityImplications.inspectsUi,
            AnsightToolSecurityImplications.metadataDisclosure,
        ]
    )

    public static let showOverlay = AnsightToolSecurity(
        level: .moderate,
        summary: "Draws an input-transparent diagnostic overlay over the active UI.",
        implications: [
            AnsightToolSecurityImplications.inspectsUi,
            AnsightToolSecurityImplications.mutatesRuntimeState,
        ]
    )

    public static let getOverlay = AnsightToolSecurity(
        level: .low,
        summary: "Reads metadata for an active diagnostic overlay.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.inspectsRuntimeState,
        ]
    )

    public static let queryOverlays = AnsightToolSecurity(
        level: .low,
        summary: "Lists active diagnostic overlays and their metadata.",
        implications: [
            AnsightToolSecurityImplications.metadataDisclosure,
            AnsightToolSecurityImplications.inspectsRuntimeState,
        ]
    )

    public static let updateOverlay = AnsightToolSecurity(
        level: .moderate,
        summary: "Updates an active diagnostic overlay over the app UI.",
        implications: [
            AnsightToolSecurityImplications.inspectsUi,
            AnsightToolSecurityImplications.mutatesRuntimeState,
        ]
    )

    public static let removeOverlay = AnsightToolSecurity(
        level: .moderate,
        summary: "Removes an active diagnostic overlay from the app UI.",
        implications: [
            AnsightToolSecurityImplications.mutatesRuntimeState,
        ]
    )

    public static let clearOverlays = AnsightToolSecurity(
        level: .moderate,
        summary: "Removes diagnostic overlays from the app UI.",
        implications: [
            AnsightToolSecurityImplications.mutatesRuntimeState,
        ]
    )
}
