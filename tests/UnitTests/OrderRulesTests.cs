using Application.Services;
using Xunit;

namespace UnitTests;

public sealed class OrderRulesTests
{
    [Theory]
    [InlineData("aberto", "confirmado", true)]
    [InlineData("concluido", "cancelado", false)]
    [InlineData("cancelado", "confirmado", false)]
    public void CanTransition_ShouldRespectRules(string current, string next, bool expected)
    {
        Assert.Equal(expected, OrderRules.CanTransition(current, next));
    }
}
