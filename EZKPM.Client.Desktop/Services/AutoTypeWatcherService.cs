using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Desktop.Services
{
    public static class AutoTypeWatcherService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("psapi.dll")]
        private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;

        private static Timer _watcherTimer;
        private static IntPtr _lastPromptedHwnd = IntPtr.Zero;
        private static Func<List<VaultAssetPayload>> _getAssetsFunc;
        private static Action<List<VaultAssetPayload>, IntPtr> _onMatchAction;

        public static void StartWatching(Func<List<VaultAssetPayload>> getAssetsFunc, Action<List<VaultAssetPayload>, IntPtr> onMatchAction)
        {
            _getAssetsFunc = getAssetsFunc;
            _onMatchAction = onMatchAction;
            
            if (_watcherTimer == null)
            {
                _watcherTimer = new Timer(OnTimerTick, null, 1000, 1000);
            }
        }

        public static void StopWatching()
        {
            _watcherTimer?.Dispose();
            _watcherTimer = null;
        }

        private static void OnTimerTick(object state)
        {
            if (_getAssetsFunc == null || _onMatchAction == null) return;

            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            // Optional: Skip if we already prompted for this exact window instance
            if (hWnd == _lastPromptedHwnd) return;

            var info = GetWindowInfo(hWnd);
            if (info == null || string.IsNullOrWhiteSpace(info.Title)) return;

            var assets = _getAssetsFunc();
            if (assets == null || assets.Count == 0) return;

            var matches = new List<VaultAssetPayload>();

            foreach (var asset in assets)
            {
                if (asset.AutoType == null) continue;
                
                bool matchTitle = false;
                bool matchProcess = false;
                bool matchClass = false;
                
                bool hasConditions = false;

                if (!string.IsNullOrWhiteSpace(asset.AutoType.TargetWindowTitle))
                {
                    hasConditions = true;
                    // Simple contains match. Could be regex in future.
                    if (info.Title.IndexOf(asset.AutoType.TargetWindowTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                        matchTitle = true;
                }

                if (!string.IsNullOrWhiteSpace(asset.AutoType.TargetProcessName))
                {
                    hasConditions = true;
                    if (info.ProcessName.Equals(asset.AutoType.TargetProcessName, StringComparison.OrdinalIgnoreCase))
                        matchProcess = true;
                }

                if (!string.IsNullOrWhiteSpace(asset.AutoType.TargetWindowClass))
                {
                    hasConditions = true;
                    if (info.ClassName.Equals(asset.AutoType.TargetWindowClass, StringComparison.OrdinalIgnoreCase))
                        matchClass = true;
                }

                // If ANY condition is specified and ALL specified conditions match
                if (hasConditions)
                {
                    bool isMatch = true;
                    if (!string.IsNullOrWhiteSpace(asset.AutoType.TargetWindowTitle) && !matchTitle) isMatch = false;
                    if (!string.IsNullOrWhiteSpace(asset.AutoType.TargetProcessName) && !matchProcess) isMatch = false;
                    if (!string.IsNullOrWhiteSpace(asset.AutoType.TargetWindowClass) && !matchClass) isMatch = false;

                    if (isMatch)
                    {
                        matches.Add(asset);
                    }
                }
            }

            if (matches.Count > 0)
            {
                _lastPromptedHwnd = hWnd;
                Dispatcher.UIThread.InvokeAsync(() => 
                {
                    _onMatchAction(matches, hWnd);
                });
            }
        }

        private static WindowInfo GetWindowInfo(IntPtr hWnd)
        {
            var info = new WindowInfo();

            int length = GetWindowTextLength(hWnd);
            if (length > 0)
            {
                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                info.Title = sb.ToString();
            }

            var classSb = new StringBuilder(256);
            if (GetClassName(hWnd, classSb, classSb.Capacity) > 0)
            {
                info.ClassName = classSb.ToString();
            }

            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId > 0)
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
                if (hProcess != IntPtr.Zero)
                {
                    var processNameSb = new StringBuilder(1024);
                    if (GetModuleFileNameEx(hProcess, IntPtr.Zero, processNameSb, processNameSb.Capacity) > 0)
                    {
                        string fullPath = processNameSb.ToString();
                        info.ProcessName = System.IO.Path.GetFileName(fullPath);
                    }
                    CloseHandle(hProcess);
                }
            }

            return info;
        }
    }
}
