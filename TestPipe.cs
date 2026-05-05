using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

class Program {
    static void Main() {
        try {
            using var pipe = new NamedPipeClientStream(".", "EZKPM_BrowserBridge_Pipe", PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(2000);
            Console.WriteLine("Connected");
            
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            
            writer.WriteLine("{\"Type\":\"REQUEST_AUTOFILL\",\"Url\":\"github.com\"}");
            Console.WriteLine("Sent data");
            
            string response = reader.ReadLine();
            Console.WriteLine("Response: " + response);
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
