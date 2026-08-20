using System.Security.Cryptography;
using System.Text;

namespace C3850GUI.Services;

/// <summary>Windows DPAPI wrapper. Secrets are bound to the current Windows user account.</summary>
public static class Protector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("C3850-GUI:v1");

    public static string Protect(string clear)
    {
        if (string.IsNullOrEmpty(clear)) return "";
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(clear), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedB64)
    {
        if (string.IsNullOrEmpty(protectedB64)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedB64), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; }
    }
}
