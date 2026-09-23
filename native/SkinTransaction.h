#pragma once
#include <windows.h>
#include <cstdint>
#include <string>

namespace SkinTransaction {
struct File {
    std::uint32_t key;
    std::uint32_t missingBefore;
    std::uint64_t beforeSize;
    std::uint64_t afterSize;
    unsigned char beforeHash[32];
    unsigned char afterHash[32];
};
struct Request {
    std::uint32_t schema;
    std::uint32_t byteSize;
    unsigned char id[16];
    std::uint64_t expiresAt;
    std::uint32_t ownerPid;
    std::uint32_t count;
    std::uint64_t ownerStartedAt;
    File files[12];
};
static_assert(sizeof(File) == 88);
static_assert(sizeof(Request) == 1104);
struct Result {
    DWORD error = 0;
    bool committed = false;
    bool refreshed = false;
    bool rollbackComplete = true;
};
using Guard = bool (*)(void *);
using Refresh = bool (*)(void *);
bool Validate(const Request &request);
std::wstring Id(const Request &request);
Result Commit(const std::wstring &root, const Request &request, Guard guard, Refresh refresh,
              void *context);
}
