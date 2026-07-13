package ai.ansight.runtime;

import okio.ByteString;

final class OkioCompat {
    private OkioCompat() {
    }

    static ByteString byteStringOf(byte[] bytes) {
        return ByteString.of(bytes);
    }

    static byte[] decodeBase64(String value) {
        ByteString decoded = ByteString.decodeBase64(value);
        if (decoded == null) {
            throw new IllegalArgumentException("Invalid Base64 value.");
        }
        return decoded.toByteArray();
    }

    static String encodeBase64(byte[] bytes) {
        return ByteString.of(bytes).base64();
    }

    static String encodeBase64UrlWithoutPadding(byte[] bytes) {
        return ByteString.of(bytes).base64Url().replace("=", "");
    }
}
