using System.Collections.Generic;
using System.Linq;
using EQClassic.Shared.Characters;

namespace EQClassic.ClientCore
{
    /// <summary>
    /// The creation screen's state, as the Trilogy client ran it: race, then a class that race may be,
    /// a deity, a city (start zone), among the combinations the server offers; statistics start at
    /// race base + class additions and the bonus points are spent one at a time.
    /// </summary>
    public sealed class CharacterBuilder
    {
        public static readonly string[] StatNames = { "STR", "STA", "CHA", "DEX", "INT", "AGI", "WIS" };

        private readonly IReadOnlyList<CreationOption> _options;
        private int[] _start = new int[7];
        private readonly int[] _spent = new int[7];
        private int _points;

        public CharacterBuilder(IReadOnlyList<CreationOption> options)
        {
            _options = options;
            Race = Races.FirstOrDefault();
            PickDefaults();
        }

        public int Race { get; private set; }
        public int Class { get; private set; }
        public int Deity { get; private set; }
        public string Zone { get; private set; } = "";
        public int Gender { get; set; }
        public int Face { get; set; }

        public IReadOnlyList<int> Races => _options.Select(o => o.Race).Distinct().OrderBy(r => r).ToList();
        public IReadOnlyList<int> Classes => _options.Where(o => o.Race == Race).Select(o => o.Class).Distinct().OrderBy(c => c).ToList();
        public IReadOnlyList<int> Deities => _options.Where(o => o.Race == Race && o.Class == Class).Select(o => o.Deity).Distinct().OrderBy(d => d).ToList();
        public IReadOnlyList<string> Zones => _options.Where(o => o.Race == Race && o.Class == Class && o.Deity == Deity)
            .Select(o => o.Zone).Distinct().ToList();

        public void SelectRace(int race)
        {
            Race = race;
            PickDefaults();
        }

        public void SelectClass(int @class)
        {
            Class = @class;
            PickDefaults(keepClass: true);
        }

        public void SelectDeity(int deity)
        {
            Deity = deity;
            Zone = Zones.FirstOrDefault() ?? "";
        }

        public void SelectZone(string zone) => Zone = zone;

        public int Stat(int index) => _start[index] + _spent[index];
        public int PointsLeft => _points - _spent.Sum();

        public void AddPoint(int index)
        {
            if (PointsLeft > 0 && _spent[index] < CreationRules.MaxPointsPerStat)
                _spent[index]++;
        }

        public void RemovePoint(int index)
        {
            if (_spent[index] > 0)
                _spent[index]--;
        }

        public CharacterStats Stats => new CharacterStats(Stat(0), Stat(1), Stat(2), Stat(3), Stat(4), Stat(5), Stat(6));

        /// <summary>Complete when a combination is chosen and every bonus point is spent.</summary>
        public bool Complete => Zone.Length > 0 && PointsLeft == 0;

        public CreateCharacterRequest Request(string name) => new CreateCharacterRequest(name, Race, Class, Gender, Deity, Face, Zone, Stats);

        private void PickDefaults(bool keepClass = false)
        {
            if (!keepClass || !Classes.Contains(Class))
                Class = Classes.FirstOrDefault();
            Deity = Deities.FirstOrDefault();
            Zone = Zones.FirstOrDefault() ?? "";
            for (int i = 0; i < 7; i++)
                _spent[i] = 0;
            if (Race == 0 || Class == 0)
            {
                _start = new int[7];
                _points = 0;
                return;
            }
            var (stats, points) = CreationRules.Starting(Race, Class);
            _start = CreationRules.ToArray(stats);
            _points = points;
        }
    }
}
