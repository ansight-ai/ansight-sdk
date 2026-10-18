package ai.ansight.runtime

fun interface AnsightMetricSampler {
    fun sample(): Number?
}
