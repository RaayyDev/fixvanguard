#pragma once

#include <optional>
#include <string>
#include <string_view>
#include <unordered_set>
#include <vector>

namespace acsim::procmon {

struct ProcessInfo {
    unsigned long pid;
    std::wstring  name;
    std::wstring  image_path;
};

std::vector<ProcessInfo> snapshot();
std::optional<ProcessInfo> find_by_name(std::wstring_view name);

// Diferencia entre poll y poll: quienes arrancaron, quienes murieron.
class Watcher {
public:
    struct Delta {
        std::vector<ProcessInfo> started;
        std::vector<ProcessInfo> ended;
    };
    Delta poll();

private:
    std::unordered_set<unsigned long> known_;
};

} // namespace acsim::procmon
