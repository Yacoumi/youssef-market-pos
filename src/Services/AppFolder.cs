namespace MarketPos.Services;

/// <summary>
/// The one place the software keeps anything: the folder its exe is in.
///
/// marketpos.db, the product photos, settings.json, the activation key and the logs all sit
/// beside POS-Server.exe or POS-Till.exe. Nothing is read from or written to AppData, so what
/// the shop sees comes only from the marketpos.db in that folder — replace that file and the
/// shop is replaced, whatever any other folder on the computer holds.
/// </summary>
public static class AppFolder
{
    /// <summary>The folder of the running exe.</summary>
    public static string Path { get; } = Find();

    /// <summary>A file or folder inside the exe's folder.</summary>
    public static string File(string name) => System.IO.Path.Combine(Path, name);

    private static string Find()
    {
        var exe = Environment.ProcessPath;

        // Run through the dotnet host during development, the process is dotnet.exe; the app's
        // own files are in its base directory instead.
        if (exe is not null &&
            !string.Equals(System.IO.Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return System.IO.Path.GetDirectoryName(exe)!;
        }

        return AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }
}
