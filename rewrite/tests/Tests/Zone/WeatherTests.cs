using EQClassic.Server.Zone;

namespace EQClassic.Tests.Zone;

/// <summary>Zone::weatherProc: rain or snow comes and goes.</summary>
public class WeatherTests
{
    [Fact]
    public void Rain_comes_and_goes_within_the_legacy_times()
    {
        var zone = new ZoneInstance(new ZoneData("qeytoqrg", [], new Dictionary<int, Grid>()) { Weather = 1 }, seed: 1);
        var changes = new List<(double At, int Weather)>();
        double t = 0;
        for (int i = 0; i < 4 * 3600 * 2; i++) // four hours at 0.5 s
        {
            zone.Tick(0.5f);
            t += 0.5;
            foreach (var e in zone.DrainEvents().OfType<ZoneInstance.WeatherChanged>())
                changes.Add((t, e.Weather));
        }
        Assert.True(changes.Count >= 2);
        Assert.Equal(1, changes[0].Weather);
        Assert.Equal(0, changes[1].Weather);
        Assert.InRange(changes[0].At, 30, 2400);                  // clear for 30 s to 40 min
        Assert.InRange(changes[1].At - changes[0].At, 30, 3600);  // then rain for 30 s to an hour
        Assert.Equal("Raindrops begin to fall from the sky.", ZoneInstance.WeatherMessage(1));
    }

    [Fact]
    public void Zones_without_weather_stay_clear()
    {
        var zone = new ZoneInstance(new ZoneData("befallen", [], new Dictionary<int, Grid>()));
        for (int i = 0; i < 20000; i++)
            zone.Tick(0.5f);
        Assert.Equal(0, zone.Weather);
    }
}
