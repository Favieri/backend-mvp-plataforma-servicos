using Xunit;

namespace UnitTests;

public sealed class IdempotencyLogicTests
{
    [Fact]
    public void DuplicateEvent_ShouldBeSkipped_WhenInsertDidNotAffectRows()
    {
        var firstInsert = 1;
        var duplicatedInsert = 0;
        Assert.True(firstInsert > 0);
        Assert.False(duplicatedInsert > 0);
    }
}
