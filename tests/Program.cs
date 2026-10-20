using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NoSnowyBiome;

int assertions = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAILED: " + name);
    assertions++;
}
void Reject(string yaml, string name)
{
    bool rejected = false;
    try { SnowRules.Parse(yaml); }
    catch { rejected = true; }
    Check(rejected, name);
}

var defaults = SnowRules.Parse(SnowRuleFile.DefaultYaml);
Check(defaults.Count == 1 && defaults[0].Biome == "Mistlands" && defaults[0].NoSnow, "first-run rule enables Mistlands snow suppression only");
Check(defaults[0].NoSnowMinAltitude == 50, "first-run altitude remains 50");
Check(SnowRules.Parse("[]").Count == 0, "explicit clear");
var rules = SnowRules.Parse("- biome: Mountain\n  noSnow: true\n- biome: 'Custom # One'\n  noSnow: true\n  noSnowMinAltitude: -10.5\n");
Check(rules[0].NoSnowMinAltitude == 50, "default altitude");
Check(rules[1].Biome == "Custom # One" && rules[1].NoSnowMinAltitude == -10.5f, "quoted names and negative altitude");
Check(!SnowRules.Parse("- biome: Mountain")[0].NoSnow, "missing noSnow is disabled");
Check(SnowRules.Parse("[{biome: Mountain, noSnow: true, noSnowMinAltitude: 5e1}]")[0].NoSnowMinAltitude == 50, "flow YAML");
var savedCulture = System.Globalization.CultureInfo.CurrentCulture;
try
{
    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
    Check(SnowRules.Parse("- biome: Mountain\n  noSnowMinAltitude: 50.5")[0].NoSnowMinAltitude == 50.5f, "culture independent numbers");
}
finally { System.Globalization.CultureInfo.CurrentCulture = savedCulture; }

Reject("", "empty save does not clear rules");
Reject("# partial save", "comment-only file rejected");
Reject("- biome: Mountain\n  noSnow: maybe", "invalid boolean");
Reject("- biome: Mountain\n  noSnowMinAltitude: .nan", "NaN altitude");
Reject("- biome: Mountain\n  noSnowMinAltitude: .inf", "infinite altitude");
Reject("- biome: Mountain\n  noSnowMinAltitude: -Infinity", "negative infinite altitude");
Reject("- biome: Mountain\n  terrain: Plains", "EWD fields not accepted");
Reject("- biome: Mountain\n  noSnowWhitening: true", "legacy field not accepted");
Reject("- biome: Mountain\n- biome: ' mountain '", "case-insensitive duplicate rule");
Reject("- biome: Mountain\n  noSnow: true\n  noSnow: false", "duplicate key");
Reject("- noSnow: true", "missing biome");
Reject("- biome: null", "null biome");
Reject("- null", "null entry");
Reject("biome: Mountain", "mapping instead of sequence");
Reject("- biome: 1024", "unstable numeric ID");
Reject("- biome: -2147483648", "negative numeric ID");
Reject("- biome: None", "None not configurable");
Reject("- biome: Mountain,Plains", "biome masks not configurable");
Reject(new string(' ', SnowRules.MaximumYamlLength + 1), "oversized input");

