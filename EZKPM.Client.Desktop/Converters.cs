#nullable enable
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace EZKPM.Client.Desktop
{
    public class AssetTypeToIconConverter : IValueConverter
    {
        public static readonly AssetTypeToIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string type)
            {
                return type switch
                {
                    "Login" => "🔑",
                    "Passkey" => "🛡️",
                    "Payment" => "💳",
                    "SecureNote" => "📝",
                    "SSH Key" => "🔐",
                    "SSL Key" => "📜",
                    "API Key" => "⚙️",
                    "Authenticator" => "⏱️",
                    "Folder" => "📁",
                    _ => "📄"
                };
            }
            return "📄";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringTruncateConverter : IValueConverter
    {
        public static readonly StringTruncateConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str && str.Length > 50)
            {
                return str.Substring(0, 47) + "...";
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
