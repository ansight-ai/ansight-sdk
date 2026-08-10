package ai.ansight.runtime

import java.time.Instant
import java.time.format.DateTimeFormatter

object AnsightClock {
    fun isoNow(): String = DateTimeFormatter.ISO_INSTANT.format(Instant.now())

    fun isoAt(epochMilliseconds: Long): String =
        DateTimeFormatter.ISO_INSTANT.format(Instant.ofEpochMilli(epochMilliseconds))
}
