// Managed test doubles for ownership and scheduling tests, not a Unity rendering simulation.
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public bool Destroyed { get; private set; }
        public string name = "test";
        public int GetInstanceID() => _id;
        public static void Destroy(Object value)
        {
            if (value == null) return;
            if (value is MonoBehaviour behaviour)
            {
                behaviour.enabled = false;
                behaviour.InvokeMessage("OnDestroy");
            }
            value.Destroyed = true;
            if (value is Material material && material.IsClone) Material.DestroyedClones++;
        }
        public static bool operator ==(Object? a, Object? b)
        {
            bool aNull = ReferenceEquals(a, null) || a.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b.Destroyed;
            return aNull || bNull ? aNull == bNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? value) => ReferenceEquals(this, value);
        public override int GetHashCode() => _id;
    }

    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T? GetComponentInParent<T>(bool includeInactive) where T : Component => gameObject.GetComponentInParent<T>(includeInactive);
    }

    public class MonoBehaviour : Component
    {
        private bool _enabled = true;
        public bool enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                InvokeMessage(value ? "OnEnable" : "OnDisable");
            }
        }
        internal void InvokeMessage(string method) => GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(this, null);
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new();
        private readonly List<GameObject> _children = new();
        public GameObject? Parent;
        public bool activeSelf = true;
        public readonly Transform transform = new();

        public T AddComponent<T>() where T : Component
        {
            T result = (T)Activator.CreateInstance(typeof(T), true)!;
            result.gameObject = this;
            _components.Add(result);
            if (result is MonoBehaviour behaviour && activeSelf) behaviour.InvokeMessage("OnEnable");
            return result;
        }
        public void AddChild(GameObject child) { child.Parent = this; _children.Add(child); }
        public T? GetComponentInParent<T>(bool includeInactive) where T : Component
        {
            return (includeInactive || activeSelf ? _components.OfType<T>().FirstOrDefault(c => !c.Destroyed) : null)
                ?? Parent?.GetComponentInParent<T>(includeInactive);
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            return _components.OfType<T>().Where(c => !c.Destroyed && (includeInactive || activeSelf))
                .Concat(_children.SelectMany(c => c.GetComponentsInChildren<T>(includeInactive))).ToArray();
        }
    }

    public sealed class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }

    public class Renderer : Component
    {
        private Material[] _materials = Array.Empty<Material>();
        public int Assignments;
        public Material[] sharedMaterials
        {
            get => (Material[])_materials.Clone();
            set { _materials = (Material[])value.Clone(); Assignments++; }
        }
    }
    public sealed class MeshRenderer : Renderer { }
    public sealed class SkinnedMeshRenderer : Renderer { }

    public sealed class Shader
    {
        public bool HasAddSnow = true;
        public Rendering.ShaderPropertyType PropertyType;
        public static int PropertyToID(string name) => name.GetHashCode();
        public int FindPropertyIndex(string name) => HasAddSnow && name == "_AddSnow" ? 0 : -1;
        public Rendering.ShaderPropertyType GetPropertyType(int index) => PropertyType;
    }

    public sealed class Material : Object
    {
        public static int CreatedClones;
        public static int DestroyedClones;
        public readonly bool IsClone;
        public bool ThrowOnClone;
        public Shader shader;
        public float FloatSnow = 1;
        public int IntegerSnow = 1;
        public bool Keyword;
        public bool enableInstancing = true;

        public Material(Shader shader) { this.shader = shader; }
        public Material(Material source)
        {
            if (source.ThrowOnClone) throw new InvalidOperationException("Simulated material copy failure");
            shader = source.shader;
            FloatSnow = source.FloatSnow;
            IntegerSnow = source.IntegerSnow;
            Keyword = source.Keyword;
            enableInstancing = source.enableInstancing;
            IsClone = true;
            CreatedClones++;
        }
        public bool HasProperty(int id) => shader.HasAddSnow && id == Shader.PropertyToID("_AddSnow");
        public bool IsKeywordEnabled(string name) => name == "_ADDSNOW_ON" && Keyword;
        public void DisableKeyword(string name) { if (name == "_ADDSNOW_ON") Keyword = false; }
        public void SetFloat(int id, float value)
        {
            if (shader.PropertyType == Rendering.ShaderPropertyType.Int) throw new Exception("Float write to integer shader property");
            FloatSnow = value;
        }
        public void SetInteger(int id, int value)
        {
            if (shader.PropertyType != Rendering.ShaderPropertyType.Int) throw new Exception("Integer write to float shader property");
            IntegerSnow = value;
        }
    }
    public static class Time { public static float realtimeSinceStartup; }
}

namespace UnityEngine.Rendering
{
    public enum ShaderPropertyType { Float, Int }
}

public sealed class WorldGenerator { public static WorldGenerator? instance = new(); }
public sealed class ZoneSystem { public static ZoneSystem? instance = new(); public float m_waterLevel = 30; }
public sealed class Heightmap { public enum Biome { None = 0, Mountain = 4 } }

namespace NoSnowyBiome
{
    internal sealed class NoSnowyBiomePlugin
    {
        internal static NoSnowyBiomePlugin? Instance = new();
        internal static readonly TestConfig VerboseLogging = new();
        internal static readonly TestLog Log = new();
        internal int RulesRevision;
        internal int Evaluations;
        internal Func<UnityEngine.Vector3, bool> Suppression = _ => true;
        internal bool ShouldSuppress(UnityEngine.Vector3 position, out Heightmap.Biome biome, out float altitude)
        {
            Evaluations++;
            biome = Heightmap.Biome.Mountain;
            altitude = position.y - 30;
            return Suppression(position);
        }
    }
    internal sealed class TestConfig { public bool Value { get; set; } }
    internal sealed class TestLog
    {
        public readonly List<string> Warnings = new();
        public void LogInfo(string message) { }
        public void LogWarning(string message) => Warnings.Add(message);
    }
}
