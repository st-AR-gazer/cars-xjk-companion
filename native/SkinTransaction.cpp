#include "SkinTransaction.h"
#include <bcrypt.h>
#include <algorithm>
#include <array>
#include <cstring>
#include <filesystem>
#include <vector>
#include "GameProfile.g.h"
#pragma comment(lib, "bcrypt.lib")

namespace SkinTransaction {
namespace {
struct Failure { DWORD code; };
void Need(bool success, DWORD code = 0) { if (!success) throw Failure{code ? code : GetLastError()}; }
struct Handle {
    HANDLE value = INVALID_HANDLE_VALUE;
    explicit Handle(HANDLE h = INVALID_HANDLE_VALUE) : value(h) {}
    Handle(Handle&& other) noexcept : value(other.value) { other.value = INVALID_HANDLE_VALUE; }
    Handle& operator=(Handle&& other) noexcept { if (this != &other) { Reset(); value = other.value; other.value = INVALID_HANDLE_VALUE; } return *this; }
    Handle(const Handle&) = delete;
    ~Handle() { Reset(); }
    void Reset() { if (value != INVALID_HANDLE_VALUE) CloseHandle(value); value = INVALID_HANDLE_VALUE; }
};
struct Entry {
    std::wstring target, source, backup;
    File file{};
    Handle before, after;
    bool oldMoved = false, newMoved = false;
};
std::uint64_t Now() {
    FILETIME time{}; GetSystemTimeAsFileTime(&time);
    return (static_cast<std::uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
}
void NoLinks(const std::wstring& path) {
    auto current = std::filesystem::path(path);
    while (!current.empty()) {
        const auto attributes = GetFileAttributesW(current.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES) Need(!(attributes & FILE_ATTRIBUTE_REPARSE_POINT), ERROR_REPARSE_TAG_INVALID);
        else Need(GetLastError() == ERROR_FILE_NOT_FOUND || GetLastError() == ERROR_PATH_NOT_FOUND);
        auto parent = current.parent_path();
        if (parent == current) break;
        current = parent;
    }
}
Handle LockDirectory(const std::wstring& path) {
    NoLinks(path);
    Handle h(CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    Need(h.value != INVALID_HANDLE_VALUE);
    BY_HANDLE_FILE_INFORMATION info{}; Need(GetFileInformationByHandle(h.value, &info));
    Need((info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && !(info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT), ERROR_REPARSE_TAG_INVALID);
    return h;
}
void Hash(HANDLE file, unsigned char result[32]) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD size = 0, actual = 0;
    Need(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0, ERROR_CRC);
    try {
        Need(BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&size), sizeof(size), &actual, 0) >= 0 && size < 65536, ERROR_CRC);
        std::vector<unsigned char> object(size), buffer(65536);
        Need(BCryptCreateHash(algorithm, &hash, object.data(), size, nullptr, 0, 0) >= 0, ERROR_CRC);
        LARGE_INTEGER beginning{}; Need(SetFilePointerEx(file, beginning, nullptr, FILE_BEGIN));
        while (true) {
            DWORD count = 0; Need(ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &count, nullptr));
            if (!count) break;
            Need(BCryptHashData(hash, buffer.data(), count, 0) >= 0, ERROR_CRC);
        }
        Need(BCryptFinishHash(hash, result, 32, 0) >= 0, ERROR_CRC);
        BCryptDestroyHash(hash); hash = nullptr;
    } catch (...) {
        if (hash) BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        throw;
    }
    BCryptCloseAlgorithmProvider(algorithm, 0);
}
Handle OpenVerified(const std::wstring& path, const unsigned char expected[32], std::uint64_t size) {
    NoLinks(path);
    Handle h(CreateFileW(path.c_str(), GENERIC_READ | DELETE, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    Need(h.value != INVALID_HANDLE_VALUE);
    BY_HANDLE_FILE_INFORMATION info{}; Need(GetFileInformationByHandle(h.value, &info));
    Need(!(info.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) && info.nNumberOfLinks == 1, ERROR_REPARSE_TAG_INVALID);
    Need(((static_cast<std::uint64_t>(info.nFileSizeHigh) << 32) | info.nFileSizeLow) == size, ERROR_CRC);
    unsigned char digest[32]{}; Hash(h.value, digest);
    Need(std::memcmp(digest, expected, 32) == 0, ERROR_CRC);
    return h;
}
bool Rename(HANDLE source, const std::wstring& target) {
    const auto bytes = static_cast<DWORD>(target.size() * sizeof(wchar_t));
    std::vector<unsigned char> storage(sizeof(FILE_RENAME_INFO) + bytes);
    auto info = reinterpret_cast<FILE_RENAME_INFO*>(storage.data());
    info->ReplaceIfExists = FALSE;
    info->RootDirectory = nullptr;
    info->FileNameLength = bytes;
    std::memcpy(info->FileName, target.c_str(), bytes + sizeof(wchar_t));
    return SetFileInformationByHandle(source, FileRenameInfo, info, static_cast<DWORD>(storage.size())) != FALSE;
}
bool Rollback(std::vector<Entry>& entries) {
    bool complete = true;
    for (auto i = entries.rbegin(); i != entries.rend(); ++i) {
        try {
        if (i->newMoved && i->after.value == INVALID_HANDLE_VALUE) i->after = OpenVerified(i->target, i->file.afterHash, i->file.afterSize);
        if (i->oldMoved && i->before.value == INVALID_HANDLE_VALUE) i->before = OpenVerified(i->backup, i->file.beforeHash, i->file.beforeSize);
        if (i->newMoved) { const auto moved = Rename(i->after.value, i->source); complete = complete && moved; if (moved) i->newMoved = false; }
        if (i->oldMoved) { const auto moved = Rename(i->before.value, i->target); complete = complete && moved; if (moved) i->oldMoved = false; }
        } catch (...) { complete = false; }
    }
    return complete;
}
}

bool Validate(const Request& request) {
    if (request.schema != 1 || request.byteSize != sizeof(Request) || request.count < 2 || request.count > 12 ||
        request.files[request.count - 1].key != 11 || !request.ownerPid || !request.ownerStartedAt || !request.expiresAt ||
        std::all_of(std::begin(request.id), std::end(request.id), [](auto byte) { return byte == 0; })) return false;
    std::uint32_t previous = 0;
    for (std::uint32_t i = 0; i < request.count; ++i) {
        const auto& file = request.files[i];
        const std::uint64_t maximum = file.key == 11 ? 1024 * 1024 : 512ULL * 1024 * 1024;
        if (file.key > 11 || (i && file.key <= previous) || file.missingBefore > 1 || file.beforeSize > maximum ||
            !file.afterSize || file.afterSize > maximum || (!file.missingBefore && !file.beforeSize) ||
            (file.missingBefore && (file.key == 11 || file.beforeSize || !std::all_of(std::begin(file.beforeHash), std::end(file.beforeHash), [](auto b) { return b == 0; })))) return false;
        previous = file.key;
    }
    return true;
}

std::wstring Id(const Request& request) {
    constexpr wchar_t digits[] = L"0123456789abcdef";
    std::wstring id; id.reserve(32);
    for (auto value : request.id) { id.push_back(digits[value >> 4]); id.push_back(digits[value & 15]); }
    return id;
}

Result Commit(const std::wstring& root, const Request& request, Guard guard, Refresh refresh, void* context) {
    Result result{};
    std::vector<Entry> entries;
    std::vector<Handle> directories;
    try {
        Need(Validate(request), ERROR_INVALID_DATA);
        Need(guard && refresh && guard(context), ERROR_BUSY);
        const auto now = Now();
        Need(request.expiresAt > now && request.expiresAt - now <= 30ULL * 60 * 10000000, ERROR_TIMEOUT);
        const auto staging = root + L"\\.morecars\\native\\" + Id(request);
        for (const auto& path : {root, root + L"\\GameData", root + L"\\GameData\\Vehicles", root + L"\\GameData\\Vehicles\\Skins",
                root + L"\\.morecars", root + L"\\.morecars\\native", staging}) directories.push_back(LockDirectory(path));
        entries.reserve(request.count);
        for (std::uint32_t i = 0; i < request.count; ++i) {
            const auto& file = request.files[i];
            Entry entry;
            entry.file = file;
            entry.target = root + L"\\" + Profile::SkinPaths[file.key];
            entry.source = staging + L"\\" + std::to_wstring(file.key) + L".next";
            entry.backup = staging + L"\\" + std::to_wstring(file.key) + L".before";
            NoLinks(entry.backup);
            Need(GetFileAttributesW(entry.backup.c_str()) == INVALID_FILE_ATTRIBUTES && GetLastError() == ERROR_FILE_NOT_FOUND, ERROR_ALREADY_EXISTS);
            entry.after = OpenVerified(entry.source, file.afterHash, file.afterSize);
            if (file.missingBefore) {
                NoLinks(entry.target);
                Need(GetFileAttributesW(entry.target.c_str()) == INVALID_FILE_ATTRIBUTES && GetLastError() == ERROR_FILE_NOT_FOUND, ERROR_FILE_EXISTS);
            } else entry.before = OpenVerified(entry.target, file.beforeHash, file.beforeSize);
            entries.push_back(std::move(entry));
        }
        Need(guard(context) && request.expiresAt > Now(), ERROR_CANCELLED);
        for (auto& entry : entries) {
            if (entry.before.value != INVALID_HANDLE_VALUE) { Need(Rename(entry.before.value, entry.backup)); entry.oldMoved = true; }
            Need(Rename(entry.after.value, entry.target)); entry.newMoved = true;
        }
        // Trackmania opens archives without sharing DELETE access. Release the
        // rename handles before its FID loader opens the newly committed files.
        // A rollback reopens and verifies the exact expected bytes first.
        for (auto& entry : entries) { entry.before.Reset(); entry.after.Reset(); }
        Need(guard(context) && refresh(context), ERROR_FUNCTION_FAILED);
        result.committed = result.refreshed = true;
        // Backups remain until the companion verifies the receipt and removes
        // the journal. The final ownership-file rename is the disk commit point.
    } catch (const Failure& failure) {
        result.error = failure.code ? failure.code : ERROR_FUNCTION_FAILED;
        result.rollbackComplete = Rollback(entries);
    } catch (...) {
        result.error = ERROR_UNHANDLED_EXCEPTION;
        result.rollbackComplete = Rollback(entries);
    }
    return result;
}
}
