#include "AnsightCrashCapture.h"

#include <fcntl.h>
#include <signal.h>
#include <errno.h>
#include <pthread.h>
#include <stdatomic.h>
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
static atomic_flag ansight_handling_signal = ATOMIC_FLAG_INIT;
static int ansight_installed = 0;
static volatile sig_atomic_t ansight_enabled = 0;
static int ansight_registered[sizeof(ansight_signals) / sizeof(ansight_signals[0])];
static int ansight_owns_stack = 0;
static pthread_t ansight_stack_thread;
static stack_t ansight_previous_stack;
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
    int saved_errno = errno;
    size_t index = ansight_signal_index(signal_number);
    size_t count = sizeof(ansight_signals) / sizeof(ansight_signals[0]);
    if (index >= count) {
        _exit(128 + signal_number);
    }

    struct sigaction previous = ansight_previous_handlers[index];
    if (previous.sa_handler == SIG_IGN) {
        errno = saved_errno;
        return;
    }

    int owns_record = ansight_enabled && !atomic_flag_test_and_set_explicit(
        &ansight_handling_signal, memory_order_relaxed);
    if (owns_record) {
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

    if (previous.sa_handler != SIG_DFL) {
        // Runtime handlers may translate a fault into a managed exception and
        // return with a modified context. Preserve the original fault and thread.
        if (previous.sa_flags & SA_RESETHAND) {
            struct sigaction reset = {0};
            reset.sa_handler = SIG_DFL;
            sigemptyset(&reset.sa_mask);
            (void)sigaction(signal_number, &reset, NULL);
        }
        errno = saved_errno;
        if (previous.sa_flags & SA_SIGINFO) {
            previous.sa_sigaction(signal_number, signal_info, context);
        } else {
            previous.sa_handler(signal_number);
        }

        // A returning handler recovered. Do not turn that signal into a crash
        // report on the next launch, or force the application to exit.
        if (owns_record) {
            if (ansight_signal_fd >= 0) {
                (void)ftruncate(ansight_signal_fd, 0);
                (void)fsync(ansight_signal_fd);
            }
            atomic_flag_clear_explicit(&ansight_handling_signal, memory_order_relaxed);
        }
        errno = saved_errno;
        return;
    }

    (void)sigaction(signal_number, &previous, NULL);
    int hardware_signal = signal_number == SIGSEGV
        || signal_number == SIGBUS
        || signal_number == SIGFPE
        || signal_number == SIGILL;
    int hardware_fault = signal_info != NULL
        && signal_info->si_code > 0
        && hardware_signal;
    if (!hardware_fault && raise(signal_number) != 0) {
        _exit(128 + signal_number);
    }
    // Hardware faults repeat at the original instruction. Raised signals are
    // pending on this thread and are delivered when its signal mask is restored.
    errno = saved_errno;
}

void ansight_crash_uninstall_signal_handlers(void) {
    if (!ansight_installed) {
        return;
    }
    ansight_enabled = 0;
    size_t count = sizeof(ansight_signals) / sizeof(ansight_signals[0]);
    int chained_handlers = 0;
    for (size_t index = 0; index < count; index++) {
        struct sigaction current;
        int owned = sigaction(ansight_signals[index], NULL, &current) == 0
            && (current.sa_flags & SA_SIGINFO)
            && current.sa_sigaction == ansight_handle_signal;
        if (owned) {
            (void)sigaction(ansight_signals[index], &ansight_previous_handlers[index], NULL);
            ansight_registered[index] = 0;
        } else if (ansight_registered[index]) {
            // A newer handler may still chain to us. Retain our predecessor and
            // do not wrap that newer handler again when capture is re-enabled.
            chained_handlers = 1;
        }
    }
    int restore_stack = !chained_handlers
        && ansight_owns_stack
        && pthread_equal(pthread_self(), ansight_stack_thread);
    if (restore_stack) {
        stack_t current;
        if (sigaltstack(NULL, &current) == 0 && current.ss_sp == ansight_signal_stack) {
            (void)sigaltstack(&ansight_previous_stack, NULL);
            ansight_owns_stack = 0;
        }
    }
    ansight_installed = 0;
    // Keep the descriptor alive for a handler already running on another thread.
    // It is reused on re-enable and closed by the OS at process exit.
}

int ansight_crash_install_signal_handlers(const char *path) {
    if (path == NULL || path[0] == '\0') {
        return -1;
    }
    if (ansight_installed) {
        return 0;
    }
    if (ansight_signal_fd < 0) {
        ansight_signal_fd = open(path, O_CREAT | O_WRONLY, S_IRUSR | S_IWUSR);
        if (ansight_signal_fd < 0) {
            return -1;
        }
    }
    atomic_flag_clear_explicit(&ansight_handling_signal, memory_order_relaxed);

    // Alternate stacks belong to threads. Never replace a runtime's existing
    // stack; SA_ONSTACK also uses stacks installed by the runtime on other threads.
    stack_t current_stack;
    int stack_available = sigaltstack(NULL, &current_stack) == 0
        && (current_stack.ss_flags & SS_DISABLE)
        && !ansight_owns_stack;
    if (stack_available) {
        stack_t alternate_stack = {0};
        alternate_stack.ss_sp = ansight_signal_stack;
        alternate_stack.ss_size = sizeof(ansight_signal_stack);
        if (sigaltstack(&alternate_stack, &ansight_previous_stack) == 0) {
            ansight_owns_stack = 1;
            ansight_stack_thread = pthread_self();
        }
    }

    size_t count = sizeof(ansight_signals) / sizeof(ansight_signals[0]);
    size_t installed_count = 0;
    int newly_installed[sizeof(ansight_signals) / sizeof(ansight_signals[0])] = {0};
    for (size_t index = 0; index < count; index++) {
        if (ansight_registered[index]) {
            installed_count++;
            continue;
        }
        struct sigaction previous;
        if (sigaction(ansight_signals[index], NULL, &previous) != 0) {
            break;
        }
        struct sigaction action = {0};
        action.sa_mask = previous.sa_mask;
        action.sa_sigaction = ansight_handle_signal;
        action.sa_flags = SA_SIGINFO | SA_ONSTACK |
            (previous.sa_flags & (SA_RESTART | SA_NODEFER));
        if (sigaction(ansight_signals[index], &action, &ansight_previous_handlers[index]) != 0) {
            break;
        }
        ansight_registered[index] = 1;
        newly_installed[index] = 1;
        installed_count++;
    }
    if (installed_count != count) {
        for (size_t index = 0; index < count; index++) {
            if (newly_installed[index]) {
                (void)sigaction(ansight_signals[index], &ansight_previous_handlers[index], NULL);
                ansight_registered[index] = 0;
            }
        }
        return -1;
    }
    ansight_installed = 1;
    ansight_enabled = 1;
    return 0;
}

int ansight_crash_read_signal_record(
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
    if (length != sizeof(candidate) || candidate.version != ANSIGHT_SIGNAL_RECORD_VERSION) {
        return 0;
    }
    *record = candidate;
    return 1;
}

int ansight_crash_consume_signal_record(const char *path, AnsightCrashSignalRecord *record) {
    int found = ansight_crash_read_signal_record(path, record);
    if (found) {
        unlink(path);
    }
    return found;
}
