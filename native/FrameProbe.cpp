#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstring>
#include <vector>

#pragma comment(lib, "bcrypt.lib")
#include "GameProfile.g.h"

struct alignas(8) ProbeState {
    std::uint32_t schema = 1;
    volatile LONG running = 0;
    volatile LONG64 frames = 0;
    volatile LONG threadId = 0;
    volatile LONG mixedThreads = 0;
    volatile LONG editor = 0;
    volatile LONG playground = 0;
    volatile LONG map = 0;
    volatile LONG fault = 0;
    volatile LONG stopped = 0;
    volatile LONG reserved = 0;
};

extern "C" __declspec(dllexport) ProbeState MoreCarsProbeState;
ProbeState MoreCarsProbeState;
struct alignas(8) FidState {
    volatile LONG pending = 0;
    volatile LONG outcome = 0;
    volatile LONG64 completed = 0;
    volatile LONG threadId = 0;
    volatile LONG fault = 0;
};
extern "C" __declspec(dllexport) FidState MoreCarsFidState;
FidState MoreCarsFidState;
using FrameCallback = void(__fastcall *)(void *);
static std::uintptr_t gameBase;
static void *gameApp;
static void *volatile *callbackSlot;
static FrameCallback originalCallback;
#ifdef MORECARS_SKIN_RUNTIME
static void ProcessSkinCommit(void *self);
static bool PrepareSkinRuntimeStop();
#endif

static bool MenuState(void *self) {
    auto app = reinterpret_cast<std::uintptr_t>(self);
    return *reinterpret_cast<void **>(gameBase + Profile::AppSingleton) == self &&
           !*reinterpret_cast<void **>(app + Profile::Editor) &&
           !*reinterpret_cast<void **>(app + Profile::Playground);
}

static bool ValidFolder(std::uintptr_t folder) {
    if (!folder)
        return false;
    auto table = *reinterpret_cast<std::uintptr_t *>(folder);
    return (table == gameBase + Profile::RootFolderVtable ||
            table == gameBase + Profile::FolderVtable) &&
           *reinterpret_cast<std::uintptr_t *>(table + Profile::UpdateTreeSlot) ==
               gameBase + Profile::UpdateTree;
}

static std::uintptr_t FindFolder(std::uintptr_t parent, const char *expected) {
    if (!ValidFolder(parent))
        return 0;
    auto count = *reinterpret_cast<std::uint32_t *>(parent + Profile::FolderTreeCount);
    auto entries = *reinterpret_cast<std::uintptr_t **>(parent + Profile::FolderTrees);
    if (count > 2048 || (count && !entries))
        return 0;
    std::uintptr_t result = 0;
    const auto expectedLength = std::strlen(expected);
    for (std::uint32_t i = 0; i < count; ++i) {
        const auto folder = entries[i];
        if (!ValidFolder(folder))
            return 0;
        auto text = reinterpret_cast<unsigned char *>(folder + Profile::FolderName);
        const auto length = *reinterpret_cast<std::uint32_t *>(text + 12);
        if (length > 512 || text[11] > 1 || (!text[11] && length > 11))
            return 0;
        const auto characters = text[11] ? *reinterpret_cast<const char **>(text)
                                         : reinterpret_cast<const char *>(text);
        if (length != expectedLength || _strnicmp(characters, expected, length) != 0)
            continue;
        if (result)
            return 0;
        result = folder;
    }
    return result;
}

static std::uintptr_t GameDataFolder() {
    auto filesystem = *reinterpret_cast<std::uintptr_t *>(gameBase + Profile::FileSystemSingleton);
    if (!filesystem)
        return 0;
    return FindFolder(*reinterpret_cast<std::uintptr_t *>(filesystem + Profile::GameFolder),
                      "GameData");
}

