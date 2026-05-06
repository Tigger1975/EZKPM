using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input.Platform;

namespace EZKPM.Client.Desktop.Services
{
    public class AutoTypeService
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        // Key Codes
        private const ushort VK_TAB = 0x09;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;

        public static async Task PerformAutoType(string pattern, string username, string password, string title, int mode, Avalonia.Input.Platform.IClipboard clipboard, List<EZKPM.Shared.Contracts.CustomField> customFields = null)
        {
            // Modes: 1 = RandomChunks (Paste), 2 = FullBlock (Paste), 3 = Keystrokes
            
            // Give the OS time to restore focus to the previous window after our app minimizes
            await Task.Delay(500);

            pattern = pattern ?? "{USERNAME}{TAB}{PASSWORD}{ENTER}";
            var parts = ParsePattern(pattern, username, password, title, customFields);

            foreach (var part in parts)
            {
                if (part.IsSpecialKey)
                {
                    if (part.Value == "{TAB}") SendKey(VK_TAB);
                    else if (part.Value == "{ENTER}") SendKey(VK_RETURN);
                    else if (part.Value.StartsWith("{DELAY ")) 
                    {
                        if (int.TryParse(part.Value.Substring(7, part.Value.Length - 8), out int delay))
                            await Task.Delay(delay);
                    }
                }
                else
                {
                    await TypeText(part.Value, mode, clipboard);
                }
                await Task.Delay(50); // Small delay between actions
            }
        }

        private static async Task TypeText(string text, int mode, Avalonia.Input.Platform.IClipboard clipboard)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (mode == 2) // Full Block
            {
                await clipboard.SetTextAsync(text);
                await Task.Delay(50);
                SendCtrlV();
            }
            else if (mode == 1) // Random Chunks
            {
                var rand = new Random();
                int idx = 0;
                while (idx < text.Length)
                {
                    int chunkSize = rand.Next(2, 5); // 2 to 4 chars
                    if (idx + chunkSize > text.Length) chunkSize = text.Length - idx;
                    
                    string chunk = text.Substring(idx, chunkSize);
                    await clipboard.SetTextAsync(chunk);
                    await Task.Delay(20);
                    SendCtrlV();
                    await Task.Delay(20);
                    
                    idx += chunkSize;
                }
            }
            else // 3 = Keystrokes (Worst choice)
            {
                foreach (char c in text)
                {
                    SendChar(c);
                    await Task.Delay(5); // Simulate typing speed
                }
            }
        }

        private static void SendKey(ushort vkCode)
        {
            var inputs = new INPUT[2];
            inputs[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vkCode, dwFlags = 0 } } };
            inputs[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vkCode, dwFlags = KEYEVENTF_KEYUP } } };
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void SendCtrlV()
        {
            var inputs = new INPUT[4];
            inputs[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = 0 } } };
            inputs[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = 0 } } };
            inputs[2] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } } };
            inputs[3] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } };
            SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void SendChar(char c)
        {
            var inputs = new INPUT[2];
            inputs[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE } } };
            inputs[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private class PatternPart
        {
            public bool IsSpecialKey { get; set; }
            public string Value { get; set; }
        }

        private static List<PatternPart> ParsePattern(string pattern, string username, string password, string title, List<EZKPM.Shared.Contracts.CustomField> customFields)
        {
            var result = new List<PatternPart>();
            
            // Replaces basic variables. Note: We keep special keys intact.
            string processed = pattern
                .Replace("{USERNAME}", username ?? "")
                .Replace("{PASSWORD}", password ?? "")
                .Replace("{TITLE}", title ?? "");

            if (customFields != null)
            {
                foreach (var cf in customFields)
                {
                    if (!string.IsNullOrWhiteSpace(cf.Name))
                    {
                        processed = processed.Replace("{" + cf.Name + "}", cf.Value ?? "");
                    }
                }
            }

            int i = 0;
            while (i < processed.Length)
            {
                if (processed[i] == '{')
                {
                    int end = processed.IndexOf('}', i);
                    if (end > i)
                    {
                        string token = processed.Substring(i, end - i + 1);
                        if (token == "{TAB}" || token == "{ENTER}" || token.StartsWith("{DELAY "))
                        {
                            result.Add(new PatternPart { IsSpecialKey = true, Value = token });
                            i = end + 1;
                            continue;
                        }
                    }
                }

                // Normal character or unrecognized { token
                int nextSpecial = processed.IndexOf('{', i);
                if (nextSpecial == -1) nextSpecial = processed.Length;

                string text = processed.Substring(i, nextSpecial - i);
                if (!string.IsNullOrEmpty(text))
                {
                    result.Add(new PatternPart { IsSpecialKey = false, Value = text });
                }
                i = nextSpecial;
            }

            return result;
        }
    }
}
