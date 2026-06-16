import SwiftUI

struct HarnessStatusBadge: View {
    let title: String
    let systemImage: String
    let tint: Color

    init(_ title: String, systemImage: String, tint: Color) {
        self.title = title
        self.systemImage = systemImage
        self.tint = tint
    }

    var body: some View {
        Label(title, systemImage: systemImage)
            .font(.caption.weight(.semibold))
            .lineLimit(1)
            .minimumScaleFactor(0.75)
            .padding(.horizontal, 9)
            .padding(.vertical, 6)
            .foregroundStyle(tint)
            .background(tint.opacity(0.12))
            .clipShape(Capsule())
    }
}
