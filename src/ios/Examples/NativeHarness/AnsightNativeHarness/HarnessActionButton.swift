import SwiftUI

struct HarnessActionButton: View {
    let title: String
    let systemImage: String
    let role: ButtonRole?
    let isBusy: Bool
    let isProminent: Bool
    let action: () -> Void

    init(
        _ title: String,
        systemImage: String,
        role: ButtonRole? = nil,
        isBusy: Bool,
        isProminent: Bool = false,
        action: @escaping () -> Void
    ) {
        self.title = title
        self.systemImage = systemImage
        self.role = role
        self.isBusy = isBusy
        self.isProminent = isProminent
        self.action = action
    }

    var body: some View {
        Button(role: role, action: action) {
            Label(title, systemImage: systemImage)
                .font(.subheadline.weight(.semibold))
                .lineLimit(1)
                .minimumScaleFactor(0.72)
                .padding(.horizontal, 12)
                .frame(maxWidth: .infinity, minHeight: 44)
                .foregroundStyle(foregroundStyle)
                .background(backgroundStyle)
                .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
        }
        .buttonStyle(.plain)
        .disabled(isBusy)
        .opacity(isBusy ? 0.55 : 1)
    }

    private var foregroundStyle: Color {
        if role == .destructive {
            return .red
        }

        return isProminent ? .white : .accentColor
    }

    private var backgroundStyle: Color {
        if role == .destructive {
            return Color.red.opacity(0.12)
        }

        return isProminent ? .accentColor : Color.accentColor.opacity(0.12)
    }
}
