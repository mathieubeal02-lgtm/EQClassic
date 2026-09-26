// Minimal declarations of the UnityEngine / UnityEditor members the client scripts use.
// Signatures follow the Unity 2021.3 scripting reference. Bodies are never run.
#pragma warning disable CS1591
namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static T FindObjectOfType<T>() where T : Object => default;
        public static void DontDestroyOnLoad(Object target) { }
        public static T Instantiate<T>(T original) where T : Object => original;
        public static void Destroy(Object obj) { }
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
    }

    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public string tag { get; set; }
        public T AddComponent<T>() where T : Component => default;
        public T GetComponent<T>() => default;
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
        public T[] GetComponentsInChildren<T>() => System.Array.Empty<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => System.Array.Empty<T>();
        public int layer { get; set; }
        public T GetComponentInChildren<T>() => default;
    }

    public sealed class AssetBundle : Object
    {
        public static AssetBundle LoadFromFile(string path) => null;
        public T LoadAsset<T>(string name) where T : Object => default;
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Bounds bounds => default;
    }

    public struct Bounds
    {
        public Vector3 min => default;
        public Vector3 max => default;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 localPosition { get; set; }
        public Transform parent { get; set; }
        public void LookAt(Vector3 worldPosition) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    }

    public struct Vector2
    {
        public float x, y;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator *(Vector3 a, float d) => a;
        public static Vector3 operator *(float d, Vector3 a) => a;
        public static Vector3 operator /(Vector3 a, float d) => a;
        public static float Distance(Vector3 a, Vector3 b) => 0f;
    }

    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
        public float x => 0;
        public float y => 0;
        public float width => 0;
        public float height => 0;
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum LightType { Spot, Directional, Point }
    public enum KeyCode
    {
        Tab = 9, Escape = 27, Space = 32, Q = 113, C = 99, E = 101, F = 102, H = 104, I = 105, L = 108, R = 114, T = 116, U = 117, X = 120, Numlock = 300, RightShift = 303, LeftShift = 304,
        Home = 278, PageUp = 280, PageDown = 281, F9 = 290, Return = 13, KeypadEnter = 271, Slash = 47,
        LeftControl = 306, K = 107, B = 98, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53, Alpha6 = 54, Alpha7 = 55, Alpha8 = 56,
    }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }
    public enum CameraClearFlags { Skybox = 1, SolidColor = 2 }

    public static class RenderSettings
    {
        public static bool fog { get; set; }
        public static FogMode fogMode { get; set; }
        public static Color fogColor { get; set; }
        public static float fogStartDistance { get; set; }
        public static float fogEndDistance { get; set; }
    }

    public struct LayerMask
    {
        public static int GetMask(params string[] layerNames) => 0;
        public static int NameToLayer(string layerName) => 0;
    }

    public sealed class Shader : Object
    {
        public static int PropertyToID(string name) => 0;
        public static void SetGlobalColor(int nameID, Color value) { }
        public static Shader Find(string name) => null;
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Color color { get; set; }
    }

    // Particle systems (weather).
    public enum ParticleSystemSimulationSpace { Local, World, Custom }
    public enum ParticleSystemShapeType { Sphere = 0, Box = 5, Cone = 4 }
    public enum ParticleSystemRenderMode { Billboard = 0, Stretch = 1 }

    public sealed class ParticleSystem : Component
    {
        public struct MinMaxCurve
        {
            public static implicit operator MinMaxCurve(float constant) => default;
        }

        public struct MinMaxGradient
        {
            public static implicit operator MinMaxGradient(Color color) => default;
        }

        public struct MainModule
        {
            public bool loop { get; set; }
            public MinMaxCurve startLifetime { get; set; }
            public MinMaxCurve startSpeed { get; set; }
            public MinMaxCurve startSize { get; set; }
            public MinMaxGradient startColor { get; set; }
            public MinMaxCurve gravityModifier { get; set; }
            public int maxParticles { get; set; }
            public ParticleSystemSimulationSpace simulationSpace { get; set; }
        }

        public struct EmissionModule
        {
            public MinMaxCurve rateOverTime { get; set; }
        }

        public struct ShapeModule
        {
            public ParticleSystemShapeType shapeType { get; set; }
            public Vector3 scale { get; set; }
        }

        public MainModule main => default;
        public EmissionModule emission => default;
        public ShapeModule shape => default;
        public void Play() { }
    }

    public sealed class ParticleSystemRenderer : Renderer
    {
        public Material material { get; set; }
        public ParticleSystemRenderMode renderMode { get; set; }
        public float lengthScale { get; set; }
    }

    public sealed class Camera : Behaviour
    {
        public int cullingMask { get; set; }
        public float fieldOfView { get; set; }
        public float farClipPlane { get; set; }
        public float nearClipPlane { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public Vector3 WorldToScreenPoint(Vector3 position) => default;
        public static Camera main => null;
    }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public float range { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
    }

    public struct Color
    {
        public Color(float r, float g, float b) { }
        public Color(float r, float g, float b, float a) { }
        public static Color white => default;
        public static Color black => default;
        public static Color Lerp(Color a, Color b, float t) => a;
        public static Color green => default;
        public static Color yellow => default;
        public static Color red => default;
    }

    public enum TextAnchor { MiddleCenter = 4 }

    public static class Input
    {
        public static float GetAxis(string axisName) => 0;
        public static bool GetKey(KeyCode key) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetMouseButton(int button) => false;
        public static Vector2 mouseScrollDelta => default;
    }

    public static class Random
    {
        public static float value => 0f;
    }

    public static class Time
    {
        public static float time => 0f;
        public static float deltaTime => 0;
        public static int frameCount => 0;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
    }

    public class Texture : Object { }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public void SetPixel(int x, int y, Color color) { }
        public void Apply() { }
    }

    public enum WrapMode { Default = 0, Once = 1, Loop = 2, PingPong = 4, ClampForever = 8 }

    public sealed class AnimationState
    {
        public string name => "";
        public WrapMode wrapMode { get; set; }
    }

    public sealed class Animation : Behaviour, System.Collections.IEnumerable
    {
        public bool Play(string animation) => false;
        public System.Collections.IEnumerator GetEnumerator() => System.Linq.Enumerable.Empty<AnimationState>().GetEnumerator();
    }

    public enum EventType { KeyDown = 4 }

    public sealed class Event
    {
        public static Event current => null;
        public EventType type => default;
        public KeyCode keyCode => default;
        public void Use() { }
    }

    public static class Screen
    {
        public static int height => 0;
        public static int width => 0;
    }

    public static class Mathf
    {
        public static float MoveTowards(float current, float target, float maxDelta) => target;
        public static float Clamp(float value, float min, float max) => value;
        public static float Max(float a, float b) => a;
    }

    public static class Application
    {
        public static string dataPath => "Assets";
        public static string streamingAssetsPath => "Assets/StreamingAssets";
    }

    public static class PlayerPrefs
    {
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static void SetString(string key, string value) { }
    }

    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public TextAnchor alignment { get; set; }
    }

    public class GUISkin : Object
    {
        public GUIStyle box => null;
        public GUIStyle label => null;
    }

    public static class GUI
    {
        public static void DrawTexture(Rect position, Texture image) { }
        public static void SetNextControlName(string name) { }
        public static void FocusControl(string name) { }
        public static string TextField(Rect position, string text) => text;
        public static Color color { get; set; }
        public static void Label(Rect position, string text, GUIStyle style) { }
        public static void Box(Rect position, string text) { }
        public static GUISkin skin => null;
        public static void Label(Rect position, string text) { }
        public static bool Button(Rect position, string text) => false;
    }

    public sealed class GUILayoutOption { }

    public static class GUILayout
    {
        public static void BeginArea(Rect screenRect, GUIStyle style) { }
        public static void EndArea() { }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static string TextField(string text) => text;
        public static string PasswordField(string password, char maskChar) => password;
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static GUILayoutOption Width(float width) => new GUILayoutOption();
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) => scrollPosition;
        public static void EndScrollView() { }
        public static void Space(float pixels) { }
        public static int SelectionGrid(int selected, string[] texts, int xCount) => selected;
        public static int Toolbar(int selected, string[] texts) => selected;
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static void BeginVertical() { }
        public static void EndVertical() { }
    }
}

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => default;
    }
}

