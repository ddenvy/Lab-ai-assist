using LabAi.Infrastructure.Security;

namespace LabAi.Tests.Security;

/// <summary>
/// The hasher is the only thing standing between the demo credentials and the accounts that author
/// audit rows, so its edge cases are pinned: random salt, constant-time comparison, and corrupt stored
/// data that must degrade to "wrong password" rather than an exception.
/// </summary>
public sealed class PasswordHasherTests
{
    private static readonly PasswordHasher Hasher = new();

    [Fact]
    public void HashThenVerify_WithTheCorrectPassword_ReturnsTrue()
    {
        var (hash, salt) = Hasher.Hash("S3cret!Chrom");

        Hasher.Verify("S3cret!Chrom", hash, salt).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithAWrongPassword_ReturnsFalse()
    {
        var (hash, salt) = Hasher.Hash("S3cret!Chrom");

        Hasher.Verify("s3cret!chrom", hash, salt).Should().BeFalse();
    }

    [Fact]
    public void HashingTheSamePasswordTwice_ProducesDifferentSaltAndHash()
    {
        var first = Hasher.Hash("repeat");
        var second = Hasher.Hash("repeat");

        first.salt.Should().NotBe(second.salt, "a repeated salt would make identical passwords identical in the database");
        first.hash.Should().NotBe(second.hash);

        // Both stay independently verifiable — the salt is stored per user.
        Hasher.Verify("repeat", first.hash, first.salt).Should().BeTrue();
        Hasher.Verify("repeat", second.hash, second.salt).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithACorruptedStoredHash_ReturnsFalseInsteadOfThrowing()
    {
        var (_, salt) = Hasher.Hash("any");

        Hasher.Verify("any", "not-valid-base64!!", salt).Should().BeFalse();
        Hasher.Verify("any", Convert.ToBase64String(new byte[8]), salt)
            .Should().BeFalse("a hash of the wrong length must not be compared against a truncated buffer");
    }

    [Fact]
    public void Hash_ProducesBase64OfTheDocumentedKeyAndSaltSizes()
    {
        var (hash, salt) = Hasher.Hash("format-check");

        Convert.FromBase64String(hash).Should().HaveCount(32);
        Convert.FromBase64String(salt).Should().HaveCount(16);
    }

    [Fact]
    public void Verify_RoundTripsANonAsciiPassword()
    {
        // Rfc2898DeriveBytes.Pbkdf2(string, ...) encodes UTF-8 on both ends, so non-ASCII
        // characters must survive. An ASCII-only password would leave the UTF-8 path untested.
        var (hash, salt) = Hasher.Hash("Naïve🔬2026");

        Hasher.Verify("Naïve🔬2026", hash, salt).Should().BeTrue();
        Hasher.Verify("Naïve🔬2025", hash, salt).Should().BeFalse();
    }

    [Fact]
    public void Verify_DoesNotAcceptHashAndSaltSwapped()
    {
        var (hash, salt) = Hasher.Hash("order-matters");

        Hasher.Verify("order-matters", salt, hash).Should().BeFalse();
    }
}
