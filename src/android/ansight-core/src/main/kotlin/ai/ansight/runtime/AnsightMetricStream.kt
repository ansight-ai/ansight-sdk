package ai.ansight.runtime

class AnsightMetricStream(
    val channel: AnsightChannel,
    private val sampler: AnsightMetricSampler,
) {
    fun sample(): Long? = sampler.sample()
}
