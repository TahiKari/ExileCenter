// <copyright file="GameOverlay.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Drawing;
    using Size = System.Drawing.Size;
    using Point = System.Drawing.Point;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using ClickableTransparentOverlay;
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Utils;
    using ImGuiNET;
    using Plugin;
    using Settings;
    using Ui;
    using Vortice.Direct3D11;
    using Vortice.DXGI;
    using Vortice.Mathematics;
    using L = GameHelper.Localization.OverlayLocalization;

    /// <inheritdoc />
    public sealed class GameOverlay : Overlay
    {
        private SettingsDisplay? settingsDisplay;
        private volatile bool closeRequested;
        internal string? SettingsDisplayError { get; private set; }
        internal void ResetSettingsDisplay() => this.settingsDisplay?.ResetPosition();
        public override void Close() => this.closeRequested = true;
        /// <summary>
        ///     Initializes a new instance of the <see cref="GameOverlay" /> class.
        /// </summary>
        internal GameOverlay(string windowTitle)
            : base(windowTitle, true, 3840, 2160)
        {
            CoroutineHandler.Start(this.UpdateOverlayBounds(), priority: int.MaxValue);
            SettingsWindow.InitializeCoroutines();
            PerformanceStats.InitializeCoroutines();
            DataVisualization.InitializeCoroutines();
            GameUiExplorer.InitializeCoroutines();
            ElementFinder.InitializeCoroutines();
            PerformanceProfiler.InitializeCoroutines();
            MemoryReadDiagnostics.InitializeCoroutines();
            OffsetHelper.InitializeCoroutines();
            OverlayKiller.InitializeCoroutines();
            NearbyVisualization.InitializeCoroutines();
            KrangledPassiveDetector.InitializeCoroutines();
        }

        /// <summary>
        ///     Gets the fonts loaded in the overlay.
        /// </summary>
        public ImFontPtr[]? Fonts { get; private set; }

        /// <inheritdoc />
        public override async Task Run()
        {
            Core.Initialize();
            Core.InitializeCororutines();
            this.VSync = Core.GHSettings.Vsync;
            await base.Run();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Core.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        protected override Task PostInitialized()
        {
            Ui.ImGuiTheme.Apply();

            UniversalFont.ApplyFromSettings();

            PManager.InitializePlugins();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Render()
        {
            PerformanceProfiler.StartFrame();

            try { CoroutineHandler.Tick(ImGui.GetIO().DeltaTime); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.Tick] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.PerFrameDataUpdate); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.PerFrameDataUpdate] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.PostPerFrameDataUpdate); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.PostPerFrameDataUpdate] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.OnRender); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.OnRender] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.OnPostRender); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.OnPostRender] {ex}"); }

            LocalUpdates.Render();

            this.RenderSettingsDisplay();

            if (this.closeRequested || !Core.GHSettings.IsOverlayRunning)
            {
                this.settingsDisplay?.Dispose();
                this.settingsDisplay = null;
                base.Close();
            }
        }

        private void RenderSettingsDisplay()
        {
            try
            {
                if (!Core.GHSettings.SettingsOnSeparateWindow)
                {
                    this.settingsDisplay?.Dispose();
                    this.settingsDisplay = null;
                    return;
                }
                if (this.settingsDisplay == null && SettingsWindow.IsVisible)
                {
                    this.settingsDisplay = new SettingsDisplay(this, () => SettingsWindow.DrawWindow(true), SettingsWindow.HideSettingsWindow);
                    this.SettingsDisplayError = null;
                }
                this.settingsDisplay?.Render(SettingsWindow.IsVisible);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsDisplay] {ex}");
                this.SettingsDisplayError = ex.Message;
                this.settingsDisplay?.Dispose();
                this.settingsDisplay = null;
                Core.GHSettings.SettingsOnSeparateWindow = false;
                CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
            }
        }

        private IEnumerator<Wait> UpdateOverlayBounds()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnMoved);
                this.Position = Core.Process.WindowArea.Location;
                this.Size = Core.Process.WindowArea.Size -
                    (Core.GHSettings.FixTaskbarNotShowing ?
                        new System.Drawing.Size(0, 1) :
                        System.Drawing.Size.Empty);
            }
        }
    }

    // CTO 11.1.0 exposes one overlay window. Reuse its renderer/device on the SAME render
    // thread for a second swap chain: no competing ImGui threads or duplicate plugin runtime.
    // The adapter resolves the three backend fields once, before creating any native window.
    public sealed class SettingsDisplay : IDisposable
    {
        private readonly string className = "GameHelper.Settings." + Guid.NewGuid().ToString("N");
        private WNDCLASSEX windowClass;
        private Win32Window? window;
        private readonly ID3D11Device device;
        private readonly ID3D11DeviceContext deviceContext;
        private readonly Action renderDrawData;
        private readonly Action draw;
        private readonly Action hide;
        private readonly IntPtr gameWindow;
        private readonly State placement;
        private readonly Func<string>? title;
        private readonly Action<Rectangle>? savePlacement;
        private readonly Size minimumSize;
        private readonly Size initialSize;
        private string WindowTitle => this.title?.Invoke() ?? L.T("settings.window.compact_title", "GameHelper · Settings") + " · " + LocalUpdates.InstalledVersion;
        private Func<bool>? updateInput;
        private Func<WindowMessage, UIntPtr, IntPtr, bool>? processInput;
        private IDXGISwapChain? swapChain;
        private ID3D11RenderTargetView? renderView;
        private IntPtr context;
        private bool visible;
        private bool minimized;
        private bool registered;

        internal SettingsDisplay(Overlay overlay, Action draw, Action hide)
            : this(overlay, draw, hide, Core.GHSettings, null, null, new Size(840, 640), new Size(1100, 800)) { }

        public SettingsDisplay(Overlay overlay, Action draw, Action hide, State placement,
            Func<string>? title, Action<Rectangle>? savePlacement, Size minimumSize, Size initialSize)
        {
            this.placement = placement;
            this.title = title;
            this.savePlacement = savePlacement;
            this.minimumSize = new Size(Math.Max(160, minimumSize.Width), Math.Max(100, minimumSize.Height));
            this.initialSize = initialSize;
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            this.device = (ID3D11Device)(typeof(Overlay).GetField("device", fields)?.GetValue(overlay) ?? throw new NotSupportedException("CTO device unavailable"));
            this.deviceContext = (ID3D11DeviceContext)(typeof(Overlay).GetField("deviceContext", fields)?.GetValue(overlay) ?? throw new NotSupportedException("CTO device context unavailable"));
            var renderer = typeof(Overlay).GetField("renderer", fields)?.GetValue(overlay) ?? throw new NotSupportedException("CTO renderer unavailable");
            this.renderDrawData = renderer.GetType().GetMethod("Render")!.CreateDelegate<Action>(renderer);
            this.draw = draw;
            this.hide = hide;
            this.gameWindow = overlay.window.Handle;
            var previous = ImGui.GetCurrentContext();
            try
            {
                // Share the atlas and the renderer's registered textures, including plugin images.
                this.context = ImGui.CreateContext(ImGui.GetIO().Fonts);
                ImGui.SetCurrentContext(this.context);
                unsafe { ImGui.GetIO().NativePtr->IniFilename = null; }
                ImGui.GetIO().ConfigFlags = ImGuiConfigFlags.NavEnableKeyboard;
                ImGui.GetIO().BackendFlags = ImGuiBackendFlags.RendererHasVtxOffset;
                ImGuiTheme.Apply();
                this.windowClass = new WNDCLASSEX
                {
                    Size = Marshal.SizeOf<WNDCLASSEX>(),
                    WindowProc = this.WindowProcedure,
                    InstanceHandle = Native.GetModuleHandle(null),
                    CursorHandle = Native.LoadCursor(IntPtr.Zero, (IntPtr)32512),
                    ClassName = this.className,
                    MenuName = string.Empty,
                };
                if (Native.RegisterClassEx(ref this.windowClass) == 0) throw new System.ComponentModel.Win32Exception();
                this.registered = true;
                var bounds = InitialBounds(this.placement, overlay.window.Handle, this.minimumSize, this.initialSize);
                this.window = new Win32Window(this.className, bounds.Width, bounds.Height, bounds.X, bounds.Y,
                    this.WindowTitle, (WindowStyles)0x00CF0000, (WindowExStyles)0x00040000);
                if (this.window.Handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                var inputType = typeof(Overlay).Assembly.GetType("ClickableTransparentOverlay.ImGuiInputHandler", true)!;
                var input = Activator.CreateInstance(inputType, this.window.Handle)!;
                this.updateInput = inputType.GetMethod("Update")!.CreateDelegate<Func<bool>>(input);
                this.processInput = inputType.GetMethod("ProcessMessage")!.CreateDelegate<Func<WindowMessage, UIntPtr, IntPtr, bool>>(input);
                Native.GetClientRect(this.window.Handle, out var client);
                using var dxgiDevice = this.device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetParent<IDXGIAdapter>();
                using var factory = adapter.GetParent<IDXGIFactory>();
                this.swapChain = factory.CreateSwapChain(this.device, new SwapChainDescription
                {
                    BufferCount = 1,
                    BufferDescription = new ModeDescription(Math.Max(1, client.Width), Math.Max(1, client.Height), Format.R8G8B8A8_UNorm),
                    Windowed = true,
                    OutputWindow = this.window.Handle,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.Discard,
                    BufferUsage = Usage.RenderTargetOutput,
                });
                factory.MakeWindowAssociation(this.window.Handle, WindowAssociationFlags.IgnoreAll);
                this.Resize(client.Width, client.Height);
            }
            catch { this.Dispose(); throw; }
            finally { ImGui.SetCurrentContext(previous); }
        }

        public void Render(bool show)
        {
            if (this.window == null) return;
            // CTO pumps one message per frame. Drain a bounded batch so this extra window
            // does not introduce a backlog of mouse, keyboard or resize messages.
            for (var i = 0; i < 32; i++) this.window.PumpEvents();
            if (show != this.visible)
            {
                if (!show) this.SaveBounds();
                Native.ShowWindow(this.window.Handle, show ? 4 : 0); // Show without stealing game focus.
                this.visible = show;
            }
            if (!show || this.minimized) return;
            var previous = ImGui.GetCurrentContext();
            var mainIo = ImGui.GetIO();
            try
            {
                ImGui.SetCurrentContext(this.context);
                Native.SetWindowText(this.window.Handle, this.WindowTitle);
                Native.GetClientRect(this.window.Handle, out var client);
                var io = ImGui.GetIO();
                io.DisplaySize = new System.Numerics.Vector2(client.Width, client.Height);
                io.DeltaTime = Math.Max(0.001f, mainIo.DeltaTime);
                unsafe { io.NativePtr->FontDefault = mainIo.NativePtr->FontDefault; }
                this.updateInput!();
                ImGui.NewFrame();
                try { using var theme = ImGuiTheme.UsePluginTheme(ImGuiTheme.ActivePluginTheme); this.draw(); }
                finally { ImGui.Render(); }
                this.deviceContext.OMSetRenderTargets(this.renderView!);
                this.deviceContext.ClearRenderTargetView(this.renderView!, new Color4(0.075f, 0.085f, 0.105f, 1f));
                this.renderDrawData();
                this.swapChain!.Present(0, PresentFlags.None);
            }
            finally { ImGui.SetCurrentContext(previous); }
        }

        public void ResetPosition()
        {
            if (this.window == null) return;
            var bounds = InitialBounds(this.placement, this.gameWindow, this.minimumSize, this.initialSize);
            Native.SetWindowPos(this.window.Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x14);
            this.SaveBounds();
        }

        private void Resize(int width, int height)
        {
            if (this.swapChain == null || width <= 0 || height <= 0) return;
            this.deviceContext.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>());
            this.renderView?.Dispose();
            this.swapChain.ResizeBuffers(1, width, height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);
            using var buffer = this.swapChain.GetBuffer<ID3D11Texture2D>(0);
            this.renderView = this.device.CreateRenderTargetView(buffer);
        }

        private IntPtr WindowProcedure(IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam)
        {
            // Native callbacks must never allow exceptions to escape into user32.
            try
            {
                if (message == 0x0010) { this.SaveBounds(); this.hide(); return IntPtr.Zero; } // Close hides; F11 opens again.
                if (message == 0x0005)
                {
                    this.minimized = wParam.ToUInt64() == 1;
                    if (!this.minimized) this.Resize((int)((long)lParam & 0xffff), (int)(((long)lParam >> 16) & 0xffff));
                }
                if (message == 0x0232) this.SaveBounds(); // End of native move/resize.
                if (message == 0x0024)
                {
                    var limits = Marshal.PtrToStructure<Native.MinMaxInfo>(lParam);
                    limits.MinTrackSize = new Native.Point { X = this.minimumSize.Width, Y = this.minimumSize.Height };
                    Marshal.StructureToPtr(limits, lParam, false);
                    return IntPtr.Zero;
                }
                if (this.processInput != null)
                {
                    var previous = ImGui.GetCurrentContext();
                    try
                    {
                        ImGui.SetCurrentContext(this.context);
                        if (this.processInput((WindowMessage)message, wParam, lParam)) return IntPtr.Zero;
                    }
                    finally { ImGui.SetCurrentContext(previous); }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[SettingsDisplay.Message] {ex}"); }
            return Native.DefWindowProc(handle, message, wParam, lParam);
        }

        private void SaveBounds()
        {
            if (this.window == null || this.minimized || !Native.GetWindowRect(this.window.Handle, out var bounds)) return;
            var s = this.placement;
            s.SettingsWindowX = bounds.Left;
            s.SettingsWindowY = bounds.Top;
            s.SettingsWindowWidth = bounds.Width;
            s.SettingsWindowHeight = bounds.Height;
            s.SettingsWindowPositionSaved = true;
            if (this.savePlacement != null) this.savePlacement(bounds.Rectangle);
            else CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
        }

        internal static Rectangle FitBounds(Rectangle desired, Rectangle workArea)
            => FitDisplayBounds(desired, workArea, new Size(840, 640));

        private static Rectangle FitDisplayBounds(Rectangle desired, Rectangle workArea, Size minimum)
        {
            var width = Math.Clamp(desired.Width, Math.Min(minimum.Width, workArea.Width), workArea.Width);
            var height = Math.Clamp(desired.Height, Math.Min(minimum.Height, workArea.Height), workArea.Height);
            return new Rectangle(Math.Clamp(desired.X, workArea.Left, workArea.Right - width),
                Math.Clamp(desired.Y, workArea.Top, workArea.Bottom - height), width, height);
        }

        private static Rectangle InitialBounds(State s, IntPtr gameWindow, Size minimum, Size initial)
        {
            var monitors = new List<(IntPtr Handle, Rectangle Work)>();
            Native.MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref Native.Rect rect, IntPtr data) =>
            {
                var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
                if (Native.GetMonitorInfo(monitor, ref info)) monitors.Add((monitor, info.Work.Rectangle));
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            var gameMonitor = Native.MonitorFromWindow(gameWindow, 2);
            var chosen = monitors.Find(m => m.Handle != gameMonitor);
            if (chosen.Handle == IntPtr.Zero) chosen = monitors.Find(m => m.Handle == gameMonitor);
            if (chosen.Handle == IntPtr.Zero) return new Rectangle(new Point(40, 40), initial);
            if (s.SettingsWindowPositionSaved)
            {
                var desired = new Rectangle(s.SettingsWindowX, s.SettingsWindowY, Math.Clamp(s.SettingsWindowWidth, minimum.Width, 8192), Math.Clamp(s.SettingsWindowHeight, minimum.Height, 8192));
                var savedMonitor = monitors.Find(m => m.Work.Contains(desired.Location));
                if (savedMonitor.Handle != IntPtr.Zero) chosen = savedMonitor;
                return FitDisplayBounds(desired, chosen.Work, minimum);
            }
            return FitDisplayBounds(new Rectangle(new Point(chosen.Work.X + 40, chosen.Work.Y + 40), initial), chosen.Work, minimum);
        }

        public void Dispose()
        {
            this.SaveBounds();
            this.processInput = null;
            this.deviceContext.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>());
            this.renderView?.Dispose();
            this.renderView = null;
            this.swapChain?.Dispose();
            this.swapChain = null;
            this.window?.Dispose();
            this.window = null;
            if (this.context != IntPtr.Zero)
            {
                ImGui.DestroyContext(this.context);
                this.context = IntPtr.Zero;
            }
            if (this.registered) Native.UnregisterClass(this.className, this.windowClass.InstanceHandle);
            this.registered = false;
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
            [StructLayout(LayoutKind.Sequential)] internal struct Rect
            {
                internal int Left, Top, Right, Bottom;
                internal int Width => Right - Left;
                internal int Height => Bottom - Top;
                internal Rectangle Rectangle => new(Left, Top, Width, Height);
            }
            [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
            [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo { internal Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
            internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool UnregisterClass(string name, IntPtr instance);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
            [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
            [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out Rect rect);
            [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
            [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
            [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetWindowText(IntPtr window, string text);
        }
    }
}
