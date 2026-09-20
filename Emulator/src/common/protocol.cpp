#include "acsim/protocol.hpp"

namespace acsim::proto {

nlohmann::json envelope(std::string_view type, nlohmann::json payload) {
    return {{"type", std::string(type)}, {"payload", std::move(payload)}};
}

std::string encode(const nlohmann::json& message) {
    return message.dump();
}

nlohmann::json decode(std::string_view line) {
    return nlohmann::json::parse(line, nullptr, false);
}

} // namespace acsim::proto
