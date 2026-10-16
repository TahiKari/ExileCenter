namespace Hiveblood
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;

    internal sealed class HivebloodUiScanner
    {
        private const int UiElementChildrenOffset = 0x10;
        private const int UiElementFlagsOffset = 0x180;
        private const int NameWStringOffset = 0x390;
        private const uint IsVisibleMask = 0x800;
        private const int MaxNodesPerScan = 6000;
        private const int MaxDepth = 22;
        private const int MaxLabelChildrenToMerge = 6;

        private static readonly Regex TreeTotalPattern = new(
            @"hiveblood\s*:\s*([0-9][0-9\s,.\u00A0\u202F\u2009]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex GainPattern = new(
            @"\+\s*([0-9][0-9\s,.\u00A0\u202F\u2009]*)\s*hiveblood",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex DigitsOnly = new(@"[^\d]", RegexOptions.Compiled);

        private readonly Queue<(IntPtr Addr, int Depth)> queue = new();
        private readonly HashSet<IntPtr> visited = new();
        private readonly List<string> textPartsScratch = new(MaxLabelChildrenToMerge + 1);
        private readonly byte[] ptrBuf = new byte[8];
        private readonly byte[] flagsBuf = new byte[4];
        private readonly byte[] vectorBuf = new byte[16];
        private readonly byte[] wstringHeaderBuf = new byte[0x20];
        private byte[] wstringDataBuf = new byte[512];

        private IntPtr processHandle = IntPtr.Zero;
        private int handlePid;

        internal bool TryScan(
            IntPtr gameUiRoot,
            out long? treeTotal,
            List<long> gains)
        {
            treeTotal = null;
            gains.Clear();
            if (!this.EnsureProcess() || gameUiRoot == IntPtr.Zero)
            {
                return false;
            }

            this.queue.Clear();
            this.visited.Clear();
            this.queue.Enqueue((gameUiRoot, 0));

            long bestTreeTotal = -1;
            var nodes = 0;
            while (this.queue.Count > 0 && nodes < MaxNodesPerScan)
            {
                var (addr, depth) = this.queue.Dequeue();
                if (addr == IntPtr.Zero || !this.visited.Add(addr))
                {
                    continue;
                }

                nodes++;
                if (this.IsUiElementVisible(addr))
                {
                    foreach (var text in this.ReadCandidateTexts(addr))
                    {
                        if (text.Length < 8 ||
                            !text.Contains("hiveblood", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var treeMatch = TreeTotalPattern.Match(text);
                        if (treeMatch.Success && TryParseAmount(treeMatch.Groups[1].Value, out var total) && total > bestTreeTotal)
                        {
                            bestTreeTotal = total;
                        }

                        var gainMatch = GainPattern.Match(text);
                        if (gainMatch.Success && TryParseAmount(gainMatch.Groups[1].Value, out var gain) && gain > 0)
                        {
                            gains.Add(gain);
                        }
                    }
                }

                if (depth >= MaxDepth)
                {
                    continue;
                }

                if (!this.TryReadStdVector(addr + UiElementChildrenOffset, out var first, out var last))
                {
                    continue;
                }

                long childCount = ((long)last - (long)first) / 8;
                if (childCount <= 0 || childCount > 500)
                {
                    continue;
                }

                for (int i = 0; i < childCount; i++)
                {
                    var child = this.ReadPtr(first + (nint)(i * 8));
                    if (child != IntPtr.Zero)
                    {
                        this.queue.Enqueue((child, depth + 1));
                    }
                }
            }

            if (bestTreeTotal >= 0)
            {
                treeTotal = bestTreeTotal;
            }

            return treeTotal != null || gains.Count > 0;
        }

        internal void ResetProcess()
        {
            if (this.processHandle != IntPtr.Zero)
            {
                CloseHandle(this.processHandle);
                this.processHandle = IntPtr.Zero;
            }

            this.handlePid = 0;
        }

        private IEnumerable<string> ReadCandidateTexts(IntPtr addr)
        {
            this.textPartsScratch.Clear();
            var direct = this.ReadStdWString(addr + NameWStringOffset);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                yield return direct;
                this.textPartsScratch.Add(direct);
            }

            if (!this.TryReadStdVector(addr + UiElementChildrenOffset, out var first, out var last))
            {
                yield break;
            }

            long childCount = Math.Min(((long)last - (long)first) / 8, MaxLabelChildrenToMerge);
            for (int i = 0; i < childCount; i++)
            {
                var child = this.ReadPtr(first + (nint)(i * 8));
                if (child == IntPtr.Zero)
                {
                    continue;
                }

                var childText = this.ReadStdWString(child + NameWStringOffset);
                if (string.IsNullOrWhiteSpace(childText))
                {
                    continue;
                }

                yield return childText;
                this.textPartsScratch.Add(childText);
            }

            if (this.textPartsScratch.Count > 1)
            {
                yield return string.Concat(this.textPartsScratch);
            }
        }

        private static bool TryParseAmount(string raw, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var digits = DigitsOnly.Replace(raw, string.Empty);
            return digits.Length > 0 && long.TryParse(digits, out value) && value >= 0;
        }

        private bool EnsureProcess()
        {
            int pid = (int)ExileCenter.Core.Process.Pid;
            if (pid == 0)
            {
                this.ResetProcess();
                return false;
            }

            if (pid == this.handlePid && this.processHandle != IntPtr.Zero)
            {
                return true;
            }

            this.ResetProcess();
            this.processHandle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, pid);
            if (this.processHandle == IntPtr.Zero)
            {
                return false;
            }

            this.handlePid = pid;
            return true;
        }

        private IntPtr ReadPtr(IntPtr addr)
        {
            if (addr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (!ReadProcessMemory(this.processHandle, addr, this.ptrBuf, (uint)this.ptrBuf.Length, out _))
            {
                return IntPtr.Zero;
            }

            return (IntPtr)BitConverter.ToInt64(this.ptrBuf, 0);
        }

        private bool IsUiElementVisible(IntPtr addr) =>
            this.TryReadFlags(addr, out var flags) && (flags & IsVisibleMask) != 0;

        private bool TryReadFlags(IntPtr addr, out uint flags)
        {
            flags = 0;
            if (addr == IntPtr.Zero)
            {
                return false;
            }

            if (!ReadProcessMemory(this.processHandle, addr + UiElementFlagsOffset, this.flagsBuf, (uint)this.flagsBuf.Length, out _))
            {
                return false;
            }

            flags = BitConverter.ToUInt32(this.flagsBuf, 0);
            return true;
        }

        private bool TryReadStdVector(IntPtr addr, out IntPtr first, out IntPtr last)
        {
            first = IntPtr.Zero;
            last = IntPtr.Zero;
            if (!ReadProcessMemory(this.processHandle, addr, this.vectorBuf, (uint)this.vectorBuf.Length, out _))
            {
                return false;
            }

            first = (IntPtr)BitConverter.ToInt64(this.vectorBuf, 0);
            last = (IntPtr)BitConverter.ToInt64(this.vectorBuf, 8);
            if (first == IntPtr.Zero)
            {
                return false;
            }

            ulong f = (ulong)(long)first;
            if (f < 0x10000 || f > 0x7FFFFFFFFFFFul)
            {
                return false;
            }

            if ((long)last < (long)first)
            {
                return false;
            }

            return true;
        }

        private string ReadStdWString(IntPtr addr)
        {
            if (!ReadProcessMemory(this.processHandle, addr, this.wstringHeaderBuf, (uint)this.wstringHeaderBuf.Length, out _))
            {
                return string.Empty;
            }

            int len = BitConverter.ToInt32(this.wstringHeaderBuf, 0x10);
            if (len <= 0 || len > 256)
            {
                return string.Empty;
            }

            int cap = BitConverter.ToInt32(this.wstringHeaderBuf, 0x18);
            if (cap < len)
            {
                return string.Empty;
            }

            if (cap < 8)
            {
                int byteLen = Math.Min(len * 2, 16);
                return Encoding.Unicode.GetString(this.wstringHeaderBuf, 0, byteLen);
            }

            long ptr = BitConverter.ToInt64(this.wstringHeaderBuf, 0);
            if (ptr < 0x10000 || ptr > 0x7FFFFFFFFFFF)
            {
                return string.Empty;
            }

            int outLen = len * 2;
            if (this.wstringDataBuf.Length < outLen)
            {
                this.wstringDataBuf = new byte[outLen];
            }

            if (!ReadProcessMemory(this.processHandle, (IntPtr)ptr, this.wstringDataBuf, (uint)outLen, out _))
            {
                return string.Empty;
            }

            return Encoding.Unicode.GetString(this.wstringDataBuf, 0, outLen);
        }

        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryInformation = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            uint dwSize,
            out int lpNumberOfBytesRead);
    }
}
