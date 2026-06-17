package ai.ansight.tools.reflection

import ai.ansight.runtime.AnsightOptionsBuilder

fun AnsightOptionsBuilder.withReflectionTools(): AnsightOptionsBuilder {
    return addTools(AndroidReflectionTools.create())
}

fun AnsightOptionsBuilder.withReflectionTools(
    options: AndroidReflectionToolsOptions,
): AnsightOptionsBuilder {
    return addTools(AndroidReflectionTools.create(options))
}

fun AnsightOptionsBuilder.withReflectionTools(
    configure: AndroidReflectionToolsOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    val builder = AndroidReflectionToolsOptions.createBuilder()
    builder.configure()
    return withReflectionTools(builder.build())
}
