// Agent/DotEnv.cs — Person F
// Tiny, dependency-free loader for a local .env file. The Azure OpenAI keys that PolicyStore
// and LlmReasoner need live in backend/.env (gitignored). .NET's default configuration reads
// environment variables automatically, so we just need to copy .env entries into the process
// environment before the host is built. If no .env is found, the services run in mock mode.

namespace PolicyGuard.Agent;

/// <summary>Loads key=value pairs from a local .env file into environment variables.</summary>
public static class DotEnv
{
    /// <summary>
    /// Locates the nearest .env file (probing the working/base directory and their parents) and
    /// sets any keys that are not already present in the environment. Never throws.
    /// </summary>
    public static void Load()
    {
        try
        {
            var path = LocateEnvFile();
            if (path is null)
            {
                return;
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim().Trim('"', '\'');

                if (key.Length == 0)
                {
                    continue;
                }

                // Do not override values already provided by the real environment.
                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
        catch
        {
            // Best-effort: a missing or malformed .env simply leaves the services in mock mode.
        }
    }

    private static string? LocateEnvFile()
    {
        var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var root in roots)
        {
            var dir = new DirectoryInfo(root);
            for (int depth = 0; depth < 8 && dir is not null; depth++)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
        }
        return null;
    }
}
