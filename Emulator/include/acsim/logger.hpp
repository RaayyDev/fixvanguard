#pragma once

#include <nlohmann/json.hpp>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>
#include <string_view>

namespace acsim {

enum class Level { Debug, Info, Warn, Error };

class Logger {
public:
    static Logger& instance();

    void configure(std::string component, std::filesystem::path file);
    void log(Level level, std::string_view message, nlohmann::json fields = {});

private:
    Logger() = default;
    std::string level_name(Level level) const;

    std::mutex mutex_;
    std::string component_ = "acsim";
    std::ofstream file_;
};

void log_debug(std::string_view msg, nlohmann::json fields = {});
void log_info (std::string_view msg, nlohmann::json fields = {});
void log_warn (std::string_view msg, nlohmann::json fields = {});
void log_error(std::string_view msg, nlohmann::json fields = {});

} // namespace acsim
