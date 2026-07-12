using Application.Services;
using Xunit;

namespace UnitTests;

public sealed class AccountTokenServiceTests
{
    [Fact]
    public void GenerateToken_ProducesDifferentTokens_ForConsecutiveCalls()
    {
        var first = AccountTokenService.GenerateToken();
        var second = AccountTokenService.GenerateToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HashToken_IsDeterministic_ForSameInput()
    {
        var token = AccountTokenService.GenerateToken();

        var hash1 = AccountTokenService.HashToken(token);
        var hash2 = AccountTokenService.HashToken(token);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashToken_ChangesCompletely_WhenOneCharacterDiffers()
    {
        var token = "a-fixed-token-value-for-testing-purposes";
        var mutated = "a-fixed-token-value-for-testing-purposeS";

        var hash1 = AccountTokenService.HashToken(token);
        var hash2 = AccountTokenService.HashToken(mutated);

        Assert.NotEqual(hash1, hash2);
    }
}
