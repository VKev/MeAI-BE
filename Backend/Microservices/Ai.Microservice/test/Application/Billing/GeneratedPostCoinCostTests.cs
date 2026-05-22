using Application.Billing;
using FluentAssertions;

namespace AiMicroservice.Tests.Application.Billing;

public sealed class GeneratedPostCoinCostTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 100)]
    [InlineData(2, 150)]
    [InlineData(3, 200)]
    [InlineData(4, 250)]
    public void Calculate_ShouldChargeBasePlusExtraPerImageAfterFirst(int requestedImageCount, decimal expectedCoins)
    {
        var result = GeneratedPostCoinCost.Calculate(requestedImageCount);

        result.Should().Be(expectedCoins);
    }
}