var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Mountain"] = 4 };
int? Resolve(string name) => names.TryGetValue(name, out int id) ? id : null;
var initial = SnowRules.Resolve(rules, Resolve);
Check(initial.Count == 1, "vanilla works without EWD");
Check(!SnowRules.ShouldSuppress(initial, 4, 49.99f), "below threshold");
Check(SnowRules.ShouldSuppress(initial, 4, 50), "inclusive threshold");
Check(SnowRules.ShouldSuppress(initial, 4, 500), "high altitude");
Check(!SnowRules.ShouldSuppress(initial, 16, 500), "unconfigured biome unchanged");
Check(SnowRules.Equivalent(initial, SnowRules.Resolve(rules, Resolve)), "unchanged map does not invalidate");
names["Custom # One"] = 1024;
var late = SnowRules.Resolve(rules, Resolve);
Check(late.Count == 2 && SnowRules.ShouldSuppress(late, 1024, 10), "late EWD sync resolves custom name");
Check(!SnowRules.Equivalent(initial, late), "late resolution invalidates visuals");
names["Custom # One"] = 2048;
var reordered = SnowRules.Resolve(rules, Resolve);
Check(!reordered.ContainsKey(1024) && reordered.ContainsKey(2048), "EWD IDs can be reassigned");
Check(!SnowRules.Equivalent(late, reordered), "reassigned ID invalidates visuals");
names.Remove("Custom # One");
Check(SnowRules.Equivalent(initial, SnowRules.Resolve(rules, Resolve)), "removed custom mapping clears suppression");
var disabled = SnowRules.Resolve(SnowRules.Parse("- biome: Mountain\n  noSnow: false"), Resolve);
Check(!SnowRules.ShouldSuppress(disabled, 4, 500) && !SnowRules.Equivalent(initial, disabled), "false restores visuals");
var changedAltitude = SnowRules.Resolve(SnowRules.Parse("- biome: Mountain\n  noSnow: true\n  noSnowMinAltitude: 100"), Resolve);
Check(!SnowRules.Equivalent(initial, changedAltitude), "altitude edits invalidate visuals");
names["Custom # One"] = int.MinValue;
Check(SnowRules.Resolve(rules, Resolve).ContainsKey(int.MinValue), "valid sign-bit custom biome returned by EWD");
var buffer = new Dictionary<int, BiomeSnowRule> { [123] = rules[0] };
Check(ReferenceEquals(buffer, SnowRules.Resolve(rules, Resolve, buffer)) && !buffer.ContainsKey(123), "resolution reuses caller buffer and clears stale entries");
SnowRules.Resolve(Array.Empty<BiomeSnowRule>(), Resolve, buffer);
Check(buffer.Count == 0, "reused resolution buffer handles empty rules");

string scratch = Path.Combine(Path.GetTempPath(), "NoSnowyBiome-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    string path = Path.Combine(scratch, "NoSnowyBiome.yaml");
    DateTime fileStamp = DateTime.UtcNow;
    void WriteRules(string text)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, fileStamp = fileStamp.AddSeconds(2));
    }
    var warnings = new List<string>();
    var file = new SnowRuleFile(path, warnings.Add);
    file.EnsureExists();
    Check(file.TryRead(out var yaml) && yaml == SnowRuleFile.DefaultYaml, "first file read");
    Check(!file.TryRead(out _), "unchanged file ignored");
    int reads = file.ContentReadCount;
    for (int i = 0; i < 100; i++) file.TryRead(out _);
    Check(file.ContentReadCount == reads, "100 idle polls do not reopen/read YAML contents");
    WriteRules("[]");
    file.EnsureExists();
    Check(File.ReadAllText(path) == "[]", "existing config never overwritten");
    Check(file.TryRead(out yaml) && yaml == "[]", "live clear read");
    WriteRules("- biome: Mountain\n  noSnow: bad");
    Check(!file.TryRead(out _) && warnings.Count == 1, "invalid edit not published");
    Check(!file.TryRead(out _) && warnings.Count == 1, "invalid edit warning not spammed");
    reads = file.ContentReadCount;
    for (int i = 0; i < 100; i++) file.TryRead(out _);
    Check(file.ContentReadCount == reads, "invalid but unchanged YAML is not reread/reparsed");
    WriteRules("[]");
    Check(file.TryRead(out yaml) && yaml == "[]", "valid edit recovers after error");
    File.Delete(path);
    Check(!file.TryRead(out _), "deletion does not clear last good rules");
    int warned = warnings.Count;
    Check(!file.TryRead(out _) && warnings.Count == warned, "missing file warning not spammed");
    WriteRules(SnowRuleFile.DefaultYaml);
    Check(file.TryRead(out _), "atomic replacement recovers");
    WriteRules("- biome: Mountain\n  noSnow: true");
    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Check(!file.TryRead(out _), "locked file preserves last good rules");
    Check(file.TryRead(out yaml) && yaml.Contains("noSnow: true"), "read error retries even when file stamp has not changed");
    WriteRules("- biome: Mountain\n  noSnowMinAltitude: 50");
    Check(file.TryRead(out _), "prepare equal-length edit");
    WriteRules("- biome: Mountain\n  noSnowMinAltitude: 60");
    Check(file.TryRead(out yaml) && yaml.Contains(": 60"), "equal-length edit detected by modification time");
    reads = file.ContentReadCount;
    File.SetLastWriteTimeUtc(path, fileStamp = fileStamp.AddSeconds(2));
    Check(!file.TryRead(out _) && file.ContentReadCount == reads + 1, "timestamp-only change does not republish identical contents");
}
finally
{
    File.Delete(Path.Combine(scratch, "NoSnowyBiome.yaml"));
    Directory.Delete(scratch);
}

