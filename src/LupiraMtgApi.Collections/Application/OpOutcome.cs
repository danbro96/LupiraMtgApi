namespace LupiraMtgApi.Collections.Application;

/// <summary>Outcome of a Collections use case that has more than one non-exception result.</summary>
public enum OpOutcome
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
}
