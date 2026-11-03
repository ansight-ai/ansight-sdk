#ifndef ANSIGHT_WEBP_H
#define ANSIGHT_WEBP_H
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
size_t AnsightWebPEncodeRGBA(const uint8_t *rgba, int width, int height, int stride, float quality, uint8_t **output);
void AnsightWebPFree(void *output);
#ifdef __cplusplus
}
#endif
#endif
