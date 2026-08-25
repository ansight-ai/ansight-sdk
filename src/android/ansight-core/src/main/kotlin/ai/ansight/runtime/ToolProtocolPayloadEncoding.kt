package ai.ansight.runtime

import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.nio.charset.StandardCharsets
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream

internal object ToolProtocolPayloadEncoding {
    private const val EncodingPropertyName = "\$ansightEncoding"
    private const val GzipBase64JsonEncoding = "gzip-base64-json"
    private const val CompressionThresholdBytes = 32 * 1024
    private const val Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"

    fun encodeEnvelopeIfBeneficial(envelope: JSONObject): JSONObject {
        val payload = envelope.optJSONObject("payload") ?: return envelope
        val encodedPayload = encodeIfBeneficial(payload)
        if (encodedPayload === payload) {
            return envelope
        }

        return JSONObject(envelope.toString()).put("payload", encodedPayload)
    }

    fun encodeIfBeneficial(payload: JSONObject): JSONObject {
        val sourceBytes = payload.toString().toByteArray(StandardCharsets.UTF_8)
        if (sourceBytes.size < CompressionThresholdBytes) {
            return payload
        }

        val compressedBytes = ByteArrayOutputStream().use { output ->
            GZIPOutputStream(output).use { gzip ->
                gzip.write(sourceBytes)
            }
            output.toByteArray()
        }
        val encodedPayload = JSONObject()
            .put(EncodingPropertyName, GzipBase64JsonEncoding)
            .put("contentType", "application/json")
            .put("originalByteCount", sourceBytes.size)
            .put("compressedByteCount", compressedBytes.size)
            .put("data", encodeBase64(compressedBytes))
        return if (encodedPayload.toString().toByteArray(StandardCharsets.UTF_8).size < sourceBytes.size) {
            encodedPayload
        } else {
            payload
        }
    }

    fun decodeIfNeeded(payload: JSONObject): JSONObject? {
        val encoding = payload.optString(EncodingPropertyName).takeIf { it.isNotBlank() }
            ?: return payload
        if (encoding != GzipBase64JsonEncoding) {
            return null
        }

        return runCatching {
            val encodedData = payload.getString("data")
            val compressedBytes = decodeBase64(encodedData)
                ?: error("Encoded tool payload contains invalid base64 data.")
            val sourceBytes = GZIPInputStream(compressedBytes.inputStream()).use { gzip ->
                gzip.readBytes()
            }
            JSONObject(String(sourceBytes, StandardCharsets.UTF_8))
        }.getOrNull()
    }

    private fun encodeBase64(bytes: ByteArray): String {
        val encoded = StringBuilder(((bytes.size + 2) / 3) * 4)
        var index = 0
        while (index < bytes.size) {
            val first = bytes[index].toInt() and 0xff
            val hasSecond = index + 1 < bytes.size
            val hasThird = index + 2 < bytes.size
            val second = if (hasSecond) bytes[index + 1].toInt() and 0xff else 0
            val third = if (hasThird) bytes[index + 2].toInt() and 0xff else 0

            encoded.append(Base64Alphabet[first shr 2])
            encoded.append(Base64Alphabet[((first and 0x03) shl 4) or (second shr 4)])
            encoded.append(if (hasSecond) Base64Alphabet[((second and 0x0f) shl 2) or (third shr 6)] else '=')
            encoded.append(if (hasThird) Base64Alphabet[third and 0x3f] else '=')
            index += 3
        }
        return encoded.toString()
    }

    private fun decodeBase64(value: String): ByteArray? {
        if (value.length % 4 != 0) {
            return null
        }

        return ByteArrayOutputStream((value.length / 4) * 3).use { output ->
            var index = 0
            while (index < value.length) {
                val first = Base64Alphabet.indexOf(value[index])
                val second = Base64Alphabet.indexOf(value[index + 1])
                val thirdCharacter = value[index + 2]
                val fourthCharacter = value[index + 3]
                val third = if (thirdCharacter == '=') -1 else Base64Alphabet.indexOf(thirdCharacter)
                val fourth = if (fourthCharacter == '=') -1 else Base64Alphabet.indexOf(fourthCharacter)
                val isFinalGroup = index + 4 == value.length
                if (first < 0 || second < 0 ||
                    (thirdCharacter != '=' && third < 0) ||
                    (fourthCharacter != '=' && fourth < 0) ||
                    (thirdCharacter == '=' && fourthCharacter != '=') ||
                    (!isFinalGroup && (thirdCharacter == '=' || fourthCharacter == '='))) {
                    return null
                }

                output.write((first shl 2) or (second shr 4))
                if (third >= 0) {
                    output.write(((second and 0x0f) shl 4) or (third shr 2))
                }
                if (fourth >= 0) {
                    output.write(((third and 0x03) shl 6) or fourth)
                }
                index += 4
            }
            output.toByteArray()
        }
    }
}
