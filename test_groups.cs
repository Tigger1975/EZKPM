using System;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

class Program
{
    static string HashSid(string sid)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(sid)));
    }

    static void Main()
    {
        var identity = WindowsIdentity.GetCurrent();
        Console.WriteLine($"User: {identity.Name}");
        Console.WriteLine($"SID: {identity.User.Value}");
        Console.WriteLine($"Hashed SID: {HashSid(identity.User.Value)}");

        Console.WriteLine("\nGroups:");
        if (identity.Groups != null)
        {
            foreach (var group in identity.Groups)
            {
                string name = "";
                try
                {
                    name = group.Translate(typeof(NTAccount)).Value;
                }
                catch { }
                Console.WriteLine($"{group.Value} ({name}) -> {HashSid(group.Value)}");
            }
        }
    }
}
