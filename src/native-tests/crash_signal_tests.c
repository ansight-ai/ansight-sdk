#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include <sys/resource.h>
#include <sys/wait.h>

#ifdef ANSIGHT_TEST_ANDROID
#include "../android/ansight-core/src/main/cpp/ansight_crash_signal.c"
#define SignalRecord AnsightSignalRecord
#define faultAddress fault_address
#else
#include "../ios/Sources/CAnsightCrashCapture/AnsightCrashCapture.c"
#define SignalRecord AnsightCrashSignalRecord
#endif

static volatile sig_atomic_t returned;
static int evidence_pipe[2];
static struct sigaction chained_action;
static void chained_handler(int number, siginfo_t *info, void *context) {
    chained_action.sa_sigaction(number, info, context);
}
static void returning_handler(int number) { (void)number; returned++; }
static void fault_handler(int number, siginfo_t *info, void *context) {
    (void)number;
    assert(context != NULL);
    uintptr_t address = (uintptr_t)info->si_addr;
    (void)write(evidence_pipe[1], &address, sizeof(address));
    _exit(51);
}

static void run_case(int kind) {
    char path[] = "/tmp/ansight-signal-test-XXXXXX";
    int descriptor = mkstemp(path);
    assert(descriptor >= 0);
    close(descriptor);
    assert(pipe(evidence_pipe) == 0);
    pid_t child = fork();
    assert(child >= 0);
    if (child == 0) {
        struct rlimit limit = {0, 0};
        setrlimit(RLIMIT_CORE, &limit);
        if (kind == 0 || kind == 4 || kind == 7) signal(SIGTRAP, returning_handler);
        if (kind == 1) signal(SIGTRAP, SIG_IGN);
        if (kind == 2) {
            struct sigaction action = {0};
            action.sa_sigaction = fault_handler;
            action.sa_flags = SA_SIGINFO;
            sigaction(SIGSEGV, &action, NULL);
        }
        unsigned char previous_stack[64 * 1024];
        stack_t stack = {0};
        if (kind == 3) {
            stack.ss_sp = previous_stack;
            stack.ss_size = sizeof(previous_stack);
            assert(sigaltstack(&stack, NULL) == 0);
        }
        assert(ansight_crash_install_signal_handlers(path) == 0);
        assert(ansight_crash_install_signal_handlers(path) == 0);
        if (kind == 0) {
            raise(SIGTRAP);
            raise(SIGTRAP);
            assert(returned == 2);
        } else if (kind == 1) {
            raise(SIGTRAP);
        } else if (kind == 2) {
            *(volatile int *)(uintptr_t)0x123 = 1;
            _exit(99);
        } else if (kind == 3) {
            stack_t current;
            assert(sigaltstack(NULL, &current) == 0);
            assert(current.ss_sp == previous_stack);
            ansight_crash_uninstall_signal_handlers();
            assert(sigaltstack(NULL, &current) == 0);
            assert(current.ss_sp == previous_stack);
        } else if (kind == 4) {
            ansight_crash_uninstall_signal_handlers();
            raise(SIGTRAP);
            assert(returned == 1);
            assert(ansight_crash_install_signal_handlers(path) == 0);
            raise(SIGTRAP);
            assert(returned == 2);
        } else if (kind == 5) {
            signal(SIGTRAP, returning_handler);
            ansight_crash_uninstall_signal_handlers();
            raise(SIGTRAP);
            assert(returned == 1);
        } else if (kind == 6) {
            raise(SIGABRT);
            _exit(99);
        } else if (kind == 7) {
            struct sigaction outer = {0};
            outer.sa_sigaction = chained_handler;
            outer.sa_flags = SA_SIGINFO;
            assert(sigaction(SIGTRAP, &outer, &chained_action) == 0);
            ansight_crash_uninstall_signal_handlers();
            raise(SIGTRAP);
            assert(returned == 1);
            assert(ansight_crash_install_signal_handlers(path) == 0);
            raise(SIGTRAP);
            assert(returned == 2);
        }
        _exit(0);
    }
    close(evidence_pipe[1]);
    int status;
    assert(waitpid(child, &status, 0) == child);
    SignalRecord record = {0};
    int found = ansight_crash_read_signal_record(path, &record);
    assert(access(path, F_OK) == 0);
    if (kind == 2) {
        uintptr_t address = 0;
        assert(read(evidence_pipe[0], &address, sizeof(address)) == sizeof(address));
        assert(address == 0x123);
        assert(WIFEXITED(status) && WEXITSTATUS(status) == 51);
        assert(found == 1 && record.faultAddress == 0x123);
        assert(ansight_crash_read_signal_record(path, &record) == 1);
    } else if (kind == 6) {
        assert(WIFSIGNALED(status) && WTERMSIG(status) == SIGABRT);
        assert(found == 1);
    } else {
        assert(WIFEXITED(status) && WEXITSTATUS(status) == 0);
        assert(found == 0);
    }
    close(evidence_pipe[0]);
    unlink(path);
}

int main(void) {
    for (int kind = 0; kind < 8; kind++) run_case(kind);
    puts("Passed 8 signal capture regression cases.");
    return 0;
}
