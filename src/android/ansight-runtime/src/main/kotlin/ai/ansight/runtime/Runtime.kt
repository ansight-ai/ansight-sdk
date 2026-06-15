package ai.ansight.runtime

import android.app.Application

object Runtime {
    @JvmStatic
    fun isInitialized(): Boolean = AnsightRuntime.snapshot().initialized

    @JvmStatic
    @JvmOverloads
    fun initialize(application: Application, options: AnsightOptions = AnsightOptions()) {
        AnsightRuntime.initialize(application, options)
    }

    @JvmStatic
    @JvmOverloads
    fun Initialize(application: Application, options: AnsightOptions = AnsightOptions()) {
        initialize(application, options)
    }

    @JvmStatic
    @JvmOverloads
    fun initializeAndActivate(application: Application, options: AnsightOptions = AnsightOptions()) {
        AnsightRuntime.initializeAndActivate(application, options)
    }

    @JvmStatic
    @JvmOverloads
    fun InitializeAndActivate(application: Application, options: AnsightOptions = AnsightOptions()) {
        initializeAndActivate(application, options)
    }

    @JvmStatic
    fun activate() {
        if (isInitialized()) {
            AnsightRuntime.activate()
        }
    }

    @JvmStatic
    fun Activate() {
        activate()
    }

    @JvmStatic
    fun deactivate() {
        if (isInitialized()) {
            AnsightRuntime.deactivate()
        }
    }

    @JvmStatic
    fun Deactivate() {
        deactivate()
    }

    @JvmStatic
    fun clear() {
        if (isInitialized()) {
            AnsightRuntime.clear()
        }
    }

    @JvmStatic
    fun Clear() {
        clear()
    }

    @JvmStatic
    @JvmOverloads
    fun metric(value: Long, channel: Int = AnsightChannels.Unspecified) {
        if (isInitialized()) {
            AnsightRuntime.metric(value, channel)
        }
    }

    @JvmStatic
    @JvmOverloads
    fun Metric(value: Long, channel: Int = AnsightChannels.Unspecified) {
        metric(value, channel)
    }

    @JvmStatic
    fun event(label: String) {
        event(label, AnsightEventType.Info, null, AnsightChannels.Unspecified)
    }

    @JvmStatic
    fun event(label: String, type: AnsightEventType) {
        event(label, type, null, AnsightChannels.Unspecified)
    }

    @JvmStatic
    fun event(label: String, type: AnsightEventType, details: String?) {
        event(label, type, details, AnsightChannels.Unspecified)
    }

    @JvmStatic
    fun event(label: String, channel: Int) {
        event(label, AnsightEventType.Info, null, channel)
    }

    @JvmStatic
    fun event(label: String, type: AnsightEventType, channel: Int) {
        event(label, type, null, channel)
    }

    @JvmStatic
    fun event(label: String, type: AnsightEventType, channel: Int, details: String?) {
        event(label, type, details, channel)
    }

    @JvmStatic
    fun event(label: String, type: AnsightEventType, details: String?, channel: Int) {
        if (isInitialized()) {
            AnsightRuntime.event(label, type, details, channel)
        }
    }

    @JvmStatic
    fun Event(label: String) {
        event(label)
    }

    @JvmStatic
    fun Event(label: String, type: AnsightEventType) {
        event(label, type)
    }

    @JvmStatic
    fun Event(label: String, type: AnsightEventType, details: String?) {
        event(label, type, details)
    }

    @JvmStatic
    fun Event(label: String, channel: Int) {
        event(label, channel)
    }

    @JvmStatic
    fun Event(label: String, type: AnsightEventType, channel: Int) {
        event(label, type, channel)
    }

    @JvmStatic
    fun Event(label: String, type: AnsightEventType, channel: Int, details: String?) {
        event(label, type, channel, details)
    }

    @JvmStatic
    fun screenViewed(screenName: String) {
        screenViewed(screenName, emptyMap())
    }

    @JvmStatic
    fun screenViewed(screenName: String, details: Map<String, String>) {
        if (isInitialized()) {
            AnsightRuntime.screenViewed(screenName, details)
        }
    }

    @JvmStatic
    fun ScreenViewed(screenName: String) {
        screenViewed(screenName)
    }

    @JvmStatic
    fun ScreenViewed(screenName: String, details: Map<String, String>) {
        screenViewed(screenName, details)
    }

    @JvmStatic
    @JvmOverloads
    fun setAppLifecycleState(state: AppLifecycleState, changedAtUtc: String = AnsightClock.isoNow()) {
        if (isInitialized()) {
            AnsightRuntime.setAppLifecycleState(state, changedAtUtc)
        }
    }

    @JvmStatic
    @JvmOverloads
    fun SetAppLifecycleState(state: AppLifecycleState, changedAtUtc: String = AnsightClock.isoNow()) {
        setAppLifecycleState(state, changedAtUtc)
    }

    @JvmStatic
    fun snapshot(): AnsightDebugSnapshot = AnsightRuntime.snapshot()

    @JvmStatic
    fun Snapshot(): AnsightDebugSnapshot = snapshot()
}
