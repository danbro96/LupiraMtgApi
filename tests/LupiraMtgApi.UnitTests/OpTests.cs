using LupiraMtgApi.Collections.Application;
using Xunit;

namespace LupiraMtgApi.UnitTests;

/// <summary>
/// Smoke coverage for the Collections lightweight result type. Mostly here so the test project (and
/// the CI `dotnet test` step) builds against the context libraries and has somewhere to grow.
/// </summary>
public class OpTests
{
    [Fact]
    public void Ok_carries_value_with_ok_outcome()
    {
        var op = Op<string>.Ok("hello");
        Assert.Equal(OpOutcome.Ok, op.Outcome);
        Assert.Equal("hello", op.Value);
        Assert.Null(op.Error);
    }

    [Fact]
    public void Invalid_carries_error_and_no_value()
    {
        var op = Op<string>.Invalid("bad input");
        Assert.Equal(OpOutcome.Invalid, op.Outcome);
        Assert.Null(op.Value);
        Assert.Equal("bad input", op.Error);
    }

    [Fact]
    public void NotFound_and_Conflict_map_to_their_outcomes()
    {
        Assert.Equal(OpOutcome.NotFound, Op<string>.NotFound().Outcome);
        Assert.Equal(OpOutcome.Conflict, Op<string>.Conflict("dupe").Outcome);
    }
}
