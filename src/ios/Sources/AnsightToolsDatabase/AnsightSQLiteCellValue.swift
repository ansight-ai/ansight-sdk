import AnsightKit
import Foundation

internal struct AnsightSQLiteCellValue: Sendable, Equatable {
    let storageType: String
    let value: JSONValue
}
