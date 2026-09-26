using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Client;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.ClientCore
{
    public enum GameState { Disconnected, Connecting, Login, LoggingIn, ServerSelect, EnteringWorld, CharacterSelect, EnteringZone, InZone }

    /// <summary>
    /// The whole client flow without any engine: login server → world → zone, reconnecting at each
    /// hop and on zone changes. Call <see cref="Update"/> every frame; read <see cref="State"/>,
    /// <see cref="Worlds"/>, <see cref="Characters"/>, <see cref="Zone"/> and <see cref="Player"/>.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private LoginClient? _connection;
        private IMessage? _sendOnConnect;
        private string _sessionId = "";
        private string _characterName = "";
        private GameState _state = GameState.Disconnected;

        public GameClient(string? expectedServerFingerprint = null) => ExpectedServerFingerprint = expectedServerFingerprint;

        public string? ExpectedServerFingerprint { get; }
        public GameState State => _state;
        public string? LastError { get; private set; }
        public IReadOnlyList<WorldServerInfo> Worlds { get; private set; } = Array.Empty<WorldServerInfo>();
        public IReadOnlyList<CharacterSummary> Characters { get; private set; } = Array.Empty<CharacterSummary>();
        public ZoneView? Zone { get; private set; }
        public LocalPlayer? Player { get; private set; }
        public double Now => _clock.Elapsed.TotalSeconds;

        public event Action<GameState>? StateChanged;
        /// <summary>A new zone view (first entry or after a zone change): rebuild the scene.</summary>
        public event Action<ZoneView>? ZoneEntered;
        /// <summary>A line from the server for the chat window.</summary>
        public event Action<string>? MessageReceived;

        /// <summary>Reach of the Use key: the server accepts clicks within 40 units (ZoneInstance.DoorReach).</summary>
        public const float DoorUseReach = 30f;
        /// <summary>Tab targets the nearest NPC within this distance.</summary>
        public const float TargetReach = 100f;
        /// <summary>Reason of the server's move after the player was slain (ZoneInstance.DeathReason).</summary>
        public const string DeathReason = "death";

        public int? TargetId { get; private set; }
        public bool AutoAttacking { get; private set; }
        public int Hp { get; private set; }
        /// <summary>Experience, level and progress through the level (0-1), from the server.</summary>
        public PlayerExperience? Experience { get; private set; }
        /// <summary>The legacy zone header of the current zone (fog, clip, safe point), when the server has it.</summary>
        public ZoneInfo? ZoneInfo { get; private set; }
        public event Action<ZoneInfo>? ZoneInfoReceived;
        /// <summary>Norrath's clock, from the server's time at zone entry (advanced with <see cref="Now"/>).</summary>
        public EqClock? Clock { get; private set; }
        public int MaxHp { get; private set; }
        /// <summary>A melee swing near the player (for animations; the text goes to <see cref="MessageReceived"/>).</summary>
        public event Action<CombatEvent>? CombatReceived;

        /// <summary>
        /// Tab: targets the nearest NPC, or the next one by distance when the current target is
        /// near (repeated Tabs cycle). Clears the target when none is near. Returns the new target.
        /// </summary>
        public ZoneView.EntityView? TargetNearest()
        {
            if (_state != GameState.InZone || Zone == null || Player == null)
                return null;
            var target = Zone.NextNpc(Player.Position, TargetReach, TargetId);
            SetTarget(target?.Id);
            return target;
        }

        /// <summary>Seconds /camp takes (the player must stay put).</summary>
        public const float CampSeconds = 30f;
        private float? _campLeft;
        private Vec3 _campFrom;

        /// <summary>Runs a line typed in the chat box: chat to send, or a slash command.</summary>
        public void ExecuteChat(string line)
        {
            if (_state != GameState.InZone || Player == null || Zone == null)
                return;
            var parsed = Chat.Parse(line);
            switch (parsed.Action)
            {
                case ChatAction.Send:
                    _connection?.Send(new ChatSend(parsed.Channel, parsed.Target, parsed.Text));
                    break;
                case ChatAction.Who:
                    _connection?.Send(new WhoRequest());
                    break;
                case ChatAction.Location:
                    MessageReceived?.Invoke(Chat.Location(Player.Position));
                    break;
                case ChatAction.Sit:
                    _connection?.Send(new SetSitting(true));
                    break;
                case ChatAction.Stand:
                    _connection?.Send(new SetSitting(false));
                    break;
                case ChatAction.Consider:
                    Consider();
                    break;
                case ChatAction.Target:
                    var found = parsed.Target.Length == 0 ? null : Zone.Entities.Where(e => e.Id != Zone.YourEntityId)
                        .FirstOrDefault(e => e.DisplayName.StartsWith(parsed.Target, StringComparison.OrdinalIgnoreCase)
                                          || e.Spawn.Name.StartsWith(parsed.Target, StringComparison.OrdinalIgnoreCase));
                    if (found == null)
                        MessageReceived?.Invoke("Couldn't find anyone by that name.");
                    else
                        SetTarget(found.Id);
                    break;
                case ChatAction.Camp:
                    _connection?.Send(new SetSitting(true));
                    _campLeft = CampSeconds;
                    _campFrom = Player.Position;
                    MessageReceived?.Invoke("It will take you about 30 seconds to prepare your camp.");
                    break;
                case ChatAction.Pet:
                    if (parsed.Target.StartsWith("attack"))
                        _connection?.Send(new PetCommand(PetOrder.Attack));
                    else if (parsed.Target.StartsWith("back"))
                        _connection?.Send(new PetCommand(PetOrder.BackOff));
                    else if (parsed.Target.StartsWith("get lost") || parsed.Target.StartsWith("dismiss"))
                        _connection?.Send(new PetCommand(PetOrder.GetLost));
                    else
                        MessageReceived?.Invoke("Usage: /pet attack | /pet back off | /pet get lost");
                    break;
                case ChatAction.Trade:
                    if (TargetId is int tradeWith && Zone.Get(tradeWith) is { Spawn: { IsPlayer: true } })
                        _connection?.Send(new TradeCommand(TradeAction.Request, tradeWith));
                    else
                        MessageReceived?.Invoke("Target a player to trade with.");
                    break;
                case ChatAction.Help:
                    foreach (var help in Chat.HelpLines)
                        MessageReceived?.Invoke(help);
                    break;
                case ChatAction.Invite:
                    string invitee = parsed.Target.Length > 0 ? parsed.Target
                        : TargetId is int t && Zone.Get(t) is { Spawn: { IsPlayer: true } } targeted ? targeted.Spawn.Name : "";
                    if (invitee.Length == 0)
                        MessageReceived?.Invoke("Invite whom? Target a player or type /invite name.");
                    else
                        _connection?.Send(new GroupCommand(GroupAction.Invite, invitee));
                    break;
                case ChatAction.Follow:
                    _connection?.Send(new GroupCommand(GroupAction.Accept, ""));
                    break;
                case ChatAction.Decline:
                    _connection?.Send(new GroupCommand(GroupAction.Decline, ""));
                    break;
                case ChatAction.Disband:
                    _connection?.Send(new GroupCommand(GroupAction.Leave, ""));
                    break;
                case ChatAction.Ability:
                    UseAbility(parsed.Target switch
                    {
                        "kick" => 30, "bash" => 10, "taunt" => 73, "mend" => 32, "hide" => 29, "sneak" => 42, _ => 27,
                    });
                    break;
                case ChatAction.Cast:
                    if (int.TryParse(parsed.Target, out int gem) && gem >= 1 && gem <= GemCount)
                        Cast(gem - 1);
                    else
                        MessageReceived?.Invoke("Usage: /cast <spell gem 1-8>");
                    break;
                case ChatAction.Unknown:
                    MessageReceived?.Invoke(parsed.Text);
                    break;
            }
        }

        public const int GemCount = 8;
        public int Mana { get; private set; }
        public int MaxMana { get; private set; }
        /// <summary>The spell book and the memorised gems (spell ids, −1 when empty), from the server.</summary>
        public SpellBook? SpellBook { get; private set; }
        /// <summary>The spells lasting on the player (the buff window), from the server.</summary>
        public PlayerBuffs? Buffs { get; private set; }
        /// <summary>The spell being cast (casting bar) and when it started, or null.</summary>
        public SpellCast? Casting { get; private set; }
        public double CastingSince { get; private set; }
        /// <summary>Any caster near you starting or ending a spell (the hands animation, particles).</summary>
        public event Action<SpellCast>? SpellCastReceived;

        /// <summary>0 to 1: how far the current cast is (the casting bar).</summary>
        public float CastProgress => Casting is { } c && c.CastMs > 0 ? (float)Math.Min(1.0, (Now - CastingSince) * 1000.0 / c.CastMs) : 0f;

        public SpellView? GemSpell(int gem)
        {
            if (SpellBook is not { } book || gem < 0 || gem >= book.Gems.Count || book.Gems[gem] < 0)
                return null;
            int id = book.Gems[gem];
            return book.Spells.FirstOrDefault(s => s.SpellId == id);
        }

        /// <summary>Casts the spell of a gem (keys 1-8) at the target; the server checks mana, range and fizzles.</summary>
        public void Cast(int gem)
        {
            if (_state != GameState.InZone)
                return;
            if (GemSpell(gem) == null)
            {
                MessageReceived?.Invoke("You do not have a spell memorized in that gem.");
                return;
            }
            _connection?.Send(new CastSpell(gem));
        }

        /// <summary>Memorises a spell of the book in a gem (−1 forgets it).</summary>
        public void Memorize(int gem, int spellId)
        {
            if (_state == GameState.InZone)
                _connection?.Send(new MemorizeSpell(gem, spellId));
        }

        /// <summary>Scribes the spell scroll of an inventory slot into the book.</summary>
        public void Scribe(int slot)
        {
            if (_state == GameState.InZone)
                _connection?.Send(new ScribeScroll(slot));
        }

        private void UpdateCamp(float seconds)
        {
            if (_campLeft is not float left || Player == null)
                return;
            var p = Player.Position;
            if (Math.Abs(p.X - _campFrom.X) + Math.Abs(p.Y - _campFrom.Y) > 0.5f)
            {
                _campLeft = null;
                MessageReceived?.Invoke("You abandon your preparations to camp.");
                return;
            }
            _campLeft = left - seconds;
            if (_campLeft > 0)
                return;
            _campLeft = null;
            Fail("You have camped. Log in again to play.");
        }

        /// <summary>Inventory slots and money, from the server.</summary>
        public PlayerInventory? Inventory { get; private set; }
        /// <summary>The corpse being looted and what is left on it (the loot window), or null.</summary>
        public int? LootingCorpse { get; private set; }
        public IReadOnlyList<ItemView> LootItems { get; private set; } = Array.Empty<ItemView>();
        public const float LootReach = 20f;

        /// <summary>Loots the targeted corpse, or the nearest one (L). The server answers with the loot window or a refusal.</summary>
        public void Loot()
        {
            if (_state != GameState.InZone || Zone == null || Player == null)
                return;
            var corpse = TargetId is int id && Zone.Get(id) is { Spawn: { IsCorpse: true } } targeted ? targeted : Zone.NearestCorpse(Player.Position, LootReach);
            if (corpse == null)
            {
                MessageReceived?.Invoke("There is no corpse near enough to loot.");
                return;
            }
            _connection?.Send(new LootRequest(corpse.Id));
        }

        public void TakeLoot(int index)
        {
            if (LootingCorpse is int corpse)
                _connection?.Send(new LootTake(corpse, index));
        }

        /// <summary>Moves or swaps two inventory slots (equip, unequip); the server answers with the new inventory or a refusal.</summary>
        public void MoveItem(int from, int to)
        {
            if (_state == GameState.InZone)
                _connection?.Send(new EQClassic.Shared.Zone.MoveItem(from, to));
        }

        public void EndLoot()
        {
            if (LootingCorpse is int corpse)
                _connection?.Send(new LootEnd(corpse));
            LootingCorpse = null;
            LootItems = Array.Empty<ItemView>();
        }

        /// <summary>Considers the target (C): the answer arrives as a line in <see cref="MessageReceived"/>.</summary>
        public void Consider()
        {
            if (_state != GameState.InZone)
                return;
            if (TargetId is int id)
                _connection?.Send(new ConsiderRequest(id));
            else
                MessageReceived?.Invoke("You must first select a target for this command!");
        }

        /// <summary>Whether the player sits (server-confirmed through EntityAppearance).</summary>
        public bool Sitting => Zone?.Get(Zone.YourEntityId)?.Sitting ?? false;

        /// <summary>Sit down or stand up (the server stands you up when you move).</summary>
        public void ToggleSit()
        {
            if (_state == GameState.InZone)
                _connection?.Send(new SetSitting(!Sitting));
        }

        /// <summary>Turns the player toward the target, if any (T).</summary>
        public void FaceTarget()
        {
            if (TargetId is int id && Zone?.Get(id) is { } target)
                Player?.Face(target.Latest);
        }

        public void SetTarget(int? entityId)
        {
            TargetId = entityId;
            if (entityId == null)
                AutoAttacking = false;
            _connection?.Send(new EQClassic.Shared.Zone.SetTarget(entityId ?? 0));
        }

        /// <summary>Auto-attack on or off (the server refuses without a target and says so).</summary>
        public void ToggleAutoAttack()
        {
            if (_state != GameState.InZone)
                return;
            bool on = !AutoAttacking;
            AutoAttacking = on && TargetId != null;
            _connection?.Send(new AutoAttack(on)); // without a target the server answers "You must first select a target..."
        }

        /// <summary>
        /// Uses the nearest door (the Use key, U in EverQuest). Returns it, or null when none is in
        /// reach. The server answers with the door's new state, a teleport or a message.
        /// </summary>
        public DoorInfo? UseNearestDoor()
        {
            if (_state != GameState.InZone || Zone == null || Player == null)
                return null;
            var door = Zone.NearestDoor(Player.Position, DoorUseReach);
            if (door != null)
                _connection?.Send(new ClickDoor(door.Id));
            return door;
        }

        /// <summary>
        /// Use (U): the nearest door within reach, else the targeted NPC (a merchant opens their window;
        /// the server refuses the others). Returns what was used, or null.
        /// </summary>
        public string? Use()
        {
            if (UseNearestDoor() != null)
                return "door";
            if (TargetId is int id && Zone?.Get(id) is { Spawn: { IsPlayer: false, IsCorpse: false } })
            {
                _connection?.Send(new MerchantRequest(id));
                return "npc";
            }
            return null;
        }

        /// <summary>The bank window (U on a banker), or null.</summary>
        public BankContents? Bank { get; private set; }

        public void CloseBank()
        {
            Bank = null;
            _connection?.Send(new BankCommand(BankAction.Close));
        }

        /// <summary>Money into the bank (deposit) and out of it (withdraw), coin by coin.</summary>
        public void BankMoney(int depositPlatinum, int depositGold, int depositSilver, int depositCopper,
            int withdrawPlatinum, int withdrawGold, int withdrawSilver, int withdrawCopper) =>
            _connection?.Send(new BankCommand(BankAction.Money, 0, depositPlatinum, depositGold, depositSilver, depositCopper,
                withdrawPlatinum, withdrawGold, withdrawSilver, withdrawCopper));

        /// <summary>The trade window, or null.</summary>
        public TradeWindow? Trade { get; private set; }

        public void OfferItem(int slot) => _connection?.Send(new TradeCommand(TradeAction.Offer, slot));
        public void OfferCoins(int platinum, int gold, int silver, int copper) =>
            _connection?.Send(new TradeCommand(TradeAction.Coins, 0, platinum, gold, silver, copper));
        public void AcceptTrade() => _connection?.Send(new TradeCommand(TradeAction.Accept));
        public void CancelTrade() => _connection?.Send(new TradeCommand(TradeAction.Cancel));

        /// <summary>The zone's weather: 0 clear, 1 rain, 2 snow.</summary>
        public int Weather { get; private set; }

        /// <summary>The player's group (leader and members), or null when not grouped.</summary>
        public GroupUpdate? Group { get; private set; }

        /// <summary>Skill values (the skills window) and the abilities the class can use, from the server.</summary>
        public PlayerSkills? Skills { get; private set; }

        /// <summary>Uses an ability (kick, bash, taunt, mend, hide, sneak, forage) by its skill id.</summary>
        public void UseAbility(int skill)
        {
            if (_state != GameState.InZone)
                return;
            if (Skills != null && !Skills.Abilities.Contains(skill))
            {
                MessageReceived?.Invoke("You do not have that ability.");
                return;
            }
            _connection?.Send(new UseAbility(skill));
        }

        /// <summary>The open merchant window (goods with their values), or null.</summary>
        public MerchantGoods? Merchant { get; private set; }

        public void Buy(int index)
        {
            if (Merchant is { } m)
                _connection?.Send(new MerchantBuy(m.NpcId, index));
        }

        public void Sell(int slot)
        {
            if (Merchant is { } m)
                _connection?.Send(new MerchantSell(m.NpcId, slot));
        }

        public void CloseMerchant()
        {
            if (Merchant is { } m)
                _connection?.Send(new MerchantEnd(m.NpcId));
            Merchant = null;
        }

        public void Connect(string host, int port = ProtocolInfo.DefaultLoginPort)
        {
            LastError = null;
            Open(host, port, null, GameState.Connecting);
        }

        public void Login(string user, string password)
        {
            Require(GameState.Login);
            _connection!.Login(user, password);
            SetState(GameState.LoggingIn);
        }

        public void SelectWorld(int worldId)
        {
            Require(GameState.ServerSelect);
            _connection!.Send(new PlayRequest(worldId));
            SetState(GameState.EnteringWorld);
        }

        /// <summary>The combinations creation offers (race, class, deity, city), once World answered <see cref="RequestCreationOptions"/>.</summary>
        public IReadOnlyList<CreationOption>? CreationOptions { get; private set; }

        public void RequestCreationOptions()
        {
            Require(GameState.CharacterSelect);
            _connection?.Send(new CreationOptionsRequest());
        }

        public void CreateCharacter(CreateCharacterRequest request)
        {
            Require(GameState.CharacterSelect);
            _connection!.Send(request);
        }

        public void EnterWorld(string characterName)
        {
            Require(GameState.CharacterSelect);
            _characterName = characterName;
            _connection!.Send(new EnterWorldRequest(characterName));
            SetState(GameState.EnteringZone);
        }

        /// <summary>Network, then the local player's report to the zone.</summary>
        public void Update(float seconds)
        {
            if (_connection == null)
                return;
            foreach (var message in _connection.Poll())
                Handle(message);
            if (_connection != null && _connection.State == ConnectionState.Connected && _sendOnConnect != null)
            {
                _connection.Send(_sendOnConnect);
                _sendOnConnect = null;
            }
            if (_connection != null && _connection.State == ConnectionState.Disconnected && _state != GameState.Disconnected)
                Fail(_connection.DisconnectReason ?? "connection lost");
            if (_state == GameState.InZone && Player?.Due(seconds) is PlayerMove move)
                _connection?.Send(move);
            if (_state == GameState.InZone)
                UpdateCamp(seconds);
        }

        private void Handle(IMessage message)
        {
            switch (message)
            {
                case EQClassic.Shared.Security.ServerHello _ when _state == GameState.Connecting:
                    SetState(GameState.Login);
                    break;
                case LoginResponse login:
                    if (login.Result != LoginResult.Success)
                    {
                        LastError = login.Message;
                        SetState(GameState.Login); // a new ServerHello follows: the player may retry
                        break;
                    }
                    _sessionId = login.SessionId;
                    _connection!.Send(new ServerListRequest());
                    break;
                case ServerListResponse list:
                    Worlds = list.Worlds;
                    SetState(GameState.ServerSelect);
                    break;
                case PlayResponse play:
                    if (!play.Accepted) { LastError = play.Message; SetState(GameState.ServerSelect); break; }
                    Open(play.Address, play.Port, new WorldLoginRequest(_sessionId, play.SessionKey), GameState.EnteringWorld);
                    break;
                case WorldLoginResponse world:
                    if (!world.Accepted) { Fail(world.Message); break; }
                    Characters = world.Characters;
                    SetState(GameState.CharacterSelect);
                    break;
                case CreationOptionsResponse options:
                    CreationOptions = options.Options;
                    break;
                case CreateCharacterResponse created:
                    if (!created.Accepted) LastError = created.Message;
                    else Characters = created.Characters;
                    StateChanged?.Invoke(_state);
                    break;
                case EnterWorldResponse enter:
                    if (!enter.Accepted || enter.ZonePort == 0) { LastError = enter.Accepted ? "no zone server" : enter.Message; SetState(GameState.CharacterSelect); break; }
                    Open(enter.ZoneAddress, enter.ZonePort, new ZoneEnterRequest(_characterName, enter.ZoneKey), GameState.EnteringZone);
                    break;
                case ZoneEnterResponse entered:
                    if (!entered.Accepted) { Fail(entered.Message); break; }
                    Zone = new ZoneView(entered, Now);
                    var me = Zone.Get(entered.YourEntityId)!.Spawn;
                    Player = new LocalPlayer(entered.YourEntityId, new Vec3(me.X, me.Y, me.Z), me.Heading);
                    SetState(GameState.InZone);
                    ZoneEntered?.Invoke(Zone);
                    break;
                case EntitySpawned spawned:
                    Zone?.Apply(spawned, Now);
                    break;
                case EntityRemoved removed:
                    Zone?.Apply(removed);
                    if (removed.Id == TargetId)
                    {
                        TargetId = null;
                        AutoAttacking = false;
                    }
                    break;
                case EntityPositions positions:
                    Zone?.Apply(positions, Now);
                    break;
                case MoveCorrection correction:
                    Player?.Apply(correction);
                    if (correction.Reason == DeathReason)
                    {
                        TargetId = null;
                        AutoAttacking = false;
                    }
                    break;
                case ZoneDoors doors:
                    Zone?.Apply(doors);
                    break;
                case DoorState door:
                    Zone?.Apply(door);
                    break;
                case ZoneMessage text:
                    if (text.Text == "Auto attack is off.")
                        AutoAttacking = false;
                    MessageReceived?.Invoke(text.Text);
                    break;
                case CombatEvent swing when Zone != null:
                    Zone.Apply(swing);
                    CombatReceived?.Invoke(swing);
                    MessageReceived?.Invoke(CombatText.Describe(swing, Zone.YourEntityId, id => Zone.Get(id)?.DisplayName));
                    break;
                case ConsiderResult considered when Zone?.Get(considered.EntityId) is { } seen:
                    seen.Con = considered.Con;
                    MessageReceived?.Invoke(ConsiderRules.Message(seen.DisplayName, considered.Standing, considered.Con));
                    break;
                case ChatMessage chat:
                    MessageReceived?.Invoke(Chat.Format(chat, _characterName ?? ""));
                    break;
                case EntityAppearance appearance:
                    Zone?.Apply(appearance);
                    break;
                case TimeOfDay time:
                    Clock = new EqClock(time.Hour, time.Minute, Now);
                    break;
                case ZoneInfo info:
                    ZoneInfo = info;
                    ZoneInfoReceived?.Invoke(info);
                    break;
                case LootContents contents:
                    LootingCorpse = contents.CorpseId;
                    LootItems = contents.Items;
                    break;
                case PlayerInventory inventory:
                    Inventory = inventory;
                    break;
                case PlayerExperience experience:
                    Experience = experience;
                    break;
                case BankContents bank:
                    Bank = bank.Open ? bank : null;
                    break;
                case TradeWindow trade:
                    Trade = trade.Partner.Length == 0 ? null : trade;
                    break;
                case ZoneWeather weather:
                    Weather = weather.Weather;
                    break;
                case GroupUpdate group:
                    Group = group.Members.Count == 0 ? null : group;
                    break;
                case PlayerSkills skills:
                    Skills = skills;
                    break;
                case MerchantGoods goods:
                    Merchant = goods.NpcId == 0 ? null : goods;
                    break;
                case PlayerMana mana:
                    Mana = mana.Mana;
                    MaxMana = mana.MaxMana;
                    break;
                case PlayerBuffs buffs:
                    Buffs = buffs;
                    if (Player != null)
                    {
                        Player.SpeedFactor = Math.Max(0f, (100 + buffs.MovementSpeed) / 100f); // 0 when rooted
                        Player.Levitating = buffs.Levitating;
                    }
                    break;
                case SpellBook book:
                    SpellBook = book;
                    break;
                case SpellCast cast:
                    if (Zone != null && cast.CasterId == Zone.YourEntityId)
                    {
                        Casting = cast.Phase == SpellPhase.Begin && cast.CastMs > 0 ? cast : null;
                        CastingSince = Now;
                        if (cast.Phase == SpellPhase.Begin)
                            MessageReceived?.Invoke($"You begin casting {cast.SpellName}.");
                    }
                    else if (cast.Phase == SpellPhase.Begin && Zone?.Get(cast.CasterId) is { } other)
                        MessageReceived?.Invoke($"{other.DisplayName} begins to cast a spell.");
                    SpellCastReceived?.Invoke(cast);
                    break;
                case PlayerHealth health:
                    Hp = health.Hp;
                    MaxHp = health.MaxHp;
                    break;
                case ZoneChange change:
                    Bank = null;
                    LootingCorpse = null;
                    Trade = null;
                    Merchant = null;
                    Casting = null;
                    ZoneInfo = null;
                    Zone = null;
                    Player = null;
                    TargetId = null;
                    AutoAttacking = false;
                    Open(change.Address, change.Port, new ZoneEnterRequest(_characterName, change.ZoneKey), GameState.EnteringZone);
                    break;
            }
        }

        private void Open(string host, int port, IMessage? firstMessage, GameState state)
        {
            _connection?.Dispose();
            _connection = new LoginClient { ExpectedServerFingerprint = firstMessage == null ? ExpectedServerFingerprint : null };
            _sendOnConnect = firstMessage;
            SetState(state);
            _connection.Connect(host, port);
        }

        private void Fail(string reason)
        {
            LastError = reason;
            _connection?.Dispose();
            _connection = null;
            Zone = null;
            Player = null;
            SetState(GameState.Disconnected);
        }

        private void Require(GameState expected)
        {
            if (_state != expected)
                throw new InvalidOperationException($"{expected} expected, client is {_state}");
        }

        private void SetState(GameState state)
        {
            if (_state == state)
                return;
            _state = state;
            StateChanged?.Invoke(state);
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
