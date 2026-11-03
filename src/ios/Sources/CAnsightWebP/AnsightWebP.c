#include "vendor/ansight_symbol_prefix.h"
#include "include/AnsightWebP.h"
#include "vendor/src/webp/encode.h"

size_t AnsightWebPEncodeRGBA(const uint8_t *rgba, int width, int height, int stride, float quality, uint8_t **output) {
    const int invalidInput = !rgba
        || !output
        || width < 1
        || height < 1
        || width > 8192
        || height > 8192
        || stride < width * 4;
    if (invalidInput) {
        return 0;
    }
    return WebPEncodeRGBA(rgba, width, height, stride, quality, output);
}

void AnsightWebPFree(void *output) {
    WebPFree(output);
}
