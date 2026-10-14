package ai.ansight.runtime

import org.json.JSONObject

data class DeviceApplicationIconProfile(
    val format: String,
    val mimeType: String,
    val width: Int,
    val height: Int,
    val byteCount: Int,
    val dataBase64: String,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("format", format)
        .put("mimeType", mimeType)
        .put("width", width)
        .put("height", height)
        .put("byteCount", byteCount)
        .put("dataBase64", dataBase64)
}
