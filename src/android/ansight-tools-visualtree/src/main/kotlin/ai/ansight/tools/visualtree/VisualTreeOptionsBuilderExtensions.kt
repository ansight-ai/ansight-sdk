package ai.ansight.tools.visualtree

import ai.ansight.runtime.AnsightOptionsBuilder

fun AnsightOptionsBuilder.withVisualTreeTools(): AnsightOptionsBuilder {
    return addTools(AndroidVisualTreeTools.create())
}
