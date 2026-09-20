#include "acsim/logger.hpp"
#include "acsim/ipc.hpp"
#include "acsim/process_monitor.hpp"
#include "acsim/integrity.hpp"
#include "acsim/protocol.hpp"

#include <windows.h>

#include <algorithm>
#include <chrono>
#include <thread>
#include <unordered_map>
#include <vector>

using acsim::log_info;
using acsim::log_warn;

namespace {

constexpr const wchar_t* kPipeName      = L"\\\\.\\pipe\\acsim_kernel";
constexpr const wchar_t* kProtectedName = L"acsim_game.exe";

const std::vector<std::wstring> kDenylist = {
    L"cheatengine-x86_64.exe",
    L"processhacker.exe",
    L"x64dbg.exe",
    L"ida64.exe",
    L"ollydbg.exe",
    L"wireshark.exe"
};

bool in_denylist(std::wstring_view name) {
    std::wstring n(name);
    for (auto& bad : kDenylist)
        if (_wcsicmp(n.c_str(), bad.c_str()) == 0) return true;
    return false;
}

std::string to_utf8(std::wstring_view w) {
    if (w.empty()) return {};
    int need = WideCharToMultiByte(CP_UTF8, 0, w.data(), static_cast<int>(w.size()),
                                   nullptr, 0, nullptr, nullptr);
    std::string out(need, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), static_cast<int>(w.size()),
                        out.data(), need, nullptr, nullptr);
    return out;
}

} // namespace

int main() {
    acsim::Logger::instance().configure("kernel_sim", "logs/kernel_sim.log");
    log_info("kernel simulado arrancando", {{"pipe", to_utf8(kPipeName)}});

    acsim::ipc::PipeServer server(kPipeName);
    server.start([](std::string_view line) {
        log_info("orden del servicio", {{"raw", std::string(line)}});
    });

    acsim::procmon::Watcher watcher;
    std::unordered_map<unsigned long, std::vector<std::wstring>> known_modules;
    bool was_connected = false;

    for (;;) {
        // Al detectar (re)conexion del servicio, resetear estado para que el
        // primer poll siguiente considere todos los procesos como "nuevos".
        if (server.is_connected() && !was_connected) {
            watcher = acsim::procmon::Watcher{};
            known_modules.clear();
            log_info("servicio conectado; reenviando snapshot inicial");
        }
        was_connected = server.is_connected();

        if (!server.is_connected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(200));
            continue;
        }

        auto delta = watcher.poll();
        if (!delta.started.empty() || !delta.ended.empty())
            log_info("poll delta", {{"nuevos", delta.started.size()},
                                    {"terminados", delta.ended.size()}});

        for (auto& p : delta.started) {
            server.send_line(acsim::proto::encode(
                acsim::proto::envelope(acsim::proto::kProcess, {
                    {"pid", p.pid}, {"name", to_utf8(p.name)},
                    {"path", to_utf8(p.image_path)}
                })));

            if (in_denylist(p.name)) {
                server.send_line(acsim::proto::encode(
                    acsim::proto::envelope(acsim::proto::kAlert, {
                        {"kind", "denylist_process"},
                        {"pid",  p.pid},
                        {"name", to_utf8(p.name)}
                    })));
                log_warn("proceso en denylist detectado", {{"name", to_utf8(p.name)}});
            }
        }
        for (auto& p : delta.ended) {
            server.send_line(acsim::proto::encode(
                acsim::proto::envelope(acsim::proto::kProcess, {
                    {"pid", p.pid}, {"ended", true}
                })));
        }

        // Modulos del proceso protegido: compara con el snapshot anterior.
        if (auto game = acsim::procmon::find_by_name(kProtectedName)) {
            auto mods = acsim::integrity::hash_process_modules(game->pid);
            std::vector<std::wstring> names;
            names.reserve(mods.size());
            for (auto& m : mods) names.push_back(m.name);

            auto& prev = known_modules[game->pid];
            for (auto& m : mods) {
                bool seen = std::find(prev.begin(), prev.end(), m.name) != prev.end();
                if (!seen) {
                    server.send_line(acsim::proto::encode(
                        acsim::proto::envelope(acsim::proto::kModule, {
                            {"pid",  game->pid},
                            {"name", to_utf8(m.name)},
                            {"path", to_utf8(m.path)},
                            {"base", m.base},
                            {"size", m.size},
                            {"sha256_disk", m.sha256_disk}
                        })));
                }
            }
            prev = std::move(names);
        }

        std::this_thread::sleep_for(std::chrono::seconds(1));
    }
}
