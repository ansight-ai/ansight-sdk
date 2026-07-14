package ai.ansight.tools.filedescriptordiagnostics

import ai.ansight.runtime.AnsightOptionsBuilder

fun AnsightOptionsBuilder.withFileDescriptorDiagnosticsTools(): AnsightOptionsBuilder =
    addTools(AndroidFileDescriptorDiagnosticsTools.create())

fun AnsightOptionsBuilder.withFileDescriptorDiagnosticsTools(
    options: AndroidFileDescriptorDiagnosticsOptions,
): AnsightOptionsBuilder = addTools(AndroidFileDescriptorDiagnosticsTools.create(options))

fun AnsightOptionsBuilder.withFileDescriptorDiagnosticsTools(
    configure: AndroidFileDescriptorDiagnosticsOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    val builder = AndroidFileDescriptorDiagnosticsOptions.createBuilder()
    builder.configure()
    return withFileDescriptorDiagnosticsTools(builder.build())
}
