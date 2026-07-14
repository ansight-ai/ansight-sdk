#include "AnsightFileDescriptorProbe.h"

#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <string.h>
#include <sys/param.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <unistd.h>

static int32_t ansight_negative_errno(void) {
    return errno == 0 ? -1 : -errno;
}

int32_t ansight_fd_is_open(int32_t descriptor) {
    errno = 0;
    if (fcntl(descriptor, F_GETFD) >= 0) {
        return 1;
    }

    return errno == EBADF ? 0 : ansight_negative_errno();
}

int32_t ansight_fd_descriptor_flags(int32_t descriptor) {
    errno = 0;
    int result = fcntl(descriptor, F_GETFD);
    return result >= 0 ? result : ansight_negative_errno();
}

int32_t ansight_fd_status_flags(int32_t descriptor) {
    errno = 0;
    int result = fcntl(descriptor, F_GETFL);
    return result >= 0 ? result : ansight_negative_errno();
}

int32_t ansight_fd_kind(int32_t descriptor) {
    struct stat descriptor_stat;
    errno = 0;
    if (fstat(descriptor, &descriptor_stat) != 0) {
        return ansight_negative_errno();
    }

    mode_t mode = descriptor_stat.st_mode;
    if (S_ISREG(mode)) return ANSIGHT_FD_KIND_REGULAR_FILE;
    if (S_ISDIR(mode)) return ANSIGHT_FD_KIND_DIRECTORY;
    if (S_ISSOCK(mode)) return ANSIGHT_FD_KIND_SOCKET;
    if (S_ISFIFO(mode)) return ANSIGHT_FD_KIND_PIPE;
    if (S_ISCHR(mode)) return ANSIGHT_FD_KIND_CHARACTER_DEVICE;
    if (S_ISBLK(mode)) return ANSIGHT_FD_KIND_BLOCK_DEVICE;
    if (S_ISLNK(mode)) return ANSIGHT_FD_KIND_SYMBOLIC_LINK;
    return ANSIGHT_FD_KIND_OTHER;
}

int64_t ansight_fd_position(int32_t descriptor, int32_t *error_code) {
    errno = 0;
    off_t result = lseek(descriptor, 0, SEEK_CUR);
    if (result == (off_t)-1) {
        if (error_code != NULL) {
            *error_code = errno;
        }
        return -1;
    }

    if (error_code != NULL) {
        *error_code = 0;
    }
    return (int64_t)result;
}

uint64_t ansight_fd_inode(int32_t descriptor, int32_t *error_code) {
    struct stat descriptor_stat;
    errno = 0;
    if (fstat(descriptor, &descriptor_stat) != 0) {
        if (error_code != NULL) {
            *error_code = errno;
        }
        return 0;
    }

    if (error_code != NULL) {
        *error_code = 0;
    }
    return (uint64_t)descriptor_stat.st_ino;
}

int32_t ansight_fd_path(int32_t descriptor, char *buffer, size_t buffer_length) {
    if (buffer == NULL || buffer_length < MAXPATHLEN) {
        return -EINVAL;
    }

    buffer[0] = '\0';
    errno = 0;
    if (fcntl(descriptor, F_GETPATH, buffer) != 0) {
        return ansight_negative_errno();
    }

    buffer[buffer_length - 1] = '\0';
    return 0;
}

size_t ansight_fd_path_buffer_size(void) {
    return MAXPATHLEN;
}

int32_t ansight_fd_limits(uint64_t *soft_limit, uint64_t *hard_limit, int32_t *hard_limit_unlimited) {
    if (soft_limit == NULL || hard_limit == NULL || hard_limit_unlimited == NULL) {
        return -EINVAL;
    }

    struct rlimit limits;
    errno = 0;
    if (getrlimit(RLIMIT_NOFILE, &limits) != 0) {
        return ansight_negative_errno();
    }

    *soft_limit = (uint64_t)limits.rlim_cur;
    *hard_limit_unlimited = limits.rlim_max == RLIM_INFINITY ? 1 : 0;
    *hard_limit = *hard_limit_unlimited ? 0 : (uint64_t)limits.rlim_max;
    return 0;
}
