#pragma once

#include <nlohmann/json.hpp>

namespace acsim::trust {

// Fotografia del estado de seguridad de la plataforma al arrancar el servicio.
struct Report {
    bool secure_boot_enabled;
    bool tpm_present;
    bool kernel_debugger_attached;
    bool test_signing_enabled;
    bool hvci_enabled;
    bool driver_signature_enforced;
    bool virtualization_based_security;

    nlohmann::json to_json() const;
    bool acceptable() const;
};

Report collect();

} // namespace acsim::trust
