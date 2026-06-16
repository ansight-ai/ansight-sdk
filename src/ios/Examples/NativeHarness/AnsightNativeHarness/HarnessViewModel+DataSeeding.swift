import Ansight
import Foundation

extension HarnessViewModel {
    func seedDataTapped() {
        do {
            try seedHarnessData()
            connectionMessage = "Harness data re-seeded."
        } catch {
            connectionMessage = error.localizedDescription
        }

        refresh()
    }

    func databasePathText() -> String {
        databaseURL()?.path ?? "<unresolved>"
    }

    func seedHarnessData() throws {
        seededAtUtc = AnsightClock.isoNow()
        try prepareHarnessFileSystemSample()
        try prepareHarnessDatabaseSample()
        try prepareHarnessSecureStorageSample()

        let defaults = UserDefaults.standard
        defaults.set("native-harness", forKey: "\(HarnessConstants.preferencePrefix)mode")
        defaults.set(seededAtUtc, forKey: "\(HarnessConstants.preferencePrefix)lastSeededAtUtc")
        defaults.set(defaults.integer(forKey: "\(HarnessConstants.preferencePrefix)launchCount") + 1, forKey: "\(HarnessConstants.preferencePrefix)launchCount")
    }
}
