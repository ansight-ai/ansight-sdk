import Foundation

internal struct AnsightPendingBinaryTransfer {
    let transferId: UUID
    let data: Data
    let chunkBytes: Int
    let description: String

    init(
        transferId: UUID,
        data: Data,
        chunkBytes: Int,
        description: String
    ) {
        self.transferId = transferId
        self.data = data
        self.chunkBytes = max(1, min(chunkBytes, 1024 * 1024))
        self.description = description
    }
}
