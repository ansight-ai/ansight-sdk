package ai.ansight.tools.database

import ai.ansight.runtime.AnsightOptionsBuilder

fun AnsightOptionsBuilder.withDatabaseTools(): AnsightOptionsBuilder {
    return addTools(AndroidDatabaseTools.create())
}

fun AnsightOptionsBuilder.withDatabaseTools(
    options: AndroidDatabaseToolsOptions,
): AnsightOptionsBuilder {
    return addTools(AndroidDatabaseTools.create(options))
}

fun AnsightOptionsBuilder.withDatabaseTools(
    configure: AndroidDatabaseToolsOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    val builder = AndroidDatabaseToolsOptions.createBuilder()
    builder.configure()
    return withDatabaseTools(builder.build())
}
