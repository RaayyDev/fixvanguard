#pragma once

#include <atomic>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <thread>

namespace acsim::ipc {

// Servidor de tubería con nombre; entrega líneas terminadas en '\n' al callback.
// Un solo cliente por instancia, suficiente para el laboratorio.
class PipeServer {
public:
    using LineHandler = std::function<void(std::string_view)>;

    explicit PipeServer(std::wstring name);
    ~PipeServer();

    bool start(LineHandler handler);
    void stop();
    bool send_line(std::string_view line);
    bool is_connected() const { return connected_.load(); }

private:
    void run();

    std::wstring name_;
    LineHandler handler_;
    std::thread worker_;
    std::atomic_bool running_{false};
    std::atomic_bool connected_{false};
    void* pipe_ = nullptr;
    std::mutex write_mutex_;
};

class PipeClient {
public:
    explicit PipeClient(std::wstring name);
    ~PipeClient();

    bool connect();
    bool send_line(std::string_view line);
    std::optional<std::string> read_line();
    void close();

private:
    std::wstring name_;
    void* pipe_ = nullptr;
    std::string buffer_;
};

} // namespace acsim::ipc
