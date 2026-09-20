using NoSnowyBiome;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

internal static class VisualRegression
{
    private static (GameObject Object, MeshRenderer Renderer, ConditionalSnowSuppressor Owner) Create(params Material[] materials)
    {
        GameObject obj = new();
        obj.transform.position = new Vector3(0, 100, 0);
        MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        return (obj, renderer, obj.AddComponent<ConditionalSnowSuppressor>());
    }

    internal static void Run(Action<bool, string> check)
    {
        Material source = new(new Shader { PropertyType = ShaderPropertyType.Float }) { Keyword = true };
        var first = Create(source, source);
        var second = Create(source);
        SnowMaterialState one = new(), two = new();
        int created = Material.CreatedClones;
        one.SetSuppressed(first.Object, first.Owner, true);
        two.SetSuppressed(second.Object, second.Owner, true);
        Material shared = first.Renderer.sharedMaterials[0];
        check(Material.CreatedClones == created + 1 && SnowMaterialState.CachedMaterialCount == 1, "same original uses one shared material across slots and objects");
        check(ReferenceEquals(shared, first.Renderer.sharedMaterials[1]) && ReferenceEquals(shared, second.Renderer.sharedMaterials[0]), "all slots reference the same variant");
        check(shared.FloatSnow == 0 && !shared.Keyword && shared.enableInstancing && source.FloatSnow == 1 && source.Keyword, "snow variant does not mutate source");
        int assignments = first.Renderer.Assignments;
        check(!one.SetSuppressed(first.Object, first.Owner, true) && Material.CreatedClones == created + 1 && first.Renderer.Assignments == assignments, "same snow state does no renderer/material work");
        one.Restore();
        check(!shared.Destroyed && SnowMaterialState.CachedMaterialCount == 1, "restoring one user leaves other user's variant alive");
        check(first.Renderer.sharedMaterials.All(m => ReferenceEquals(m, source)), "original slots restored");
        two.Restore();
        check(shared.Destroyed && SnowMaterialState.CachedMaterialCount == 0, "last release destroys cached variant");
        int destroyed = Material.DestroyedClones;
        two.Restore();
        check(Material.DestroyedClones == destroyed, "double restore is harmless");

        Material piece = new(new Shader { PropertyType = ShaderPropertyType.Int });
        Material unsupported = new(new Shader { HasAddSnow = false });
        var mixed = Create(source, piece, unsupported);
        SnowMaterialState mixedState = new();
        mixedState.SetSuppressed(mixed.Object, mixed.Owner, true);
        check(mixed.Renderer.sharedMaterials[0].FloatSnow == 0 && mixed.Renderer.sharedMaterials[1].IntegerSnow == 0, "mixed float/int shader properties use correct setters");
        check(ReferenceEquals(mixed.Renderer.sharedMaterials[2], unsupported) && piece.IntegerSnow == 1, "unsupported and original materials remain untouched");
        Material replacement = new(new Shader());
        Material[] slots = mixed.Renderer.sharedMaterials;
        slots[1] = replacement;
        mixed.Renderer.sharedMaterials = slots;
        mixedState.Restore();
        check(ReferenceEquals(mixed.Renderer.sharedMaterials[0], source) && ReferenceEquals(mixed.Renderer.sharedMaterials[1], replacement), "external slot replacement survives restore");
        check(SnowMaterialState.CachedMaterialCount == 0, "externally replaced slots release their cache leases");

        var vanished = Create(source);
        SnowMaterialState vanishedState = new();
        vanishedState.SetSuppressed(vanished.Object, vanished.Owner, true);
        Object.Destroy(vanished.Renderer);
        vanishedState.Restore();
        check(SnowMaterialState.CachedMaterialCount == 0, "destroyed renderer still releases shared material");

        Material failing = new(new Shader()) { ThrowOnClone = true };
        var partial = Create(source, failing);
        SnowMaterialState partialState = new();
        bool failed = false;
        try { partialState.SetSuppressed(partial.Object, partial.Owner, true); }
        catch (InvalidOperationException) { failed = true; }
        check(failed && SnowMaterialState.CachedMaterialCount == 0 && ReferenceEquals(partial.Renderer.sharedMaterials[0], source), "partial material-copy failure rolls back leases");

        var parent = Create(source);
        var child = Create(source);
        parent.Object.AddChild(child.Object);
        SnowMaterialState parentState = new(), childState = new();
        parentState.SetSuppressed(parent.Object, parent.Owner, true);
        check(ReferenceEquals(child.Renderer.sharedMaterials[0], source), "nested owner keeps its own material decision");
        childState.SetSuppressed(child.Object, child.Owner, true);
        check(ReferenceEquals(parent.Renderer.sharedMaterials[0], child.Renderer.sharedMaterials[0]), "independent nested owners share the same variant");
        parentState.Restore();
        childState.Restore();

        var lodRoot = Create(source);
        GameObject inactiveLod = new() { activeSelf = false };
        MeshRenderer lodRenderer = inactiveLod.AddComponent<MeshRenderer>();
        lodRenderer.sharedMaterials = new[] { source };
        lodRoot.Object.AddChild(inactiveLod);
        SnowMaterialState lodState = new();
        lodState.SetSuppressed(lodRoot.Object, lodRoot.Owner, true);
        check(ReferenceEquals(lodRoot.Renderer.sharedMaterials[0], lodRenderer.sharedMaterials[0]) && !ReferenceEquals(lodRenderer.sharedMaterials[0], source), "inactive wear/LOD children also share the variant");
        lodRoot.Renderer.sharedMaterials = Array.Empty<Material>();
        lodState.Restore();
        check(SnowMaterialState.CachedMaterialCount == 0 && ReferenceEquals(lodRenderer.sharedMaterials[0], source), "shrunk material arrays release every lease without restoring removed slots");
        ConditionalSnowSuppressor.RestoreAll();
        check(SnowMaterialState.CachedMaterialCount == 0, "ownership fixtures cleaned up");

        // Test the real round-robin and state-transition code, using managed stand-ins for Unity calls.
        Time.realtimeSinceStartup = 0;
        NoSnowyBiomePlugin.Instance = new NoSnowyBiomePlugin();
        var plugin = NoSnowyBiomePlugin.Instance;
        var objects = Enumerable.Range(0, 1000).Select(_ => Create(source)).ToArray();
        created = Material.CreatedClones;
        int firstFrame = ConditionalSnowSuppressor.RefreshFrame();
        check(firstFrame > 0 && firstFrame <= ConditionalSnowSuppressor.MaxRefreshesPerFrame && plugin.Evaluations < objects.Length, "large initial load is spread across frames");

        int Drain(int target)
        {
            int frames = 0;
            while (plugin.Evaluations < target && frames++ < 10000)
            {
                Time.realtimeSinceStartup += 1f / 60;
                int processed = ConditionalSnowSuppressor.RefreshFrame();
                if (processed > ConditionalSnowSuppressor.MaxRefreshesPerFrame) throw new Exception("Frame cap exceeded");
            }
            check(plugin.Evaluations >= target, "round-robin reaches every active object");
            return frames;
        }

        Drain(objects.Length);
        check(Material.CreatedClones == created + 1 && SnowMaterialState.CachedMaterialCount == 1, "1000 identical renderers create one variant, not 1000");
        assignments = objects.Sum(o => o.Renderer.Assignments);
        foreach (var obj in objects) obj.Object.transform.position = new Vector3(5, 101, 0);
        ConditionalSnowSuppressor.RequestRefresh();
        Drain(plugin.Evaluations + objects.Length);
        check(Material.CreatedClones == created + 1 && objects.Sum(o => o.Renderer.Assignments) == assignments, "movement with unchanged suppression neither clones nor rebinds");
        plugin.RulesRevision++;
        ConditionalSnowSuppressor.RequestRefresh();
        Drain(plugin.Evaluations + objects.Length);
        check(Material.CreatedClones == created + 1 && objects.Sum(o => o.Renderer.Assignments) == assignments, "rule revision with unchanged result neither clones nor rebinds");

        plugin.Suppression = _ => false;
        plugin.RulesRevision++;
        ConditionalSnowSuppressor.RequestRefresh();
        Drain(plugin.Evaluations + objects.Length);
        check(objects.All(o => ReferenceEquals(o.Renderer.sharedMaterials[0], source)) && SnowMaterialState.CachedMaterialCount == 0, "spread-out disable restores all originals and evicts shared cache");
        plugin.Suppression = _ => true;
        plugin.RulesRevision++;
        ConditionalSnowSuppressor.RequestRefresh();
        ConditionalSnowSuppressor.RefreshFrame();
        // Remove a pending node and add a new one while a pass is in progress.
        objects[500].Owner.enabled = false;
        var added = Create(source);
        int limit = 0;
        while (ReferenceEquals(added.Renderer.sharedMaterials[0], source) && limit++ < 10000)
        {
            Time.realtimeSinceStartup += 1f / 60;
            ConditionalSnowSuppressor.RefreshFrame();
        }
        check(!ReferenceEquals(added.Renderer.sharedMaterials[0], source) && ReferenceEquals(objects[500].Renderer.sharedMaterials[0], source), "removal/addition during a pass does not lose pending work");
        objects[500].Owner.enabled = true;
        limit = 0;
        while (ReferenceEquals(objects[500].Renderer.sharedMaterials[0], source) && limit++ < 10000)
        {
            Time.realtimeSinceStartup += 1f / 60;
            ConditionalSnowSuppressor.RefreshFrame();
        }
        check(!ReferenceEquals(objects[500].Renderer.sharedMaterials[0], source), "re-enabled owner is registered and refreshed");
        ConditionalSnowSuppressor.RestoreAll();
        check(SnowMaterialState.CachedMaterialCount == 0 && ConditionalSnowSuppressor.RefreshFrame() == 0, "unload restores all owners and clears scheduler/cache");

        var slow = Enumerable.Range(0, 3).Select(_ => Create(source)).ToArray();
        plugin.Suppression = _ => { Thread.Sleep(5); return true; };
        check(ConditionalSnowSuppressor.RefreshFrame() == 1, "elapsed-time budget stops after an expensive object while guaranteeing progress");
        plugin.Suppression = _ => true;
        WorldGenerator.instance = null;
        ConditionalSnowSuppressor.RequestRefresh();
        for (int i = 0; i < 10; i++)
        {
            Time.realtimeSinceStartup += 1;
            ConditionalSnowSuppressor.RefreshFrame();
        }
        check(SnowMaterialState.CachedMaterialCount == 0, "missing world restores queued materials");
        WorldGenerator.instance = new WorldGenerator();
        ConditionalSnowSuppressor.RestoreAll();
        Console.WriteLine("Managed visual regression: 1000 equal-source renderers shared one clone; unchanged-state movement/revisions produced no material rebinds. This is an operation-count test, not a GPU/FPS measurement.");
    }
}
