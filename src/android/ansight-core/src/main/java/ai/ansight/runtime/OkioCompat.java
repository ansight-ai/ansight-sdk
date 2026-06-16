package ai.ansight.runtime;

import okio.ByteString;

final class OkioCompat {
    private OkioCompat() {
    }

    static ByteString byteStringOf(byte[] bytes) {
        return ByteString.of(bytes);
    }
}
