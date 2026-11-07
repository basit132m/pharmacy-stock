using System.Security.Cryptography;
using System.Text;

namespace PharmacyStock.Providers.Seasoft;

/// <summary>
/// Keeps the saved database password out of plain sight.
///
/// On Windows the value is encrypted with the logged-in user's own key, so it cannot be
/// read by another user of the PC or by copying the file to a different machine. On any
/// other system there is no equivalent, so the value is only obscured - which is why the
/// marker in front of it records which was used.
/// </summary>
public static class Secrets
{
    private const string Encrypted = "dpapi:";
    private const string Obscured = "plain:";

    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var bytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
                return Encrypted + Convert.ToBase64String(bytes);
            }
            catch
            {
                // Fall through to the weaker form rather than losing the password.
            }
        }

        return Obscured + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";

        try
        {
            if (stored.StartsWith(Encrypted, StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows()) return "";
                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored[Encrypted.Length..]), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }

            if (stored.StartsWith(Obscured, StringComparison.Ordinal))
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[Obscured.Length..]));
        }
        catch
        {
            // A password saved by a different Windows user cannot be read back.
            return "";
        }

        return stored;   // written before this was introduced
    }
}
