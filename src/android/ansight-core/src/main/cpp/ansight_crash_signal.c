#include <jni.h>

#include <fcntl.h>
#include <signal.h>
#include <stdint.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#define ANSIGHT_SIGNAL_RECORD_VERSION 1
#define ANSIGHT_SIGNAL_STACK_SIZE (64 * 1024)

typedef struct AnsightSignalRecord {
    uint32_t version;
    int32_t signal_number;
    int32_t signal_code;
    uint64_t fault_address;
    int64_t occurred_at_epoch_seconds;
    int32_t process_id;
} AnsightSignalRecord;

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

        AnsightSignalRecord record = {0};
        record.version = ANSIGHT_SIGNAL_RECORD_VERSION;
        record.signal_number = signal_number;
        record.signal_code = signal_info == NULL ? 0 : signal_info->si_code;
        record.fault_address = signal_info == NULL
            ? 0
            : (uint64_t)(uintptr_t)signal_info->si_addr;
        record.occurred_at_epoch_seconds = timestamp.tv_sec;
        record.process_id = getpid();

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

static int ansight_install(const char *path) {
    int new_fd = open(path, O_CREAT | O_WRONLY | O_TRUNC, S_IRUSR | S_IWUSR);
    if (new_fd < 0) {
        return 0;
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
        return 0;
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
            return 0;
        }
    }
    return 1;
}

JNIEXPORT jboolean JNICALL
Java_ai_ansight_runtime_AndroidCrashSignalBridge_nativeInstall(
    JNIEnv *environment,
    jobject receiver,
    jstring path
) {
    (void)receiver;
    if (path == NULL) {
        return JNI_FALSE;
    }
    const char *characters = (*environment)->GetStringUTFChars(environment, path, NULL);
    if (characters == NULL) {
        return JNI_FALSE;
    }
    int installed = ansight_install(characters);
    (*environment)->ReleaseStringUTFChars(environment, path, characters);
    return installed ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jlongArray JNICALL
Java_ai_ansight_runtime_AndroidCrashSignalBridge_nativeConsume(
    JNIEnv *environment,
    jobject receiver,
    jstring path
) {
    (void)receiver;
    if (path == NULL) {
        return NULL;
    }
    const char *characters = (*environment)->GetStringUTFChars(environment, path, NULL);
    if (characters == NULL) {
        return NULL;
    }
    int fd = open(characters, O_RDONLY);
    AnsightSignalRecord record;
    ssize_t length = fd < 0 ? -1 : read(fd, &record, sizeof(record));
    if (fd >= 0) {
        close(fd);
    }
    unlink(characters);
    (*environment)->ReleaseStringUTFChars(environment, path, characters);
    if (length != sizeof(record) || record.version != ANSIGHT_SIGNAL_RECORD_VERSION) {
        return NULL;
    }

    jlong values[6] = {
        record.version,
        record.signal_number,
        record.signal_code,
        (jlong)record.fault_address,
        record.occurred_at_epoch_seconds,
        record.process_id,
    };
    jlongArray result = (*environment)->NewLongArray(environment, 6);
    if (result != NULL) {
        (*environment)->SetLongArrayRegion(environment, result, 0, 6, values);
    }
    return result;
}
