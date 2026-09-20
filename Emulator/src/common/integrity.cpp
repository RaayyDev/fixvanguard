#include "acsim/integrity.hpp"

#include <windows.h>
#include <bcrypt.h>
#include <wintrust.h>
#include <softpub.h>
#include <tlhelp32.h>

#include <array>
#include <fstream>

namespace acsim::integrity {

namespace {

std::string to_hex(const uint8_t* data, size_t len) {
    static const char hex[] = "0123456789abcdef";
    std::string out(len * 2, '0');
    for (size_t i = 0; i < len; ++i) {
        out[i * 2]     = hex[(data[i] >> 4) & 0xF];
        out[i * 2 + 1] = hex[data[i] & 0xF];
    }
    return out;
}

} // namespace

std::optional<std::string> sha256_file(const std::filesystem::path& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) return std::nullopt;

    BCRYPT_ALG_HANDLE alg = nullptr;
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
        return std::nullopt;

    BCRYPT_HASH_HANDLE hash = nullptr;
    BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0);

    std::array<char, 64 * 1024> buffer{};
    while (in) {
        in.read(buffer.data(), buffer.size());
        auto got = in.gcount();
        if (got > 0)
            BCryptHashData(hash, reinterpret_cast<PUCHAR>(buffer.data()),
                           static_cast<ULONG>(got), 0);
    }

    std::array<uint8_t, 32> digest{};
    BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0);
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(alg, 0);
    return to_hex(digest.data(), digest.size());
}

bool verify_authenticode(const std::filesystem::path& path) {
    WINTRUST_FILE_INFO file{};
    file.cbStruct = sizeof(file);
    file.pcwszFilePath = path.c_str();

    GUID policy = WINTRUST_ACTION_GENERIC_VERIFY_V2;
    WINTRUST_DATA data{};
    data.cbStruct            = sizeof(data);
    data.dwUIChoice          = WTD_UI_NONE;
    data.fdwRevocationChecks = WTD_REVOKE_NONE;
    data.dwUnionChoice       = WTD_CHOICE_FILE;
    data.pFile               = &file;
    data.dwStateAction       = WTD_STATEACTION_VERIFY;

    LONG status = WinVerifyTrust(nullptr, &policy, &data);
    data.dwStateAction = WTD_STATEACTION_CLOSE;
    WinVerifyTrust(nullptr, &policy, &data);
    return status == 0;
}

std::vector<ModuleHash> hash_process_modules(unsigned long pid) {
    std::vector<ModuleHash> out;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snap == INVALID_HANDLE_VALUE) return out;

    MODULEENTRY32W me{};
    me.dwSize = sizeof(me);
    if (Module32FirstW(snap, &me)) {
        do {
            ModuleHash m;
            m.name = me.szModule;
            m.path = me.szExePath;
            m.base = reinterpret_cast<uintptr_t>(me.modBaseAddr);
            m.size = me.modBaseSize;
            if (auto h = sha256_file(m.path); h) m.sha256_disk = std::move(*h);
            out.push_back(std::move(m));
        } while (Module32NextW(snap, &me));
    }
    CloseHandle(snap);
    return out;
}

} // namespace acsim::integrity
