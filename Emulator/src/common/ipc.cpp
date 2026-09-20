#include "acsim/ipc.hpp"
#include "acsim/logger.hpp"

#include <windows.h>

namespace acsim::ipc {

PipeServer::PipeServer(std::wstring name) : name_(std::move(name)) {}
PipeServer::~PipeServer() { stop(); }

bool PipeServer::start(LineHandler handler) {
    handler_ = std::move(handler);
    running_.store(true);
    worker_ = std::thread([this] { run(); });
    return true;
}

void PipeServer::stop() {
    running_.store(false);
    if (pipe_) {
        HANDLE h = static_cast<HANDLE>(pipe_);
        CancelIoEx(h, nullptr);
        CloseHandle(h);
        pipe_ = nullptr;
    }
    if (worker_.joinable()) worker_.join();
}

// Wrapper de operacion overlapped: dispara, espera y devuelve bytes reales.
namespace {

bool overlapped_wait(HANDLE pipe, OVERLAPPED& ov, DWORD& transferred) {
    DWORD gle = GetLastError();
    if (gle != ERROR_IO_PENDING) {
        // La operacion completo sincronamente; GetOverlappedResult sirve igual.
        return GetOverlappedResult(pipe, &ov, &transferred, FALSE) != FALSE;
    }
    if (WaitForSingleObject(ov.hEvent, INFINITE) != WAIT_OBJECT_0)
        return false;
    return GetOverlappedResult(pipe, &ov, &transferred, FALSE) != FALSE;
}

} // namespace

bool PipeServer::send_line(std::string_view line) {
    if (!pipe_ || !connected_.load()) return false;
    std::string txt(line);
    txt.push_back('\n');

    std::lock_guard lock(write_mutex_);
    OVERLAPPED ov{};
    ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!ov.hEvent) return false;

    DWORD written = 0;
    BOOL ok = WriteFile(static_cast<HANDLE>(pipe_), txt.data(),
                        static_cast<DWORD>(txt.size()), &written, &ov);
    if (!ok) ok = overlapped_wait(static_cast<HANDLE>(pipe_), ov, written);
    CloseHandle(ov.hEvent);

    if (!ok) {
        log_warn("PipeServer::send_line WriteFile fallo",
                 {{"gle", GetLastError()}, {"bytes", txt.size()}});
    }
    return ok != FALSE;
}

void PipeServer::run() {
    while (running_.load()) {
        HANDLE pipe = CreateNamedPipeW(
            name_.c_str(),
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1, 64 * 1024, 64 * 1024, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) {
            log_error("CreateNamedPipeW fallo", {{"gle", GetLastError()}});
            return;
        }
        pipe_ = pipe;

        // Conexion overlapped: no bloqueamos el hilo indefinidamente para
        // poder salir cuando stop() cancele la I/O.
        OVERLAPPED cov{};
        cov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        BOOL cok = ConnectNamedPipe(pipe, &cov);
        DWORD cbytes = 0;
        if (!cok && GetLastError() == ERROR_PIPE_CONNECTED) {
            cok = TRUE;
        } else if (!cok) {
            cok = overlapped_wait(pipe, cov, cbytes);
        }
        CloseHandle(cov.hEvent);

        if (!cok || !running_.load()) {
            CloseHandle(pipe);
            pipe_ = nullptr;
            if (!running_.load()) return;
            continue;
        }
        connected_.store(true);
        log_info("cliente conectado al pipe");

        std::string buffer;
        char chunk[4096];
        while (running_.load()) {
            OVERLAPPED rov{};
            rov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            DWORD read = 0;
            BOOL rok = ReadFile(pipe, chunk, sizeof(chunk), &read, &rov);
            if (!rok) rok = overlapped_wait(pipe, rov, read);
            CloseHandle(rov.hEvent);
            if (!rok || read == 0) break;

            buffer.append(chunk, read);
            for (;;) {
                auto pos = buffer.find('\n');
                if (pos == std::string::npos) break;
                std::string line = buffer.substr(0, pos);
                buffer.erase(0, pos + 1);
                if (!line.empty() && handler_) handler_(line);
            }
        }

        connected_.store(false);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
        pipe_ = nullptr;
        log_info("cliente desconectado del pipe");
    }
}

PipeClient::PipeClient(std::wstring name) : name_(std::move(name)) {}
PipeClient::~PipeClient() { close(); }

bool PipeClient::connect() {
    for (int attempt = 0; attempt < 40; ++attempt) {
        HANDLE h = CreateFileW(name_.c_str(), GENERIC_READ | GENERIC_WRITE,
                               0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (h != INVALID_HANDLE_VALUE) {
            pipe_ = h;
            return true;
        }
        Sleep(250);
    }
    return false;
}

bool PipeClient::send_line(std::string_view line) {
    if (!pipe_) return false;
    std::string txt(line);
    txt.push_back('\n');
    DWORD written = 0;
    return WriteFile(static_cast<HANDLE>(pipe_), txt.data(),
                     static_cast<DWORD>(txt.size()), &written, nullptr) != FALSE;
}

std::optional<std::string> PipeClient::read_line() {
    if (!pipe_) return std::nullopt;
    for (;;) {
        auto pos = buffer_.find('\n');
        if (pos != std::string::npos) {
            std::string line = buffer_.substr(0, pos);
            buffer_.erase(0, pos + 1);
            return line;
        }
        char chunk[4096];
        DWORD read = 0;
        if (!ReadFile(static_cast<HANDLE>(pipe_), chunk, sizeof(chunk),
                      &read, nullptr) || read == 0)
            return std::nullopt;
        buffer_.append(chunk, read);
    }
}

void PipeClient::close() {
    if (pipe_) {
        CloseHandle(static_cast<HANDLE>(pipe_));
        pipe_ = nullptr;
    }
}

} // namespace acsim::ipc
