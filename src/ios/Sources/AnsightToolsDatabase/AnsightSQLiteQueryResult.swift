import AnsightCore
import Foundation

internal struct AnsightSQLiteQueryResult: Sendable, Equatable {
    let columns: [String]
    let columnMetadata: [AnsightSQLiteColumnMetadata]
    let rows: [[String: JSONValue]]
    let rowValues: [[JSONValue]]
    let truncated: Bool
}
