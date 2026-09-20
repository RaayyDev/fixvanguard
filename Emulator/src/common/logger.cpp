#include "acsim/logger.hpp"

#include <chrono>
#include <ctime>
#include <iostream>

namespace acsim {

Logger& Logger::instance() {
    static Logger inst;
    return inst;
}

void Logger::configure(std::string component, std::filesystem::path file) {
    std::lock_guard lock(mutex_);
    component_ = std::move(component);
    if (!file.parent_path().empty())
        std::filesystem::create_directories(file.parent_path());
    file_.open(file, std::ios::app);
}

void Logger::log(Level level, std::string_view message, nlohmann::json fields) {
    using namespace std::chrono;
    auto now = system_clock::now();
    auto tt = system_clock::to_time_t(now);
    std::tm tm{};
    localtime_s(&tm, &tt);
    char stamp[32];
    std::strftime(stamp, sizeof(stamp), "%FT%T", &tm);

    nlohmann::json record = {
        {"ts", stamp},
        {"comp", component_},
        {"lvl", level_name(level)},
        {"msg", std::string(message)},
    };
    if (!fields.is_null() && !fields.empty())
        record["fields"] = std::move(fields);

    std::string line = record.dump();
    std::lock_guard lock(mutex_);
    std::cout << line << '\n';
    if (file_.is_open()) {
        file_ << line << '\n';
        file_.flush();
    }
}

std::string Logger::level_name(Level level) const {
    switch (level) {
        case Level::Debug: return "debug";
        case Level::Info:  return "info";
        case Level::Warn:  return "warn";
        case Level::Error: return "error";
    }
    return "info";
}

void log_debug(std::string_view m, nlohmann::json f) { Logger::instance().log(Level::Debug, m, std::move(f)); }
void log_info (std::string_view m, nlohmann::json f) { Logger::instance().log(Level::Info,  m, std::move(f)); }
void log_warn (std::string_view m, nlohmann::json f) { Logger::instance().log(Level::Warn,  m, std::move(f)); }
void log_error(std::string_view m, nlohmann::json f) { Logger::instance().log(Level::Error, m, std::move(f)); }

} // namespace acsim
