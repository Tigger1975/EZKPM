using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;

class Program {
    static async Task Main() {
        IClipboard clipboard = null;
        await clipboard.SetTextAsync("test");
    }
}
