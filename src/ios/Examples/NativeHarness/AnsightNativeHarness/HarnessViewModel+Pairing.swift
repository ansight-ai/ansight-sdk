import Ansight

extension HarnessViewModel {
    func connect(_ request: HostConnectionRequest) async {
        let result = await AnsightRuntime.shared.connect(request)
        connectionMessage = result.message
        refresh()
    }

    func disconnect() async {
        let result = await AnsightRuntime.shared.disconnect()
        connectionMessage = result.message
        refresh()
    }

    func clearPairingState() {
        AnsightRuntime.shared.clearSavedPairing()
        AnsightRuntime.shared.clearCachedSession()
        connectionMessage = "Saved registration and cached session cleared."
        refresh()
    }
}
