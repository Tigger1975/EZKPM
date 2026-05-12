using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EZKPM.Client.Desktop.Services
{
    public static class WindowsAuthService
    {
        [DllImport("credui.dll", CharSet = CharSet.Unicode)]
        private static extern uint CredUIPromptForCredentials(
            ref CREDUI_INFO uiInfo,
            string targetName,
            IntPtr reserved1,
            uint iError,
            System.Text.StringBuilder userName,
            uint maxUserName,
            System.Text.StringBuilder password,
            uint maxPassword,
            ref bool save,
            uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDUI_INFO
        {
            public int cbSize;
            public IntPtr hwndParent;
            public string pszMessageText;
            public string pszCaptionText;
            public IntPtr hbmBanner;
        }

        private const uint CREDUI_FLAGS_GENERIC_CREDENTIALS = 0x1;
        private const uint CREDUI_FLAGS_ALWAYS_SHOW_UI = 0x2;
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_CANCELLED = 1223;

        public static bool PromptForCredentials(string message)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false; // Mock for non-windows - should fail close
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

                bool save = false;

                // Pre-fill with the currently logged-in user
                var currentUser = WindowsIdentity.GetCurrent().Name;
                var userName = new System.Text.StringBuilder(currentUser, 256);
                var password = new System.Text.StringBuilder(256);

                Program.LogDebug($"Triggering CredUI prompt. Parent HWND: {parentHandle}");

                uint result = CredUIPromptForCredentials(
                    ref uiInfo,
                    "EZKPM",
                    IntPtr.Zero,
                    0,
                    userName,
                    256,
                    password,
                    256,
                    ref save,
                    CREDUI_FLAGS_GENERIC_CREDENTIALS | CREDUI_FLAGS_ALWAYS_SHOW_UI);

                if (result == ERROR_SUCCESS)
                {
                    string userStr = userName.ToString();
                    string pwdStr = password.ToString();
                    
                    // Wipe password from StringBuilder
                    password.Clear();

                    string resolvedDomain = Environment.MachineName;
                    string userOnly = userStr;

                    if (userStr.Contains("\\"))
                    {
                        var parts = userStr.Split('\\');
                        resolvedDomain = parts[0];
                        userOnly = parts[1];
                    }
                    else if (userStr.Contains("@"))
                    {
                        var parts = userStr.Split('@');
                        resolvedDomain = parts[1];
                        userOnly = parts[0];
                    }

                    var contextType = string.Equals(resolvedDomain, Environment.MachineName, StringComparison.OrdinalIgnoreCase) 
                        ? System.DirectoryServices.AccountManagement.ContextType.Machine 
                        : System.DirectoryServices.AccountManagement.ContextType.Domain;

                    using var context = new System.DirectoryServices.AccountManagement.PrincipalContext(contextType, resolvedDomain);

                    bool isValid = context.ValidateCredentials(userOnly, pwdStr);
                    
                    if (!isValid)
                    {
                        Program.LogDebug($"Credential validation failed! Incorrect password for user {resolvedDomain}\\{userOnly}.");
                        return false;
                    }
                    
                    return true;
                }

                if (result == ERROR_CANCELLED) 
                {
                    Program.LogDebug("Windows Auth cancelled by user.");
                    return false;
                }

                Program.LogDebug($"CredUI failed technically with error code: {result}. Failing closed for security.");
                return false;
            }
            catch (Exception ex)
            {
                Program.LogDebug($"CredUI Exception: {ex.Message}. Failing closed for security.");
                return false;
            }
        }
    }
}
