#define MORECARS_SKIN_RUNTIME
#include "FrameProbe.cpp"
#include "SkinTransaction.h"
#include <string>

// 0 idle, 1 queued, 2 committing, 3 complete, 4 failed, 5 cancelled.
struct alignas(8) CommitState {
    std::uint32_t schema = 1;
    volatile LONG state = 0;
    unsigned char id[16]{};
    volatile LONG error = 0;
    volatile LONG rollbackComplete = 1;
    volatile LONG refreshed = 0;
    volatile LONG threadId = 0;
};
static_assert(sizeof(CommitState) == 40);
extern "C" __declspec(dllexport) CommitState MoreCarsCommitState;
CommitState MoreCarsCommitState;
static SRWLOCK commitLock = SRWLOCK_INIT;
static SkinTransaction::Request commitRequest{};
static HANDLE commitOwner = nullptr;

static std::uint64_t FileTimeValue(const FILETIME& time) {
    return (static_cast<std::uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
}
static bool RequestAlive() {
    FILETIME now{}; GetSystemTimeAsFileTime(&now);
    return commitOwner && WaitForSingleObject(commitOwner, 0) == WAIT_TIMEOUT && FileTimeValue(now) < commitRequest.expiresAt;
}

static bool CopyRequest(void* source, SkinTransaction::Request* target) {
    __try { std::memcpy(target, source, sizeof(*target)); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

extern "C" __declspec(dllexport) DWORD WINAPI MoreCarsSubmitSkinCommit(void* input) {
    SkinTransaction::Request request{};
    if (!CopyRequest(input, &request) || !SkinTransaction::Validate(request)) return ERROR_INVALID_DATA;
    HANDLE owner = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, request.ownerPid);
    FILETIME created{}, exited{}, kernel{}, user{};
    if (!owner) return GetLastError();
    if (!GetProcessTimes(owner, &created, &exited, &kernel, &user) || FileTimeValue(created) != request.ownerStartedAt) { CloseHandle(owner); return ERROR_INVALID_OWNER; }
    AcquireSRWLockExclusive(&commitLock);
    DWORD result = 0;
    if (!MoreCarsProbeState.running || MoreCarsProbeState.mixedThreads || MoreCarsProbeState.fault) result = ERROR_NOT_READY;
    else if (MoreCarsCommitState.state == 1 || MoreCarsCommitState.state == 2) result = ERROR_BUSY;
    else {
        if (commitOwner) CloseHandle(commitOwner);
        commitOwner = owner; owner = nullptr;
        commitRequest = request;
        std::memcpy(MoreCarsCommitState.id, request.id, sizeof(request.id));
        MoreCarsCommitState.error = 0; MoreCarsCommitState.rollbackComplete = 1;
        MoreCarsCommitState.refreshed = 0; MoreCarsCommitState.threadId = 0;
        InterlockedExchange(&MoreCarsCommitState.state, 1);
    }
    ReleaseSRWLockExclusive(&commitLock);
    if (owner) CloseHandle(owner);
    return result;
}

extern "C" __declspec(dllexport) DWORD WINAPI MoreCarsCancelSkinCommit(void* input) {
    unsigned char id[16]{};
    __try { std::memcpy(id, input, sizeof(id)); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return ERROR_INVALID_DATA; }
    AcquireSRWLockExclusive(&commitLock);
    DWORD result = 0;
    if (std::memcmp(id, MoreCarsCommitState.id, sizeof(id)) != 0) result = ERROR_NOT_FOUND;
    else if (MoreCarsCommitState.state == 2) result = ERROR_BUSY;
    else if (MoreCarsCommitState.state == 1) {
        if (commitOwner) { CloseHandle(commitOwner); commitOwner = nullptr; }
        InterlockedExchange(&MoreCarsCommitState.state, 5);
    }
    ReleaseSRWLockExclusive(&commitLock);
    return result;
}

static bool RuntimeMenu(void* self) {
    __try { return MoreCarsProbeState.running && !MoreCarsProbeState.mixedThreads && !MoreCarsProbeState.fault && MenuState(self); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
static bool RuntimeCommitGuard(void* self) { return RuntimeMenu(self) && RequestAlive(); }

static bool RefreshCommittedSkins(void* self) {
    __try {
        if (!RuntimeMenu(self) || std::memcmp(reinterpret_cast<void*>(gameBase + Profile::UpdateTree), Profile::UpdateTreePrefix, sizeof(Profile::UpdateTreePrefix))) return false;
        auto folder = GameDataFolder();
        if (!folder) return false;
        const auto update = reinterpret_cast<void(__fastcall*)(void*, unsigned int)>(gameBase + Profile::UpdateTree);
        update(reinterpret_cast<void*>(folder), 0);
        folder = FindFolder(GameDataFolder(), "Vehicles");
        if (!folder || !RuntimeMenu(self)) return false;
        update(reinterpret_cast<void*>(folder), 1);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        InterlockedExchange(&MoreCarsProbeState.fault, static_cast<LONG>(GetExceptionCode()));
        return false;
    }
}

static void ProcessSkinCommit(void* self) {
    if (!TryAcquireSRWLockExclusive(&commitLock)) return;
    if (MoreCarsCommitState.state != 1) { ReleaseSRWLockExclusive(&commitLock); return; }
    if (!RequestAlive()) {
        MoreCarsCommitState.error = ERROR_CANCELLED;
        if (commitOwner) { CloseHandle(commitOwner); commitOwner = nullptr; }
        InterlockedExchange(&MoreCarsCommitState.state, 5);
        ReleaseSRWLockExclusive(&commitLock); return;
    }
    if (!RuntimeMenu(self)) { ReleaseSRWLockExclusive(&commitLock); return; }
    const auto request = commitRequest;
    InterlockedExchange(&MoreCarsCommitState.state, 2);
    ReleaseSRWLockExclusive(&commitLock);
    SkinTransaction::Result result{ERROR_PATH_NOT_FOUND};
    try {
        wchar_t path[32768]{};
        const auto length = GetModuleFileNameW(nullptr, path, 32768);
        if (length && length < 32768) {
            std::wstring root(path, length);
            const auto slash = root.find_last_of(L"\\/");
            if (slash != std::wstring::npos) result = SkinTransaction::Commit(root.substr(0, slash), request, RuntimeCommitGuard, RefreshCommittedSkins, self);
        }
    } catch (...) { result.error = ERROR_UNHANDLED_EXCEPTION; }
    AcquireSRWLockExclusive(&commitLock);
    MoreCarsCommitState.error = static_cast<LONG>(result.error);
    MoreCarsCommitState.rollbackComplete = result.rollbackComplete;
    MoreCarsCommitState.refreshed = result.refreshed;
    MoreCarsCommitState.threadId = static_cast<LONG>(GetCurrentThreadId());
    if (commitOwner) { CloseHandle(commitOwner); commitOwner = nullptr; }
    InterlockedExchange(&MoreCarsCommitState.state, result.committed && result.refreshed ? 3 : 4);
    ReleaseSRWLockExclusive(&commitLock);
}

static bool PrepareSkinRuntimeStop() {
    AcquireSRWLockExclusive(&commitLock);
    const bool ready = MoreCarsCommitState.state != 2;
    if (ready) {
        InterlockedExchange(&MoreCarsProbeState.running, 0);
        if (commitOwner) { CloseHandle(commitOwner); commitOwner = nullptr; }
    }
    if (MoreCarsCommitState.state == 1) InterlockedExchange(&MoreCarsCommitState.state, 5);
    ReleaseSRWLockExclusive(&commitLock);
    return ready;
}
