package ai.ansight.tools.preferences

import ai.ansight.runtime.AnsightOptionsBuilder

fun AnsightOptionsBuilder.withPreferencesTools(): AnsightOptionsBuilder {
    return addTools(AndroidPreferencesTools.create())
}

fun AnsightOptionsBuilder.withPreferencesTools(
    options: AndroidPreferencesToolsOptions,
): AnsightOptionsBuilder {
    return addTools(AndroidPreferencesTools.create(options))
}

fun AnsightOptionsBuilder.withPreferencesTools(
    configure: AndroidPreferencesToolsOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    val builder = AndroidPreferencesToolsOptions.createBuilder()
    builder.configure()
    return withPreferencesTools(builder.build())
}
