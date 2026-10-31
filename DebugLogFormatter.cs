using System.Text.RegularExpressions;

namespace DontBlink.Services;

public static class DebugLogFormatter
{
    public static string Format(IEnumerable<string> entries)
    {
        string text = "Weeping Angel debug log · " + DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss") + Environment.NewLine
            + string.Join(Environment.NewLine, entries);
        text = Regex.Replace(text, @"(?i)(Bearer\s+)[A-Za-z0-9._~+/-]+=*", "$1[redacted]");
        text = Regex.Replace(text, "(?i)(\\\"(?:password|access_token|refresh_token|client_secret|2fa_code)\\\"\\s*:\\s*\\\")[^\\\"]*(\\\")", "$1[redacted]$2");
        return Regex.Replace(text, @"(?i)([?&](?:token|access_token|refresh_token|password)=)[^&\s]+", "$1[redacted]");
    }
}
