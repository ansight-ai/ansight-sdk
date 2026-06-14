import Foundation

internal struct AnsightPreferenceValueResult {
    let store: String
    let key: String
    let exists: Bool
    let value: String?
    let valueKind: AnsightPreferenceValueKind?
}
