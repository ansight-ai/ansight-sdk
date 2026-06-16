import SwiftUI

struct HarnessFlyoutDrawerView: View {
    @ObservedObject var harness: HarnessViewModel
    @Binding var isVisible: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 10) {
                Image(systemName: "sidebar.leading")
                    .font(.headline.weight(.semibold))
                    .foregroundStyle(.white)
                    .frame(width: 34, height: 34)
                    .background(Color.blue)
                    .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))

                VStack(alignment: .leading, spacing: 2) {
                    Text("Flyout")
                        .font(.headline.weight(.semibold))
                    Text(harness.flyoutSelection)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }

            ForEach(harness.flyoutItems, id: \.self) { item in
                Button {
                    harness.flyoutChanged(item)
                    withAnimation(.easeInOut(duration: 0.2)) {
                        isVisible = false
                    }
                } label: {
                    Label(item, systemImage: item == harness.flyoutSelection ? "checkmark.circle.fill" : "circle")
                        .font(.subheadline.weight(.medium))
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .frame(minHeight: 40)
                }
                .buttonStyle(.borderless)
                .foregroundStyle(item == harness.flyoutSelection ? .blue : .primary)
            }

            Divider()

            Button {
                harness.flyoutChanged("drawer-closed")
                withAnimation(.easeInOut(duration: 0.2)) {
                    isVisible = false
                }
            } label: {
                Label("Close", systemImage: "xmark")
                    .font(.subheadline.weight(.semibold))
                    .frame(maxWidth: .infinity, minHeight: 42)
            }
            .buttonStyle(.bordered)
        }
        .padding(20)
        .frame(width: 304)
        .frame(maxHeight: .infinity, alignment: .topLeading)
        .background(Color(.systemBackground))
        .shadow(radius: 14)
    }
}
