namespace EQClassic.Server.Zone;

/// <summary>
/// Zone::weatherProc: a zone that has weather (zone.weather 1 rain, 2 snow) alternates clear skies
/// for 30 s to 40 min and its weather for 30 s to an hour.
/// </summary>
public sealed partial class ZoneInstance
{
    public int WeatherType { get; }
    /// <summary>0 clear, 1 rain, 2 snow.</summary>
    public int Weather { get; private set; }
    private double _nextWeather = double.NaN;

    public sealed record WeatherChanged(int Weather) : ZoneEvent;

    private void AdvanceWeather()
    {
        if (WeatherType == 0)
            return;
        if (double.IsNaN(_nextWeather))
            _nextWeather = _time + 30 + _random.Next(2400 - 30);
        if (_time < _nextWeather)
            return;
        Weather = Weather > 0 ? 0 : WeatherType;
        _nextWeather = _time + 30 + _random.Next((Weather == 0 ? 2400 : 3600) - 30);
        _events.Add(new WeatherChanged(Weather));
    }

    /// <summary>The weather's line as the Trilogy client wrote it.</summary>
    public static string WeatherMessage(int weather) => weather switch
    {
        1 => "Raindrops begin to fall from the sky.",
        2 => "Snowflakes begin to fall from the sky.",
        _ => "The sky clears.",
    };
}
