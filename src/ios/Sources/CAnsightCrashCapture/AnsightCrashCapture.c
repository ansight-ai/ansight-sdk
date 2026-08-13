#include "AnsightCrashCapture.h"

#include <fcntl.h>
#include <signal.h>
#include <stddef.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#define ANSIGHT_SIGNAL_RECORD_VERSION 1
#define ANSIGHT_SIGNAL_STACK_SIZE (64 * 1024)

static const int ansight_signals[] = {
    SIGABRT,
    SIGBUS,
    SIGFPE,
    SIGILL,
    SIGSEGV,
    SIGTRAP,
};

static struct sigaction ansight_previous_handlers[
    sizeof(ansight_signals) / sizeof(ansight_signals[0])
];
static int ansight_signal_fd = -1;
static volatile sig_atomic_t ansight_handling_signal = 0;
static _Alignas(16) unsigned char ansight_signal_stack[ANSIGHT_SIGNAL_STACK_SIZE];

static size_t ansight_signal_index(int signal_number) {
    size_t count = sizeof(ansight_signals) / sizeof(ansight_signals[0]);
    for (size_t index = 0; index < count; index++) {
        if (ansight_signals[index] == signal_number) {
            return index;
        }
    }
    return count;
}

static void ansight_handle_signal(
    int signal_number,
    siginfo_t *signal_info,
    void *context
) {
    (void)context;
    if (!ansight_handling_signal) {
        ansight_handling_signal = 1;
        struct timespec timestamp = {0};
        (void)clock_gettime(CLOCK_REALTIME, &timestamp);
        AnsightCrashSignalRecord record = {0};
        record.version = ANSIGHT_SIGNAL_RECORD_VERSION;
        record.signalNumber = signal_number;
        record.signalCode = signal_info == NULL ? 0 : signal_info->si_code;
        record.faultAddress = signal_info == NULL
            ? 0
            : (uint64_t)(uintptr_t)signal_info->si_addr;
        record.occurredAtEpochSeconds = timestamp.tv_sec;
        record.processId = (int32_t)getpid();

        if (ansight_signal_fd >= 0) {
            (void)lseek(ansight_signal_fd, 0, SEEK_SET);
            (void)write(ansight_signal_fd, &record, sizeof(record));
            (void)fsync(ansight_signal_fd);
        }
    }

    size_t index = ansight_signal_index(signal_number);
    size_t count = sizeof(ansight_signals) / sizeof(ansight_signals[0]);
    if (index < count) {
        (void)sigaction(signal_number, &ansight_previous_handlers[index], NULL);
    } else {
        _exit(128 + signal_number);
    }
    sigset_t unblock;
    sigemptyset(&unblock);
    sigaddset(&unblock, signal_number);
    (void)sigprocmask(SIG_UNBLOCK, &unblock, NULL);
    (void)kill(getpid(), signal_number);
    _exit(128 + signal_number);
}

int ansight_crash_install_signal_handlers(const char *path) {
    if (path == NULL || path[0] == '\0') {
        return -1;
    }

    int new_fd = open(path, O_CREAT | O_WRONLY | O_TRUNC, S_IRUSR | S_IWUSR);
    if (new_fd < 0) {
        return -1;
    }
    if (ansight_signal_fd >= 0) {
        close(ansight_signal_fd);
    }
    ansight_signal_fd = new_fd;
    ansight_handling_signal = 0;

    stack_t alternate_stack = {0};
    alternate_stack.ss_sp = ansight_signal_stack;
    alternate_stack.ss_size = sizeof(ansight_signal_stack);
    if (sigaltstack(&alternate_stack, NULL) != 0) {
        close(ansight_signal_fd);
        ansight_signal_fd = -1;
        return -1;
    }

    struct sigaction action;
    memset(&action, 0, sizeof(action));
    sigemptyset(&action.sa_mask);
    action.sa_sigaction = ansight_handle_signal;
    action.sa_flags = SA_SIGINFO | SA_RESTART | SA_ONSTACK;

    size_t count = sizeof(ansight_signals) / sizeof(ansight_signals[0]);
    for (size_t index = 0; index < count; index++) {
        if (sigaction(
                ansight_signals[index],
                &action,
                &ansight_previous_handlers[index]
            ) != 0) {
            return -1;
        }
    }
    return 0;
}

int ansight_crash_consume_signal_record(
    const char *path,
    AnsightCrashSignalRecord *record
) {
    if (path == NULL || record == NULL) {
        return 0;
    }
    int fd = open(path, O_RDONLY);
    if (fd < 0) {
        return 0;
    }
    AnsightCrashSignalRecord candidate;
    ssize_t length = read(fd, &candidate, sizeof(candidate));
    close(fd);
    unlink(path);
    if (length != sizeof(candidate) || candidate.version != ANSIGHT_SIGNAL_RECORD_VERSION) {
        return 0;
    }
    *record = candidate;
    return 1;
}
