import SwiftUI

struct HarnessKeyValueRow: View {
    let title: String
    let value: String

    init(_ title: String, value: String) {
        self.title = title
        self.value = value
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(title)
                .font(.subheadline)
                .foregroundStyle(.secondary)

            Spacer(minLength: 12)

            Text(value)
                .font(.subheadline.weight(.medium))
                .multilineTextAlignment(.trailing)
                .lineLimit(2)
                .minimumScaleFactor(0.75)
        }
        .padding(.vertical, 8)
    }
}
