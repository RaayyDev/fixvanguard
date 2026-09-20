#include "acsim/process_monitor.hpp"

#include <windows.h>
#include <tlhelp32.h>

#include <algorithm>

namespace acsim::procmon {

namespace {

std::wstring image_path_of(unsigned long pid) {
    HANDLE p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!p) return {};
    wchar_t buf[MAX_PATH * 2] = {};
    DWORD len = static_cast<DWORD>(std::size(buf));
    if (!QueryFullProcessImageNameW(p, 0, buf, &len)) len = 0;
    CloseHandle(p);
    return std::wstring(buf, len);
}

} // namespace

std::vector<ProcessInfo> snapshot() {
    std::vector<ProcessInfo> out;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return out;

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);
    if (Process32FirstW(snap, &pe)) {
        do {
            ProcessInfo p;
            p.pid = pe.th32ProcessID;
            p.name = pe.szExeFile;
            p.image_path = image_path_of(p.pid);
            out.push_back(std::move(p));
        } while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
    return out;
}

std::optional<ProcessInfo> find_by_name(std::wstring_view name) {
    std::wstring target(name);
    auto list = snapshot();
    auto it = std::find_if(list.begin(), list.end(), [&](const auto& p) {
        return _wcsicmp(p.name.c_str(), target.c_str()) == 0;
    });
    if (it == list.end()) return std::nullopt;
    return *it;
}

Watcher::Delta Watcher::poll() {
    Delta d;
    auto current = snapshot();
    std::unordered_set<unsigned long> now;
    for (auto& p : current) {
        now.insert(p.pid);
        if (!known_.contains(p.pid)) d.started.push_back(p);
    }
    for (auto pid : known_) {
        if (!now.contains(pid)) {
            ProcessInfo p{}; p.pid = pid; d.ended.push_back(p);
        }
    }
    known_ = std::move(now);
    return d;
}

} // namespace acsim::procmon
