#include "acsim/logger.hpp"
#include "acsim/trust.hpp"
#include "acsim/integrity.hpp"
#include "acsim/process_monitor.hpp"
#include "acsim/backend_client.hpp"
#include "acsim/ipc.hpp"
#include "acsim/protocol.hpp"

#include <windows.h>

#include <atomic>
#include <chrono>
#include <thread>

using acsim::log_info;
using acsim::log_warn;
using acsim::log_error;

namespace {

constexpr const char*    kBackendHost   = "127.0.0.1";
constexpr unsigned short kBackendPort   = 47811;
const std::string        kSharedKey     = "acsim-lab-shared-secret-change-me";
constexpr const wchar_t* kPipeName      = L"\\\\.\\pipe\\acsim_kernel";
constexpr const wchar_t* kProtectedName = L"acsim_game.exe";

std::atomic_bool g_running{true};

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
    acsim::Logger::instance().configure("service", "logs/service.log");
    log_info("servicio arrancando");

    // 1. Estado de confianza de la plataforma.
    auto trust = acsim::trust::collect();
    if (!trust.acceptable())
        log_warn("plataforma no aceptable segun politica local; seguimos observando",
                 trust.to_json());

    // 2. Handshake con el backend.
    acsim::backend::Client backend(kBackendHost, kBackendPort, kSharedKey);
    if (!backend.connect()) { log_error("no se pudo conectar al backend"); return 1; }
    auto token = backend.attest(trust.to_json());
    if (!token)             { log_error("attestacion fallida; abortando"); return 1; }

    // 3. Conexion al kernel simulado.
    acsim::ipc::PipeClient pipe(kPipeName);
    if (!pipe.connect()) { log_error("no se pudo conectar al kernel simulado"); return 1; }
    log_info("conectado al kernel simulado");
    pipe.send_line(acsim::proto::encode(
        acsim::proto::envelope(acsim::proto::kHello, {{"role", "service"}})));

    // 4. Hilo lector de eventos del kernel.
    std::thread reader([&] {
        log_info("reader arrancado, esperando bytes del pipe");
        std::size_t seen = 0;
        while (g_running.load()) {
            auto line = pipe.read_line();
            if (!line) {
                log_warn("reader: pipe cerrado", {{"total_recibidos", seen}});
                g_running.store(false);
                break;
            }
            ++seen;
            auto j = acsim::proto::decode(*line);
            if (j.is_discarded()) {
                log_warn("reader: json invalido", {{"raw", line->substr(0, 120)}});
                continue;
            }

            std::string type = j.value("type", "");
            if (type == acsim::proto::kAlert) {
                log_warn("alerta del kernel simulado", j["payload"]);
                backend.report_event("alert", j["payload"]);
            } else if (seen <= 3 || seen % 100 == 0) {
                // Muestreo para no inundar el log con 300 lineas por poll.
                log_info("evento del kernel", {{"n", seen}, {"type", type}});
                backend.report_event(type, j["payload"]);
            } else {
                backend.report_event(type, j["payload"]);
            }
        }
    });

    // 5. Comprobaciones de integridad periodicas.
    while (g_running.load()) {
        std::this_thread::sleep_for(std::chrono::seconds(5));
        auto game = acsim::procmon::find_by_name(kProtectedName);
        if (!game) { log_info("proceso protegido no esta en ejecucion todavia"); continue; }

        auto mods = acsim::integrity::hash_process_modules(game->pid);
        nlohmann::json arr = nlohmann::json::array();
        for (auto& m : mods) {
            arr.push_back({
                {"name",        to_utf8(m.name)},
                {"path",        to_utf8(m.path)},
                {"sha256_disk", m.sha256_disk}
            });
        }
        backend.report_event("integrity", {{"pid", game->pid}, {"modules", arr}});
        log_info("integridad reportada", {{"modules", mods.size()}});
    }

    reader.join();
    backend.close();
    return 0;
}
