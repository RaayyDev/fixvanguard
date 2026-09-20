#include "acsim/backend_client.hpp"
#include "acsim/logger.hpp"

#include <winsock2.h>
#include <ws2tcpip.h>
#include <bcrypt.h>

#include <array>

namespace acsim::backend {

namespace {

struct WsaGuard {
    WsaGuard() { WSADATA d; WSAStartup(MAKEWORD(2, 2), &d); }
    ~WsaGuard() { WSACleanup(); }
};
WsaGuard g_wsa;

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

std::string hmac_sha256_hex(std::string_view key, std::string_view msg) {
    BCRYPT_ALG_HANDLE alg = nullptr;
    BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr,
                                BCRYPT_ALG_HANDLE_HMAC_FLAG);

    BCRYPT_HASH_HANDLE hash = nullptr;
    BCryptCreateHash(alg, &hash, nullptr, 0,
                     reinterpret_cast<PUCHAR>(const_cast<char*>(key.data())),
                     static_cast<ULONG>(key.size()), 0);
    BCryptHashData(hash,
                   reinterpret_cast<PUCHAR>(const_cast<char*>(msg.data())),
                   static_cast<ULONG>(msg.size()), 0);

    std::array<uint8_t, 32> digest{};
    BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0);
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(alg, 0);
    return to_hex(digest.data(), digest.size());
}

Client::Client(std::string host, unsigned short port, std::string shared_key)
    : host_(std::move(host)), port_(port), shared_key_(std::move(shared_key)) {}

Client::~Client() { close(); }

bool Client::connect() {
    SOCKET s = socket(AF_INET, SOCK_STREAM, 0);
    if (s == INVALID_SOCKET) return false;

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port   = htons(port_);
    inet_pton(AF_INET, host_.c_str(), &addr.sin_addr);

    if (::connect(s, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR) {
        closesocket(s);
        return false;
    }
    socket_ = static_cast<uintptr_t>(s);
    return true;
}

void Client::close() {
    if (socket_ != ~static_cast<uintptr_t>(0)) {
        closesocket(static_cast<SOCKET>(socket_));
        socket_ = ~static_cast<uintptr_t>(0);
    }
}

bool Client::send_json(const nlohmann::json& msg) {
    if (socket_ == ~static_cast<uintptr_t>(0)) return false;
    auto s = msg.dump();
    s.push_back('\n');
    std::lock_guard lock(send_mutex_);
    // Envio atomico: enviamos toda la linea de una vez para no intercalar
    // mensajes producidos por hilos distintos (reader vs bucle de integridad).
    const char* data = s.data();
    int remaining = static_cast<int>(s.size());
    while (remaining > 0) {
        int sent = send(static_cast<SOCKET>(socket_), data, remaining, 0);
        if (sent <= 0) return false;
        data += sent;
        remaining -= sent;
    }
    return true;
}

std::optional<nlohmann::json> Client::recv_json() {
    if (socket_ == ~static_cast<uintptr_t>(0)) return std::nullopt;
    for (;;) {
        auto pos = buffer_.find('\n');
        if (pos != std::string::npos) {
            std::string line = buffer_.substr(0, pos);
            buffer_.erase(0, pos + 1);
            auto j = nlohmann::json::parse(line, nullptr, false);
            if (j.is_discarded()) return std::nullopt;
            return j;
        }
        char chunk[4096];
        int r = recv(static_cast<SOCKET>(socket_), chunk, sizeof(chunk), 0);
        if (r <= 0) return std::nullopt;
        buffer_.append(chunk, r);
    }
}

std::optional<std::string> Client::attest(const nlohmann::json& trust_report) {
    if (!send_json({{"type", "hello"}, {"payload", {{"role", "service"}}}}))
        return std::nullopt;

    auto hello = recv_json();
    if (!hello || (*hello)["type"] != "challenge") return std::nullopt;
    std::string nonce = (*hello)["payload"]["nonce"];

    std::string body = nonce + trust_report.dump();
    auto mac = hmac_sha256_hex(shared_key_, body);
    if (!send_json({{"type", "attestation"},
                    {"payload", {{"trust", trust_report}, {"nonce", nonce}, {"mac", mac}}}}))
        return std::nullopt;

    auto reply = recv_json();
    if (!reply || (*reply)["type"] != "session") {
        log_warn("attestacion rechazada por el backend",
                 reply ? *reply : nlohmann::json{});
        return std::nullopt;
    }
    session_token_ = (*reply)["payload"]["token"];
    log_info("sesion concedida por el backend",
             {{"token", session_token_.substr(0, 8) + "..."}});
    return session_token_;
}

bool Client::report_event(std::string_view type, const nlohmann::json& payload) {
    return send_json({
        {"type", "event"},
        {"payload", {{"kind", std::string(type)},
                     {"session", session_token_},
                     {"data", payload}}}
    });
}

} // namespace acsim::backend
