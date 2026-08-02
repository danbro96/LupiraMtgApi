namespace LupiraMtgApi.Dependencies;

/// <summary>Outcome of one edge probe. Unauthorized (downstream rejected our key) is deliberately
/// not Down.</summary>
public enum DependencyStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unauthorized,
    Down,
    Unconfigured,
    NoCredential,
}
