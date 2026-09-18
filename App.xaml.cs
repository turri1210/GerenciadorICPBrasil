using GerenciadorIcpBrasil.Services;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace GerenciadorIcpBrasil
{
    public partial class App : Application
    {
        private Window? window;
        public static Window? MainWindow { get; private set; }
        private static readonly string LogDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gerenciador ICP Brasil", "logs");
        private static readonly string LogPath = Path.Combine(LogDir, "app.log");

        private CertificateBridgeService? _certificateBridgeService;
        private bool _isExplicitExitRequested;
        private bool _trayIconCreated;
        private bool _isTrayMenuOpen;
        private bool _isEnsuringLeitorRunning;
        private bool _isRuntimeStopping;
        private bool _isWindowsShutdownPending;
        private IntPtr _trayIconHandle = IntPtr.Zero;
        private IntPtr _originalWndProc = IntPtr.Zero;
        private WndProcDelegate? _wndProcDelegate;
        private DispatcherTimer? _leitorMonitorTimer;

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _singleInstanceShowEvent;
        private CancellationTokenSource? _singleInstanceListenerCts;
        private Task? _singleInstanceListenerTask;
        private bool _ownsSingleInstanceMutex;

        private const int PortToCheck = 8357;
        private const int SwHide = 0;
        private const int SwRestore = 9;
        private static readonly TimeSpan LeitorMonitorInterval = TimeSpan.FromSeconds(15);
        private const string SingleInstanceMutexName = @"Local\GerenciadorIcpBrasil.SingleInstance";
        private const string SingleInstanceShowEventName = @"Local\GerenciadorIcpBrasil.ShowWindow";

        private const int WmApp = 0x8000;
        private const int WmNull = 0x0000;
        private const int WmTrayIcon = WmApp + 501;
        private const int WmLButtonDblClk = 0x0203;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmContextMenu = 0x007B;
        private const int WmQueryEndSession = 0x0011;
        private const int WmEndSession = 0x0016;
        private const int WmUser = 0x0400;
        private const int NinSelect = WmUser;
        private const int NinKeySelect = WmUser + 1;
        private const int GwlWndProc = -4;

        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;
        private const uint NifShowTip = 0x00000080;
        private const uint NimAdd = 0x00000000;
        private const uint NimDelete = 0x00000002;
        private const uint NimSetVersion = 0x00000004;
        private const uint NotifyIconVersion4 = 4;
        private const uint ImageIcon = 1;
        private const uint LrLoadFromFile = 0x00000010;

        private const uint MfString = 0x00000000;
        private const uint MfSeparator = 0x00000800;
        private const uint MfDisabled = 0x00000002;
        private const uint MfGrayed = 0x00000001;

        private const uint TpmRightButton = 0x0002;
        private const uint TpmReturnCmd = 0x0100;
        private const uint WmCommandOpen = 2001;
        private const uint WmCommandPort = 2002;
        private const uint WmCommandVersion = 2003;
        private const uint WmCommandExit = 2005;

        public App()
        {
            InitializeComponent();
            Directory.CreateDirectory(LogDir);
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopRuntimeServices();
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            try
            {
                if (!EnsureSingleInstanceOrActivateExisting())
                {
                    Current.Exit();
                    return;
                }

                UpdateChannel.Initialize(e.Arguments);
                var runInBackground = IsBackgroundStartupRequested(e.Arguments);
                window ??= new Window();
                MainWindow = window;
                window.Title = "Gerenciador ICP Brasil";
                window.Closed -= OnMainWindowClosed;
                window.Closed += OnMainWindowClosed;
                TryApplyBackdrop(window);
                TrySetWindowIcon(window);

                if (window.Content is not Frame rootFrame)
                {
                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    window.Content = rootFrame;
                }

                _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
                window.Activate();
                EnsureTrayIcon();
                StartLeitorCertificadoMonitor();
                if (runInBackground)
                {
                    HideMainWindowToTray();
                }
            }
            catch (Exception ex)
            {
                LogError("OnLaunched", ex);
                throw;
            }
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            var ex = new Exception("Failed to load Page " + e.SourcePageType.FullName);
            LogError("OnNavigationFailed", ex);
            throw ex;
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogError("UnhandledException", e.Exception);
        }

        private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogError("AppDomain.UnhandledException", ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogError("TaskScheduler.UnobservedTaskException", e.Exception);
        }

        private static void TryApplyBackdrop(Window target)
        {
            try
            {
                try
                {
                    target.SystemBackdrop = new MicaBackdrop();
                    return;
                }
                catch
                {
                    // ignore and fallback
                }

                try
                {
                    target.SystemBackdrop = new DesktopAcrylicBackdrop();
                }
                catch
                {
                    // ignore
                }
            }
            catch (Exception ex)
            {
                LogError("Backdrop", ex);
            }
        }

        private static void TrySetWindowIcon(Window target)
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(target);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                LogError("SetIcon", ex);
            }
        }

        private void EnsureTrayIcon()
        {
            if (_trayIconCreated || window == null)
            {
                return;
            }

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                _wndProcDelegate = WindowProc;
                var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
                _originalWndProc = SetWindowLongPtr(hwnd, GwlWndProc, wndProcPtr);

                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
                _trayIconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);

                var data = CreateBaseNotifyIconData(hwnd);
                data.hIcon = _trayIconHandle;
                data.uFlags = NifMessage | NifIcon | NifTip | NifShowTip;

                if (!Shell_NotifyIcon(NimAdd, ref data))
                {
                    throw new InvalidOperationException("Falha ao registrar icone de bandeja.");
                }

                data.uVersion = NotifyIconVersion4;
                _ = Shell_NotifyIcon(NimSetVersion, ref data);
                _trayIconCreated = true;
            }
            catch (Exception ex)
            {
                LogError("EnsureTrayIcon", ex);
            }
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WmQueryEndSession)
                {
                    PrepareForWindowsShutdown();
                    return new IntPtr(1);
                }

                if (msg == WmEndSession && _isWindowsShutdownPending)
                {
                    if (wParam != IntPtr.Zero)
                    {
                        window?.DispatcherQueue.TryEnqueue(Current.Exit);
                    }
                    else
                    {
                        _isWindowsShutdownPending = false;
                        _isExplicitExitRequested = false;
                        _isRuntimeStopping = false;
                        StartLeitorCertificadoMonitor();
                        EnsureTrayIcon();
                    }

                    return IntPtr.Zero;
                }

                if (msg == WmTrayIcon)
                {
                    var eventCode = ResolveTrayEventCode(wParam, lParam);
                    if (eventCode == WmLButtonDblClk || eventCode == WmLButtonUp || eventCode == NinSelect || eventCode == NinKeySelect)
                    {
                        ShowMainWindowFromTray();
                        return IntPtr.Zero;
                    }

                    if (!_isTrayMenuOpen && (eventCode == WmRButtonUp || eventCode == WmContextMenu))
                    {
                        ShowTrayMenu(hWnd);
                        return IntPtr.Zero;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("WindowProc", ex);
            }

            return _originalWndProc != IntPtr.Zero
                ? CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam)
                : DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static int ResolveTrayEventCode(IntPtr wParam, IntPtr lParam)
        {
            var l = unchecked((uint)lParam.ToInt64());
            var lo = (int)(l & 0xFFFF);
            var hi = (int)((l >> 16) & 0xFFFF);
            var wp = unchecked((uint)wParam.ToInt64());
            var wplo = (int)(wp & 0xFFFF);
            var wphi = (int)((wp >> 16) & 0xFFFF);

            // Support both packing variants observed in the shell callback.
            if (IsTrayEventCode(lo))
            {
                return lo;
            }

            if (IsTrayEventCode(hi))
            {
                return hi;
            }

            if (IsTrayEventCode(wplo))
            {
                return wplo;
            }

            if (IsTrayEventCode(wphi))
            {
                return wphi;
            }

            return lo;
        }

        private static bool IsTrayEventCode(int code)
            => code == WmLButtonDblClk
                || code == WmLButtonUp
                || code == WmRButtonDown
                || code == WmRButtonUp
                || code == WmContextMenu
                || code == NinSelect
                || code == NinKeySelect;

        private void ShowTrayMenu(IntPtr hwnd)
        {
            if (_isTrayMenuOpen)
            {
                return;
            }

            _isTrayMenuOpen = true;
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                _isTrayMenuOpen = false;
                return;
            }

            try
            {
                _ = AppendMenu(menu, MfString, WmCommandOpen, "Abrir o Gerenciador ICP Brasil");
                _ = AppendMenu(menu, MfSeparator, 0, null);

                var isRunning = IsPortOpen(PortToCheck);
                if (isRunning)
                {
                    var portText = $"Rodando na porta {PortToCheck}";
                    _ = AppendMenu(menu, MfString | MfDisabled | MfGrayed, WmCommandPort, portText);
                }

                _ = AppendMenu(menu, MfString | MfDisabled | MfGrayed, WmCommandVersion, $"Versao {GetAppVersion()}");
                _ = AppendMenu(menu, MfSeparator, 0, null);
                _ = AppendMenu(menu, MfString, WmCommandExit, "Fechar");

                _ = SetForegroundWindow(hwnd);
                _ = GetCursorPos(out var cursor);
                var command = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, cursor.X, cursor.Y, hwnd, IntPtr.Zero);
                _ = PostMessage(hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);

                switch (command)
                {
                    case WmCommandOpen:
                        ShowMainWindowFromTray();
                        break;
                    case WmCommandExit:
                        ExitFromTray();
                        break;
                }
            }
            finally
            {
                _ = DestroyMenu(menu);
                _isTrayMenuOpen = false;
            }
        }

        private static bool IsPortOpen(int port)
        {
            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync("127.0.0.1", port);
                var completed = Task.WhenAny(connectTask, Task.Delay(400)).GetAwaiter().GetResult();
                return completed == connectTask && tcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static string GetAppVersion()
            => typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        private static bool IsBackgroundStartupRequested(string? launchArguments)
        {
            var args = Environment.GetCommandLineArgs()
                .Concat(ParseRawArguments(launchArguments));

            return args.Any(arg =>
            {
                var normalized = arg.Trim().Trim('"', '\'');
                return string.Equals(normalized, "--background-startup", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "/background-startup", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "-background-startup", StringComparison.OrdinalIgnoreCase);
            });
        }

        private static IEnumerable<string> ParseRawArguments(string? rawArguments)
        {
            if (string.IsNullOrWhiteSpace(rawArguments))
            {
                return Array.Empty<string>();
            }

            return rawArguments
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Trim('"', '\''));
        }

        private void ShowMainWindowFromTray()
        {
            try
            {
                if (window == null)
                {
                    return;
                }

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                _ = ShowWindow(hwnd, SwRestore);
                _ = SetForegroundWindow(hwnd);
                window.Activate();
            }
            catch (Exception ex)
            {
                LogError("ShowMainWindowFromTray", ex);
            }
        }

        private void HideMainWindowToTray()
        {
            try
            {
                if (window == null)
                {
                    return;
                }

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                _ = ShowWindow(hwnd, SwHide);
            }
            catch (Exception ex)
            {
                LogError("HideMainWindowToTray", ex);
            }
        }

        private void ExitFromTray()
        {
            _isExplicitExitRequested = true;
            RemoveTrayIcon();
            StopRuntimeServices();
            Current.Exit();
        }

        private void PrepareForWindowsShutdown()
        {
            if (_isWindowsShutdownPending)
            {
                return;
            }

            _isWindowsShutdownPending = true;
            _isExplicitExitRequested = true;
            StopRuntimeServices();
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            if (_isExplicitExitRequested)
            {
                RemoveTrayIcon();
                StopRuntimeServices();
                return;
            }

            try
            {
                args.Handled = true;
                if (window == null)
                {
                    return;
                }

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                _ = ShowWindow(hwnd, SwHide);
            }
            catch (Exception ex)
            {
                LogError("OnMainWindowClosed", ex);
            }
        }

        private void RemoveTrayIcon()
        {
            try
            {
                if (!_trayIconCreated || window == null)
                {
                    return;
                }

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var data = CreateBaseNotifyIconData(hwnd);
                _ = Shell_NotifyIcon(NimDelete, ref data);
                _trayIconCreated = false;

                if (_trayIconHandle != IntPtr.Zero)
                {
                    _ = DestroyIcon(_trayIconHandle);
                    _trayIconHandle = IntPtr.Zero;
                }

                if (_originalWndProc != IntPtr.Zero)
                {
                    _ = SetWindowLongPtr(hwnd, GwlWndProc, _originalWndProc);
                    _originalWndProc = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                LogError("RemoveTrayIcon", ex);
            }
        }

        private bool EnsureSingleInstanceOrActivateExisting()
        {
            if (_singleInstanceMutex != null)
            {
                return _ownsSingleInstanceMutex;
            }

            try
            {
                _singleInstanceMutex = new Mutex(false, SingleInstanceMutexName);
                try
                {
                    _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0, false);
                }
                catch (AbandonedMutexException)
                {
                    _ownsSingleInstanceMutex = true;
                }
            }
            catch (Exception ex)
            {
                LogError("EnsureSingleInstanceOrActivateExisting", ex);
                return true;
            }

            if (!_ownsSingleInstanceMutex)
            {
                SignalExistingInstanceToShow();
                return false;
            }

            StartSingleInstanceListener();
            return true;
        }

        private void SignalExistingInstanceToShow()
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(SingleInstanceShowEventName);
                _ = existing.Set();
            }
            catch
            {
                // first instance not ready yet or event unavailable
            }
        }

        private void StartSingleInstanceListener()
        {
            if (!_ownsSingleInstanceMutex || _singleInstanceListenerTask != null)
            {
                return;
            }

            _singleInstanceShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceShowEventName);
            _singleInstanceListenerCts = new CancellationTokenSource();
            _singleInstanceListenerTask = Task.Run(() => ListenSingleInstanceSignalsAsync(_singleInstanceListenerCts.Token));
        }

        private async Task ListenSingleInstanceSignalsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_singleInstanceShowEvent != null && _singleInstanceShowEvent.WaitOne(500))
                    {
                        var dispatcher = window?.DispatcherQueue;
                        _ = dispatcher?.TryEnqueue(ShowMainWindowFromTray);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError("ListenSingleInstanceSignalsAsync", ex);
                }

                await Task.Yield();
            }
        }

        private void StartLeitorCertificadoMonitor()
        {
            if (window == null || _leitorMonitorTimer != null || _isRuntimeStopping)
            {
                return;
            }

            _certificateBridgeService ??= new CertificateBridgeService(new BiometriaService());

            _leitorMonitorTimer = new DispatcherTimer();
            _leitorMonitorTimer.Interval = LeitorMonitorInterval;
            _leitorMonitorTimer.Tick += async (_, _) => await EnsureLeitorCertificadoRunningAsync().ConfigureAwait(false);
            _leitorMonitorTimer.Start();

            _ = EnsureLeitorCertificadoRunningAsync();
        }

        private void StopLeitorCertificadoMonitor()
        {
            if (_leitorMonitorTimer == null)
            {
                return;
            }

            _leitorMonitorTimer.Stop();
            _leitorMonitorTimer = null;
        }

        private async Task EnsureLeitorCertificadoRunningAsync()
        {
            if (_isEnsuringLeitorRunning)
            {
                return;
            }

            _isEnsuringLeitorRunning = true;
            try
            {
                if (IsPortOpen(PortToCheck))
                {
                    if (!TryStopLegacyCertificateClient())
                    {
                        return;
                    }

                    await Task.Delay(750).ConfigureAwait(false);
                    if (IsPortOpen(PortToCheck))
                    {
                        return;
                    }
                }

                _certificateBridgeService ??= new CertificateBridgeService(new BiometriaService());
                await _certificateBridgeService.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError("EnsureLeitorCertificadoRunningAsync", ex);
            }
            finally
            {
                _isEnsuringLeitorRunning = false;
            }
        }

        private async Task RestartLeitorCertificadoManagedInstanceAsync()
        {
            if (_isEnsuringLeitorRunning)
            {
                return;
            }

            _isEnsuringLeitorRunning = true;
            try
            {
                _certificateBridgeService ??= new CertificateBridgeService(new BiometriaService());
                await _certificateBridgeService.RestartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError("RestartLeitorCertificadoManagedInstanceAsync", ex);
            }
            finally
            {
                _isEnsuringLeitorRunning = false;
            }
        }

        private static bool TryStopLegacyCertificateClient()
        {
            var stoppedAny = false;
            var allowedRoots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rede ICP Client"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Rede ICP Client"),
            };

            foreach (var process in Process.GetProcessesByName("Rede ICP Client"))
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(executable);
                    var isLegacyInstallation = allowedRoots.Any(root =>
                    {
                        if (string.IsNullOrWhiteSpace(root))
                        {
                            return false;
                        }

                        var normalizedRoot = Path.GetFullPath(root)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
                    });

                    if (isLegacyInstallation)
                    {
                        process.Kill(entireProcessTree: true);
                        stoppedAny = true;
                    }
                }
                catch
                {
                    // Não encerra processos cuja origem não possa ser confirmada.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return stoppedAny;
        }

        private void StopRuntimeServices()
        {
            if (_isRuntimeStopping)
            {
                return;
            }

            _isRuntimeStopping = true;
            StopLeitorCertificadoMonitor();
            try
            {
                _certificateBridgeService?.StopAsync().GetAwaiter().GetResult();
                _certificateBridgeService = null;
            }
            catch (Exception ex)
            {
                LogError("StopRuntimeServices.StopCertificateBridge", ex);
            }

            try
            {
                _singleInstanceListenerCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            _singleInstanceShowEvent?.Dispose();
            _singleInstanceShowEvent = null;
            _singleInstanceListenerCts?.Dispose();
            _singleInstanceListenerCts = null;
            _singleInstanceListenerTask = null;

            try
            {
                if (_ownsSingleInstanceMutex)
                {
                    _singleInstanceMutex?.ReleaseMutex();
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
                _ownsSingleInstanceMutex = false;
            }
        }

        private static NotifyIconData CreateBaseNotifyIconData(IntPtr hwnd)
            => new()
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = hwnd,
                uID = 1,
                uCallbackMessage = WmTrayIcon,
                szTip = "Gerenciador ICP Brasil",
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
            };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;

            public uint uVersion
            {
                readonly get => uTimeoutOrVersion;
                set => uTimeoutOrVersion = value;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private static void LogError(string source, Exception ex)
        {
            try
            {
                var payload = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}\n";
                File.AppendAllText(LogPath, payload);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }
}
