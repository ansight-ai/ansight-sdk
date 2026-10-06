import SwiftUI

struct HarnessNativeUISectionView: View {
    @ObservedObject var harness: HarnessViewModel

    var body: some View {
        HarnessSection("Native Controls", systemImage: "iphone.gen3") {
            VStack(alignment: .leading, spacing: 14) {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Keyboard")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)

                    TextField("Type to validate input capture", text: $harness.keyboardText)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .textFieldStyle(.roundedBorder)
                        .onSubmit {
                            harness.recordEvent(label: "ios_harness_keyboard_submit")
                        }
                        .onChange(of: harness.keyboardText) { _ in
                            harness.formStateChanged("keyboardText")
                        }
                }

                VStack(alignment: .leading, spacing: 6) {
                    Text("Picker Overlay")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)

                    HarnessPickerInputField(
                        title: "Shipping Speed",
                        values: harness.shippingSpeeds,
                        selection: $harness.pickerValue
                    )
                    .frame(height: 46)
                    .onChange(of: harness.pickerValue) { _ in
                        harness.formStateChanged("pickerValue")
                    }
                }

                Divider()

                Toggle(isOn: $harness.expeditedBilling) {
                    Label("Expedited billing", systemImage: "creditcard")
                        .font(.subheadline.weight(.medium))
                }
                .onChange(of: harness.expeditedBilling) { _ in
                    harness.formStateChanged("expeditedBilling")
                }

                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Label("Quantity", systemImage: "number")
                            .font(.subheadline.weight(.medium))

                        Spacer()

                        Text("\(Int(harness.quantity))")
                            .font(.headline.monospacedDigit())
                            .padding(.horizontal, 10)
                            .padding(.vertical, 5)
                            .background(Color(.tertiarySystemGroupedBackground))
                            .clipShape(Capsule())
                    }

                    Slider(value: $harness.quantity, in: 1...10, step: 1)
                        .onChange(of: harness.quantity) { _ in
                            harness.formStateChanged("quantity")
                        }
                }
            }
        }
    }
}
