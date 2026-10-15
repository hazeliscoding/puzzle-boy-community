using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PuzzleBoy.Community.Classic;
using PuzzleBoy.Community.Content;
using PuzzleBoy.Community.Graphics;
using PuzzleBoy.Community.Save;

namespace PuzzleBoy.Community.Tests;

public class StandaloneTests
{
    private const string ClassicNamespace = "PuzzleBoy.Community.Classic";
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly Game = typeof(CommunityGame).Assembly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    private static string[] ReferencesAmong(Assembly assembly, IEnumerable<string> names) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Intersect(names).ToArray();

    private static IEnumerable<MethodBase> MethodsOf(Type type) =>
        type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));

    private static MethodBase Method(Type type, string name) => MethodsOf(type).Single(m => m.Name == name);

    /// <summary>The types, methods and fields <paramref name="method"/>'s IL names, found by walking its opcodes.</summary>
    private static List<MemberInfo> References(MethodBase method)
    {
        var found = new List<MemberInfo>();
        if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            return found;
        }

        var typeArgs = method.DeclaringType is { IsGenericType: true } t ? t.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var i = 0; i < il.Length;)
        {
            var op = il[i] == 0xFE ? OpCodesByValue[(short)(0xFE00 | il[i + 1])] : OpCodesByValue[il[i]];
            i += op.Size;
            switch (op.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                    found.Add(method.Module.ResolveMember(BitConverter.ToInt32(il, i), typeArgs, methodArgs)!);
                    i += 4;
                    break;
                case OperandType.InlineSwitch:
                    i += 4 + (4 * BitConverter.ToInt32(il, i));
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    i += 8;
                    break;
                default:
                    i += 4;
                    break;
            }
        }

        return found;
    }

    private static Type? Owner(MemberInfo member) => member as Type ?? member.DeclaringType;

    /// <summary>
    /// Every method of the game that <paramref name="roots"/> can reach through direct calls, lambdas and the state
    /// machines of iterators and async methods. Virtual calls into the game aren't followed.
    /// </summary>
    private static HashSet<MethodBase> Reachable(params MethodBase[] roots)
    {
        var seen = new HashSet<MethodBase>();
        var queue = new Queue<MethodBase>(roots);
        while (queue.TryDequeue(out var method))
        {
            if (!seen.Add(method))
            {
                continue;
            }

            foreach (var member in References(method))
            {
                if (member is MethodBase called && called.Module == Game.ManifestModule)
                {
                    queue.Enqueue(called is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericMethodDefinition() : called);
                }

                // A lambda's closure, an iterator or an async state machine runs the code it was made for.
                var generated = new[] { Owner(member) }.Concat(member is MethodInfo { IsGenericMethod: true } m ? m.GetGenericArguments() : [])
                    .Where(t => t is not null && t.Assembly == Game && t.IsDefined(typeof(CompilerGeneratedAttribute)));
                foreach (var type in generated)
                {
                    foreach (var inner in MethodsOf(type!))
                    {
                        queue.Enqueue(inner);
                    }
                }
            }
        }

        return seen;
    }

    private static bool TouchesClassic(MethodBase method) =>
        method.DeclaringType?.Namespace == ClassicNamespace || References(method).Any(m => Owner(m)?.Namespace == ClassicNamespace);

    /// <summary>The game's types, nested ones too, that name a type from <paramref name="assembly"/> in their IL or fields.</summary>
    private static List<Type> TypesUsing(string assembly) =>
        [.. Game.GetTypes().Where(type =>
            type.GetFields(Declared).Any(f => f.FieldType.Assembly.GetName().Name == assembly)
            || MethodsOf(type).SelectMany(References).Any(m => Owner(m)?.Assembly.GetName().Name == assembly))];

    [Fact]
    public void The_game_references_no_audio_reader_for_a_Nocturne_install() =>
        Assert.Empty(ReferencesAmong(Game, ["Fmod5Sharp"]));

    [Fact]
    public void Only_the_classic_module_reads_Unity_bundles()
    {
        var users = TypesUsing("AssetsTools.NET");
        Assert.Contains(typeof(ClassicInstall), users);
        Assert.All(users, t => Assert.Equal(ClassicNamespace, t.Namespace));
    }

    [Fact]
    public void The_title_and_content_loading_never_reach_the_classic_module()
    {
        var startup = Reachable(
            Method(typeof(CommunityGame), ".ctor"),
            Method(typeof(CommunityGame), "LoadContent"),
            Method(typeof(CommunityGame), "DrawTitle"),
            Method(typeof(ContentManifest), "Load"),
            Method(typeof(SaveStore), "Load"),
            Method(typeof(Theme), "Load"));
        Assert.True(startup.Count > 50, $"the walk found only {startup.Count} methods");
        Assert.DoesNotContain(startup, TouchesClassic);
    }

    [Fact]
    public void The_reachability_walk_finds_classic_code_where_it_is_used()
    {
        // Positive controls: stage select reads the imported stages, and an import reads the install.
        Assert.Contains(Reachable(Method(typeof(CommunityGame), "OpenImportClassic")), TouchesClassic);
        Assert.Contains(Reachable(Method(typeof(ClassicImport), "FromSteam")), m => m.DeclaringType == typeof(ClassicInstall));
    }

    [Fact]
    public void Showing_imported_classic_stages_never_looks_at_the_install() =>
        Assert.DoesNotContain(Reachable(Method(typeof(ClassicStages), "Load")), m => m.DeclaringType == typeof(ClassicInstall) || m.DeclaringType == typeof(ClassicImport));

    [Fact]
    public void Only_the_import_screen_and_its_command_start_an_import()
    {
        var callers = Game.GetTypes().SelectMany(MethodsOf)
            .Where(m => m.DeclaringType?.Namespace != ClassicNamespace && References(m).Any(r => Owner(r) == typeof(ClassicImport)))
            // A local function compiles to <Main>$g__Name|0_1; its own name is what matters.
            .Select(m => $"{m.DeclaringType!.Name}.{(System.Text.RegularExpressions.Regex.Match(m.Name, @"^<.*>g__(\w+)\|").Groups[1] is { Success: true } local ? local.Value : m.Name)}")
            .Order()
            .ToList();
        Assert.Equal(["CommunityGame.RunClassicImport", "Program.ImportClassic"], callers);
    }

    [Fact]
    public void The_third_party_notices_sit_beside_the_game_and_name_every_package_it_ships()
    {
        var notices = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.txt"));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "LICENSE")));
        using var deps = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PuzzleBoyCommunity.deps.json")));
        var packages = deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(l => l.Value.GetProperty("type").GetString() == "package")
            .Select(l => l.Name.Replace('/', ' '))
            .ToList();
        Assert.NotEmpty(packages);
        Assert.All(packages, p => Assert.Contains(p, notices));
    }

    [Fact]
    public void The_reference_check_finds_a_reference_that_is_there() =>
        Assert.Equal(["PuzzleBoyCommunity"], ReferencesAmong(typeof(StandaloneTests).Assembly, ["Fmod5Sharp", "PuzzleBoyCommunity"]));
}
