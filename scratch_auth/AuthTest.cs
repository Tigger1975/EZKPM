using System;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint CredUIPromptForWindowsCredentials(
        ref CREDUI_INFO pUiInfo,
        int authError,
        ref uint authPackage,
        IntPtr inAuthBuffer,
        uint inAuthBufferSize,
        out IntPtr refOutAuthBuffer,
        out uint refOutAuthBufferSize,
        ref bool fSave,
        int flags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredUnPackAuthenticationBuffer(
        int dwFlags,
        IntPtr pAuthBuffer,
        uint cbAuthBuffer,
        StringBuilder pszUserName,
        ref int pcchMaxUserName,
        StringBuilder pszDomainName,
        ref int pcchMaxDomainName,
        StringBuilder pszPassword,
        ref int pcchMaxPassword);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDUI_INFO
    {
        public int cbSize;
        public IntPtr hwndParent;
        public string pszMessageText;
        public string pszCaptionText;
        public IntPtr hbmBanner;
    }

    static void Main()
    {
        var uiInfo = new CREDUI_INFO
        {
            cbSize = Marshal.SizeOf(typeof(CREDUI_INFO)),
            hwndParent = IntPtr.Zero,
            pszCaptionText = "Test",
            pszMessageText = "Please login"
        };

        uint authPackage = 0;
        bool save = false;

        Console.WriteLine("Prompting...");
        uint result = CredUIPromptForWindowsCredentials(
            ref uiInfo, 0, ref authPackage, IntPtr.Zero, 0, 
            out IntPtr outAuthBuffer, out uint outAuthBufferSize, ref save, 0x200);

        if (result == 0 && outAuthBuffer != IntPtr.Zero)
        {
            var user = new StringBuilder(100);
            int userLen = 100;
            var domain = new StringBuilder(100);
            int domainLen = 100;
            var pwd = new StringBuilder(100);
            int pwdLen = 100;

            // 1 = CRED_PACK_PROTECTED_CREDENTIALS
            bool unpacked = CredUnPackAuthenticationBuffer(1, outAuthBuffer, outAuthBufferSize, user, ref userLen, domain, ref domainLen, pwd, ref pwdLen);
            Console.WriteLine($"Unpacked: {unpacked}, User: {user}, Domain: {domain}, Pwd: {(string.IsNullOrEmpty(pwd.ToString()) ? "EMPTY" : "SET")}");
        }
        else
        {
            Console.WriteLine($"Failed or cancelled: {result}");
        }
    }
}
