using EQClassic.Shared.World;

namespace EQClassic.Tests.World;

public class EqClockTests
{
    [Fact]
    public void An_hour_lasts_three_real_minutes()
    {
        var clock = new EqClock(hour: 6, minute: 30, nowSeconds: 1000);
        Assert.Equal((6, 30), clock.TimeAt(1000));
        Assert.Equal((7, 0), clock.TimeAt(1000 + 90));
        Assert.Equal((24, 0), new EqClock(23, 0, 0).TimeAt(180)); // midnight shows as 24
        Assert.Equal(0.25f, new EqClock(6, 0, 0).DayFraction(0), precision: 4);
    }

    [Fact]
    public void Day_runs_from_seven_to_twenty_one_with_dawn_and_dusk()
    {
        Assert.False(new EqClock(6, 59, 0).IsDaytime(0));
        Assert.True(new EqClock(7, 0, 0).IsDaytime(0));
        Assert.False(new EqClock(21, 0, 0).IsDaytime(0));
        Assert.Equal(0.5f, new EqClock(6, 30, 0).Daylight(0), precision: 3); // dawn
        Assert.Equal(1f, new EqClock(12, 0, 0).Daylight(0));
        Assert.Equal(0.5f, new EqClock(20, 30, 0).Daylight(0), precision: 3); // dusk
        Assert.Equal(0f, new EqClock(2, 0, 0).Daylight(0));
    }
}