VisualRegression.Run(Check);
EwdCompatRegression.Run(Check);

string dll = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "../../../../bin/Debug/NoSnowyBiome.dll"));
Assembly merged = Assembly.LoadFile(dll);
Check((string)merged.GetType("NoSnowyBiome.SnowRuleFile", true)!.GetField("DefaultYaml", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()! == SnowRuleFile.DefaultYaml, "actual merged DLL contains current first-run defaults");
Type parser = merged.GetType("NoSnowyBiome.SnowRules", true)!;
var parsed = (System.Collections.IList)parser.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { SnowRuleFile.DefaultYaml })!;
Check(parsed.Count == 1, "actual merged DLL can deserialize with internalized YamlDotNet");

// Exercise the vendored CustomSyncedValue method, without Unity initialization or network transport.
Type customType = merged.GetType("ServerSync.CustomSyncedValue`1", true)!.MakeGenericType(typeof(string));
object value = RuntimeHelpers.GetUninitializedObject(customType);
Type baseType = customType.BaseType!;
FieldInfo owner = baseType.GetField("localIsOwner", BindingFlags.NonPublic | BindingFlags.Instance)!;
FieldInfo local = baseType.GetField("LocalBaseValue")!;
PropertyInfo current = customType.GetProperty("Value")!;
MethodInfo assign = customType.GetMethod("AssignLocalValue")!;
owner.SetValue(value, true);
assign.Invoke(value, new object[] { "local rules" });
Check((string)current.GetValue(value)! == "local rules", "standalone uses local rules");
local.SetValue(value, current.GetValue(value));
owner.SetValue(value, false);
current.SetValue(value, "server rules");
assign.Invoke(value, new object[] { "edited local rules" });
Check((string)current.GetValue(value)! == "server rules", "client file cannot replace active server rules");
Check((string)local.GetValue(value)! == "edited local rules", "local edits retained for disconnect");
// ConfigSync.resetConfigsFromServer uses this saved local value on shutdown; checked below in IL.
current.SetValue(value, local.GetValue(value));
local.SetValue(value, null);
owner.SetValue(value, true);
Check((string)current.GetValue(value)! == "edited local rules", "saved local value is recoverable");
assign.Invoke(value, new object[] { "second server local base" });
Check((string)current.GetValue(value)! == "second server local base", "local authority resumes");

using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(dll)!);
using var module = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = resolver });
IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Flatten(t.NestedTypes)));
var allTypes = Flatten(module.Types).ToArray();
var plugin = allTypes.Single(t => t.FullName == "NoSnowyBiome.NoSnowyBiomePlugin");
var dependency = plugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInDependency");
Check((string)dependency.ConstructorArguments[0].Value == "expand_world_data" &&
    Convert.ToInt32(dependency.ConstructorArguments[1].Value) == 2, "EWD is a soft dependency in final DLL");
Check(!module.AssemblyReferences.Any(r => r.Name is "ExpandWorldData" or "ServerSync" or "YamlDotNet"), "DLL has no external EWD/ServerSync/YAML dependency");
var material = allTypes.Single(t => t.FullName == "NoSnowyBiome.SnowMaterialState");
var materialCalls = material.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>().ToArray();
Check(materialCalls.Any(m => m.Name == "SetInteger") && materialCalls.Any(m => m.Name == "SetFloat"), "integer piece and float vegetation writes");
Check(!materialCalls.Any(m => m.Name == "SetPropertyBlock"), "does not overwrite other systems' property blocks");
var sync = allTypes.Single(t => t.FullName == "ServerSync.ConfigSync");
var receiver = sync.Methods.Single(m => m.Name == "HandleConfigSyncRPC");
Check(receiver.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "IsAdmin" && m.DeclaringType.Name == "ZNet"), "merged sync checks current public admin API");
Check(receiver.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_IsLocked"), "merged sync checks server lock");
var reset = sync.Methods.Single(m => m.Name == "resetConfigsFromServer");
Check(reset.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "LocalBaseValue" && f.DeclaringType.Name == "CustomSyncedValueBase") &&
    reset.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_BoxedValue" && m.DeclaringType.Name == "CustomSyncedValueBase"), "actual shutdown reset restores custom local value");
Check(!allTypes.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
    .Any(i => i.Operand is FieldReference f && f.DeclaringType.Name == "ZRoutedRpc" && f.Name == "Everybody"), "no obsolete Everybody field accesses");

var compat = allTypes.Single(t => t.FullName == "NoSnowyBiome.ExpandWorldDataCompat");
Check(compat.Methods.Single(m => m.Name == "Initialize").Body.Instructions
    .Any(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, ExpandWorldDataCompat.BiomeManagerTypeName)),
    "merged DLL binds the current EWD manager namespace");
