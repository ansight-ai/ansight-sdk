package ai.ansight.runtime

import android.app.Activity

object AnsightUnattendedProvisioning {
    const val PayloadIntentExtra = "ai.ansight.bootstrap.payload"

    internal fun consumePayload(activity: Activity, enabled: Boolean): String? {
        val intent = activity.intent ?: return null
        val payload = normalizePayload(enabled, intent.getStringExtra(PayloadIntentExtra)) ?: return null
        intent.removeExtra(PayloadIntentExtra)
        return payload
    }

    internal fun normalizePayload(enabled: Boolean, payload: String?): String? {
        if (!enabled) {
            return null
        }

        return payload?.trim()?.ifBlank { null }
    }
}