// LanternUnityTools (Assets/Scripts/Lantern/EQ/Animation), the members the client uses.
namespace Lantern.EQ.Animation
{
    public enum AnimationType
    {
        CombatKick = 1, CombatPiercing = 2, Combat2HSlash = 3, Combat2HBlunt = 4, Combat1HSlash = 5, CombatHandToHand = 8,
        Damage1 = 12, LocomotionWalk = 17, LocomotionRun = 18, PassiveStand = 32, PassiveSitting = 38,
        SpellCastDefense = 42, SpellCastGeneral = 43, SpellCastMissile = 44,
    }

    public class CharacterAnimationController : UnityEngine.MonoBehaviour
    {
        public void Initialize(AnimationType initialAnimation) { }
        public void SetNewConstantState(AnimationType animationType, int priority, float speed = 1f) { }
        public void PlayOneShotAnimation(AnimationType animationType, float speed = 1f, int importance = 0, bool canSelfInterrupt = true, AnimationType? newConstantState = null) { }
        public bool HasAnimation(string animationName) => false;
    }
}

namespace UnityEngine.Rendering.Universal
{
    public enum CameraRenderType { Base, Overlay }

    public sealed class UniversalAdditionalCameraData : UnityEngine.MonoBehaviour
    {
        public CameraRenderType renderType { get; set; }
        public System.Collections.Generic.List<UnityEngine.Camera> cameraStack => null;
    }

    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this UnityEngine.Camera camera) => null;
    }
}

