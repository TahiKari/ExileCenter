// ExileCenter archive adapter. GPL-3.0-or-later; see NOTICE.txt.
// Only the decoder is from ooz. The stored-block writer below is independent.
#include <Windows.h>
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
extern "C" int Ooz_Decompress(const uint8_t*, int, uint8_t*, size_t,
    int, int, int, uint8_t*, size_t, void*, void*, void*, size_t, int);
#define EXPORT extern "C" __declspec(dllexport)
static constexpr size_t BlockSize = 0x40000;
static constexpr intptr_t MaxChunk = 32 * 1024 * 1024;

// Keep SEH separate from C++ objects. A rejected native stream returns no bytes.
static int Decode(const uint8_t* input, int length, uint8_t* output, size_t size) {
    __try { return Ooz_Decompress(input, length, output, size, 1, 0, 0, nullptr, 0, nullptr, nullptr, nullptr, 0, 3); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return -1; }
}
EXPORT int ExileCenterArchiveCodecVersion() { return 1; }
EXPORT intptr_t OodleLZ_Decompress(const uint8_t* input, intptr_t length, uint8_t* output, intptr_t size,
    int, int checkCrc, int, uint8_t* dictionary, intptr_t dictionarySize,
    void* callback, void*, void*, intptr_t, int phase) {
    if (!input || !output || length < 2 || length > MaxChunk || size <= 0 || size > MaxChunk ||
        checkCrc || dictionary || dictionarySize || callback || phase != 3) return 0;
    // ooz requires 64 bytes of padding. Never expose the caller's buffer to it.
    auto source = static_cast<uint8_t*>(calloc(static_cast<size_t>(length) + 64, 1));
    auto decoded = static_cast<uint8_t*>(calloc(static_cast<size_t>(size) + 128, 1));
    if (!source || !decoded) { free(source); free(decoded); return 0; }
    memcpy(source, input, static_cast<size_t>(length));
    memset(decoded + size + 64, 0xA5, 64);
    int result = Decode(source, static_cast<int>(length), decoded, static_cast<size_t>(size));
    bool valid = result == size;
    for (int i = 0; i < 64; ++i) valid = valid && decoded[size + 64 + i] == 0xA5;
    if (valid) memcpy(output, decoded, static_cast<size_t>(size));
    free(source); free(decoded);
    return valid ? size : 0;
}
EXPORT intptr_t OodleLZ_GetCompressedBufferSizeNeeded(int, intptr_t size) {
    if (size < 0 || size > MaxChunk) return 0;
    return size + 2 * ((size + BlockSize - 1) / BlockSize) + 64;
}
EXPORT intptr_t OodleLZ_Compress(int, const uint8_t* input, intptr_t size, uint8_t* output,
    int, void* options, void* dictionary, void* matcher, void*, intptr_t) {
    if (!input || !output || size <= 0 || size > MaxChunk || options || dictionary || matcher) return 0;
    size_t read = 0, written = 0;
    while (read < static_cast<size_t>(size)) {
        const size_t count = (std::min)(BlockSize, static_cast<size_t>(size) - read);
        // Restart + uncompressed block, Kraken framing. Each block is standalone.
        output[written++] = 0xCC;
        output[written++] = 0x06;
        memcpy(output + written, input + read, count);
        read += count; written += count;
    }
    return static_cast<intptr_t>(written);
}
// LibBundle3's compatibility entry points; the adapter owns its decoder buffers.
EXPORT int OodleLZDecoder_MemorySizeNeeded(int, intptr_t) { return 1; }
EXPORT intptr_t OodleLZ_GetCompressScratchMemBound(int, int, intptr_t, void*) { return 1; }
