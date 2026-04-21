using Application.Follows.Queries;
using Application.Validators;
using FluentAssertions;

namespace test;

public sealed class FollowQueryValidatorTests
{
    [Fact]
    public void GetFollowersValidator_Should_Fail_WhenCursorPairIncomplete()
    {
        var validator = new GetFollowersQueryValidator();

        var result = validator.Validate(new GetFollowersQuery(Guid.NewGuid(), DateTime.UtcNow, null, 20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.ErrorMessage == "cursorCreatedAt and cursorId must be provided together.");
    }

    [Fact]
    public void GetFollowersValidator_Should_Fail_WhenUserIdEmpty()
    {
        var validator = new GetFollowersQueryValidator();

        var result = validator.Validate(new GetFollowersQuery(Guid.Empty, null, null, 20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "UserId");
    }

    [Fact]
    public void GetFollowingValidator_Should_Fail_WhenCursorPairIncomplete()
    {
        var validator = new GetFollowingQueryValidator();

        var result = validator.Validate(new GetFollowingQuery(Guid.NewGuid(), null, Guid.NewGuid(), 20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.ErrorMessage == "cursorCreatedAt and cursorId must be provided together.");
    }

    [Fact]
    public void GetFollowingValidator_Should_Pass_WhenCursorPairComplete()
    {
        var validator = new GetFollowingQueryValidator();
        var cursorCreatedAt = DateTime.UtcNow;
        var cursorId = Guid.NewGuid();

        var result = validator.Validate(new GetFollowingQuery(Guid.NewGuid(), cursorCreatedAt, cursorId, 20));

        result.IsValid.Should().BeTrue();
    }
}
