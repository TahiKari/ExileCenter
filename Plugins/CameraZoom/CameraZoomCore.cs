namespace CameraZoom
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using ExileCenter;
    using ExileCenter.Plugin;
    using ExileCenter.RemoteEnums;
    using ImGuiNET;

    public sealed class CameraZoomSettings : IPSettings
    {
        public bool Enabled { get; set; }
        public float MinimumZoom { get; set; } = 0f;
        public float MaximumZoom { get; set; } = 3f;
        public float? LastDistance { get; set; }

        internal void Normalize()
        {
            MinimumZoom = Math.Clamp(float.IsFinite(MinimumZoom) ? MinimumZoom : 0f, 0f, 9.99f);
            MaximumZoom = Math.Clamp(float.IsFinite(MaximumZoom) ? MaximumZoom : 3f, MinimumZoom + 0.01f, 10f);
            if (LastDistance is float distance && (!float.IsFinite(distance) || distance < 0f || distance > 10f)) LastDistance = null;
        }
    }

    public sealed class CameraZoomCore : PCore<CameraZoomSettings>
    {
        // Camera clamp signature documented by NattKh/POE2Radar (CheatDefinition.cs).
        // The final store offset was additionally verified in the current game client.
        private static readonly byte?[] Signature = { 0xF3, 0x0F, 0x5F, 0xC8, 0xF3, 0x0F, 0x5D, 0x0D,
            null, null, null, null, 0xF3, 0x0F, 0x11, 0x8E, 0x28, 0x05, 0x00, 0x00 };
        private readonly object gate = new();
        private IntPtr handle, instruction, constant;
        private uint attachedPid, attemptedPid;
        private byte[]? original;
        private bool active, exitSubscribed, limitingStopped;
        private string statusKey = "idle", detail = string.Empty;
        private DateTime nextCheck;
        private string SettingsPath => Path.Combine(DllDirectory, "config", "settings.txt");

        public override void OnEnable(bool isGameOpened)
        {
            try
            {
                if (File.Exists(SettingsPath))
                    Settings = JsonSerializer.Deserialize<CameraZoomSettings>(File.ReadAllText(SettingsPath)) ?? new();
            }
            catch (Exception ex) { Console.WriteLine($"[CameraZoom] Settings: {ex.Message}"); }
            Settings.Normalize();
            attemptedPid = 0;
            statusKey = Settings.Enabled ? "waiting" : "idle";
            detail = string.Empty;
            if (!exitSubscribed) { AppDomain.CurrentDomain.ProcessExit += OnExit; exitSubscribed = true; }
        }

        public override void OnDisable()
        {
            lock (gate)
            {
                if (Settings.LastDistance.HasValue) SaveSettings();
                if (!TryRestore()) return; // Keep the exit recovery handler if restoration failed.
                Close();
                if (exitSubscribed) { AppDomain.CurrentDomain.ProcessExit -= OnExit; exitSubscribed = false; }
            }
        }

        public override void SaveSettings()
        {
            Settings.Normalize();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Console.WriteLine($"[CameraZoom] Settings: {ex.Message}"); }
        }

        public override void DrawSettings()
        {
            Settings.Normalize();
            this.DrawSettingsTabs("CameraZoomSettings",
                ("general", "Settings", () =>
                {
                    ImGui.TextWrapped(PluginText.T("help", "Enable extended zoom, then use the game's mouse wheel. Disable it to restore the original limit."));
                    bool requested = Settings.Enabled || active;
                    if (ImGui.Checkbox(PluginText.Label("enable", "Enable extended camera zoom", "EnableCameraZoom"), ref requested))
                        SetEnabled(requested);
                    ImGui.TextWrapped(PluginText.T("status." + statusKey, statusKey));
                    if (detail.Length > 0) ImGui.TextWrapped(detail);
                }),
                ("limits", "Distances", () =>
                {
                    float minimum = Settings.MinimumZoom * 100f, maximum = Settings.MaximumZoom * 100f;
                    if (ImGui.SliderFloat(PluginText.Label("minimum", "Minimum distance (maximum zoom)", "MinimumCameraZoom"),
                        ref minimum, 0f, (Settings.MaximumZoom - 0.01f) * 100f, "%.0f %%"))
                    {
                        Settings.MinimumZoom = minimum / 100f;
                        SaveSettings();
                    }
                    if (ImGui.SliderFloat(PluginText.Label("maximum", "Maximum distance (maximum zoom-out)", "MaximumCameraZoom"),
                        ref maximum, (Settings.MinimumZoom + 0.01f) * 100f, 1000f, "%.0f %%"))
                    {
                        Settings.MaximumZoom = maximum / 100f;
                        SaveSettings();
                    }
                    ImGui.TextUnformatted(PluginText.T("limits_help", "100% is the original zoom-out limit. Higher values move the camera further away. Changes apply while extended zoom is enabled."));
                    if (ImGui.Button(PluginText.Label("reset", "Restore original distances", "ResetCameraZoomLimits")))
                    {
                        Settings.MinimumZoom = 0f;
                        Settings.MaximumZoom = 1f;
                        SaveSettings();
                    }
                }),
                ("diagnostics", "Diagnostics", () =>
                {
                    ImGui.BeginDisabled(active);
                    if (ImGui.Button(PluginText.Label("detect", "Check game compatibility", "DetectCameraZoom")))
                    {
                        lock (gate)
                        {
                            try { Detect(); }
                            catch (Exception ex) { Fail(ex); Close(); }
                        }
                    }
                    ImGui.EndDisabled();
                    ImGui.TextWrapped(PluginText.T("status." + statusKey, statusKey));
                    if (detail.Length > 0) ImGui.TextWrapped(detail);
                }));
            
        }

        public override void DrawUI()
        {
            if (!Settings.Enabled && handle == IntPtr.Zero) return;
            lock (gate)
            {
                uint pid = Core.Process.Pid;
                if (handle != IntPtr.Zero && pid != attachedPid)
                {
                    if (!TryRestore()) return;
                    Close(); attemptedPid = 0;
                    statusKey = Settings.Enabled ? "waiting" : "idle";
                }
                ResumeIfRequested(pid, pid != 0 && Core.States.GameCurrentState == GameStateTypes.InGameState);
                if (handle == IntPtr.Zero || !active || limitingStopped || Core.States.GameCurrentState != GameStateTypes.InGameState) return;
                try
                {
                    if (DateTime.UtcNow >= nextCheck)
                    {
                        var expected = (byte[])original!.Clone();
                        expected[6] = 0x59;
                        if (!Read(instruction, expected.Length).SequenceEqual(expected) ||
                            !Read(constant, 4).SequenceEqual(BitConverter.GetBytes(1f)))
                            throw new InvalidOperationException(PluginText.T("changed", "The camera code changed. The operation was cancelled."));
                        nextCheck = DateTime.UtcNow.AddSeconds(1);
                    }
                    var state = Core.States.InGameStateObject;
                    LimitCameraZoom(state.Address, state.CurrentWorldInstance.Address);
                }
                catch (Exception ex)
                {
                    limitingStopped = true;
                    string error = ex.Message;
                    if (TryRestore()) { Close(); statusKey = "failed"; detail = error; }
                    else Fail(ex);
                }
            }
        }

        private void SetEnabled(bool requested)
        {
            lock (gate)
            {
                Settings.Enabled = requested;
                SaveSettings();
                attemptedPid = 0;
                if (requested)
                {
                    statusKey = "waiting"; detail = string.Empty;
                    uint pid = Core.Process.Pid;
                    ResumeIfRequested(pid, pid != 0 && Core.States.GameCurrentState == GameStateTypes.InGameState);
                }
                else if (TryRestore()) { Close(); statusKey = "restored"; detail = string.Empty; }
            }
        }

        private void ResumeIfRequested(uint pid, bool inGame)
        {
            if (!Settings.Enabled || active || pid == 0 || pid == attemptedPid || !inGame) return;
            // One attempt per game process. A failed compatibility check or safety
            // stop requires a new process or an explicit off/on request to retry.
            attemptedPid = pid;
            TryActivate();
        }

        private void LimitCameraZoom(IntPtr state, IntPtr camera)
        {
            // Use the current world camera, never a cached pointer from an earlier area.
            // This field is the destination of the signature's verified MOVSS instruction.
            if (state.ToInt64() < 0x10000 || camera.ToInt64() < 0x10000 ||
                BitConverter.ToInt64(Read(state + 0x368, 8), 0) != camera.ToInt64()) return;
            float current = BitConverter.ToSingle(Read(camera + 0x528, 4), 0);
            if (!float.IsFinite(current) || current < 0f)
                throw new InvalidOperationException(PluginText.T("invalid_distance", "Invalid camera distance. Extended zoom has been stopped."));
            Settings.Normalize();
            float limited = Math.Clamp(current, Settings.MinimumZoom, Settings.MaximumZoom);
            if (limited == current) { Settings.LastDistance = current; return; }
            if (BitConverter.ToInt64(Read(state + 0x368, 8), 0) != camera.ToInt64()) return;
            if (!WriteProcessMemory(handle, camera + 0x528, BitConverter.GetBytes(limited), 4, out nuint written) || written != 4)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Settings.LastDistance = limited;
        }

        private void Detect()
        {
            if (active) return;
            Close();
            uint id = Core.Process.Pid;
            if (id == 0) throw new InvalidOperationException(PluginText.T("no_game", "Open the game first."));
            using var process = Process.GetProcessById(checked((int)id));
            var module = process.MainModule ?? throw new InvalidOperationException("Missing game module.");
            handle = OpenProcess(0x1010, false, id); // Read and query only during detection.
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            attachedPid = id;
            long baseAddress = module.BaseAddress.ToInt64();
            var header = Read(module.BaseAddress, 4096);
            int pe = BitConverter.ToInt32(header, 0x3C);
            if (BitConverter.ToUInt16(header, 0) != 0x5A4D || pe < 64 || pe > 2048 ||
                BitConverter.ToUInt32(header, pe) != 0x4550 || BitConverter.ToUInt16(header, pe + 4) != 0x8664)
                throw new InvalidOperationException("Invalid x64 executable header.");
            int sections = BitConverter.ToUInt16(header, pe + 6);
            int table = pe + 24 + BitConverter.ToUInt16(header, pe + 20);
            if (sections < 1 || sections > 96 || table + sections * 40 > header.Length)
                throw new InvalidOperationException("Invalid executable sections.");
            int matches = 0;
            for (int section = 0; section < sections; section++)
            {
                int offset = table + section * 40;
                if ((BitConverter.ToUInt32(header, offset + 36) & 0x20000000) == 0) continue;
                int length = BitConverter.ToInt32(header, offset + 8), rva = BitConverter.ToInt32(header, offset + 12);
                if (length < Signature.Length || rva < 0 || (long)rva + length > module.ModuleMemorySize)
                    throw new InvalidOperationException("Invalid executable section bounds.");
                const int chunk = 65536;
                for (int start = 0; start < length; start += chunk)
                {
                    var bytes = Read(new IntPtr(baseAddress + rva + start), Math.Min(chunk + Signature.Length - 1, length - start));
                    for (int i = 0; i < Math.Min(chunk, bytes.Length - Signature.Length + 1); i++)
                    {
                        if (!Matches(bytes, i)) continue;
                        matches++;
                        instruction = new IntPtr(baseAddress + rva + start + i);
                    }
                }
            }
            if (matches != 1) throw new InvalidOperationException(PluginText.T("incompatible", "The camera signature is missing or ambiguous. No changes were made."));
            original = Read(instruction, Signature.Length);
            constant = new IntPtr(checked(instruction.ToInt64() + 12 + BitConverter.ToInt32(original, 8)));
            if (constant.ToInt64() < baseAddress || constant.ToInt64() + 4 > baseAddress + module.ModuleMemorySize ||
                !Read(constant, 4).SequenceEqual(BitConverter.GetBytes(1f)))
                throw new InvalidOperationException(PluginText.T("incompatible", "The camera signature is missing or ambiguous. No changes were made."));
            statusKey = "ready"; detail = string.Empty;
        }

        private static bool Matches(byte[] bytes, int start)
        {
            if (start < 0 || start > bytes.Length - Signature.Length) return false;
            for (int i = 0; i < Signature.Length; i++)
                if (Signature[i].HasValue && bytes[start + i] != Signature[i]!.Value) return false;
            return true;
        }

        private void TryActivate()
        {
            try
            {
                // Rescan on every activation; cached offsets from a different process are never reused.
                Detect();
                var writer = OpenProcess(0x1038, false, attachedPid);
                if (writer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                CloseHandle(handle); handle = writer;
                WriteOpcode(true);
                limitingStopped = false;
                nextCheck = DateTime.MinValue;
                statusKey = "active"; detail = string.Empty;
            }
            catch (Exception ex)
            {
                Fail(ex);
                if (active) TryRestore();
                if (!active) Close();
            }
        }

        private bool TryRestore()
        {
            if (!active) return true;
            try
            {
                if (!GetExitCodeProcess(handle, out uint code)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (code != 259) { active = false; return true; }
                WriteOpcode(false);
                statusKey = "restored"; detail = string.Empty;
                return true;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }

        private void WriteOpcode(bool enable)
        {
            if (original == null) throw new InvalidOperationException("No verified camera instruction.");
            var expected = (byte[])original.Clone();
            if (active) expected[6] = 0x59;
            if (!Read(instruction, expected.Length).SequenceEqual(expected) ||
                (enable && !Read(constant, 4).SequenceEqual(BitConverter.GetBytes(1f))))
                throw new InvalidOperationException(PluginText.T("changed", "The camera code changed. The operation was cancelled."));
            // MINSS xmm1, [1.0] -> MULSS xmm1, [1.0]. One opcode byte changes;
            // instruction length, operands and the lower zoom clamp remain unchanged.
            var address = instruction + 6;
            if (!VirtualProtectEx(handle, address, 1, 0x40, out uint protection))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var value = new[] { enable ? (byte)0x59 : original[6] };
                if (!WriteProcessMemory(handle, address, value, 1, out nuint written) || written != 1)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                active = enable;
                if (!FlushInstructionCache(handle, address, 1) || Read(address, 1)[0] != value[0])
                    throw new InvalidOperationException("Camera write verification failed.");
            }
            finally
            {
                if (!VirtualProtectEx(handle, address, 1, protection, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private byte[] Read(IntPtr address, int count)
        {
            var bytes = new byte[count];
            if (!ReadProcessMemory(handle, address, bytes, (nuint)count, out nuint read) || read != (nuint)count)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return bytes;
        }

        private void Close()
        {
            if (active) return;
            if (handle != IntPtr.Zero) CloseHandle(handle);
            handle = instruction = constant = IntPtr.Zero;
            attachedPid = 0; original = null;
        }

        private void Fail(Exception ex)
        {
            statusKey = active ? "restore_failed" : "failed";
            detail = ex.Message;
            Console.WriteLine($"[CameraZoom] {ex.Message}");
        }

        private void OnExit(object? sender, EventArgs args)
        {
            lock (gate) { if (Settings.LastDistance.HasValue) SaveSettings(); if (TryRestore()) Close(); }
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] bytes, nuint count, out nuint read);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr handle, IntPtr address, byte[] bytes, nuint count, out nuint written);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtectEx(IntPtr handle, IntPtr address, nuint length, uint protection, out uint originalProtection);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushInstructionCache(IntPtr handle, IntPtr address, nuint length);
    }
}
