using EQClassic.Shared.Characters;

namespace EQClassic.Server.Characters;

/// <summary>
/// Character creation, following the legacy World (ProcessOP_NameApproval + ProcessOP_CharacterCreate,
/// Database::CreateCharacter): name checks, starting items and position from the database, level 1,
/// food and drink with 5 charges, GM flag for status 100+ is left to the zone server.
/// </summary>
public sealed class CharacterCreation
{
    public const string NameRefused = "That name is not available. Please choose another.";
    public const string NameTaken = "That name is already taken. Please choose another.";
    public const string TooManyCharacters = "You cannot create more characters on this account.";
    public const string InvalidChoice = "Invalid character choices.";

    // Trilogy races (1-12 and the Iksar, 128) and classes (1-14).
    private static readonly HashSet<int> Races = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 128];

    private readonly ICharacterStore _characters;
    private readonly ICreationData _data;

    public CharacterCreation(ICharacterStore characters, ICreationData data)
    {
        _characters = characters;
        _data = data;
    }

    /// <summary>Returns null on success, otherwise the message for the player.</summary>
    public string? Create(int worldAccountId, CreateCharacterRequest request)
    {
        // Legacy ReserveName/CreateCharacter: at most 15 characters, letters and digits only.
        var name = request.Name;
        if (name.Length is 0 or > 15 || !name.All(char.IsAsciiLetterOrDigit) || _data.IsNameFiltered(name))
            return NameRefused;
        if (!Races.Contains(request.Race) || request.Class is < 1 or > 14 || request.Gender is < 0 or > 1
            || request.StartZone.Length is 0 or > 14 || !request.StartZone.All(c => char.IsAsciiLetterOrDigit(c)))
            return InvalidChoice;
        var s = request.Stats;
        if (new[] { s.Str, s.Sta, s.Cha, s.Dex, s.Int, s.Agi, s.Wis }.Any(v => v is < 1 or > 255))
            return InvalidChoice;
        if (_characters.ListForAccount(worldAccountId).Count >= CharacterStoreExtensions.MaxCharacters)
            return TooManyCharacters;

        var profile = BuildProfile(request);
        return _characters.TryCreate(worldAccountId, name, profile) ? null : NameTaken;
    }

    public byte[] BuildProfile(CreateCharacterRequest r)
    {
        var p = ProfileTemplate.NewProfile();
        ProfileTemplate.SetName(p, r.Name);
        ProfileTemplate.SetGender(p, r.Gender);
        ProfileTemplate.SetDeity(p, r.Deity);
        ProfileTemplate.SetRace(p, r.Race);
        ProfileTemplate.SetClass(p, r.Class);
        ProfileTemplate.SetLevel(p, 1);
        ProfileTemplate.SetFace(p, r.Face);
        ProfileTemplate.SetStats(p, r.Stats.Str, r.Stats.Sta, r.Stats.Cha, r.Stats.Dex, r.Stats.Int, r.Stats.Agi, r.Stats.Wis);
        ProfileTemplate.SetZone(p, r.StartZone);
        if (_data.StartPosition(r.StartZone, r.Race, r.Class) is var (x, y, z))
            ProfileTemplate.SetPosition(p, x, y, z);

        int slot = ProfileTemplate.FirstGeneralSlot;
        foreach (int item in _data.StartingItems(r.Race, r.Class))
        {
            if (slot >= 30)
                break; // the legacy loop has no bound; the rewrite stops at the last general slot
            ProfileTemplate.SetItem(p, slot++, (ushort)item, 0);
        }
        // Legacy: "cc.invItemProprieties[22].charges=5; [23].charges=5; //we have 5 food / drink".
        ProfileTemplate.SetItem(p, 22, ProfileTemplate.GetItem(p, 22), 5);
        ProfileTemplate.SetItem(p, 23, ProfileTemplate.GetItem(p, 23), 5);
        return p;
    }
}
