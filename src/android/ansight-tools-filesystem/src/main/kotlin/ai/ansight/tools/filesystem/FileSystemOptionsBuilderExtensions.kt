package ai.ansight.tools.filesystem

import ai.ansight.runtime.AnsightOptionsBuilder

fun AnsightOptionsBuilder.withFileSystemTools(): AnsightOptionsBuilder {
    return addTools(AndroidFileSystemTools.create())
}

fun AnsightOptionsBuilder.withFileSystemTools(
    options: AndroidFileSystemToolsOptions,
): AnsightOptionsBuilder {
    return addTools(AndroidFileSystemTools.create(options))
}

fun AnsightOptionsBuilder.withFileSystemTools(
    configure: AndroidFileSystemToolsOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    val builder = AndroidFileSystemToolsOptions.createBuilder()
    builder.configure()
    return withFileSystemTools(builder.build())
}
