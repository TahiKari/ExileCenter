#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <cmath>
#include <cstring>
#include <vector>
#include <algorithm>
extern "C" float CameraFixture(void*, float);
int checks = 0;
void Check(bool ok, const char* label) { if (!ok) { fprintf(stderr, "FAIL: %s\n", label); exit(1); } ++checks; }
void Clamp(float low, float high) {
    BYTE camera[0x600]{};
    for (float value : {-10.f, 0.f, 0.1f, 0.5f, 1.f, 1.5f, 2.f, 3.f, 9.f, 10.f, 100.f}) {
        float output = CameraFixture(camera, value), stored;
        memcpy(&stored, camera + 0x528, 4);
        Check(output == std::clamp(value, low, high), "camera clamp result");
        Check(stored == output, "camera destination store");
    }
}
void Save(unsigned enabled, float low, float high) {
    struct Config { DWORD magic, size, enabled; float minimum, maximum; } config{0x315A4345,20,enabled,low,high};
    HANDLE file = CreateFileW(L"ExileCenter.Camera.bin.tmp", GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
    DWORD count; Check(file != INVALID_HANDLE_VALUE && WriteFile(file, &config, sizeof(config), &count, nullptr) && count == sizeof(config), "save config");
    CloseHandle(file);
    Check(MoveFileExW(L"ExileCenter.Camera.bin.tmp", L"ExileCenter.Camera.bin", MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE, "atomic settings");
    Sleep(800);
}
int main(int argc, char** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    Check(argc == 4, "arguments");
    wchar_t path[MAX_PATH]; GetSystemDirectoryW(path, MAX_PATH); wcscat_s(path, L"\\kernel32.dll");
    DWORD ignored;
    DWORD size = GetFileVersionInfoSizeW(path, &ignored);
    Check(size > 0, "forward version size");
    std::vector<BYTE> data(size);
    Check(GetFileVersionInfoW(path, 0, size, data.data()) != FALSE, "forward version info");
    VS_FIXEDFILEINFO* info = nullptr; UINT length = 0;
    Check(VerQueryValueW(data.data(), L"\\", reinterpret_cast<void**>(&info), &length) && info && info->dwSignature == 0xFEEF04BD, "forward version query");
    Clamp(static_cast<float>(atof(argv[1])), static_cast<float>(atof(argv[2])));
    if (atoi(argv[3])) {
        Save(1, 0.5f, 3.f); Clamp(0.5f, 3.f);
        Save(1, 2.f, 1.f); Clamp(0.5f, 3.f); // Invalid pair must not replace valid bounds.
        Save(1, NAN, 2.f); Clamp(0.5f, 3.f);
        Save(0, 0.5f, 3.f); Clamp(0.f, 1.f);
        Save(1, 0.f, 2.f); Clamp(0.f, 2.f);
    }
    printf("%d camera/forwarding checks passed\n", checks);
    return 0;
}
