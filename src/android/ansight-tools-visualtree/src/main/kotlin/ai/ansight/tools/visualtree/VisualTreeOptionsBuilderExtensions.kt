package ai.ansight.tools.visualtree

import ai.ansight.runtime.AnsightOptionsBuilder
import ai.ansight.runtime.SessionVisualTreeCaptureRegistry

fun AnsightOptionsBuilder.withVisualTreeTools(): AnsightOptionsBuilder {
    SessionVisualTreeCaptureRegistry.setProvider { context ->
        AndroidVisualTreeProviderRegistry.registeredProviders().mapNotNull { provider ->
            provider.getVisualTree(
                mapOf(
                    "includeBounds" to "true",
                    "includeComputedStyles" to "true",
                    "maxDepth" to "40",
                    "maxNodes" to "2000",
                ),
                context,
            ).takeIf { it.success }?.payload
        }
    }
    return addTools(AndroidVisualTreeTools.create())
}
