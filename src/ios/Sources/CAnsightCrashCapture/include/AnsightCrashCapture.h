#ifndef ANSIGHT_CRASH_CAPTURE_H
#define ANSIGHT_CRASH_CAPTURE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct AnsightCrashSignalRecord {
    uint32_t version;
    int32_t signalNumber;
    int32_t signalCode;
    uint64_t faultAddress;
    int64_t occurredAtEpochSeconds;
    int32_t processId;
} AnsightCrashSignalRecord;

/// Opens the destination before installing async-signal-safe fatal signal handlers.
int ansight_crash_install_signal_handlers(const char *path);

/// Restores handlers still owned by Ansight without replacing newer handlers.
void ansight_crash_uninstall_signal_handlers(void);

/// Reads evidence without deleting it. Recovery removes it after report persistence.
int ansight_crash_read_signal_record(const char *path, AnsightCrashSignalRecord *record);

/// Reads and removes a signal record left by the previous process.
int ansight_crash_consume_signal_record(
    const char *path,
    AnsightCrashSignalRecord *record
);

#ifdef __cplusplus
}
#endif

#endif
