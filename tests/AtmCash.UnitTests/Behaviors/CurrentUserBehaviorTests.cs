using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Application.Behaviors;
using AtmCash.Application.Common;
using AtmCash.Domain.Common;

namespace AtmCash.UnitTests.Behaviors;

public class CurrentUserBehaviorTests
{
    private sealed record StaffCommand : IRequiresUser;

    private sealed record CustomerCommand;

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(string? userId) => UserId = userId;

        public string? UserId { get; }
    }

    private static Task<string> RunAsync(object request, string? userId)
    {
        var behavior = new CurrentUserBehavior<object, string>(new FakeCurrentUser(userId));
        return behavior.Handle(request, () => Task.FromResult("handled"), CancellationToken.None);
    }

    [Theory]
    [InlineData("ensar.gokhan")]
    [InlineData("bob")]
    [InlineData("ops_team-01")]
    public async Task Valid_user_ids_pass(string userId)
    {
        var result = await RunAsync(new StaffCommand(), userId);

        result.Should().Be("handled");
    }

    [Theory]
    [InlineData("Gökhan")]
    [InlineData("şükrü")]
    [InlineData("Ensar Gokhan")]
    [InlineData("ensar;gokhan")]
    public async Task Invalid_user_ids_are_rejected(string userId)
    {
        var act = () => RunAsync(new StaffCommand(), userId);

        (await act.Should().ThrowAsync<BadRequestException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidUser);
    }

    [Fact]
    public async Task Staff_command_without_a_user_is_rejected()
    {
        var act = () => RunAsync(new StaffCommand(), userId: null);

        (await act.Should().ThrowAsync<BadRequestException>()).Which.ErrorCode.Should().Be(ErrorCodes.UserHeaderRequired);
    }

    [Fact]
    public async Task Customer_command_does_not_need_a_user()
    {
        var result = await RunAsync(new CustomerCommand(), userId: null);

        result.Should().Be("handled");
    }
}
