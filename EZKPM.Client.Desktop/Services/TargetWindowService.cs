using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EZKPM.Client.Desktop.Services
{
    public static class TargetWindowService
    {
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(System.Drawing.Point p);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("psapi.dll")]
        public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;

        public static WindowInfo GetWindowInfoAtCursor()
        {
            if (GetCursorPos(out var point))
            {
                IntPtr hWnd = WindowFromPoint(point);
                if (hWnd != IntPtr.Zero)
                {
                    // Some controls return their own hWnd, we might want the root window?
                    // WindowFromPoint returns the exact window/control. To get the main window, we can loop GetParent, but for Auto-Type, the exact control or its parent is often fine.
                    // For now, let's just get the info of the returned hWnd.

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
            return null;
        }
    }

    public class WindowInfo
    {
        public string Title { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string ProcessName { get; set; } = "";
    }
}
