namespace LupiraMtgApi.Collections.Application;

/// <summary>Outcome of a Collections use case that has more than one non-exception result.</summary>
public enum OpOutcome
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>
/// Lightweight per-context result for Collections Application services. The host adapter maps
/// <see cref="Outcome"/> onto HTTP (<c>NotFound</c> → 404, <c>Invalid</c> → 400 with <see cref="Error"/>,
/// <c>Ok</c> → 200 with <see cref="Value"/>). This is intentionally NOT the full OpResult/Caller seam —
/// just enough to express validation/not-found without leaking <c>TypedResults</c> into the context.
/// </summary>
public sealed record Op<T>(OpOutcome Outcome, T? Value, string? Error)
{
    public static Op<T> Ok(T value) => new(OpOutcome.Ok, value, null);

    public static Op<T> NotFound() => new(OpOutcome.NotFound, default, null);

    public static Op<T> Invalid(string error) => new(OpOutcome.Invalid, default, error);

    public static Op<T> Conflict(string error) => new(OpOutcome.Conflict, default, error);
}
