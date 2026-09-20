cmake_minimum_required(VERSION 3.20)
project(acsim CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT WIN32)
    message(FATAL_ERROR "acsim solo compila en Windows")
endif()

if(MSVC)
    add_compile_options(/W4 /permissive- /Zc:__cplusplus /utf-8)
    add_compile_definitions(_WIN32_WINNT=0x0A00 NOMINMAX WIN32_LEAN_AND_MEAN UNICODE _UNICODE)
endif()

add_library(nlohmann_json INTERFACE)
target_include_directories(nlohmann_json INTERFACE third_party)
add_library(nlohmann_json::nlohmann_json ALIAS nlohmann_json)

add_library(acsim_common STATIC
    src/common/logger.cpp
    src/common/protocol.cpp
    src/common/ipc.cpp
    src/common/trust.cpp
    src/common/integrity.cpp
    src/common/process_monitor.cpp
    src/common/backend_client.cpp)

target_include_directories(acsim_common PUBLIC include third_party)
target_link_libraries(acsim_common PUBLIC
    nlohmann_json::nlohmann_json
    bcrypt wintrust crypt32 ntdll advapi32 tbs ws2_32)

add_executable(acsim_kernel_sim src/kernel_sim/main.cpp)
target_link_libraries(acsim_kernel_sim PRIVATE acsim_common)

add_executable(acsim_service src/service/main.cpp)
target_link_libraries(acsim_service PRIVATE acsim_common)

add_executable(acsim_backend src/backend/main.cpp)
target_link_libraries(acsim_backend PRIVATE acsim_common)

add_executable(acsim_game src/game/main.cpp)
target_link_libraries(acsim_game PRIVATE acsim_common)
