package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AnsightNetworkRequestSanitizerTest {
    @Test
    fun `redacts credentials and preserves only the typed v1 contract`() {
        val request = AnsightNetworkRequest.fromJson(
            org.json.JSONObject(
                """
                {
                  "schema":"ansight.network-request.v1",
                  "id":"request-1",
                  "source":"react-native.fetch",
                  "startedAtUtc":"2026-08-23T00:00:00Z",
                  "completedAtUtc":"2026-08-22T23:59:59Z",
                  "durationMilliseconds":10,
                  "method":"get",
                  "url":"https://user:password@example.test/items?token=secret&visible=yes",
                  "requestHeaders":[{"name":"Authorization","value":"Bearer secret"}],
                  "responseHeaders":[{"name":"Set-Cookie","value":"session=secret"}],
                  "requestBody":"must not cross the model boundary"
                }
                """.trimIndent(),
            ),
        )!!

        val sanitized = AnsightNetworkRequestSanitizer.sanitize(request)
        val encoded = sanitized.toJson()

        assertEquals("GET", sanitized.method)
        assertEquals(sanitized.startedAtUtc, sanitized.completedAtUtc)
        assertFalse(sanitized.url.contains("password"))
        assertFalse(sanitized.url.contains("token=secret"))
        assertEquals("<redacted>", sanitized.requestHeaders.single().value)
        assertEquals("<redacted>", sanitized.responseHeaders.single().value)
        assertFalse(encoded.has("requestBody"))
    }

    @Test
    fun `redacts cloud signatures and captured text bodies`() {
        val request = AnsightNetworkRequest.fromJson(
            org.json.JSONObject(
                """
                {
                  "schema":"ansight.network-request.v1",
                  "id":"request-cloud",
                  "source":"flutter.http",
                  "startedAtUtc":"2026-08-23T00:00:00Z",
                  "completedAtUtc":"2026-08-23T00:00:01Z",
                  "durationMilliseconds":10,
                  "method":"post",
                  "url":"https://blob.test/a?sv=1&sp=rw&se=tomorrow&sig=azure-secret&safe=yes",
                  "requestBody":{
                    "contentType":"application/json",
                    "encoding":"utf8",
                    "data":"{\"token\":\"body-secret\",\"visible\":\"yes\"}",
                    "capturedBytes":39,
                    "totalBytes":39,
                    "truncated":false
                  }
                }
                """.trimIndent(),
            ),
        )!!

        val sanitized = AnsightNetworkRequestSanitizer.sanitize(request)
        assertFalse(sanitized.url.contains("azure-secret"))
        assertFalse(sanitized.url.contains("tomorrow"))
        assertTrue(sanitized.url.contains("safe=yes"))
        assertNotNull(sanitized.requestBody)
        assertFalse(sanitized.requestBody!!.data.contains("body-secret"))
        assertTrue(sanitized.requestBody!!.data.contains("visible"))
    }
}
