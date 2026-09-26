using System.Collections.Generic;
using System.IO;
using EQClassic.ClientCore;
using EQClassic.Shared.World;
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
        private readonly HashSet<string> _missingModels = new HashSet<string>();
        private GameObject _zoneRoot;
        private ZoneCollisionMesh _mesh;
        private Camera _camera;

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
            }

            if (_objects.TryGetValue(player.EntityId, out var me) && _camera != null)
            {
                var back = me.transform.rotation * new Vector3(0f, 0f, -1f);
                _camera.transform.position = me.transform.position + back * 12f + new Vector3(0f, 6f, 0f);
                _camera.transform.LookAt(me.transform.position + new Vector3(0f, 2f, 0f));
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
                go = Instantiate(prefab); // imported character prefabs keep the importer's own scale
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

        private void RemoveEntity(int id)
        {
            if (_objects.TryGetValue(id, out var go))
            {
                Destroy(go);
                _objects.Remove(id);
            }
        }

        private void Clear()
        {
            foreach (var go in _objects.Values)
                Destroy(go);
            _objects.Clear();
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
            var path = Path.Combine(Application.dataPath, "EQAssets", zone, "Zone", "Meshes", zone + "_collision.txt");
            if (File.Exists(path))
                return ZoneCollisionMesh.LoadLantern(path);
            Debug.LogWarning($"EQClassic: no collision mesh at {path}; the player will not follow the ground.");
            return null;
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
        /// Editor play mode loads the imported prefabs directly. Player builds need the asset bundles
        /// LanternUnityTools builds (EQ > Assets > Build Asset Bundles), not wired yet (M4 follow-up).
        /// </summary>
        private GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }
    }
}
