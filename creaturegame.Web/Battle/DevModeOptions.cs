namespace creaturegame.Web.Battle;

/// <summary>
/// The server-side authority for Dev Mode (docs/TODO.md — Dev Mode). Every dev endpoint re-checks
/// <see cref="Enabled"/>, so a client-side toggle can never unlock anything the server hasn't allowed.
/// </summary>
public sealed class DevModeOptions(bool enabled)
{
    public bool Enabled { get; } = enabled;

    /// <summary>An explicit <c>DevMode:Enabled</c> config value (e.g. the <c>DevMode__Enabled</c> env var) wins
    /// either way; absent, Dev Mode follows the hosting environment — on under <c>dev.ps1</c>
    /// (<c>ASPNETCORE_ENVIRONMENT=Development</c>), off in Production.</summary>
    public static DevModeOptions Resolve(bool? configured, bool isDevelopment) =>
        new(configured ?? isDevelopment);
}
