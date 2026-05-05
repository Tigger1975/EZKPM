using System;
using System.Drawing;
using System.Drawing.Imaging;

class Program {
    static void Main() {
        try {
            Bitmap bmp = new Bitmap(@"C:\Users\adm-kh\source\repos\EZKPM\EZKPM.Client.Desktop\Assets\logo.png");
            
            // Create Grayscale
            Bitmap gray = new Bitmap(bmp.Width, bmp.Height);
            for (int y = 0; y < bmp.Height; y++) {
                for (int x = 0; x < bmp.Width; x++) {
                    Color c = bmp.GetPixel(x, y);
                    int g = (int)((c.R * 0.3) + (c.G * 0.59) + (c.B * 0.11));
                    gray.SetPixel(x, y, Color.FromArgb(c.A, g, g, g));
                }
            }
            gray.Save(@"C:\Users\adm-kh\source\repos\EZKPM\EZKPM.BrowserExtension\icons\icon_gray.png", ImageFormat.Png);
            
            // Create Red
            Bitmap red = new Bitmap(bmp.Width, bmp.Height);
            for (int y = 0; y < bmp.Height; y++) {
                for (int x = 0; x < bmp.Width; x++) {
                    Color c = bmp.GetPixel(x, y);
                    int r = Math.Min(255, (int)(c.R * 1.5 + c.B * 0.5));
                    red.SetPixel(x, y, Color.FromArgb(c.A, r, c.G/2, c.B/2));
                }
            }
            red.Save(@"C:\Users\adm-kh\source\repos\EZKPM\EZKPM.BrowserExtension\icons\icon_red.png", ImageFormat.Png);
            
            // Create Green
            Bitmap green = new Bitmap(bmp.Width, bmp.Height);
            for (int y = 0; y < bmp.Height; y++) {
                for (int x = 0; x < bmp.Width; x++) {
                    Color c = bmp.GetPixel(x, y);
                    int g = Math.Min(255, (int)(c.G * 1.5 + c.R * 0.5));
                    green.SetPixel(x, y, Color.FromArgb(c.A, c.R/2, g, c.B/2));
                }
            }
            green.Save(@"C:\Users\adm-kh\source\repos\EZKPM\EZKPM.BrowserExtension\icons\icon_green.png", ImageFormat.Png);
            
            Console.WriteLine("Icons generated!");
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
