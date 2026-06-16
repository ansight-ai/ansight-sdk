import Ansight

extension HarnessViewModel {
    func selectedTabChanged(_ tab: HarnessTab) {
        noteNavigation("tab:\(tab.rawValue)")
        recordScreen(tab.screenName)
    }

    func formStateChanged(_ field: String) {
        noteNavigation("form:\(field)")
        refresh()
    }

    func sceneStateChanged(_ event: String) {
        noteNavigation("scene:\(event)")
        refresh()
    }

    func modalStateChanged(_ modal: String) {
        activeModal = modal
        noteNavigation("modal:\(modal)")
        refresh()
    }

    func pushDepthChanged(_ depth: Int) {
        pushDepth = depth
        noteNavigation("push-depth:\(depth)")
        refresh()
    }

    func flyoutChanged(_ selection: String) {
        flyoutSelection = selection
        noteNavigation("flyout:\(selection)")
        refresh()
    }

    func noteNavigation(_ event: String) {
        let entry = "\(AnsightClock.isoNow()) \(event)"
        navigationEvents.append(entry)
        if navigationEvents.count > 20 {
            navigationEvents.removeFirst(navigationEvents.count - 20)
        }
        syncInspectionState()
    }
}
