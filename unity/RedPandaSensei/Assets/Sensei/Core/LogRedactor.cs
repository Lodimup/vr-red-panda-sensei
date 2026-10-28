#nullable enable
using Sensei.Engine.Trace;

namespace Sensei.App.Core
{
    public static class LogRedactor
    {
        public static string Redact(string text, string? key)
        {
            if (!string.IsNullOrEmpty(key)) text = text.Replace(key, "[redacted]");
            return CredentialPattern.Redact(text);
        }
    }
}
