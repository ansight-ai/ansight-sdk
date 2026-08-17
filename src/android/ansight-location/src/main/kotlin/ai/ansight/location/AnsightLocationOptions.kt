package ai.ansight.location

data class AnsightLocationOptions(
    val enabled: Boolean = false,
    val decimalPlaces: Int = 5,
    val minimumIntervalMilliseconds: Long = 1_000,
    val minimumDistanceMeters: Double = 1.0,
) {
    fun normalized(): AnsightLocationOptions = copy(
        decimalPlaces = decimalPlaces.coerceIn(0, 7),
        minimumIntervalMilliseconds = minimumIntervalMilliseconds.coerceAtLeast(0),
        minimumDistanceMeters = minimumDistanceMeters
            .takeIf { it.isFinite() && it >= 0 } ?: 0.0,
    )

    companion object {
        @JvmStatic
        @JvmOverloads
        fun enabled(
            decimalPlaces: Int = 5,
            minimumIntervalMilliseconds: Long = 1_000,
            minimumDistanceMeters: Double = 1.0,
        ): AnsightLocationOptions = AnsightLocationOptions(
            enabled = true,
            decimalPlaces = decimalPlaces,
            minimumIntervalMilliseconds = minimumIntervalMilliseconds,
            minimumDistanceMeters = minimumDistanceMeters,
        ).normalized()
    }
}
