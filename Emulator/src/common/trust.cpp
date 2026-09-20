#include "acsim/trust.hpp"
#include "acsim/logger.hpp"

#include <windows.h>
#include <tbs.h>
#include <winreg.h>

// Estructuras de NtQuerySystemInformation que no estan en la SDK publica.
extern "C" {
typedef LONG NTSTATUS;

typedef struct _SYSTEM_KERNEL_DEBUGGER_INFORMATION {
    BOOLEAN DebuggerEnabled;
    BOOLEAN DebuggerNotPresent;
} SYSTEM_KERNEL_DEBUGGER_INFORMATION;

typedef struct _SYSTEM_CODEINTEGRITY_INFORMATION {
    ULONG Length;
    ULONG CodeIntegrityOptions;
} SYSTEM_CODEINTEGRITY_INFORMATION;

NTSTATUS NTAPI NtQuerySystemInformation(
    ULONG SystemInformationClass,
    PVOID SystemInformation,
    ULONG SystemInformationLength,
    PULONG ReturnLength);
}

namespace acsim::trust {

namespace {

constexpr ULONG kSystemKernelDebuggerInformation = 35;
constexpr ULONG kSystemCodeIntegrityInformation  = 103;
constexpr ULONG kCodeIntegrityEnabled            = 0x1;
constexpr ULONG kCodeIntegrityTestSign           = 0x2;

bool read_reg_dword(HKEY root, const wchar_t* subkey, const wchar_t* value, DWORD& out) {
    HKEY key = nullptr;
    if (RegOpenKeyExW(root, subkey, 0, KEY_READ, &key) != ERROR_SUCCESS) return false;
    DWORD type = 0, size = sizeof(DWORD);
    bool ok = RegQueryValueExW(key, value, nullptr, &type,
                               reinterpret_cast<BYTE*>(&out), &size) == ERROR_SUCCESS
              && type == REG_DWORD;
    RegCloseKey(key);
    return ok;
}

bool query_secure_boot() {
    DWORD v = 0;
    return read_reg_dword(HKEY_LOCAL_MACHINE,
        L"SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State",
        L"UEFISecureBootEnabled", v) && v != 0;
}

bool query_hvci() {
    DWORD v = 0;
    return read_reg_dword(HKEY_LOCAL_MACHINE,
        L"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity",
        L"Enabled", v) && v != 0;
}

bool query_tpm_present() {
    TBS_CONTEXT_PARAMS2 params{};
    params.version = TBS_CONTEXT_VERSION_TWO;
    params.includeTpm20 = 1;
    TBS_HCONTEXT ctx = nullptr;
    TBS_RESULT r = Tbsi_Context_Create(reinterpret_cast<PCTBS_CONTEXT_PARAMS>(&params), &ctx);
    if (r != TBS_SUCCESS) return false;
    Tbsip_Context_Close(ctx);
    return true;
}

} // namespace

nlohmann::json Report::to_json() const {
    return {
        {"secure_boot",     secure_boot_enabled},
        {"tpm",             tpm_present},
        {"kernel_debugger", kernel_debugger_attached},
        {"test_signing",    test_signing_enabled},
        {"hvci",            hvci_enabled},
        {"dse",             driver_signature_enforced},
        {"vbs",             virtualization_based_security}
    };
}

bool Report::acceptable() const {
    // Politica estricta del anti-cheat de laboratorio.
    return secure_boot_enabled
        && tpm_present
        && !kernel_debugger_attached
        && !test_signing_enabled
        && driver_signature_enforced;
}

Report collect() {
    Report r{};
    r.secure_boot_enabled = query_secure_boot();
    r.tpm_present         = query_tpm_present();

    SYSTEM_KERNEL_DEBUGGER_INFORMATION dbg{};
    NtQuerySystemInformation(kSystemKernelDebuggerInformation, &dbg, sizeof(dbg), nullptr);
    r.kernel_debugger_attached = dbg.DebuggerEnabled && !dbg.DebuggerNotPresent;

    SYSTEM_CODEINTEGRITY_INFORMATION ci{};
    ci.Length = sizeof(ci);
    NtQuerySystemInformation(kSystemCodeIntegrityInformation, &ci, sizeof(ci), nullptr);
    r.driver_signature_enforced = (ci.CodeIntegrityOptions & kCodeIntegrityEnabled) != 0;
    r.test_signing_enabled      = (ci.CodeIntegrityOptions & kCodeIntegrityTestSign) != 0;

    r.hvci_enabled = query_hvci();
    r.virtualization_based_security = r.hvci_enabled;

    log_info("informe de confianza recogido", r.to_json());
    return r;
}

} // namespace acsim::trust
