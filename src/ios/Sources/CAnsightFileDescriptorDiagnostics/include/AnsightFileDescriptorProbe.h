#ifndef ANSIGHT_FILE_DESCRIPTOR_PROBE_H
#define ANSIGHT_FILE_DESCRIPTOR_PROBE_H

#include <stddef.h>
#include <stdint.h>

enum {
    ANSIGHT_FD_KIND_UNKNOWN = 0,
    ANSIGHT_FD_KIND_REGULAR_FILE = 1,
    ANSIGHT_FD_KIND_DIRECTORY = 2,
    ANSIGHT_FD_KIND_SOCKET = 3,
    ANSIGHT_FD_KIND_PIPE = 4,
    ANSIGHT_FD_KIND_CHARACTER_DEVICE = 5,
    ANSIGHT_FD_KIND_BLOCK_DEVICE = 6,
    ANSIGHT_FD_KIND_SYMBOLIC_LINK = 7,
    ANSIGHT_FD_KIND_OTHER = 8,
};

int32_t ansight_fd_is_open(int32_t descriptor);
int32_t ansight_fd_descriptor_flags(int32_t descriptor);
int32_t ansight_fd_status_flags(int32_t descriptor);
int32_t ansight_fd_kind(int32_t descriptor);
int64_t ansight_fd_position(int32_t descriptor, int32_t *error_code);
uint64_t ansight_fd_inode(int32_t descriptor, int32_t *error_code);
int32_t ansight_fd_path(int32_t descriptor, char *buffer, size_t buffer_length);
size_t ansight_fd_path_buffer_size(void);
int32_t ansight_fd_limits(uint64_t *soft_limit, uint64_t *hard_limit, int32_t *hard_limit_unlimited);

#endif
