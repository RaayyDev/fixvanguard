#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <vector>

namespace acsim::integrity {

std::optional<std::string> sha256_file(const std::filesystem::path& path);
bool verify_authenticode(const std::filesystem::path& path);

struct ModuleHash {
    std::wstring name;
    std::wstring path;
    uintptr_t    base;
    size_t       size;
    std::string  sha256_disk;
};

std::vector<ModuleHash> hash_process_modules(unsigned long pid);

} // namespace acsim::integrity
