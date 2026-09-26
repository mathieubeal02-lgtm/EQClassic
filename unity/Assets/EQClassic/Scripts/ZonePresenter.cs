using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EQClassic.ClientCore;
using EQClassic.Shared.World;
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
        }
        private GameObject _zoneRoot;
        private ZoneCollisionMesh _mesh;
        private Camera _camera;
        private Light _playerLight;

        public void Enter(ZoneView zone)
        {
            Clear();
            var zonePrefab = LoadPrefab(ContentRoot + "Zones/" + zone.Zone + "/" + zone.Zone + ".prefab");
            _zoneRoot = zonePrefab != null ? Instantiate(zonePrefab) : null; // the prefab carries the 0.5 world scale
            if (_zoneRoot == null)
            {
                Debug.LogWarning($"EQClassic: zone '{zone.Zone}' is not imported (EQ > Assets > Import Zone). Drawing entities only.");
                _zoneRoot = new GameObject(zone.Zone + " (not imported)");
            }
            if (zonePrefab != null)
                PlaceObjects(zone.Zone);
            _mesh = LoadCollision(zone.Zone);
            foreach (var e in zone.Entities)
                AddEntity(e);
            zone.Added += AddEntity;
            zone.Removed += RemoveEntity;
            EnsureCamera();
        }

        public void Present(GameClient client, float deltaTime)
        {
            var zone = client.Zone;
            var player = client.Player;
            if (zone == null || player == null)
                return;

            float forward = Input.GetAxis("Vertical");
            float strafe = Input.GetAxis("Horizontal");
            float turn = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
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
                    Animate(animated, position, deltaTime);
            }

            if (_objects.TryGetValue(player.EntityId, out var me) && _playerLight == null)
                _playerLight = AddPlayerLight(me);

            if (me != null && _camera != null)
            {
                var back = me.transform.rotation * new Vector3(0f, 0f, -1f);
                var head = me.transform.position + new Vector3(0f, 2f, 0f);
                float distance = CameraDistance(head, back);
                _camera.transform.position = head + (back * 12f + new Vector3(0f, 4f, 0f)) * distance;
                _camera.transform.LookAt(head);
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
                // Imported character prefabs keep the importer's own scale. Their origin is not at the
                // feet (EverQuest models are centred), while entity positions are on the ground: lift
                // the model under an empty parent so the bottom of its bounds touches the ground.
                go = new GameObject("entity");
                var model = Instantiate(prefab);
                model.transform.SetParent(go.transform, false);
                model.transform.localPosition = new Vector3(0f, FeetOffset(model), 0f);
                var controller = model.GetComponentInChildren<CharacterAnimationController>();
                if (controller != null)
                {
                    controller.Initialize(AnimationType.PassiveStand);
                    _animated[entity.Id] = new Animated { Controller = controller, Last = new Vec3(entity.Spawn.X, entity.Spawn.Y, entity.Spawn.Z) };
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
            // Entities stay at the scene root: under the (scaled) zone root their positions would be scaled twice.
            _objects[entity.Id] = go;
        }

        /// <summary>
        /// Fraction (0.05 to 1) of the full camera distance that keeps the camera on the player's
        /// side of the walls: the view line from the head is tested on the collision mesh.
        /// </summary>
        private float CameraDistance(Vector3 head, Vector3 back)
        {
            if (_mesh == null)
                return 1f;
            var from = Coordinates.FromUnity(head.x, head.y, head.z, Scale);
            for (float f = 1f; f > 0.05f; f -= 0.05f)
            {
                var cam = head + (back * 12f + new Vector3(0f, 4f, 0f)) * f;
                if (_mesh.LineOfSight(from, Coordinates.FromUnity(cam.x, cam.y, cam.z, Scale)))
                    return f;
            }
            return 0.05f;
        }

        /// <summary>
        /// Stand, walk or run from the speed between frames, smoothed so that a late server update
        /// does not make the model stop for one frame. Missing clips are ignored by the controller.
        /// </summary>
        private static void Animate(Animated animated, Vec3 position, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;
            float dx = position.X - animated.Last.X, dy = position.Y - animated.Last.Y;
            float speed = (float)System.Math.Sqrt(dx * dx + dy * dy) / deltaTime;
            animated.Last = position;
            animated.Speed += (speed - animated.Speed) * System.Math.Min(1f, deltaTime * 8f);
            var state = animated.Speed > RunAbove ? AnimationType.LocomotionRun
                : animated.Speed > WalkAbove ? AnimationType.LocomotionWalk
                : AnimationType.PassiveStand;
            animated.Controller.SetNewConstantState(state, 0);
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
            _animated.Clear();
            _playerLight = null; // destroyed with the player's object
            if (_zoneRoot != null)
                Destroy(_zoneRoot);
            _zoneRoot = null;
        }

        /// <summary>
        /// The collision mesh the server walks NPCs on (LanternExtractor export, copied into
        /// Assets/EQAssets by tools/unity/setup-client.sh). Without it the player keeps its height.
        /// </summary>
        private static ZoneCollisionMesh LoadCollision(string zone)
        {
            // Editor: the Lantern export (zone and solid objects); standalone builds: the merged
            // copy EQClassicBuild writes next to the asset bundles.
            var exports = Path.Combine(Application.dataPath, "EQAssets");
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
            var list = Path.Combine(Application.dataPath, "EQAssets", zone, "Zone", "object_instances.txt");
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
