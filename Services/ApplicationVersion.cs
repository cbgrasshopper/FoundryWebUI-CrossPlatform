using System.Reflection;

namespace FoundryWebUI.Services;

/// <summary>
/// Provides the application version derived from the assembly's informational version attribute (MinVer).
/// </summary>
public sealed class ApplicationVersion
{
    public string Display { get; } =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "dev";
}
