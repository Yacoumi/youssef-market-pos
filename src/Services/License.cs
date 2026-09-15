using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MarketPos.Services;

/// <summary>
/// Activation: each computer runs the software only with a key made for that computer.
///
/// <para>
/// Every Windows installation has its own id. From it the app shows a short machine code; the
/// owner of the software turns that code into a key with the key generator, which holds a
/// secret compiled into this build. The key is saved on the computer and checked at every
/// start. Copied to another computer, the software shows a different machine code, and the
/// key from the first one does not open it.
/// </para>
///
/// <para>
/// This stops a copy from simply being handed on or sold. It is not unbreakable — software
/// that runs on someone's computer can be taken apart by a determined expert — but it means a
/// copy does not work without asking for a key.
/// </para>
/// </summary>
public static class License
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O, 1/I

    private static string Secret =>
        typeof(License).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MarketPos.Activation")?.Value ?? string.Empty;

    /// <summary>
    /// Whether this build asks for activation at all. Only builds made with the secret do;
    /// a developer build without it runs as before.
    /// </summary>
    public static bool IsRequired => Secret.Length > 0;

    /// <summary>This computer's code, as the owner reads it out: four groups of four.</summary>
    public static string MachineCode { get; } = BuildMachineCode();

    private static string KeyFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarketPos", "license.key");

    /// <summary>True when this computer has a valid key, or when this build needs none.</summary>
    public static bool IsActivated
    {
        get
        {
            if (!IsRequired) return true;
            try
            {
                return File.Exists(KeyFile) && Matches(File.ReadAllText(KeyFile), MachineCode, Secret);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Checks the key against this computer and keeps it if it is right.</summary>
    public static bool TryActivate(string key)
    {
        if (!IsRequired) return true;
        if (!Matches(key, MachineCode, Secret)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(KeyFile)!);
        File.WriteAllText(KeyFile, Normalize(key));
        return true;
    }

    /// <summary>The key for a machine code. Used by the key generator; needs the secret.</summary>
    public static string KeyFor(string machineCode) => KeyFor(machineCode, Secret);

    private static string KeyFor(string machineCode, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes("MarketPos-key|" + Normalize(machineCode)));
        return Group(Encode(mac, 20), 4);
    }

    private static bool Matches(string key, string machineCode, string secret)
    {
        if (secret.Length == 0) return false;
        var expected = Encoding.ASCII.GetBytes(Normalize(KeyFor(machineCode, secret)));
        var given = Encoding.ASCII.GetBytes(Normalize(key));
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private static string BuildMachineCode()
    {
        string id = Environment.MachineName;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var crypto = Microsoft.Win32.RegistryKey
                    .OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (crypto?.GetValue("MachineGuid") is string guid && guid.Trim().Length > 0) id = guid;
            }
        }
        catch
        {
            // The computer name is the fallback; it is still one code per computer.
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("MarketPos-machine|" + id.Trim().ToUpperInvariant()));
        return Group(Encode(hash, 16), 4);
    }

    /// <summary>Upper case, without spaces or dashes, so a key typed either way is the same key.</summary>
    private static string Normalize(string text) =>
        new string((text ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Encode(byte[] bytes, int length)
    {
        var text = new StringBuilder(length);
        for (var i = 0; text.Length < length; i++)
            text.Append(Alphabet[bytes[i % bytes.Length] % Alphabet.Length]);
        return text.ToString();
    }

    private static string Group(string text, int size) =>
        string.Join("-", Enumerable.Range(0, (text.Length + size - 1) / size)
            .Select(i => text.Substring(i * size, Math.Min(size, text.Length - i * size))));
}
