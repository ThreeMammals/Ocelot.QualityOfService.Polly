using Const = Ocelot.QualityOfService.Polly.TimeoutStrategy;

namespace Ocelot.QualityOfService.Polly.UnitTests;

[Collection(nameof(SequentialTests))]
public class TimeoutStrategyTests
{
    [Theory]
    [Trait("PR", "2073")] // https://github.com/ThreeMammals/Ocelot/pull/2073
    [InlineData(0, Const.DefTimeout)] // out of range
    [InlineData(Const.LowTimeout - 1, Const.DefTimeout)] // out of range
    [InlineData(Const.LowTimeout, Const.DefTimeout)] // out of range
    [InlineData(Const.LowTimeout + 1, Const.LowTimeout + 1)] // in range
    [InlineData(Const.DefTimeout, Const.DefTimeout)] // in range
    [InlineData(Const.HighTimeout - 1, Const.HighTimeout - 1)] // in range
    [InlineData(Const.HighTimeout, Const.DefTimeout)] // out of range
    [InlineData(Const.HighTimeout + 1, Const.DefTimeout)] // out of range
    public void DefaultTimeout_Setter_ShouldBeGreaterThan10msAndLessThan24hours(int value, int expected)
    {
        // Arrange, Act
        TimeoutStrategy.DefaultTimeout = value;

        // Assert
        Assert.Equal(expected, TimeoutStrategy.DefaultTimeout);
        TimeoutStrategy.DefaultTimeout = TimeoutStrategy.DefTimeout;
    }
}
