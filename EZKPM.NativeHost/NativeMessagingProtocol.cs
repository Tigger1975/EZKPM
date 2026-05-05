using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EZKPM.NativeHost
{
    public static class NativeMessagingProtocol
    {
        public static string Read()
        {
            var stdin = Console.OpenStandardInput();
            var lengthBytes = new byte[4];
            int bytesRead = stdin.Read(lengthBytes, 0, 4);

            if (bytesRead == 0)
                return null;

            int length = BitConverter.ToInt32(lengthBytes, 0);

            var buffer = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = stdin.Read(buffer, totalRead, length - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            return Encoding.UTF8.GetString(buffer);
        }

        public static void Write(string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var stdout = Console.OpenStandardOutput();
            
            var lengthBytes = BitConverter.GetBytes(bytes.Length);
            stdout.Write(lengthBytes, 0, 4);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }
    }
}
