import SwiftUI

struct HarnessActionGrid<Content: View>: View {
    @ViewBuilder let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        LazyVGrid(
            columns: [GridItem(.adaptive(minimum: 132), spacing: 8, alignment: .top)],
            alignment: .leading,
            spacing: 8
        ) {
            content
        }
    }
}
