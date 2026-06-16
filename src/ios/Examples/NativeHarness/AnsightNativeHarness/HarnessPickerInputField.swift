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
                action: #selector(Coordinator.doneTapped)
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

    func makeCoordinator() -> Coordinator {
        Coordinator(parent: self)
    }

    final class Coordinator: NSObject, UIPickerViewDataSource, UIPickerViewDelegate {
        var parent: HarnessPickerInputField
        weak var picker: UIPickerView?
        weak var textField: UITextField?

        init(parent: HarnessPickerInputField) {
            self.parent = parent
        }

        func numberOfComponents(in pickerView: UIPickerView) -> Int {
            1
        }

        func pickerView(_ pickerView: UIPickerView, numberOfRowsInComponent component: Int) -> Int {
            parent.values.count
        }

        func pickerView(_ pickerView: UIPickerView, titleForRow row: Int, forComponent component: Int) -> String? {
            guard parent.values.indices.contains(row) else {
                return nil
            }

            return parent.values[row]
        }

        func pickerView(_ pickerView: UIPickerView, didSelectRow row: Int, inComponent component: Int) {
            guard parent.values.indices.contains(row) else {
                return
            }

            parent.selection = parent.values[row]
            textField?.text = parent.selection
        }

        func syncSelection() {
            guard let picker, let selectedIndex = parent.values.firstIndex(of: parent.selection) else {
                return
            }

            picker.selectRow(selectedIndex, inComponent: 0, animated: false)
        }

        @objc func doneTapped() {
            textField?.resignFirstResponder()
        }
    }
}