namespace Lantern.EQ.Environment
{
    public class SkyController : UnityEngine.MonoBehaviour
    {
        public void SetActiveCameraTransform(UnityEngine.Transform cameraTransform, bool instant = false) { }
        public void SetSecondsPerDay(float seconds) { }
        public void SetEnabledSky(int skyIndex) { }
        public void UpdateSkyPosition() { }
        public void UpdateTime(float time) { }
        public void UpdateTimeLate(float deltaTime, float currentTime) { }
    }
}

// Audio (zone music and sounds).
namespace UnityEngine
{
    public enum AudioRolloffMode { Logarithmic, Linear, Custom }

    public sealed class AudioClip : Object
    {
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => null;
        public bool SetData(float[] data, int offsetSamples) => true;
    }

    public sealed class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float spatialBlend { get; set; }
        public bool loop { get; set; }
        public bool isPlaying => false;
        public float minDistance { get; set; }
        public float maxDistance { get; set; }
        public AudioRolloffMode rolloffMode { get; set; }
        public void Play() { }
        public void Stop() { }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }
    }

    public sealed class AudioListener : Behaviour { }

    public static class AudioSettings
    {
        public static int outputSampleRate => 48000;
    }
}

// LanternUnityTools' runtime MeltySynth and XMI reader (Assets/Scripts/Infrastructure/EQ/MeltySynth, Lantern/EQ/Audio/Xmi).
namespace Infrastructure.EQ.MeltySynth
{
    public sealed class Synthesizer
    {
        public Synthesizer(string soundFontPath, int sampleRate) { }
        public float MasterVolume { get; set; }
    }

    public sealed class MidiFile
    {
        public MidiFile(System.IO.Stream stream) { }
    }

    public sealed class MidiFileSequencer
    {
        public MidiFileSequencer(Synthesizer synthesizer) { }
        public void Play(MidiFile midiFile, bool loop) { }
        public void Stop() { }
        public void Render(System.Span<float> left, System.Span<float> right) { }
    }
}

namespace Lantern.EQ.Audio.Xmi
{
    public sealed class FormChunk { }

    public sealed class XmiFile
    {
        public FormChunk[] XmidiTracks => System.Array.Empty<FormChunk>();
        public System.IO.Stream WriteMidiTrack(int trackNumber) => new System.IO.MemoryStream();
    }

    public sealed class XmiFileReader : System.IDisposable
    {
        public XmiFileReader(System.IO.Stream stream) { }
        public XmiFile ReadXmiFile() => new XmiFile();
        public void Dispose() { }
    }
}