static void TryRefreshFids(void *self) {
    if (!MoreCarsProbeState.running ||
        InterlockedCompareExchange(&MoreCarsFidState.pending, 0, 0) != 1)
        return;
    if (!MenuState(self)) {
        InterlockedExchange(&MoreCarsFidState.outcome, 2);
        return;
    }
    if (MoreCarsProbeState.mixedThreads || MoreCarsProbeState.fault ||
        std::memcmp(reinterpret_cast<void *>(gameBase + Profile::UpdateTree),
                    Profile::UpdateTreePrefix, sizeof(Profile::UpdateTreePrefix)) != 0) {
        InterlockedExchange(&MoreCarsFidState.pending, 0);
        InterlockedExchange(&MoreCarsFidState.outcome, 3);
        return;
    }
    if (InterlockedCompareExchange(&MoreCarsFidState.pending, 0, 1) != 1 ||
        !MoreCarsProbeState.running)
        return;
    auto folder = GameDataFolder();
    if (!folder) {
        InterlockedExchange(&MoreCarsFidState.outcome, 3);
        return;
    }
    using UpdateTree = void(__fastcall *)(void *, unsigned int);
    const auto update = reinterpret_cast<UpdateTree>(gameBase + Profile::UpdateTree);
    update(reinterpret_cast<void *>(folder), 0);
    folder = FindFolder(GameDataFolder(), "Vehicles");
    if (!folder || !MenuState(self)) {
        InterlockedExchange(&MoreCarsFidState.outcome, 3);
        return;
    }
    update(reinterpret_cast<void *>(folder), 1);
    InterlockedExchange(&MoreCarsFidState.threadId, static_cast<LONG>(GetCurrentThreadId()));
    InterlockedIncrement64(&MoreCarsFidState.completed);
    InterlockedExchange(&MoreCarsFidState.outcome, 1);
}

static bool HashMatches(const wchar_t *filename) {
    HANDLE file =
        CreateFileW(filename, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (file == INVALID_HANDLE_VALUE)
        return false;
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD objectSize = 0, written = 0;
    bool valid = false;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0 &&
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize),
                          sizeof(objectSize), &written, 0) >= 0 &&
        objectSize < 65536) {
        std::vector<unsigned char> object(objectSize), buffer(65536);
        if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) >= 0) {
            valid = true;
            while (true) {
                DWORD count = 0;
                if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &count,
                              nullptr)) {
                    valid = false;
                    break;
                }
                if (!count)
                    break;
                if (BCryptHashData(hash, buffer.data(), count, 0) < 0) {
                    valid = false;
                    break;
                }
            }
            unsigned char digest[32]{};
            valid = valid && BCryptFinishHash(hash, digest, sizeof(digest), 0) >= 0 &&
                    std::memcmp(digest, Profile::Sha256, sizeof(digest)) == 0;
            BCryptDestroyHash(hash);
        }
    }
    if (algorithm)
        BCryptCloseAlgorithmProvider(algorithm, 0);
    CloseHandle(file);
    return valid;
}

static void ObserveFrame(void *self) {
    if (!InterlockedCompareExchange(&MoreCarsProbeState.running, 0, 0))
        return;
    __try {
        if (self != gameApp ||
            *reinterpret_cast<void **>(gameBase + Profile::AppSingleton) != self) {
            InterlockedExchange(&MoreCarsProbeState.fault, 1);
            return;
        }
        const LONG thread = static_cast<LONG>(GetCurrentThreadId());
        const LONG previous = InterlockedCompareExchange(&MoreCarsProbeState.threadId, thread, 0);
        if (previous != 0 && previous != thread)
            InterlockedExchange(&MoreCarsProbeState.mixedThreads, 1);
        auto app = reinterpret_cast<std::uintptr_t>(self);
        InterlockedExchange(&MoreCarsProbeState.editor,
                            *reinterpret_cast<void **>(app + Profile::Editor) != nullptr);
        InterlockedExchange(&MoreCarsProbeState.playground,
                            *reinterpret_cast<void **>(app + Profile::Playground) != nullptr);
        InterlockedExchange(&MoreCarsProbeState.map,
                            *reinterpret_cast<void **>(app + Profile::RootMap) != nullptr);
        InterlockedIncrement64(&MoreCarsProbeState.frames);
        TryRefreshFids(self);
#ifdef MORECARS_SKIN_RUNTIME
        ProcessSkinCommit(self);
#endif
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        InterlockedExchange(&MoreCarsProbeState.fault, static_cast<LONG>(GetExceptionCode()));
        InterlockedExchange(&MoreCarsFidState.fault, static_cast<LONG>(GetExceptionCode()));
        InterlockedExchange(&MoreCarsFidState.pending, 0);
        InterlockedExchange(&MoreCarsFidState.outcome, 3);
    }
}

static void __fastcall OnAfterMainLoop(void *self) {
    originalCallback(self);
    ObserveFrame(self);
}

