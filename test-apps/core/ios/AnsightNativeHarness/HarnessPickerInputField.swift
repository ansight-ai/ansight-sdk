import SwiftUI
import UIKit

struct HarnessPickerInputField: UIViewRepresentable {
    let title: String
    let values: [String]
    @Binding var selection: String

    func makeUIView(context: Context) -> UITextField {
        let textField = UITextField(frame: .zero)
        textField.borderStyle = .roundedRect
        textField.placeholder = title
        textField.text = selection
        textField.tintColor = .clear

        let picker = UIPickerView()
        picker.dataSource = context.coordinator
        picker.delegate = context.coordinator
        textField.inputView = picker
        context.coordinator.picker = picker

        let toolbar = UIToolbar()
        toolbar.sizeToFit()
        toolbar.items = [
            UIBarButtonItem(title: title, style: .plain, target: nil, action: nil),
            UIBarButtonItem(barButtonSystemItem: .flexibleSpace, target: nil, action: nil),
            UIBarButtonItem(
                barButtonSystemItem: .done,
                target: context.coordinator,
                action: #selector(HarnessPickerInputFieldCoordinator.doneTapped)
            ),
        ]
        textField.inputAccessoryView = toolbar
        context.coordinator.textField = textField
        context.coordinator.syncSelection()
        return textField
    }

    func updateUIView(_ uiView: UITextField, context: Context) {
        context.coordinator.parent = self
        uiView.placeholder = title
        uiView.text = selection
        context.coordinator.picker?.reloadAllComponents()
        context.coordinator.syncSelection()
    }

    func makeCoordinator() -> HarnessPickerInputFieldCoordinator {
        HarnessPickerInputFieldCoordinator(parent: self)
    }
}
