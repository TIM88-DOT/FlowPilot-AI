using FlowPilot.Shared;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests for the Result and Result&lt;T&gt; discriminated union types.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void Success_IsSuccess_True()
    {
        Result result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsFailure_True()
    {
        Error error = Error.Validation("Test.Code", "Something went wrong");

        Result result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Test.Code", result.Error.Code);
        Assert.Equal("Something went wrong", result.Error.Description);
    }

    [Fact]
    public void GenericSuccess_ReturnsValue()
    {
        Result<int> result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_ValueAccess_Throws()
    {
        Result<int> result = Result.Failure<int>(Error.Validation("Err", "fail"));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_CreatesSuccess()
    {
        Result<string> result = "hello";

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Error_NotFound_FormatsCorrectly()
    {
        Guid id = Guid.NewGuid();
        Error error = Error.NotFound("Customer", id);

        Assert.Equal("Customer.NotFound", error.Code);
        Assert.Contains(id.ToString(), error.Description);
    }

    [Fact]
    public void Error_Conflict_FormatsCorrectly()
    {
        Error error = Error.Conflict("Phone.Taken", "Phone already exists");

        Assert.Equal("Phone.Taken", error.Code);
        Assert.Equal("Phone already exists", error.Description);
    }

    [Fact]
    public void Error_Unauthorized_DefaultMessage()
    {
        Error error = Error.Unauthorized();

        Assert.Equal("Auth.Unauthorized", error.Code);
        Assert.Equal("Unauthorized.", error.Description);
    }

    [Fact]
    public void Error_Forbidden_DefaultMessage()
    {
        Error error = Error.Forbidden();

        Assert.Equal("Auth.Forbidden", error.Code);
        Assert.Equal("Forbidden.", error.Description);
    }

    [Fact]
    public void Error_NullValue_HasCorrectCode()
    {
        Assert.Equal("Error.NullValue", Error.NullValue.Code);
    }
}
