#include "acsim/logger.hpp"
#include "acsim/backend_client.hpp"

#include <winsock2.h>
#include <ws2tcpip.h>
#include <bcrypt.h>

#include <nlohmann/json.hpp>
#include <optional>
#include <string>

using acsim::log_info;
using acsim::log_warn;
using acsim::log_error;

namespace {

constexpr unsigned short kPort      = 47811;
const std::string        kSharedKey = "acsim-lab-shared-secret-change-me";

struct WsaGuard {
    WsaGuard()  { WSADATA d; WSAStartup(MAKEWORD(2, 2), &d); }
    ~WsaGuard() { WSACleanup(); }
} g_wsa;

std::string random_nonce() {
    uint8_t bytes[32];
    BCryptGenRandom(nullptr, bytes, sizeof(bytes), BCRYPT_USE_SYSTEM_PREFERRED_RNG);
    static const char hex[] = "0123456789abcdef";
    std::string h(64, '0');
    for (size_t i = 0; i < sizeof(bytes); ++i) {
        h[i * 2]     = hex[(bytes[i] >> 4) & 0xF];
        h[i * 2 + 1] = hex[bytes[i] & 0xF];
    }
    return h;
}

bool send_line(SOCKET s, const nlohmann::json& j) {
    auto txt = j.dump(); txt.push_back('\n');
    return send(s, txt.data(), static_cast<int>(txt.size()), 0) == static_cast<int>(txt.size());
}

std::optional<nlohmann::json> recv_line(SOCKET s, std::string& buffer) {
    for (;;) {
        auto pos = buffer.find('\n');
        if (pos != std::string::npos) {
            std::string line = buffer.substr(0, pos);
            buffer.erase(0, pos + 1);
            auto j = nlohmann::json::parse(line, nullptr, false);
            if (j.is_discarded()) return std::nullopt;
            return j;
        }
        char chunk[4096];
        int r = recv(s, chunk, sizeof(chunk), 0);
        if (r <= 0) return std::nullopt;
        buffer.append(chunk, r);
    }
}

void handle_client(SOCKET client) {
    std::string buffer;
    auto hello = recv_line(client, buffer);
    if (!hello || (*hello)["type"] != "hello") {
        log_warn("cliente sin hello inicial"); closesocket(client); return;
    }
    log_info("cliente saludo", (*hello)["payload"]);

    auto nonce = random_nonce();
    if (!send_line(client, {{"type", "challenge"}, {"payload", {{"nonce", nonce}}}})) {
        closesocket(client); return;
    }

    auto att = recv_line(client, buffer);
    if (!att || (*att)["type"] != "attestation") {
        log_warn("attestacion ausente"); closesocket(client); return;
    }
    const auto& payload = (*att)["payload"];
    std::string got_nonce = payload["nonce"];
    std::string got_mac   = payload["mac"];
    auto trust            = payload["trust"];

    if (got_nonce != nonce) {
        log_warn("nonce no coincide"); closesocket(client); return;
    }
    auto expected = acsim::backend::hmac_sha256_hex(kSharedKey, nonce + trust.dump());
    if (got_mac != expected) {
        log_warn("mac invalido"); closesocket(client); return;
    }

    // Politica del backend: exigencias minimas para conceder sesion.
    bool ok = trust.value("secure_boot", false)
           && !trust.value("kernel_debugger", false)
           && !trust.value("test_signing", false);
    if (!ok) {
        log_warn("plataforma no cumple politica", trust);
        send_line(client, {{"type", "reject"}, {"payload", {{"reason", "platform"}}}});
        closesocket(client); return;
    }

    std::string token = random_nonce();
    send_line(client, {{"type", "session"}, {"payload", {{"token", token}}}});
    log_info("sesion concedida", {{"token", token.substr(0, 8) + "..."}});

    // Consume eventos hasta que cierre.
    while (auto ev = recv_line(client, buffer))
        log_info("evento del cliente", *ev);

    closesocket(client);
    log_info("cliente desconectado");
}

} // namespace

int main() {
    acsim::Logger::instance().configure("backend", "logs/backend.log");
    log_info("arrancando backend simulado", {{"port", kPort}});

    SOCKET srv = socket(AF_INET, SOCK_STREAM, 0);
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port   = htons(kPort);
    addr.sin_addr.s_addr = INADDR_ANY;
    BOOL yes = TRUE;
    setsockopt(srv, SOL_SOCKET, SO_REUSEADDR,
               reinterpret_cast<const char*>(&yes), sizeof(yes));
    if (bind(srv, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR) {
        log_error("bind fallo"); return 1;
    }
    listen(srv, 1);
    log_info("backend escuchando");

    for (;;) {
        SOCKET c = accept(srv, nullptr, nullptr);
        if (c == INVALID_SOCKET) continue;
        handle_client(c);
    }
}
