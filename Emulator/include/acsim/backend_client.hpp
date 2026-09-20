#pragma once

#include <nlohmann/json.hpp>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>

namespace acsim::backend {

class Client {
public:
    Client(std::string host, unsigned short port, std::string shared_key);
    ~Client();

    bool connect();
    void close();

    // Handshake: pide reto, envia HMAC(nonce, trust) y espera token de sesion.
    std::optional<std::string> attest(const nlohmann::json& trust_report);

    bool report_event(std::string_view type, const nlohmann::json& payload);

private:
    bool send_json(const nlohmann::json& msg);
    std::optional<nlohmann::json> recv_json();

    std::string       host_;
    unsigned short    port_;
    std::string       shared_key_;
    std::string       buffer_;
    uintptr_t         socket_ = ~static_cast<uintptr_t>(0);
    std::string       session_token_;
    std::mutex        send_mutex_;
};

// HMAC-SHA256 en hex minuscula.
std::string hmac_sha256_hex(std::string_view key, std::string_view msg);

} // namespace acsim::backend
