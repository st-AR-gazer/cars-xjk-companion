#include "GameRuntime.cpp"
#include <iostream>
#include <stdexcept>

static void Check(bool condition, const char* name) { if (!condition) throw std::runtime_error(name); }
int main() {
    try {
        // Synthetic game memory in this test process only. No game is opened.
        std::vector<unsigned char> image(Profile::ImageSize), app(0x1000);
        gameBase = reinterpret_cast<std::uintptr_t>(image.data()); gameApp = app.data();
        *reinterpret_cast<void**>(gameBase + Profile::AppSingleton) = gameApp;
        SkinTransaction::Request request{};
        request.schema = 1; request.byteSize = sizeof(request); request.id[0] = 42;
        request.count = 2; request.ownerPid = GetCurrentProcessId();
        FILETIME created{}, exited{}, kernel{}, user{}, now{};
        Check(GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user), "process time");
        GetSystemTimeAsFileTime(&now);
        request.ownerStartedAt = FileTimeValue(created); request.expiresAt = FileTimeValue(now) + 600000000;
        request.files[0].key = 0; request.files[1].key = 11;
        for (auto& file : request.files) { file.beforeSize = 1; file.afterSize = 1; }
        MoreCarsProbeState.running = 1;
        Check(MoreCarsSubmitSkinCommit(&request) == 0 && MoreCarsCommitState.state == 1, "submit");
        Check(MoreCarsSubmitSkinCommit(&request) == ERROR_BUSY && MoreCarsCommitState.state == 1, "concurrent submission");
        *reinterpret_cast<void**>(app.data() + Profile::Playground) = app.data();
        ProcessSkinCommit(app.data()); Check(MoreCarsCommitState.state == 1, "map defers transaction");
        *reinterpret_cast<void**>(app.data() + Profile::Playground) = nullptr;
        *reinterpret_cast<void**>(app.data() + Profile::Editor) = app.data();
        ProcessSkinCommit(app.data()); Check(MoreCarsCommitState.state == 1, "editor defers transaction");
        unsigned char otherId[16]{};
        Check(MoreCarsCancelSkinCommit(otherId) == ERROR_NOT_FOUND && MoreCarsCommitState.state == 1, "foreign cancellation");
        Check(MoreCarsCancelSkinCommit(request.id) == 0 && MoreCarsCommitState.state == 5 && !commitOwner, "cancel closes owner handle");
        request.ownerStartedAt++;
        Check(MoreCarsSubmitSkinCommit(&request) == ERROR_INVALID_OWNER, "PID reuse rejected"); request.ownerStartedAt--;
        Check(MoreCarsSubmitSkinCommit(&request) == 0, "resubmit");
        MoreCarsCommitState.state = 2;
        Check(!PrepareSkinRuntimeStop() && MoreCarsProbeState.running == 1 && commitOwner, "in-progress commit prevents stop");
        MoreCarsCommitState.state = 1;
        Check(PrepareSkinRuntimeStop() && !MoreCarsProbeState.running && MoreCarsCommitState.state == 5 && !commitOwner, "stop cancels pending commit");
        Check(MoreCarsSubmitSkinCommit(&request) == ERROR_NOT_READY, "no new request after stop");
        MoreCarsProbeState.running = 1; request.expiresAt = 1;
        Check(MoreCarsSubmitSkinCommit(&request) == 0, "expired request queued for cancellation");
        ProcessSkinCommit(app.data()); Check(MoreCarsCommitState.state == 5 && !commitOwner, "expiry cancels before menu");
        std::cout << "12 native queue/lifecycle checks passed.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
