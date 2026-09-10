using LabAi.Application.Auth;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Security;
using NSubstitute;

namespace LabAi.Tests.Auth;

/// <summary>
/// Login is what turns an anonymous request into the <c>ActorUserId</c> every audit row is attributed
/// to, so the failure paths matter more than the success path: they must all look identical from the
/// outside while the service still knows exactly why it refused.
/// </summary>
public sealed class AuthServiceTests
{
    private readonly IUserStore userStore = Substitute.For<IUserStore>();
    private readonly PasswordHasher hasher = new();
    private readonly AuthService service;

    public AuthServiceTests()
    {
        service = new AuthService(userStore, hasher);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_Succeeds()
    {
        ArrangeStored(MakeUser());

        var result = await service.LoginAsync("alice", "Good!Pass1");

        result.Succeeded.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task Login_ReturnsTheIdentityThatTheAuditJournalWillAttributeTo()
    {
        ArrangeStored(MakeUser());

        var result = await service.LoginAsync("alice", "Good!Pass1");

        result.UserId.Should().Be(7);
        result.Username.Should().Be("alice");
        result.FullName.Should().Be("Alice Tester");
        result.Role.Should().Be(UserRole.Analyst);
    }

    [Fact]
    public async Task Login_WithAnUnknownUser_Fails()
    {
        ArrangeStored(null);

        var result = await service.LoginAsync("ghost", "whatever");

        result.Succeeded.Should().BeFalse();
        result.UserId.Should().Be(0);
    }

    [Fact]
    public async Task Login_WithAWrongPassword_Fails()
    {
        ArrangeStored(MakeUser());

        var result = await service.LoginAsync("alice", "Bad!Pass9");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Login_WithADisabledAccount_FailsEvenWithTheCorrectPassword()
    {
        ArrangeStored(MakeUser(active: false));

        var result = await service.LoginAsync("alice", "Good!Pass1");

        result.Succeeded.Should().BeFalse("a deactivated operator must not reach the corpus, password or not");
    }

    [Fact]
    public async Task Login_RevealsNothingAboutWhichHalfOfThePairWasWrong()
    {
        var unknown = await WithStoredUser(null, "alice", "x");
        var disabled = await WithStoredUser(MakeUser(active: false), "alice", "Good!Pass1");
        var wrongPassword = await WithStoredUser(MakeUser(), "alice", "Bad!Pass9");

        unknown.Error.Should().Be("Invalid username or password.");
        disabled.Error.Should().Be(unknown.Error);
        wrongPassword.Error.Should().Be(unknown.Error);
    }

    [Fact]
    public async Task EveryFailurePathBurnsExactlyOneVerification_SoTimingCannotEnumerateUsers()
    {
        // The dummy credential is not decoration: without it an unknown username returns in
        // microseconds while a real one costs 100k PBKDF2 iterations, which is a user oracle.
        await AssertOneVerificationOnFailure(stored: null);
        await AssertOneVerificationOnFailure(MakeUser(active: false));
        await AssertOneVerificationOnFailure(MakeUser(active: true));
    }

    [Fact]
    public async Task Login_PropagatesTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var user = MakeUser();
        ArrangeStored(user);

        await service.LoginAsync("alice", "Good!Pass1", cts.Token);

        await userStore.Received(1).FindByUsernameAsync("alice", cts.Token);
    }

    private void ArrangeStored(User? user) =>
        userStore.FindByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

    private async Task<AuthResult> WithStoredUser(User? user, string username, string password)
    {
        ArrangeStored(user);
        return await new AuthService(userStore, hasher).LoginAsync(username, password);
    }

    private static async Task AssertOneVerificationOnFailure(User? stored)
    {
        var countingHasher = Substitute.For<IPasswordHasher>();
        countingHasher.Hash(Arg.Any<string>()).Returns(("dummy-hash", "dummy-salt"));
        countingHasher.Verify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var store = Substitute.For<IUserStore>();
        store.FindByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(stored);

        var result = await new AuthService(store, countingHasher).LoginAsync("alice", "any-password");

        result.Succeeded.Should().BeFalse();
        countingHasher.Received(1).Verify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    private User MakeUser(bool active = true, string password = "Good!Pass1")
    {
        var (hash, salt) = hasher.Hash(password);
        return new User
        {
            Id = 7,
            Username = "alice",
            FullName = "Alice Tester",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Analyst,
            IsActive = active,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
