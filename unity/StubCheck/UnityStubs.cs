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
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
        public T[] GetComponentsInChildren<T>() => System.Array.Empty<T>();
        public T GetComponentInChildren<T>() => default;
    }

    public class Renderer : Component
    {
        public Bounds bounds => default;
    }

    public struct Bounds
    {
        public Vector3 min => default;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 localPosition { get; set; }
        public Transform parent { get; set; }
        public void LookAt(Vector3 worldPosition) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator *(Vector3 a, float d) => a;
        public static Vector3 operator *(float d, Vector3 a) => a;
    }

    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum LightType { Spot, Directional, Point }
    public enum KeyCode { Q = 113, E = 101 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
    }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
    }

    public static class Input
    {
        public static float GetAxis(string axisName) => 0;
        public static bool GetKey(KeyCode key) => false;
    }

    public static class Time
    {
        public static float deltaTime => 0;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
    }

    public static class Application
    {
        public static string dataPath => "Assets";
    }

    public static class PlayerPrefs
    {
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static void SetString(string key, string value) { }
    }

    public class GUIStyle { }

    public class GUISkin : Object
    {
        public GUIStyle box => null;
    }

    public static class GUI
    {
        public static GUISkin skin => null;
        public static void Label(Rect position, string text) { }
    }

    public static class GUILayout
    {
        public static void BeginArea(Rect screenRect, GUIStyle style) { }
        public static void EndArea() { }
        public static void Label(string text) { }
        public static string TextField(string text) => text;
        public static string PasswordField(string password, char maskChar) => password;
        public static bool Button(string text) => false;
        public static void Space(float pixels) { }
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
    public enum AnimationType { LocomotionWalk = 17, LocomotionRun = 18, PassiveStand = 32 }

    public class CharacterAnimationController : UnityEngine.MonoBehaviour
    {
        public void Initialize(AnimationType initialAnimation) { }
        public void SetNewConstantState(AnimationType animationType, int priority, float speed = 1f) { }
    }
}
