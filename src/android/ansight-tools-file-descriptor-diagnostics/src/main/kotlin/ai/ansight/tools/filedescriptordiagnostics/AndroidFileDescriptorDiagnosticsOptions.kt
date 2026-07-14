package ai.ansight.tools.filedescriptordiagnostics

data class AndroidFileDescriptorDiagnosticsOptions(
    val includeTargets: Boolean = true,
    val maximumReturnedDescriptors: Int = 2_048,
) {
    val validatedMaximumReturnedDescriptors: Int
        get() = maximumReturnedDescriptors.coerceIn(1, 8_192)

    companion object {
        @JvmField
        val Default = AndroidFileDescriptorDiagnosticsOptions()

        @JvmStatic
        fun createBuilder(): AndroidFileDescriptorDiagnosticsOptionsBuilder =
            AndroidFileDescriptorDiagnosticsOptionsBuilder()
    }
}

class AndroidFileDescriptorDiagnosticsOptionsBuilder {
    private var includeTargetsValue = true
    private var maximumReturnedDescriptorsValue = 2_048

    fun includeTargets(includeTargets: Boolean): AndroidFileDescriptorDiagnosticsOptionsBuilder {
        includeTargetsValue = includeTargets
        return this
    }

    fun maximumReturnedDescriptors(maximum: Int): AndroidFileDescriptorDiagnosticsOptionsBuilder {
        maximumReturnedDescriptorsValue = maximum
        return this
    }

    fun build(): AndroidFileDescriptorDiagnosticsOptions = AndroidFileDescriptorDiagnosticsOptions(
        includeTargets = includeTargetsValue,
        maximumReturnedDescriptors = maximumReturnedDescriptorsValue,
    )
}
