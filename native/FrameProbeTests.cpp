#include "FrameProbe.cpp"
#include <cstdio>

static void Require(bool value, const char* message) {
    if (!value) { std::fprintf(stderr, "FAIL %s\n", message); std::exit(1); }
    std::printf("PASS %s\n", message);
}

int main() {
    auto memory = static_cast<unsigned char*>(VirtualAlloc(nullptr, Profile::ImageSize, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    Require(memory != nullptr, "allocate isolated profile fixture");
    gameBase = reinterpret_cast<std::uintptr_t>(memory);
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(memory);
    dos->e_magic = IMAGE_DOS_SIGNATURE; dos->e_lfanew = 0x100;
    auto pe = reinterpret_cast<IMAGE_NT_HEADERS64*>(memory + 0x100);
    pe->Signature = IMAGE_NT_SIGNATURE; pe->FileHeader.Machine = IMAGE_FILE_MACHINE_AMD64;
    pe->FileHeader.TimeDateStamp = Profile::Timestamp; pe->OptionalHeader.SizeOfImage = static_cast<DWORD>(Profile::ImageSize);
    auto app = gameBase + 0x100000;
    auto commands = gameBase + 0x200000;
    auto command = gameBase + 0x300000;
    *reinterpret_cast<std::uintptr_t*>(gameBase + Profile::AppSingleton) = app;
    *reinterpret_cast<std::uintptr_t*>(app) = gameBase + Profile::AppVtable;
    *reinterpret_cast<std::uintptr_t*>(app + Profile::AppCommands) = commands;
    *reinterpret_cast<std::uintptr_t*>(commands + Profile::AfterLoopCommand) = command;
    *reinterpret_cast<std::uintptr_t*>(command) = gameBase + Profile::CommandVtable;
    *reinterpret_cast<std::uintptr_t*>(command + Profile::CommandArgument) = app;
    auto slot = reinterpret_cast<std::uintptr_t*>(command + Profile::CommandCallback);
    *slot = gameBase + Profile::AfterMainLoop;

    Require(AttachVerifiedCallback() == 0 && *slot == reinterpret_cast<std::uintptr_t>(&OnAfterMainLoop), "replace only the verified callback slot");
    ObserveFrame(reinterpret_cast<void*>(app));
    Require(MoreCarsProbeState.frames == 1 && MoreCarsProbeState.threadId == static_cast<LONG>(GetCurrentThreadId()), "observe the invoking thread and menu state");
    *reinterpret_cast<std::uintptr_t*>(app + Profile::Editor) = 1;
    MoreCarsFidState.pending = 1;
    ObserveFrame(reinterpret_cast<void*>(app));
    Require(MoreCarsProbeState.editor == 1 && MoreCarsFidState.pending == 1 && MoreCarsFidState.outcome == 2 && MoreCarsFidState.completed == 0, "editor blocks queued FID calls");
    MoreCarsProbeState.frames = 20;
    Require(MoreCarsRequestFidRefresh(nullptr) == 31 && MoreCarsFidState.outcome == 2, "a competing request preserves the pending result");
    MoreCarsFidState.pending = 2;
    ObserveFrame(reinterpret_cast<void*>(app));
    Require(MoreCarsFidState.pending == 2 && MoreCarsFidState.completed == 0, "a preparing request cannot run on a frame");
    MoreCarsFidState.pending = 1;
    MoreCarsProbeState.threadId = static_cast<LONG>(GetCurrentThreadId() + 1);
    ObserveFrame(reinterpret_cast<void*>(app));
    Require(MoreCarsProbeState.mixedThreads == 1 && MoreCarsRequestFidRefresh(nullptr) == 30, "mixed-thread observations cannot authorize refresh");
    Require(MoreCarsStopProbe(nullptr) == 0 && *slot == gameBase + Profile::AfterMainLoop && MoreCarsFidState.pending == 0, "restore the original callback and cancel pending work");
    Require(MoreCarsStopProbe(nullptr) == 0, "stop is idempotent");
    *slot = gameBase + 0x12345;
    Require(AttachVerifiedCallback() == 16 && *slot == gameBase + 0x12345, "foreign callback is never overwritten");
    *slot = gameBase + Profile::AfterMainLoop;
    Require(AttachVerifiedCallback() == 0, "reattach after a completed stop");
    *slot = gameBase + 0x54321;
    Require(MoreCarsStopProbe(nullptr) == 20 && *slot == gameBase + 0x54321, "stop preserves a callback changed by another owner");
    VirtualFree(memory, 0, MEM_RELEASE);
    return 0;
}
