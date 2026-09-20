using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace NoSnowyBiome
{
    internal sealed class ConditionalSnowSuppressor : MonoBehaviour
    {
        internal const int MaxRefreshesPerFrame = 64;
        private static readonly long RefreshBudgetTicks = System.Math.Max(1, Stopwatch.Frequency / 1000);
        private static readonly LinkedList<ConditionalSnowSuppressor> Active = new LinkedList<ConditionalSnowSuppressor>();
        private static LinkedListNode<ConditionalSnowSuppressor>? _cursor;
        private static int _remaining;
        private static float _nextSweep;
        private LinkedListNode<ConditionalSnowSuppressor>? _node;
        private readonly SnowMaterialState _materials = new SnowMaterialState();
        private int _revision = int.MinValue;
        private Vector3 _position;
        private float _waterLevel;

        private void OnEnable()
        {
            _revision = int.MinValue;
            _node = Active.AddLast(this);
            _cursor = _cursor ?? _node;
            RequestRefresh();
        }

        private void OnDisable()
        {
            Unregister();
            _materials.Restore();
        }

        private void OnDestroy()
        {
            Unregister();
            _materials.Restore();
        }

        private void Unregister()
        {
            if (_node == null) return;
            if (_cursor == _node)
                _cursor = Active.Count > 1 ? _node.Next ?? Active.First : null;
            Active.Remove(_node);
            _node = null;
            _remaining = System.Math.Min(_remaining, Active.Count);
        }

        internal static void RequestRefresh() => _nextSweep = 0f;

        internal static int RefreshFrame()
        {
            if (_remaining == 0)
            {
                if (Time.realtimeSinceStartup < _nextSweep) return 0;
                _remaining = Active.Count;
                _nextSweep = Time.realtimeSinceStartup + 1f;
            }

            // Keep the cursor across frames and list edits; never allocate a full-scene work array.
            long started = Stopwatch.GetTimestamp();
            int processed = 0;
            while (_remaining > 0 && _cursor != null && processed < MaxRefreshesPerFrame &&
                (processed == 0 || Stopwatch.GetTimestamp() - started < RefreshBudgetTicks))
            {
                ConditionalSnowSuppressor item = _cursor.Value;
                _cursor = _cursor.Next ?? Active.First;
                _remaining--;
                processed++;
                if (item != null) item.Refresh();
            }
            return processed;
        }

        internal static void RestoreAll()
        {
            foreach (ConditionalSnowSuppressor item in Active.ToArray())
            {
                if (item == null) continue;
                item.enabled = false;
                Destroy(item);
            }
            Active.Clear();
            _cursor = null;
            _remaining = 0;
            _nextSweep = 0f;
        }

        private void Refresh()
        {
            NoSnowyBiomePlugin? plugin = NoSnowyBiomePlugin.Instance;
            if (plugin == null || WorldGenerator.instance == null)
            {
                _materials.Restore();
                _revision = int.MinValue;
                return;
            }

            Vector3 position = transform.position;
            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            if (_revision == plugin.RulesRevision && (position - _position).sqrMagnitude < 0.0001f &&
                water == _waterLevel) return;
            _revision = plugin.RulesRevision;
            _position = position;
            _waterLevel = water;
            bool suppress = plugin.ShouldSuppress(position, out Heightmap.Biome biome, out float altitude);
            bool changed = _materials.SetSuppressed(gameObject, this, suppress);
            if (changed && suppress && NoSnowyBiomePlugin.VerboseLogging.Value)
                NoSnowyBiomePlugin.Log.LogInfo($"Suppressed snow on '{name}' in biome {biome} at altitude {altitude:0.##}");
        }
    }

    internal sealed class SnowMaterialState
    {
        private static readonly int AddSnowId = Shader.PropertyToID("_AddSnow");
        private const string AddSnowKeyword = "_ADDSNOW_ON";
        private static readonly Dictionary<int, SharedMaterial> SharedMaterials = new Dictionary<int, SharedMaterial>();
        private readonly List<RendererState> _renderers = new List<RendererState>();
        private bool _suppressed;
        internal static int CachedMaterialCount => SharedMaterials.Count;

        internal static bool HasSnowMaterials(GameObject gameObject)
        {
            foreach (Renderer renderer in Renderers(gameObject))
                foreach (Material material in renderer.sharedMaterials)
                    if (SupportsSnow(material)) return true;
            return false;
        }

        private static IEnumerable<Renderer> Renderers(GameObject gameObject)
        {
            return gameObject.GetComponentsInChildren<MeshRenderer>(true).Cast<Renderer>()
                .Concat(gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        private static bool SupportsSnow(Material material)
        {
            return material != null && (material.HasProperty(AddSnowId) || material.IsKeywordEnabled(AddSnowKeyword));
        }

        internal bool SetSuppressed(GameObject gameObject, ConditionalSnowSuppressor owner, bool suppress)
        {
            if (_suppressed == suppress) return false;
            if (!suppress)
            {
                Restore();
                return true;
            }

            try
            {
                Suppress(gameObject, owner);
                _suppressed = true;
                return true;
            }
            catch
            {
                Restore();
                throw;
            }
        }

        private void Suppress(GameObject gameObject, ConditionalSnowSuppressor owner)
        {
            foreach (Renderer renderer in Renderers(gameObject))
            {
                // Nested world objects retain their own biome/altitude decision and material ownership.
                if (renderer.GetComponentInParent<ConditionalSnowSuppressor>(true) != owner) continue;
                Material[] original = renderer.sharedMaterials;
                Material[] changed = (Material[])original.Clone();
                RendererState state = new RendererState(renderer, original, changed);
                _renderers.Add(state);
                bool any = false;
                for (int i = 0; i < original.Length; i++)
                {
                    Material source = original[i];
                    if (!SupportsSnow(source)) continue;
                    SharedMaterial shared = Acquire(source);
                    state.Leases[i] = shared;
                    changed[i] = shared.Material;
                    any = true;
                }
                if (!any) continue;
                renderer.sharedMaterials = changed;
            }
        }

        private static SharedMaterial Acquire(Material source)
        {
            int id = source.GetInstanceID();
            if (SharedMaterials.TryGetValue(id, out SharedMaterial? shared) && shared.Source != null &&
                shared.Source == source && shared.Material != null)
            {
                shared.Users++;
                return shared;
            }

            Material material = new Material(source);
            try
            {
                if (material.HasProperty(AddSnowId))
                {
                    int index = material.shader.FindPropertyIndex("_AddSnow");
                    if (index >= 0 && material.shader.GetPropertyType(index) == ShaderPropertyType.Int)
                        material.SetInteger(AddSnowId, 0);
                    else
                        material.SetFloat(AddSnowId, 0f);
                }
                material.DisableKeyword(AddSnowKeyword);
                shared = new SharedMaterial(id, source, material);
                SharedMaterials[id] = shared;
                return shared;
            }
            catch
            {
                UnityEngine.Object.Destroy(material);
                throw;
            }
        }

        private static void Release(SharedMaterial shared)
        {
            if (--shared.Users != 0) return;
            if (SharedMaterials.TryGetValue(shared.SourceId, out SharedMaterial? current) && ReferenceEquals(current, shared))
                SharedMaterials.Remove(shared.SourceId);
            if (shared.Material != null) UnityEngine.Object.Destroy(shared.Material);
        }

        internal void Restore()
        {
            foreach (RendererState state in _renderers)
            {
                if (state.Renderer != null)
                {
                    Material[] current = state.Renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < current.Length && i < state.Applied.Length; i++)
                    {
                        // Preserve slots replaced by another system since our snapshot.
                        if (state.Leases[i] == null || current[i] != state.Applied[i]) continue;
                        current[i] = state.Original[i];
                        changed = true;
                    }
                    if (changed) state.Renderer.sharedMaterials = current;
                }
                foreach (SharedMaterial? shared in state.Leases)
                    if (shared != null) Release(shared);
            }
            _renderers.Clear();
            _suppressed = false;
        }

        private sealed class SharedMaterial
        {
            internal readonly int SourceId;
            internal readonly Material Source;
            internal readonly Material Material;
            internal int Users = 1;

            internal SharedMaterial(int sourceId, Material source, Material material)
            {
                SourceId = sourceId;
                Source = source;
                Material = material;
            }
        }

        private sealed class RendererState
        {
            internal readonly Renderer Renderer;
            internal readonly Material[] Original;
            internal readonly Material[] Applied;
            internal readonly SharedMaterial?[] Leases;

            internal RendererState(Renderer renderer, Material[] original, Material[] applied)
            {
                Renderer = renderer;
                Original = original;
                Applied = applied;
                Leases = new SharedMaterial?[original.Length];
            }
        }
    }
}
