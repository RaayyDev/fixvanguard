#pragma once

#include <nlohmann/json.hpp>
#include <string>
#include <string_view>

namespace acsim::proto {

// Tipos de mensajes intercambiados entre kernel_sim, service y backend.
inline constexpr const char* kHello       = "hello";
inline constexpr const char* kProcess     = "process";
inline constexpr const char* kModule      = "module";
inline constexpr const char* kIntegrity   = "integrity";
inline constexpr const char* kTrust       = "trust";
inline constexpr const char* kChallenge   = "challenge";
inline constexpr const char* kAttestation = "attestation";
inline constexpr const char* kSession     = "session";
inline constexpr const char* kAlert       = "alert";
inline constexpr const char* kHeartbeat   = "heartbeat";

nlohmann::json envelope(std::string_view type, nlohmann::json payload);
std::string    encode(const nlohmann::json& message);
nlohmann::json decode(std::string_view line);

} // namespace acsim::proto
