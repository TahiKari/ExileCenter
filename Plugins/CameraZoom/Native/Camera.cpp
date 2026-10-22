// ExileCenter camera: startup-only code patch; no disk, shader or graphics edits.
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <cwchar>

namespace {
struct Config { DWORD magic, size, enabled; float minimum, maximum; };
static_assert(sizeof(Config) == 20);
wchar_t configPath[32768]{}, statusPath[32768]{};
volatile LONG64* limits = nullptr;
constexpr BYTE signature[] = { 0xF3,0x0F,0x5F,0xC8,0xF3,0x0F,0x5D,0x0D,
    0,0,0,0,0xF3,0x0F,0x11,0x8E,0x28,0x05,0,0 };

void Status(const char* text) {
    HANDLE file = CreateFileW(statusPath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_DELETE,
        nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written;
    WriteFile(file, text, static_cast<DWORD>(strlen(text)), &written, nullptr);
    CloseHandle(file);
}

bool ReadConfig(Config& config) {
    HANDLE file = CreateFileW(configPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    BYTE bytes[sizeof(Config) + 1]; DWORD read = 0;
    bool ok = ReadFile(file, bytes, sizeof(bytes), &read, nullptr) && read == sizeof(Config);
    CloseHandle(file);
    if (!ok) return false;
    memcpy(&config, bytes, sizeof(config));
    return config.magic == 0x315A4345 && config.size == sizeof(Config) && config.enabled <= 1 &&
        std::isfinite(config.minimum) && std::isfinite(config.maximum) &&
        config.minimum >= 0 && config.maximum <= 10 && config.maximum >= config.minimum + 0.009f;
}

void SetLimits(const Config& config) {
    float values[2] = { config.enabled ? config.minimum : 0.f, config.enabled ? config.maximum : 1.f };
    LONG64 packed;
    memcpy(&packed, values, sizeof(packed));
    InterlockedExchange64(limits, packed); // Atomic pair, including on updates.
}

DWORD WINAPI WatchConfig(void*) {
    // No loader lock, executable writes or game pointers in this worker.
    for (;;) {
        Sleep(500);
        Config config{};
        if (ReadConfig(config)) SetLimits(config);
        // Missing/partial/invalid writes retain the last verified settings.
    }
}

BYTE* FindCamera(BYTE* base) {
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 64 || dos->e_lfanew > 2048) return nullptr;
    auto pe = reinterpret_cast<IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (pe->Signature != IMAGE_NT_SIGNATURE || pe->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        pe->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        !pe->FileHeader.NumberOfSections || pe->FileHeader.NumberOfSections > 96) return nullptr;
    SIZE_T imageSize = pe->OptionalHeader.SizeOfImage;
    auto section = IMAGE_FIRST_SECTION(pe);
    if (reinterpret_cast<BYTE*>(section + pe->FileHeader.NumberOfSections) > base + pe->OptionalHeader.SizeOfHeaders) return nullptr;
    BYTE* found = nullptr;
    for (unsigned s = 0; s < pe->FileHeader.NumberOfSections; ++s) {
        if (!(section[s].Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        SIZE_T offset = section[s].VirtualAddress, size = section[s].Misc.VirtualSize;
        if (offset > imageSize || size > imageSize - offset) return nullptr;
        for (SIZE_T i = 0; size >= sizeof(signature) && i <= size - sizeof(signature); ++i) {
            BYTE* at = base + offset + i;
            if (memcmp(at, signature, 8) || memcmp(at + 12, signature + 12, 8)) continue;
            int displacement; memcpy(&displacement, at + 8, 4);
            intptr_t constantOffset = (at - base) + 12 + static_cast<intptr_t>(displacement);
            if (constantOffset < 0 || static_cast<SIZE_T>(constantOffset) > imageSize - 4) return nullptr;
            float constant; memcpy(&constant, base + constantOffset, 4);
            if (constant != 1.f || found) return nullptr;
            found = at;
        }
    }
    return found;
}

bool Install(BYTE* at, const Config& config) {
    // RX code + RW data. Preserve RAX/flags/XMM0 and read both bounds once.
    SYSTEM_INFO info{}; GetSystemInfo(&info);
    auto memory = static_cast<BYTE*>(VirtualAlloc(nullptr, info.dwPageSize * 2ull,
        MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (!memory) return false;
    limits = reinterpret_cast<volatile LONG64*>(memory + info.dwPageSize);
    SetLimits(config);
    BYTE code[] = {
        0x50,                                      // push rax
        0x48,0x8B,0x05,0,0,0,0,                    // mov rax,[rip+limits]
        0x50,                                      // push rax (min/max)
        0xF3,0x0F,0x5F,0x0C,0x24,                  // maxss xmm1,[rsp]
        0xF3,0x0F,0x5D,0x4C,0x24,0x04,             // minss xmm1,[rsp+4]
        0x48,0x8D,0x64,0x24,0x08,0x58,             // lea rsp,[rsp+8]; pop rax
        0xF3,0x0F,0x11,0x8E,0x28,0x05,0,0,        // original store
        0xFF,0x25,0,0,0,0, 0,0,0,0,0,0,0,0 };    // jmp [rip] -> continuation
    int displacement = static_cast<int>(info.dwPageSize) - 8;
    memcpy(code + 4, &displacement, 4);
    BYTE* continuation = at + sizeof(signature);
    memcpy(code + sizeof(code) - 8, &continuation, 8);
    memcpy(memory, code, sizeof(code));
    DWORD old;
    if (!VirtualProtect(memory, info.dwPageSize, PAGE_EXECUTE_READ, &old) ||
        !FlushInstructionCache(GetCurrentProcess(), memory, sizeof(code))) {
        VirtualFree(memory, 0, MEM_RELEASE); limits = nullptr; return false;
    }
    BYTE replacement[sizeof(signature)]; memset(replacement, 0x90, sizeof(replacement));
    replacement[0] = 0xFF; replacement[1] = 0x25; memset(replacement + 2, 0, 4);
    memcpy(replacement + 6, &memory, 8);
    BYTE original[sizeof(signature)]; memcpy(original, at, sizeof(original));
    if (!VirtualProtect(at, sizeof(replacement), PAGE_EXECUTE_READWRITE, &old)) {
        VirtualFree(memory, 0, MEM_RELEASE); limits = nullptr; return false;
    }
    memcpy(at, replacement, sizeof(replacement));
    bool flushed = FlushInstructionCache(GetCurrentProcess(), at, sizeof(replacement)) != FALSE;
    DWORD ignored;
    bool protectedAgain = VirtualProtect(at, sizeof(replacement), old, &ignored) != FALSE;
    // Never wait here. The worker starts after the process loader releases its lock.
    HANDLE watcher = flushed && protectedAgain ? CreateThread(nullptr, 0, WatchConfig, nullptr, 0, nullptr) : nullptr;
    if (!watcher) {
        DWORD temporary;
        if (VirtualProtect(at, sizeof(original), PAGE_EXECUTE_READWRITE, &temporary)) {
            memcpy(at, original, sizeof(original));
            FlushInstructionCache(GetCurrentProcess(), at, sizeof(original));
            VirtualProtect(at, sizeof(original), old, &temporary);
            VirtualFree(memory, 0, MEM_RELEASE); limits = nullptr;
        }
        return false;
    }
    CloseHandle(watcher);
    return true;
}

void Initialize() {
    wchar_t executable[32768];
    DWORD count = GetModuleFileNameW(nullptr, executable, ARRAYSIZE(executable));
    if (!count || count >= ARRAYSIZE(executable)) return;
    wchar_t* name = wcsrchr(executable, L'\\');
    if (!name) return;
    if (_wcsicmp(name + 1, L"PathOfExileSteam.exe") && _wcsicmp(name + 1, L"PathOfExile.exe") &&
        _wcsicmp(name + 1, L"PathOfExile_x64.exe")) return;
    name[1] = 0;
    if (wcslen(executable) + 32 >= ARRAYSIZE(configPath)) return;
    wcscpy_s(configPath, executable); wcscat_s(configPath, L"ExileCenter.Camera.bin");
    wcscpy_s(statusPath, executable); wcscat_s(statusPath, L"ExileCenter.Camera.status");
    Config config{};
    if (!ReadConfig(config)) { Status("invalid-config"); return; }
    BYTE* at = FindCamera(reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr)));
    if (!at) { Status("incompatible"); return; }
    Status(Install(at, config) ? "active" : "installation-failed");
}
}

extern "C" FARPROC ResolveVersion(unsigned index) {
    static const char* names[] = { "GetFileVersionInfoA", "GetFileVersionInfoByHandle", "GetFileVersionInfoExA",
        "GetFileVersionInfoExW", "GetFileVersionInfoSizeA", "GetFileVersionInfoSizeExA", "GetFileVersionInfoSizeExW",
        "GetFileVersionInfoSizeW", "GetFileVersionInfoW", "VerFindFileA", "VerFindFileW", "VerInstallFileA",
        "VerInstallFileW", "VerLanguageNameA", "VerLanguageNameW", "VerQueryValueA", "VerQueryValueW" };
    // Resolve lazily, outside our DllMain. Always use the original System32 DLL.
    static HMODULE system = [] {
        wchar_t path[MAX_PATH];
        UINT size = GetSystemDirectoryW(path, ARRAYSIZE(path));
        if (!size || size + 13 >= ARRAYSIZE(path)) return static_cast<HMODULE>(nullptr);
        wcscat_s(path, L"\\version.dll");
        return LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    }();
    FARPROC function = system && index < ARRAYSIZE(names) ? GetProcAddress(system, names[index]) : nullptr;
    if (!function) RaiseFailFastException(nullptr, nullptr, 0);
    return function;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID reserved) {
    // A dynamic LoadLibrary never patches live instructions. Only static startup.
    if (reason == DLL_PROCESS_ATTACH && reserved != nullptr) Initialize();
    return TRUE;
}
