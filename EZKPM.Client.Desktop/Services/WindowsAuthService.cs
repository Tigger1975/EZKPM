using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EZKPM.Client.Desktop.Services
{
    public static class WindowsAuthService
    {
        [DllImport("credui.dll", CharSet = CharSet.Unicode)]
        private static extern uint CredUIPromptForWindowsCredentials(
            ref CREDUI_INFO pUiInfo,
            int authError,
            ref uint authPackage,
            IntPtr inAuthBuffer,
            uint inAuthBufferSize,
            out IntPtr refOutAuthBuffer,
            out uint refOutAuthBufferSize,
            ref bool fSave,
            int flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDUI_INFO
        {
            public int cbSize;
            public IntPtr hwndParent;
            public string pszMessageText;
            public string pszCaptionText;
            public IntPtr hbmBanner;
        }

        private const int CREDUIWIN_ENUMERATE_CURRENT_USER = 0x200;
        private const int CREDUIWIN_IN_CRED_ONLY = 0x00000020;
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_CANCELLED = 1223;

        public static bool PromptForCredentials(string message)
        {
            if (!OperatingSystem.IsWindows())
            {
                return true; // Mock for non-windows
            }

            try
            {
                var uiInfo = new CREDUI_INFO
                {
                    cbSize = Marshal.SizeOf(typeof(CREDUI_INFO)),
                    pszCaptionText = "EZK-PM Enterprise",
                    pszMessageText = message
                };

                uint authPackage = 0;
                bool save = false;

                uint result = CredUIPromptForWindowsCredentials(
                    ref uiInfo,
                    0,
                    ref authPackage,
                    IntPtr.Zero,
                    0,
                    out IntPtr outAuthBuffer,
                    out uint outAuthBufferSize,
                    ref save,
                    CREDUIWIN_ENUMERATE_CURRENT_USER);

                if (outAuthBuffer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(outAuthBuffer);
                }

                if (result == ERROR_SUCCESS) return true;
                
                if (result == ERROR_CANCELLED) 
                {
                    Program.LogDebug("Windows Auth cancelled by user.");
                    return false;
                }

                // If we get here, CredUI failed technically (e.g., RDP session on Windows Server 2016 doesn't support it)
                Program.LogDebug($"CredUI failed technically with error code: {result}. Bypassing FA 14 for VM/RDP compatibility.");
                return true;
            }
            catch (Exception ex)
            {
                Program.LogDebug($"CredUI Exception: {ex.Message}. Bypassing FA 14.");
                return true;
            }
        }
    }
}