static DWORD AttachVerifiedCallback() {
    __try {
        const auto dos = reinterpret_cast<IMAGE_DOS_HEADER *>(gameBase);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 64 || dos->e_lfanew > 4096)
            return 11;
        const auto pe = reinterpret_cast<IMAGE_NT_HEADERS64 *>(gameBase + dos->e_lfanew);
        if (pe->Signature != IMAGE_NT_SIGNATURE ||
            pe->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
            pe->FileHeader.TimeDateStamp != Profile::Timestamp ||
            pe->OptionalHeader.SizeOfImage != Profile::ImageSize)
            return 12;
        gameApp = *reinterpret_cast<void **>(gameBase + Profile::AppSingleton);
        auto app = reinterpret_cast<std::uintptr_t>(gameApp);
        if (!app || *reinterpret_cast<std::uintptr_t *>(app) != gameBase + Profile::AppVtable)
            return 13;
        auto commands = *reinterpret_cast<std::uintptr_t *>(app + Profile::AppCommands);
        if (!commands)
            return 14;
        auto command = *reinterpret_cast<std::uintptr_t *>(commands + Profile::AfterLoopCommand);
        if (!command ||
            *reinterpret_cast<std::uintptr_t *>(command) != gameBase + Profile::CommandVtable ||
            *reinterpret_cast<void **>(command + Profile::CommandArgument) != gameApp)
            return 15;
        callbackSlot = reinterpret_cast<void *volatile *>(command + Profile::CommandCallback);
        originalCallback = reinterpret_cast<FrameCallback>(gameBase + Profile::AfterMainLoop);
        if (*callbackSlot != reinterpret_cast<void *>(originalCallback))
            return 16;
        InterlockedExchange(&MoreCarsFidState.pending, 0);
        InterlockedExchange(&MoreCarsProbeState.stopped, 0);
        InterlockedExchange(&MoreCarsProbeState.running, 1);
        if (InterlockedCompareExchangePointer(callbackSlot,
                                              reinterpret_cast<void *>(&OnAfterMainLoop),
                                              reinterpret_cast<void *>(originalCallback)) !=
            reinterpret_cast<void *>(originalCallback)) {
            InterlockedExchange(&MoreCarsProbeState.running, 0);
            return 17;
        }
        return 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return 18;
    }
}

extern "C" __declspec(dllexport) DWORD WINAPI MoreCarsStartProbe(void *) {
    if (InterlockedCompareExchange(&MoreCarsProbeState.running, 0, 0))
        return 0;
    wchar_t path[32768]{};
    try {
        if (!GetModuleFileNameW(nullptr, path, 32768) || !HashMatches(path))
            return 10;
    } catch (...) {
        return 19;
    }
    gameBase = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    return AttachVerifiedCallback();
}

extern "C" __declspec(dllexport) DWORD WINAPI MoreCarsRequestFidRefresh(void *) {
    if (!MoreCarsProbeState.running || MoreCarsProbeState.frames < 20 ||
        MoreCarsProbeState.mixedThreads || MoreCarsProbeState.fault)
        return 30;
    if (InterlockedCompareExchange(&MoreCarsFidState.pending, 2, 0))
        return 31;
    InterlockedExchange(&MoreCarsFidState.outcome, 0);
    if (InterlockedCompareExchange(&MoreCarsFidState.pending, 1, 2) != 2)
        return 32;
    if (!MoreCarsProbeState.running) {
        InterlockedCompareExchange(&MoreCarsFidState.pending, 0, 1);
        return 32;
    }
    return 0;
}

extern "C" __declspec(dllexport) DWORD WINAPI MoreCarsStopProbe(void *) {
#ifdef MORECARS_SKIN_RUNTIME
    if (!PrepareSkinRuntimeStop())
        return 40;
#endif
    if (MoreCarsProbeState.stopped)
        return 0;
    InterlockedExchange(&MoreCarsProbeState.running, 0);
    InterlockedExchange(&MoreCarsFidState.pending, 0);
    __try {
        if (callbackSlot && InterlockedCompareExchangePointer(
                                callbackSlot, reinterpret_cast<void *>(originalCallback),
                                reinterpret_cast<void *>(&OnAfterMainLoop)) !=
                                reinterpret_cast<void *>(&OnAfterMainLoop))
            return 20;
        InterlockedExchange(&MoreCarsProbeState.stopped, 1);
        return 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return 21;
    }
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH)
        DisableThreadLibraryCalls(module);
    return TRUE;
}
