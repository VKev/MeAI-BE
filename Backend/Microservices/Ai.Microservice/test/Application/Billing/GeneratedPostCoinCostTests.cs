using Application.Billing;
using FluentAssertions;

namespace AiMicroservice.Tests.Application.Billing;

public sealed class GeneratedPostCoinCostTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 20)]
    [InlineData(2, 20)]
    [InlineData(3, 20)]
    [InlineData(4, 20)]
    public void Calculate_ShouldChargeFlatRequestCost(int requestedImageCount, decimal expectedCoins)
    {
        var result = GeneratedPostCoinCost.Calculate(requestedImageCount);

        result.Should().Be(expectedCoins);
    }
}
