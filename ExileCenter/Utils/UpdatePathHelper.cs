using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameHelper.Utils
{
    // Shared with the standalone PowerShell installer; keep compatible with its C# compiler.
    public static class UpdatePathHelper
    {
        public static bool IsCloudTag(uint tag)
        {
            return (tag & 0xFFFF0FFFu) == 0x9000001Au || tag == 0x80000021u || tag == 0x80000015u;
        }

        public static bool IsAllowedReparsePoint(string path)
        {
            // Read the entry itself, without following links or downloading its contents.
            using (var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                AttributeTagInfo info;
                if (!GetFileInformationByHandleEx(handle, 9, out info, 8))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return (info.Attributes & 0x400u) == 0 || IsCloudTag(info.Tag);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AttributeTagInfo
        {
            public uint Attributes;
            public uint Tag;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass,
            out AttributeTagInfo info, uint size);
    }
}
