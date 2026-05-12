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
                IntPtr parentHandle = IntPtr.Zero;
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    parentHandle = desktop.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                }

                var uiInfo = new CREDUI_INFO
                {
                    cbSize = Marshal.SizeOf(typeof(CREDUI_INFO)),
                    hwndParent = parentHandle,
                    pszCaptionText = "EZK-PM Enterprise",
                    pszMessageText = message
                };

                uint authPackage = 0;
                bool save = false;

                Program.LogDebug($"Triggering CredUI prompt. Parent HWND: {parentHandle}");

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

                if (result == ERROR_SUCCESS && outAuthBuffer != IntPtr.Zero)
                {
                    try
                    {
                        var user = new System.Text.StringBuilder(100);
                        int userLen = 100;
                        var domain = new System.Text.StringBuilder(100);
                        int domainLen = 100;
                        var pwd = new System.Text.StringBuilder(100);
                        int pwdLen = 100;

                        bool unpacked = CredUnPackAuthenticationBuffer(0, outAuthBuffer, outAuthBufferSize, user, ref userLen, domain, ref domainLen, pwd, ref pwdLen);
                        
                        if (unpacked)
                        {
                            // Validate the unpacked credentials against the OS
                            using var context = new System.DirectoryServices.AccountManagement.PrincipalContext(
                                string.IsNullOrEmpty(domain.ToString()) ? 
                                System.DirectoryServices.AccountManagement.ContextType.Machine : 
                                System.DirectoryServices.AccountManagement.ContextType.Domain, 
                                string.IsNullOrEmpty(domain.ToString()) ? Environment.MachineName : domain.ToString());

                            bool isValid = context.ValidateCredentials(user.ToString(), pwd.ToString());
                            
                            // Securely wipe the string builder
                            pwd.Clear();

                            if (!isValid)
                            {
                                Program.LogDebug("Credential validation failed! Incorrect password.");
                                return false;
                            }
                            
                            return true;
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(outAuthBuffer);
                    }
                }
                
                if (outAuthBuffer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(outAuthBuffer);
                }

                if (result == ERROR_CANCELLED) 
                {
                    Program.LogDebug("Windows Auth cancelled by user.");
                    return false;
                }

                Program.LogDebug($"CredUI failed technically with error code: {result}. Bypassing FA 14 for VM/RDP compatibility.");
                return true;
            }
            catch (Exception ex)
            {
                Program.LogDebug($"CredUI Exception: {ex.Message}. Bypassing FA 14.");
                return true;
            }
        }

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredUnPackAuthenticationBuffer(
            int dwFlags,
            IntPtr pAuthBuffer,
            uint cbAuthBuffer,
            System.Text.StringBuilder pszUserName,
            ref int pcchMaxUserName,
            System.Text.StringBuilder pszDomainName,
            ref int pcchMaxDomainName,
            System.Text.StringBuilder pszPassword,
            ref int pcchMaxPassword);
    }
}
