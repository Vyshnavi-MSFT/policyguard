// Agent/EnvConfig.cs — Person F
// Loads the repo-root .env file into process environment variables and exposes
// them as an IConfiguration, so PolicyStore and LlmReasoner can read keys locally.
//
// NOTE (handoff to Person E): once Program.cs exists, the host already builds an
// IConfiguration. Person E should call DotNetEnv.Env.TraversePath().Load() at the
// top of Program.cs instead of using this helper. This helper exists so Person F's
// code can run/test standalone before the API host is ready.

using DotNetEnv;
using Microsoft.Extensions.Configuration;

namespace PolicyGuard.Api.Agent;

public static class EnvConfig
{
    /// <summary>
    /// Find and load the nearest .env (searching up from the working directory),
    /// then return an IConfiguration backed by environment variables.
    /// If no .env is found, configuration is empty and callers fall back to mock mode.
    /// </summary>
    public static IConfiguration Load()
    {
        // TraversePath walks up parent directories to find the .env at the repo root.
        Env.TraversePath().Load();

        return new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
    }
}
