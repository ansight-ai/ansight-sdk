package ai.ansight.tools.jnireferencediagnostics

data class AndroidJniReferenceDiagnosticsOptions(
    val maximumGraphNodes: Int = 2_048,
    val maximumGraphEdges: Int = 4_096,
    val maximumGraphDepth: Int = 8,
) {
    val validatedMaximumGraphNodes: Int
        get() = maximumGraphNodes.coerceIn(1, 8_192)

    val validatedMaximumGraphEdges: Int
        get() = maximumGraphEdges.coerceIn(1, 16_384)

    val validatedMaximumGraphDepth: Int
        get() = maximumGraphDepth.coerceIn(0, 16)

    companion object {
        @JvmField
        val Default = AndroidJniReferenceDiagnosticsOptions()

        @JvmStatic
        fun createBuilder(): AndroidJniReferenceDiagnosticsOptionsBuilder =
            AndroidJniReferenceDiagnosticsOptionsBuilder()
    }
}

class AndroidJniReferenceDiagnosticsOptionsBuilder {
    private var maximumGraphNodesValue = 2_048
    private var maximumGraphEdgesValue = 4_096
    private var maximumGraphDepthValue = 8

    fun maximumGraphNodes(maximum: Int): AndroidJniReferenceDiagnosticsOptionsBuilder {
        maximumGraphNodesValue = maximum
        return this
    }

    fun maximumGraphEdges(maximum: Int): AndroidJniReferenceDiagnosticsOptionsBuilder {
        maximumGraphEdgesValue = maximum
        return this
    }

    fun maximumGraphDepth(maximum: Int): AndroidJniReferenceDiagnosticsOptionsBuilder {
        maximumGraphDepthValue = maximum
        return this
    }

    fun build(): AndroidJniReferenceDiagnosticsOptions = AndroidJniReferenceDiagnosticsOptions(
        maximumGraphNodes = maximumGraphNodesValue,
        maximumGraphEdges = maximumGraphEdgesValue,
        maximumGraphDepth = maximumGraphDepthValue,
    )
}
