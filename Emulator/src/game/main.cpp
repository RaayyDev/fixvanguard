#include "acsim/logger.hpp"

#include <windows.h>
#include <chrono>
#include <thread>

int main() {
    acsim::Logger::instance().configure("game", "logs/game.log");
    acsim::log_info("juego de prueba iniciado", {{"pid", GetCurrentProcessId()}});

    for (int frame = 0; ; ++frame) {
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
        if (frame % 10 == 0) acsim::log_info("frame", {{"n", frame}});
    }
}
