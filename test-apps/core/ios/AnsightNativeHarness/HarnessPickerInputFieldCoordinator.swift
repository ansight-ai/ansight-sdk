import SwiftUI
import UIKit

final class HarnessPickerInputFieldCoordinator: NSObject, UIPickerViewDataSource, UIPickerViewDelegate {
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