int ewdIndex = Array.IndexOf(args, "--ewd");
if (ewdIndex >= 0)
{
    if (ewdIndex != args.Length - 2) throw new ArgumentException("Use --ewd <DLL> after the game managed paths.");
    using var ewd = ModuleDefinition.ReadModule(Path.GetFullPath(args[ewdIndex + 1]));
    var manager = ewd.GetType(ExpandWorldDataCompat.BiomeManagerTypeName);
    Check(manager != null && manager.IsPublic, "installed EWD exposes the expected public manager");
    var lookup = manager!.Methods.Single(m => m.Name == "TryGetBiome");
    Check(lookup.IsPublic && lookup.IsStatic && !lookup.HasGenericParameters &&
        lookup.ReturnType.FullName == "System.Boolean" && lookup.Parameters.Count == 2 &&
        lookup.Parameters[0].ParameterType.FullName == "System.String" && lookup.Parameters[1].IsOut &&
        lookup.Parameters[1].ParameterType.FullName == "Heightmap/Biome&" &&
        lookup.Parameters[1].ParameterType.GetElementType().Scope is AssemblyNameReference biomeScope &&
        biomeScope.Name == "assembly_valheim", "installed EWD public lookup matches the cached delegate signature");
    Check(lookup.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ToLowerInvariant") &&
        lookup.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "TryGetValue"),
        "installed EWD lookup retains case-insensitive name mapping");
    Console.WriteLine($"Installed EWD API metadata checks passed: {args[ewdIndex + 1]}");
}

foreach (string managedPath in args.Skip(1).Take(ewdIndex < 0 ? int.MaxValue : ewdIndex - 1))
{
    using var gameResolver = new DefaultAssemblyResolver();
    gameResolver.AddSearchDirectory(Path.GetFullPath(managedPath));
    gameResolver.AddSearchDirectory(Path.GetDirectoryName(dll)!);
    using var consumer = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = gameResolver });
    var gameNames = new HashSet<string> { "assembly_valheim", "assembly_utils", "assembly_guiutils" };
    foreach (MemberReference reference in consumer.GetMemberReferences())
    {
        if (reference.DeclaringType.GetElementType().Scope is not AssemblyNameReference scope || !gameNames.Contains(scope.Name)) continue;
        switch (reference)
        {
            case MethodReference method:
                var definition = method.Resolve();
                Check(definition != null && definition.IsPublic, $"original public method {managedPath}: {reference.FullName}");
                break;
            case FieldReference field:
                var fieldDefinition = field.Resolve();
                Check(fieldDefinition != null && fieldDefinition.IsPublic, $"original public field {managedPath}: {reference.FullName}");
                break;
        }
    }
    using var game = ModuleDefinition.ReadModule(Path.Combine(managedPath, "assembly_valheim.dll"));
    foreach (string typeName in new[] { "StaticPhysics", "Pickable", "PickableItem", "Plant", "TreeBase", "TreeLog", "Destructible", "MineRock5", "Piece" })
        Check(game.Types.Single(t => t.Name == typeName).Methods.Any(m => m.Name == "Awake" && !m.IsStatic && !m.HasParameters && m.ReturnType.FullName == "System.Void"), $"original Awake patch target {typeName}");
    Console.WriteLine($"Original game API and visual patch target checks passed: {managedPath}");
}

Console.WriteLine($"PASS: {assertions} assertions. Rule/file tests, managed visual and EWD adapter tests, merged-DLL checks, and isolated CustomSyncedValue tests; no actual game/network/GPU runtime was exercised.");
