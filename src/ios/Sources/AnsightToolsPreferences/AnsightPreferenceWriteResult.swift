import Foundation

internal struct AnsightPreferenceWriteResult {
    let store: String
    let key: String
    let valueKind: AnsightPreferenceValueKind
    let updated: Bool
}
