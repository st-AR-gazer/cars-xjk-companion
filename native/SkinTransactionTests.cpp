#include "SkinTransaction.h"
#include "GameProfile.g.h"
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <cstring>
#pragma comment(lib, "bcrypt.lib")
namespace fs = std::filesystem;
static void Check(bool ok, const char *message) {
    if (!ok)
        throw std::runtime_error(message);
}
static void Write(const fs::path &path, const std::string &data) {
    fs::create_directories(path.parent_path());
    std::ofstream(path, std::ios::binary) << data;
}
static std::string Read(const fs::path &path) {
    std::ifstream file(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(file), {}};
}
static void Digest(const std::string &data, unsigned char *hash) {
    BCRYPT_ALG_HANDLE algorithm{};
    Check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0,
          "hash provider");
    const auto result =
        BCryptHash(algorithm, nullptr, 0, reinterpret_cast<PUCHAR>(const_cast<char *>(data.data())),
                   static_cast<ULONG>(data.size()), hash, 32);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    Check(result >= 0, "hash");
}
struct Fixture {
    fs::path root, staging;
    SkinTransaction::Request request{};
    explicit Fixture(const fs::path &parent, int number, bool missing = false) {
        root = parent / std::to_wstring(number);
        Check(!fs::exists(root), "fixture must be new");
        request.schema = 1;
        request.byteSize = sizeof(request);
        request.id[0] = static_cast<unsigned char>(number);
        request.count = 2;
        request.ownerPid = GetCurrentProcessId();
        request.ownerStartedAt = 1;
        FILETIME now{};
        GetSystemTimeAsFileTime(&now);
        request.expiresAt =
            (static_cast<std::uint64_t>(now.dwHighDateTime) << 32) + now.dwLowDateTime + 600000000;
        staging = root / L".morecars" / L"native" / SkinTransaction::Id(request);
        fs::create_directories(staging);
        for (unsigned i = 0; i < 2; ++i) {
            auto &file = request.files[i];
            file.key = i ? 11 : 0;
            const std::string before = i ? "old-ledger" : "old-car",
                              after = i ? "new-ledger" : "new-car";
            file.beforeSize = before.size();
            file.afterSize = after.size();
            Digest(before, file.beforeHash);
            Digest(after, file.afterHash);
            Write(root / Profile::SkinPaths[file.key], before);
            Write(staging / (std::to_wstring(file.key) + L".next"), after);
        }
        if (missing) {
            fs::remove(root / Profile::SkinPaths[0]);
            request.files[0].missingBefore = 1;
            request.files[0].beforeSize = 0;
            std::memset(request.files[0].beforeHash, 0, 32);
        }
    }
    bool Before() const {
        return Read(root / Profile::SkinPaths[0]) == "old-car" &&
               Read(root / Profile::SkinPaths[11]) == "old-ledger";
    }
    bool After() const {
        return Read(root / Profile::SkinPaths[0]) == "new-car" &&
               Read(root / Profile::SkinPaths[11]) == "new-ledger";
    }
};
struct Callbacks {
    int guards = 0, refreshes = 0, refuseAt = 0;
    bool refreshOk = true;
    Fixture *fixture = nullptr;
};
static bool Guard(void *value) {
    auto &c = *static_cast<Callbacks *>(value);
    return ++c.guards != c.refuseAt;
}
static bool Refresh(void *value) {
    auto &c = *static_cast<Callbacks *>(value);
    ++c.refreshes;
    Check(c.fixture->After(), "refresh must see committed car and ledger");
    return c.refreshOk;
}
int wmain(int argc, wchar_t **argv) {
    try {
        Check(argc == 2, "provide fixture root");
        const auto parent = fs::absolute(argv[1]) / std::to_wstring(GetTickCount64());
        int count = 0;
        {
            Fixture f(parent, ++count);
            Callbacks c{};
            c.fixture = &f;
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            Check(r.committed && r.refreshed && f.After() && c.refreshes == 1, "commit");
            Check(Read(f.staging / L"0.before") == "old-car", "backup retained");
        }
        {
            Fixture f(parent, ++count);
            Callbacks c{};
            c.fixture = &f;
            Write(f.staging / L"0.next", "changed");
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            Check(!r.committed && r.error == ERROR_CRC && f.Before() && c.refreshes == 0,
                  "hash mismatch");
        }
        {
            Fixture f(parent, ++count);
            Callbacks c{};
            c.fixture = &f;
            c.refreshOk = false;
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            Check(!r.committed && r.rollbackComplete && f.Before(), "refresh rollback");
        }
        {
            Fixture f(parent, ++count);
            Callbacks c{};
            c.fixture = &f;
            c.refuseAt = 2;
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            Check(!r.committed && f.Before() && c.refreshes == 0, "menu recheck");
        }
        {
            Fixture f(parent, ++count, true);
            Callbacks c{};
            c.fixture = &f;
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            Check(r.committed && f.After(), "missing car");
        }
        {
            Fixture f(parent, ++count);
            f.request.files[1].key = 0;
            Check(!SkinTransaction::Validate(f.request), "duplicate target");
        }
        {
            Fixture f(parent, ++count);
            f.request.files[0].key = 12;
            Check(!SkinTransaction::Validate(f.request), "unregistered target");
        }
        {
            Fixture f(parent, ++count);
            Callbacks c{};
            c.fixture = &f;
            f.request.expiresAt = 1;
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            Check(r.error == ERROR_TIMEOUT && f.Before(), "expiry");
        }
        {
            Fixture f(parent, ++count);
            Callbacks c{};
            c.fixture = &f;
            HANDLE lock = CreateFileW((f.root / Profile::SkinPaths[0]).c_str(), GENERIC_READ,
                                      FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
            Check(lock != INVALID_HANDLE_VALUE, "fixture lock");
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            CloseHandle(lock);
            Check(!r.committed && f.Before() && c.refreshes == 0, "locked archive");
        }
        {
            Fixture f(parent, ++count);
            Callbacks c{};
            c.fixture = &f;
            Write(f.staging / L"0.before", "someone-else");
            auto r = SkinTransaction::Commit(f.root, f.request, Guard, Refresh, &c);
            Check(!r.committed && f.Before() && Read(f.staging / L"0.before") == "someone-else",
                  "preserve existing backup");
        }
        std::cout << count << " native transaction checks passed. Fixtures: " << parent.string()
                  << '\n';
        return 0;
    } catch (const std::exception &error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
