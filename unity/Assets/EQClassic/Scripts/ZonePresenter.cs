using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EQClassic.ClientCore;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;
using Lantern.EQ.Animation;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// Draws the zone: the zone prefab LanternUnityTools imported, one object per entity (the
    /// imported character prefab for its model code, or a capsule), moved every frame from the
    /// interpolated server positions; the player's own character follows local input.
    /// </summary>
    public sealed class ZonePresenter : MonoBehaviour
    {
        private const float Scale = Coordinates.LanternWorldScale;
        private const string ContentRoot = "Assets/Content/AssetBundleContent/";

        private readonly Dictionary<int, GameObject> _objects = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Animated> _animated = new Dictionary<int, Animated>();
        private readonly HashSet<string> _missingModels = new HashSet<string>();

        // Speeds (EverQuest units per second) above which a model walks, then runs. NPCs walk at about
        // 6 u/s and run at about 20; players run at 45.
        private const float WalkAbove = 1.5f;
        private const float RunAbove = 14f;

        /// <summary>An imported model's animation controller, with the speed measured from its positions.</summary>
        private sealed class Animated
        {
            public CharacterAnimationController Controller;
            public Vec3 Last;
            public float Speed;
            /// <summary>The model's attack animation (the first it has of hand to hand, slashes, kick...), if any.</summary>
            public AnimationType? Attack;
            public bool CanFlinch;
        }

        private static readonly (string Clip, AnimationType Type)[] AttackClips =
        {
            ("c08", AnimationType.CombatHandToHand), ("c05", AnimationType.Combat1HSlash), ("c03", AnimationType.Combat2HSlash),
            ("c04", AnimationType.Combat2HBlunt), ("c02", AnimationType.CombatPiercing), ("c01", AnimationType.CombatKick),
        };
        private GameObject _zoneRoot;
        private ZoneCollisionMesh _mesh;
        private Camera _camera;
        private Light _playerLight;
        private readonly CameraRig _rig = new CameraRig(Scale);
        private readonly SkyPresenter _sky = new SkyPresenter();
        private readonly WeatherPresenter _weather = new WeatherPresenter();
        private ZoneAudio _audio;
        private bool _autorun;
        private float _eyeHeight = 2.5f; // Unity units above the feet; measured from the player's model
        private bool _playerHidden;

        /// <summary>False while the chat line is open: the keys type text instead of moving.</summary>
        public bool InputEnabled { get; set; } = true;
        /// <summary>False while the pointer is over the chat window: the wheel scrolls the chat, not the camera.</summary>
        public bool ZoomEnabled { get; set; } = true;
        /// <summary>The part of the screen the 3D view takes (the classic interface frames it; the window layout gives it all).</summary>
        public Rect Viewport { get; set; } = new Rect(0f, 0f, 1f, 1f);
        /// <summary>WALK in the classic interface: walking without holding Shift.</summary>
        public bool WalkToggle { get; set; }

        /// <summary>VIEW: first person, behind, overhead (as F9).</summary>
        public void CycleView() => _rig.CycleView();

        /// <summary>The zone the server put us in has no imported assets in this client.</summary>
        public string MissingZone { get; private set; }
        private readonly Dictionary<int, DoorVisual> _doors = new Dictionary<int, DoorVisual>();

        /// <summary>A drawn door: its closed pose, how it opens, and how far it is open (0 to 1).</summary>
        private sealed class DoorVisual
        {
            public Transform Transform;
            public Vector3 ClosedPosition;
            public Quaternion ClosedRotation;
            public bool Slides;
            public float Height;
            public bool Open;
            public float Amount;
        }

        private const float DoorSeconds = 1f;

        private int _playerId = -1;
        private ZoneView _zone;
        private GameClient _client;
        private readonly Dictionary<int, float> _tops = new Dictionary<int, float>();
        private GUIStyle _plateStyle;
        /// <summary>Name plates are drawn for entities this close to the camera (Unity units).</summary>
        private const float PlateDistance = 60f;

        public void Enter(ZoneView zone)
        {
            if (_audio == null)
                _audio = gameObject.AddComponent<ZoneAudio>();
            _audio.Scale = Scale;
            _audio.Enter(zone.Zone);
            _playerId = zone.YourEntityId;
            _zone = zone;
            _playerHidden = false;
            Clear();
            var zonePrefab = LoadPrefab(ContentRoot + "Zones/" + zone.Zone + "/" + zone.Zone + ".prefab");
            _zoneRoot = zonePrefab != null ? Instantiate(zonePrefab) : null; // the prefab carries the 0.5 world scale
            if (_zoneRoot != null)
                Sharpen(_zoneRoot);
            MissingZone = _zoneRoot == null ? zone.Zone : null;
            if (_zoneRoot == null)
            {
                Debug.LogWarning($"EQClassic: zone '{zone.Zone}' is not imported (EQ > Assets > Import Zone). Drawing entities only.");
                _zoneRoot = new GameObject(zone.Zone + " (not imported)");
            }
            if (zonePrefab != null)
                PlaceObjects(zone.Zone);
            _mesh = LoadCollision(zone.Zone);
            _regions = LoadRegions(zone.Zone);
            foreach (var e in zone.Entities)
                AddEntity(e);
            zone.Added += AddEntity;
            zone.Removed += RemoveEntity;
            zone.DoorsLoaded += doors => PlaceDoors(zone.Zone, doors);
            zone.DoorChanged += door =>
            {
                if (_doors.TryGetValue(door.Id, out var visual))
                    visual.Open = door.Open;
            };
            EnsureCamera();
        }

        public void Present(GameClient client, float deltaTime)
        {
            _client = client;
            var zone = client.Zone;
            var player = client.Player;
            if (zone == null || player == null)
                return;

            if (InputEnabled)
                ReadCameraInput(player);
            if (InputEnabled && (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Numlock)))
                _autorun = !_autorun;
            if (InputEnabled && Input.GetKeyDown(KeyCode.Space))
                player.Jump();
            // Swimming: Space up, Ctrl down (the Trilogy client used the jump and crouch keys).
            player.Regions = _regions;
            player.SwimInput = !InputEnabled ? 0f : Input.GetKey(KeyCode.Space) ? 1f : Input.GetKey(KeyCode.LeftControl) ? -1f : 0f;
            var keys = client.Keys;
            bool Held(char key) => Input.GetKey((KeyCode)key); // KeyCode.A is 'a', ...
            float forward = !InputEnabled ? 0f
                : (Held(keys.Forward) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Held(keys.Back) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            if (forward < -0.1f)
                _autorun = false; // backing up stops autorun, as in the old client
            if (_autorun)
                forward = 1f;
            float strafe = !InputEnabled ? 0f : (Held(keys.StrafeRight) ? 1f : 0f) - (Held(keys.StrafeLeft) ? 1f : 0f);
            float turn = !InputEnabled ? 0f
                : (Held(keys.TurnRight) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Held(keys.TurnLeft) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            player.Walking = WalkToggle || InputEnabled && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (_camera != null)
                _camera.rect = Viewport;
            if (_mesh != null)
                player.Move(forward, strafe, turn, deltaTime, _mesh); // ground, steps and walls
            else
                player.Move(forward, strafe, turn, deltaTime);

            foreach (var pair in _objects)
            {
                Vec3 position;
                float heading;
                if (pair.Key == player.EntityId)
                {
                    position = player.Position;
                    heading = player.Heading;
                }
                else
                {
                    var drawn = zone.Interpolated(pair.Key, client.Now);
                    position = drawn.Position;
                    heading = drawn.Heading;
                }
                var (x, y, z) = Coordinates.ToUnity(position, Scale);
                pair.Value.transform.position = new Vector3(x, y, z);
                pair.Value.transform.rotation = Quaternion.Euler(0f, Coordinates.HeadingToUnityYaw(heading), 0f);
                if (_animated.TryGetValue(pair.Key, out var animated))
                    Animate(animated, position, deltaTime, zone.Get(pair.Key)?.Sitting ?? false);
            }

            foreach (var door in _doors.Values)
                AnimateDoor(door, deltaTime);
            _sky.Update(_camera, client, deltaTime);
            DarkenFogAtNight(client);
            _weather.Update(_camera, client.Weather, Sheltered(client.Player));

            if (_objects.TryGetValue(player.EntityId, out var me) && _playerLight == null)
                _playerLight = AddPlayerLight(me);

            if (me != null && _camera != null)
            {
                bool first = _rig.Mode == CameraRig.View.FirstPerson;
                if (first != _playerHidden)
                    SetVisible(me, !(_playerHidden = first)); // first person: do not look out from inside our own model
                _rig.Place(_camera, me.transform.position, me.transform.rotation, _eyeHeight, _mesh);
                Underwater(_camera.transform.position);
                // Music and ambient sounds by region; Norrath's day is 7:00 to 21:00.
                float hours = client.Clock != null ? (float)client.Clock.HoursAt(client.Now) : 12f;
                _audio?.Listen(_camera.transform.position, hours >= EqClock.DayStart && hours < EqClock.NightStart);
            }
        }

        private ZoneRegions _regions;
        private (Color Colour, float Start, float End)? _zoneFog;
        private bool _underwater;

        /// <summary>
        /// The zone's fog (and the background behind the sky) follows the daylight: at night the cfg's
        /// daytime colour lit the distance bright against a dark sky (Oasis's sand yellow at midnight).
        /// </summary>
        private void DarkenFogAtNight(GameClient client)
        {
            if (_underwater || _zoneFog is not { } fog || client.Clock is not { } clock)
                return;
            float k = Mathf.Lerp(0.15f, 1f, clock.Daylight(client.Now));
            var colour = new Color(fog.Colour.r * k, fog.Colour.g * k, fog.Colour.b * k);
            RenderSettings.fogColor = colour;
            if (_camera != null)
                _camera.backgroundColor = colour;
        }

        /// <summary>
        /// The entity under the mouse in the 3D view (<paramref name="gui"/>: IMGUI position, from the top
        /// left): each drawn character is taken as the box from its feet to the top of its model on screen;
        /// the nearest one containing the point wins. Null when there is none.
        /// </summary>
        public int? PickAt(Vector2 gui)
        {
            if (_camera == null)
                return null;
            float x = gui.x, y = Screen.height - gui.y;
            int? best = null;
            float bestDepth = float.MaxValue;
            foreach (var pair in _objects)
            {
                if (pair.Key == _playerId || pair.Value == null)
                    continue;
                float top = _tops.TryGetValue(pair.Key, out var h) ? h : 2f;
                var feet = _camera.WorldToScreenPoint(pair.Value.transform.position);
                var head = _camera.WorldToScreenPoint(pair.Value.transform.position + new Vector3(0f, top, 0f));
                if (feet.z <= 0f || head.z <= 0f)
                    continue;
                float half = Mathf.Max(10f, Mathf.Abs(head.y - feet.y) * 0.3f);
                if (x < Mathf.Min(feet.x, head.x) - half || x > Mathf.Max(feet.x, head.x) + half
                    || y < Mathf.Min(feet.y, head.y) - 4f || y > Mathf.Max(feet.y, head.y) + 4f)
                    continue;
                if (feet.z < bestDepth)
                {
                    bestDepth = feet.z;
                    best = pair.Key;
                }
            }
            return best;
        }

        /// <summary>Under water the view turns blue-green and short, as in the Trilogy client.</summary>
        private void Underwater(Vector3 cameraPosition)
        {
            var eq = Coordinates.FromUnity(cameraPosition.x, cameraPosition.y, cameraPosition.z, Scale);
            bool under = _regions != null && _regions.InWater(eq);
            if (under == _underwater)
                return;
            _underwater = under;
            if (under)
            {
                RenderSettings.fog = true;
                RenderSettings.fogColor = new Color(0.08f, 0.22f, 0.3f);
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = 60f * Scale;
                if (_camera != null)
                    _camera.backgroundColor = RenderSettings.fogColor;
            }
            else if (_zoneFog is { } fog)
            {
                RenderSettings.fogColor = fog.Colour;
                RenderSettings.fogStartDistance = fog.Start;
                RenderSettings.fogEndDistance = fog.End;
                if (_camera != null)
                    _camera.backgroundColor = fog.Colour;
            }
        }

        /// <summary>The zone's BSP regions (water, lava): the Lantern export in the Editor, the copy next to the bundles in builds.</summary>
        private static ZoneRegions LoadRegions(string zone)
        {
            var path = Path.Combine(ClientPaths.Exports, zone, "Zone", "bsp_tree.txt");
            if (!File.Exists(path))
                path = Path.Combine(ClientBundles.Directory, zone + "_bsp.txt");
            return File.Exists(path) ? ZoneRegions.Load(path) : null;
        }

        private static readonly HashSet<Texture> Sharpened = new HashSet<Texture>();

        /// <summary>
        /// Finer textures at a slant: trilinear filtering and 8× anisotropy on the textures of the zone and
        /// the models (they come with mipmaps from the import, but filtered for a flat look).
        /// </summary>
        private static void Sharpen(GameObject root)
        {
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            Texture.SetGlobalAnisotropicFilteringLimits(8, 16);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.mainTexture is { } texture && Sharpened.Add(texture))
                    {
                        texture.filterMode = FilterMode.Trilinear;
                        texture.anisoLevel = 8;
                    }
        }

        /// <summary>EQC_DEBUG_MODELS=1: every character model is described in the log (active renderers, bounds).</summary>
        private static readonly bool DebugModels = System.Environment.GetEnvironmentVariable("EQC_DEBUG_MODELS") == "1";

        /// <summary>
        /// The model's armour and helmet. Lantern's variant handlers give the materials (the armour's
        /// look), but their mesh choice fails on these models: they hide the last main mesh as the bare
        /// head (on the guards and the playable races it is the body) and take the robe for a helmet. The
        /// meshes are chosen here as EverQuest does: the body, or the robe (&lt;code&gt;01) for textures
        /// 10 to 16, and one head: &lt;code&gt;he0N for helmet N when the model has it, else the bare &lt;code&gt;he00.
        /// </summary>
        private static void ApplyVariant(GameObject model, string code, int texture, int helm)
        {
            if (texture != 0 || helm != 0)
            {
                var npc = model.GetComponentInChildren<Lantern.EQ.Equipment.NonPlayableVariantHandler>();
                if (npc != null)
                    npc.SetCurrentActiveVariant(texture, helm);
                var armour = model.GetComponentInChildren<Lantern.EQ.Equipment.Equipment2dHandler>();
                if (armour != null)
                    armour.SetArmorSetActive(texture, helm);
            }
            ChooseMeshes(model, code, texture, helm);
        }

        private static void ChooseMeshes(GameObject model, string code, int texture, int helm)
        {
            code = code.ToLowerInvariant();
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool robe = texture >= 10 && texture <= 16 && System.Linq.Enumerable.Any(renderers, r => r.gameObject.name.ToLowerInvariant() == code + "01");
            bool hasHelm = System.Linq.Enumerable.Any(renderers, r => r.gameObject.name.ToLowerInvariant() == code + "he" + helm.ToString("00"));
            string head = code + "he" + (hasHelm ? helm : 0).ToString("00");
            foreach (var r in renderers)
            {
                string name = r.gameObject.name.ToLowerInvariant();
                if (name == code)
                    r.gameObject.SetActive(!robe);
                else if (name == code + "01")
                    r.gameObject.SetActive(robe);
                else if (name.StartsWith(code + "he") && name.Length == code.Length + 4)
                    r.gameObject.SetActive(name == head);
            }
        }

        private void AddEntity(ZoneView.EntityView entity)
        {
            if (!ModelCodes.IsKnown(entity.Spawn.Race) && _missingModels.Add("race " + entity.Spawn.Race))
                Debug.Log($"EQClassic: no model code for race {entity.Spawn.Race} ({entity.Spawn.Name}); using '{ModelCodes.Fallback}'. Complete ModelCodes.");
            var prefab = LoadPrefab(ContentRoot + "Characters/" + entity.ModelCode + ".prefab");
            GameObject go;
            if (prefab != null)
            {
                // Imported character prefabs are in EverQuest units (scaled below). Their origin is not at the
                // feet (EverQuest models are centred), while entity positions are on the ground: lift
                // the model under an empty parent so the bottom of its bounds touches the ground.
                go = new GameObject("entity");
                var model = Instantiate(prefab);
                model.transform.SetParent(go.transform, false);
                // The spawn's size relative to its race's usual one (a giant guard, a gnome child...).
                // Character meshes are in EverQuest units like the zones, whose prefab carries the 0.5
                // world scale: the models take it too (they stood twice too tall), then the spawn's size.
                model.transform.localScale = model.transform.localScale * (Scale * ModelCodes.Scale(entity.Spawn.Race, entity.Spawn.Size));
                model.transform.localPosition = new Vector3(0f, FeetOffset(model), 0f);
                // The armour and helmet the server gives (NPC texture / helmtexture, a player's chest and head material).
                ApplyVariant(model, entity.ModelCode, entity.Spawn.Texture, entity.Spawn.Helm);
                Sharpen(model);
                if (DebugModels)
                {
                    var renderers = model.GetComponentsInChildren<Renderer>(false);
                    var all = model.GetComponentsInChildren<Renderer>(true);
                    string bounds = renderers.Length == 0 ? "none" : string.Join(" ", System.Linq.Enumerable.Select(renderers, r => $"{r.name}:{r.bounds.size}@{r.bounds.center}"));
                    Debug.Log($"EQClassic model {entity.Spawn.Name} race {entity.Spawn.Race} code {entity.ModelCode} size {entity.Spawn.Size} tex {entity.Spawn.Texture}/{entity.Spawn.Helm} " +
                        $"scale {model.transform.localScale} active {renderers.Length}/{all.Length} at {go.transform.position} bounds {bounds}");
                }
                var controller = model.GetComponentInChildren<CharacterAnimationController>();
                if (entity.Spawn.IsCorpse)
                {
                    LieDead(model);
                    controller = null; // a corpse does not walk or fight
                }
                if (controller != null)
                {
                    controller.Initialize(AnimationType.PassiveStand);
                    AnimationType? attack = null;
                    foreach (var (clip, type) in AttackClips)
                        if (controller.HasAnimation(clip)) { attack = type; break; }
                    _animated[entity.Id] = new Animated
                    {
                        Controller = controller, Last = new Vec3(entity.Spawn.X, entity.Spawn.Y, entity.Spawn.Z),
                        Attack = attack, CanFlinch = controller.HasAnimation("d01"),
                    };
                }
            }
            else
            {
                if (_missingModels.Add(entity.ModelCode))
                    Debug.Log($"EQClassic: character model '{entity.ModelCode}' not imported (EQ > Assets > Import Characters); drawing a capsule.");
                // The capsule's pivot is its centre: raise it under an empty parent so it stands on the ground.
                go = new GameObject("entity");
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.transform.localScale = new Vector3(1f, entity.Spawn.Size / 6f, 1f) * (Scale * 6f);
                capsule.transform.SetParent(go.transform, false);
                capsule.transform.localPosition = new Vector3(0f, capsule.transform.localScale.y, 0f);
            }
            go.name = entity.Spawn.Name + " #" + entity.Id;
            _tops[entity.Id] = ModelTop(go);
            if (entity.Id == _playerId)
                _eyeHeight = Mathf.Max(1f, ModelTop(go) * 0.92f);
            // Entities stay at the scene root: under the (scaled) zone root their positions would be scaled twice.
            _objects[entity.Id] = go;
        }

        /// <summary>
        /// The zone's fog and view distance (legacy cfg): linear fog in the zone's colour from its
        /// fog start to its fog end, nothing drawn beyond the larger of the fog end and the zone's
        /// clip distance, and the fog colour behind everything, as the Trilogy client showed it.
        /// </summary>
        public void ApplyZoneInfo(ZoneInfo info)
        {
            var colour = new Color(info.FogRed / 255f, info.FogGreen / 255f, info.FogBlue / 255f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = colour;
            RenderSettings.fogStartDistance = info.FogMin * Scale;
            RenderSettings.fogEndDistance = info.FogMax * Scale;
            _zoneFog = (colour, info.FogMin * Scale, info.FogMax * Scale);
            _underwater = false;
            EnsureCamera();
            if (_camera == null)
                return;
            _camera.farClipPlane = Mathf.Max(Mathf.Max(info.FogMax, info.MaxClip), 100f) * Scale + 5f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = colour;
            _sky.Enter(_camera, info, LoadPrefab);
        }

        /// <summary>
        /// Name plates above the entities near the camera (not our own): white until considered,
        /// then in the consider colour; the target's name in brackets.
        /// </summary>
        private void OnGUI()
        {
            if (_zone == null || _camera == null)
                return;
            if (_plateStyle == null)
                _plateStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            var colour = GUI.color;
            foreach (var pair in _objects)
            {
                if (pair.Key == _playerId || _zone.Get(pair.Key) is not { } entity)
                    continue;
                var top = pair.Value.transform.position + new Vector3(0f, _tops.TryGetValue(pair.Key, out var h) ? h + 0.4f : 3f, 0f);
                var screen = _camera.WorldToScreenPoint(top);
                if (screen.z <= 0f || screen.z > PlateDistance)
                    continue;
                string name = entity.DisplayName;
                if (_client?.TargetId == pair.Key)
                    name = "[ " + name + " ]";
                GUI.color = EQClassicClient.ConColour(entity.Con);
                GUI.Label(new Rect(screen.x - 120f, Screen.height - screen.y - 10f, 240f, 20f), name, _plateStyle);
            }
            GUI.color = colour;
        }

        /// <summary>F9, wheel, Page Up/Down, Home, and the mouse with the right button held.</summary>
        private void ReadCameraInput(EQClassic.ClientCore.LocalPlayer player)
        {
            if (Input.GetKeyDown(KeyCode.F9))
                _rig.CycleView();
            if (ZoomEnabled)
                _rig.Zoom(Input.mouseScrollDelta.y);
            if (Input.GetKey(KeyCode.PageUp))
                _rig.Look(60f * Time.deltaTime);
            if (Input.GetKey(KeyCode.PageDown))
                _rig.Look(-60f * Time.deltaTime);
            if (Input.GetKeyDown(KeyCode.Home))
                _rig.Centre();
            if (Input.GetMouseButton(1))
            {
                player.Turn(Input.GetAxis("Mouse X") * 3f);
                _rig.Look(Input.GetAxis("Mouse Y") * 2f);
            }
        }

        private static void SetVisible(GameObject go, bool visible)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.enabled = visible;
        }

        /// <summary>Plays the death clip (d05) once and holds its last frame.</summary>
        private static void LieDead(GameObject model)
        {
            var animation = model.GetComponentInChildren<UnityEngine.Animation>();
            if (animation == null)
                return;
            foreach (AnimationState state in animation)
            {
                var parts = state.name.Split('_');
                if (parts.Length > 1 && parts[1] == "d05")
                {
                    state.wrapMode = WrapMode.ClampForever;
                    animation.Play(state.name);
                    return;
                }
            }
        }

        /// <summary>Height from the lowest point of the model's renderers up to its origin.</summary>
        private static float FeetOffset(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0f;
            float lowest = float.MaxValue;
            foreach (var r in renderers)
                lowest = System.Math.Min(lowest, r.bounds.min.y);
            return model.transform.position.y - lowest;
        }

        private void RemoveEntity(int id)
        {
            if (_objects.TryGetValue(id, out var go))
            {
                Destroy(go);
                _objects.Remove(id);
            }
            _animated.Remove(id);
        }

        private void Clear()
        {
            foreach (var go in _objects.Values)
                Destroy(go);
            _objects.Clear();
            _tops.Clear();
            _animated.Clear();
            _doors.Clear(); // their objects are children of the zone root
            _playerLight = null; // destroyed with the player's object
            if (_zoneRoot != null)
                Destroy(_zoneRoot);
            _zoneRoot = null;
        }

        /// <summary>
        /// The collision mesh the server walks NPCs on (LanternExtractor export, copied into
        /// the export folder in the Editor, next to the asset bundles in player builds). Without it the player keeps its height.
        /// </summary>
        private bool _sheltered;
        private float _shelterCheckIn;

        /// <summary>Whether something (a roof, a cave) is above the player: checked a few times a second on the collision mesh.</summary>
        private bool Sheltered(LocalPlayer player)
        {
            if (_mesh == null || player == null)
                return false;
            _shelterCheckIn -= Time.deltaTime;
            if (_shelterCheckIn > 0f)
                return _sheltered;
            _shelterCheckIn = 0.25f;
            var p = player.Position;
            _sheltered = !_mesh.LineOfSight(new Vec3(p.X, p.Y, p.Z + 5f), new Vec3(p.X, p.Y, p.Z + 300f));
            return _sheltered;
        }

        private static ZoneCollisionMesh LoadCollision(string zone)
        {
            // Editor: the Lantern export (zone and solid objects); standalone builds: the merged
            // copy EQClassicBuild writes next to the asset bundles.
            var exports = ClientPaths.Exports;
            var path = Path.Combine(exports, zone, "Zone", "Meshes", zone + "_collision.txt");
            if (File.Exists(path))
                return ZoneCollisionMesh.LoadLanternZone(exports, zone);
            path = Path.Combine(ClientBundles.Directory, zone + "_collision.txt");
            if (File.Exists(path))
                return ZoneCollisionMesh.LoadLantern(path);
            Debug.LogWarning($"EQClassic: no collision mesh at {path}; the player will not follow the ground.");
            return null;
        }

        /// <summary>
        /// The zone's placed objects (trees, lamp posts, crates...): LanternUnityTools leaves them
        /// out of the zone prefab for its own streaming, so they are placed here from the Lantern
        /// object list, under the zone root (Lantern axes, before the root's world scale).
        /// </summary>
        private void PlaceObjects(string zone)
        {
            var list = Path.Combine(ClientPaths.Exports, zone, "Zone", "object_instances.txt");
            if (!File.Exists(list))
                list = Path.Combine(ClientBundles.Directory, zone + "_objects.txt");
            if (!File.Exists(list))
                return;
            var prefabs = new Dictionary<string, GameObject>();
            int placed = 0;
            foreach (var line in File.ReadLines(list))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;
                // ModelName, PosX, PosY, PosZ, RotX, RotY, RotZ, ScaleX, ScaleY, ScaleZ, ColorIndex
                var f = line.Split(',');
                if (f.Length < 10)
                    continue;
                var position = new Vector3(Number(f[1]), Number(f[2]), Number(f[3]));
                if (position.y < -30000f)
                    continue; // fallen to the bottom of the world
                if (!prefabs.TryGetValue(f[0], out var prefab))
                    prefabs[f[0]] = prefab = LoadPrefab(ContentRoot + "Zones/" + zone + "/Objects/" + f[0] + ".prefab");
                if (prefab == null)
                    continue;
                var go = Instantiate(prefab);
                go.transform.SetParent(_zoneRoot.transform, false);
                go.transform.localPosition = position;
                go.transform.localRotation = Quaternion.Euler(Number(f[4]), Number(f[5]), Number(f[6]));
                go.transform.localScale = new Vector3(Number(f[7]), Number(f[8]), Number(f[9]));
                placed++;
            }
            Debug.Log($"EQClassic: {placed} object(s) placed in {zone}");
        }

        /// <summary>
        /// The zone's doors (legacy doors table, sent by the server), with the object models of the
        /// zone. Placed like LanternUnityTools' DoorImporter: Lantern axes under the zone root,
        /// heading from 0-512 units. Invisible click spots (open type 54) are not drawn.
        /// </summary>
        private void PlaceDoors(string zone, IReadOnlyCollection<DoorInfo> doors)
        {
            if (_zoneRoot == null)
                return;
            foreach (var d in doors)
            {
                if (d.OpenType == 54)
                    continue;
                var prefab = LoadPrefab(ContentRoot + "Zones/" + zone + "/Objects/" + d.Name.ToLowerInvariant() + ".prefab");
                if (prefab == null)
                    continue; // e.g. post-Trilogy models in the table (poktele500)
                var go = Instantiate(prefab);
                go.name = "door " + d.Id + " " + d.Name;
                var t = go.transform;
                t.SetParent(_zoneRoot.transform, false);
                var (x, y, z) = Coordinates.ToUnity(new Vec3(d.X, d.Y, d.Z));
                t.localPosition = new Vector3(x, y, z);
                t.localRotation = Quaternion.Euler(0f, -d.Heading / 512f * 360f, 0f);
                t.localScale = Vector3.one * (d.Size / 100f);
                var visual = new DoorVisual
                {
                    Transform = t,
                    ClosedPosition = t.localPosition,
                    ClosedRotation = t.localRotation,
                    Slides = SlidesOpen(d.OpenType),
                    Height = ModelHeight(go),
                    Open = d.Open,
                };
                visual.Amount = d.Open ? 1f : 0f;
                AnimateDoor(visual, 0f);
                _doors[d.Id] = visual;
            }
        }

        /// <summary>
        /// Approximation of the Trilogy client's door open types: lifts, portcullises and gates
        /// (55 to 59, 100 and up) slide up by their height; everything else swings 90 degrees.
        /// </summary>
        private static bool SlidesOpen(int openType) => (openType >= 55 && openType <= 59) || openType >= 100;

        private static void AnimateDoor(DoorVisual door, float deltaTime)
        {
            float target = door.Open ? 1f : 0f;
            door.Amount = Mathf.MoveTowards(door.Amount, target, deltaTime / DoorSeconds);
            if (door.Slides)
                door.Transform.localPosition = door.ClosedPosition + new Vector3(0f, door.Height * door.Amount, 0f);
            else
                door.Transform.localRotation = door.ClosedRotation * Quaternion.Euler(0f, 90f * door.Amount, 0f);
        }

        /// <summary>A melee swing: the attacker plays its attack, a defender that was hit flinches.</summary>
        public void OnCombat(CombatEvent swing)
        {
            if (_animated.TryGetValue(swing.AttackerId, out var attacker) && attacker.Attack is AnimationType attack)
                attacker.Controller.PlayOneShotAnimation(attack);
            if (swing.Damage > 0 && _animated.TryGetValue(swing.DefenderId, out var defender) && defender.CanFlinch)
                defender.Controller.PlayOneShotAnimation(AnimationType.Damage1);
        }

        /// <summary>A caster near you begins a spell: the hands animation (t05, general casting) when the model has it.</summary>
        public void OnSpellCast(SpellCast cast)
        {
            if (cast.Phase == SpellPhase.Begin && _animated.TryGetValue(cast.CasterId, out var caster) && caster.Controller.HasAnimation("t05"))
                caster.Controller.PlayOneShotAnimation(AnimationType.SpellCastGeneral);
        }

        /// <summary>
        /// Stand, walk or run from the speed between frames, smoothed so that a late server update
        /// does not make the model stop for one frame. Missing clips are ignored by the controller.
        /// </summary>
        private static void Animate(Animated animated, Vec3 position, float deltaTime, bool sitting)
        {
            if (deltaTime <= 0f)
                return;
            float dx = position.X - animated.Last.X, dy = position.Y - animated.Last.Y;
            float speed = (float)System.Math.Sqrt(dx * dx + dy * dy) / deltaTime;
            animated.Last = position;
            animated.Speed += (speed - animated.Speed) * System.Math.Min(1f, deltaTime * 8f);
            var state = sitting ? AnimationType.PassiveSitting
                : animated.Speed > RunAbove ? AnimationType.LocomotionRun
                : animated.Speed > WalkAbove ? AnimationType.LocomotionWalk
                : AnimationType.PassiveStand;
            animated.Controller.SetNewConstantState(state, 0);
            // The EverQuest client plays walking and running faster with the speed (Lantern's
            // AnimationHelper: 1 + speed × 1.0667, where a player's run is speed 0.7): at a fixed
            // rate the feet do not keep up and the model slides.
            if (state is AnimationType.LocomotionWalk or AnimationType.LocomotionRun)
                animated.Controller.UpdateAnimationSpeed(state, 1f + animated.Speed / MoveUnit * AnimationSpeedPerMove);
        }

        private const float MoveUnit = LocalPlayer.RunSpeed / 0.7f, AnimationSpeedPerMove = 1.066666f;

        /// <summary>Height of the top of a model above its root, in world units.</summary>
        private static float ModelTop(GameObject model)
        {
            float high = float.MinValue;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                high = System.Math.Max(high, r.bounds.max.y);
            return high > float.MinValue ? high - model.transform.position.y : 2.5f;
        }

        private static float ModelHeight(GameObject model)
        {
            float low = float.MaxValue, high = float.MinValue;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                low = System.Math.Min(low, r.bounds.min.y);
                high = System.Math.Max(high, r.bounds.max.y);
            }
            // Bounds are in world units (under the root's 0.5 scale): back to Lantern units.
            return high > low ? (high - low) / Scale : 0f;
        }

        private static float Number(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>
        /// A warm light carried by the player, as a torch would be, so dungeons such as Permafrost
        /// are playable. The Lantern shaders add URP's per-vertex lights to the baked vertex colours.
        /// </summary>
        private static Light AddPlayerLight(GameObject player)
        {
            var light = new GameObject("EQClassic player light").AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 30f;       // Unity units: 60 EverQuest units
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.85f, 0.6f);
            light.transform.SetParent(player.transform, false);
            light.transform.localPosition = new Vector3(0f, 4f, 0f);
            return light;
        }

        private void EnsureCamera()
        {
            _camera = Camera.main;
            if (_camera != null)
                return;
            var go = new GameObject("EQClassic camera");
            _camera = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            go.tag = "MainCamera";
            var light = new GameObject("EQClassic light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        /// <summary>
        /// Editor play mode loads the imported prefabs directly; standalone builds read them from
        /// the asset bundles (EQClassic > Build Linux Player / Build Windows Player).
        /// </summary>
        private GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return ClientBundles.Load(path);
#endif
        }
    }
}
