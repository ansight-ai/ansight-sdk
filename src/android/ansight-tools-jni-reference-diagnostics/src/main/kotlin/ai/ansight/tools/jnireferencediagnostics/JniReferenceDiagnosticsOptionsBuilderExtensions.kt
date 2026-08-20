package ai.ansight.tools.jnireferencediagnostics

import ai.ansight.runtime.AnsightOptionsBuilder

fun AnsightOptionsBuilder.withJniReferenceDiagnosticsTools(): AnsightOptionsBuilder =
    addTools(AndroidJniReferenceDiagnosticsTools.create())

fun AnsightOptionsBuilder.withJniReferenceDiagnosticsTools(
    options: AndroidJniReferenceDiagnosticsOptions,
): AnsightOptionsBuilder = addTools(AndroidJniReferenceDiagnosticsTools.create(options))

fun AnsightOptionsBuilder.withJniReferenceDiagnosticsTools(
    configure: AndroidJniReferenceDiagnosticsOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    val builder = AndroidJniReferenceDiagnosticsOptions.createBuilder()
    builder.configure()
    return withJniReferenceDiagnosticsTools(builder.build())
}
