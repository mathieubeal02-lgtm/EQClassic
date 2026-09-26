using System;

namespace EQClassic.Shared.World
{
    /// <summary>
    /// Norrath's clock (World/Source/TimeOfDay.cpp): an hour lasts 180 real seconds, so a day 72
    /// minutes; day from 7:00 to 21:00. Hours run 1-24 as the legacy client shows them (0 is 24).
    /// Built from a reading (hour and minute at a real time) and advanced from there.
    /// </summary>
    public sealed class EqClock
    {
        public const double SecondsPerHour = 180;
        public const int DayStart = 7, NightStart = 21;

        private readonly double _hoursAtStart; // 0..24
        private readonly double _startSeconds;

        public EqClock(int hour, int minute, double nowSeconds)
        {
            _hoursAtStart = ((hour % 24) + minute / 60.0 + 24) % 24;
            _startSeconds = nowSeconds;
        }

        /// <summary>Hours since midnight, 0 to 24 (fractional).</summary>
        public double HoursAt(double nowSeconds) => (_hoursAtStart + (nowSeconds - _startSeconds) / SecondsPerHour) % 24;

        public (int Hour, int Minute) TimeAt(double nowSeconds)
        {
            double h = HoursAt(nowSeconds);
            int hour = (int)h, minute = (int)((h - hour) * 60);
            return (hour == 0 ? 24 : hour, minute);
        }

        /// <summary>Fraction of the day, 0 at midnight (the sky animations run over the day).</summary>
        public float DayFraction(double nowSeconds) => (float)(HoursAt(nowSeconds) / 24);

        public bool IsDaytime(double nowSeconds)
        {
            double h = HoursAt(nowSeconds);
            return h >= DayStart && h < NightStart;
        }

        /// <summary>
        /// Light of the day, 0 (night) to 1 (day), with an hour of dawn from 6:00 and an hour of dusk
        /// from 20:00, for the ambient colour.
        /// </summary>
        public float Daylight(double nowSeconds)
        {
            double h = HoursAt(nowSeconds);
            if (h >= DayStart && h < NightStart - 1) return 1f;
            if (h >= DayStart - 1 && h < DayStart) return (float)(h - (DayStart - 1));
            if (h >= NightStart - 1 && h < NightStart) return (float)(NightStart - h);
            return 0f;
        }
    }
}
