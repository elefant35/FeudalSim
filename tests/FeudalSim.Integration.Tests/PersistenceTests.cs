using System.Reflection;
using System.Runtime.InteropServices;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M0-06 (20 §20 step 6): persistence v0.</summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "feudalsim-tests", Guid.NewGuid().ToString("N"));

    private static SimWorld Configure(SimWorld world) => world.AddSystem(new WanderSystem()).AddSystem(new NeedsDecaySystem());

    private static SimWorld NewCamp(ulong seed)
    {
        var world = Configure(new SimWorld(seed));
        for (var i = 0; i < 24; i++)
        {
            world.Enqueue(new CommandEnvelope(i + 1, 0, CommandSource.Scenario, new SpawnPerson($"Settler {i + 1}", i % 6 * 4f, i / 6 * -4f)));
        }

        return world;
    }

    private static void Run(SimWorld world, int steps)
    {
        for (var i = 0; i < steps; i++) { world.Step(); }
    }

    /// <summary>
    /// M1-04a: a save taken while a decision point is open and an AI request is pending restores both — the DP's menu,
    /// pre-drawn policy pick and deadline, and the request's deadline and fallback — so the run continues identically.
    /// </summary>
    [Fact]
    public void Save_with_an_open_decision_point_and_a_pending_AI_request_continues_identically()
    {
        var content = Sim.Content.ContentDatabase.Empty;
        SimWorld Build(SimWorld w)
        {
            w.Decisions.Register(new DecisionPingOwner());
            return w.AddSystem(new LodSystem()).AddSystem(new WanderSystem()).AddSystem(new NeedsDecaySystem())
                .AddSystem(new AiPingSystem(20, 200)).AddSystem(new DecisionPingSystem(20, Sim.Decisions.DeciderKind.Llm, 40));
        }

        static List<string> RunCollect(SimWorld w, long untilStep)
        {
            var seen = new List<string>();
            while (w.Clock.Step < untilStep)
            {
                var output = w.Step();
                ScenarioRunner.LogOpened(w, output);
                foreach (var e in output.Events)
                {
                    if (e.Payload is Sim.Events.DecisionResolved or Sim.Events.AiResultApplied) { seen.Add($"{e.Step}:{e.Payload}"); }
                }
            }

            return seen;
        }

        var straight = Build(new SimWorld(42));
        for (var i = 0; i < 3; i++) { straight.Enqueue(new CommandEnvelope(i + 1, 0, CommandSource.Scenario, new SpawnPerson($"S{i}", i, 0))); }
        RunCollect(straight, 30);
        straight.Decisions.OpenCount.ShouldBe(1);
        straight.PendingAiRequests.ShouldBe(1);

        var path = Path.Combine(_dir, "pending.fssave");
        Directory.CreateDirectory(_dir);
        SaveFiles.WriteSnapshotAtomic(path, SaveCodec.Capture(straight));
        var after = RunCollect(straight, 260);

        var restored = Build(SaveCodec.Restore(SaveFiles.ReadSnapshot(path), out var warnings));
        warnings.ShouldBeEmpty();
        restored.Decisions.OpenCount.ShouldBe(1);
        restored.PendingAiRequests.ShouldBe(1);
        var afterRestored = RunCollect(restored, 260);

        afterRestored.ShouldBe(after);
        after.Count.ShouldBe(2);   // the DP at its deadline (step 61), the AI fallback at its deadline (step 221)
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(straight));
    }

    [Fact]
    public void Save_and_load_mid_run_matches_a_straight_run()
    {
        var straight = NewCamp(42);
        Run(straight, 10_000);

        var first = NewCamp(42);
        Run(first, 5_000);
        var path = Path.Combine(_dir, "slots", "quick", "snapshot.fsnap");
        SaveFiles.WriteSnapshotAtomic(path, SaveCodec.Capture(first));
        File.Exists(path + ".tmp").ShouldBeFalse();

        var image = SaveFiles.ReadSnapshot(path);
        image.Header.StateHash.ShouldBe(StateHasher.Hash(first));
        var resumed = Configure(SaveCodec.Restore(image, out var warnings));
        warnings.ShouldBeEmpty();
        StateHasher.Hash(resumed).ShouldBe(StateHasher.Hash(first));
        Run(resumed, 5_000);

        StateHasher.Hash(resumed).ShouldBe(StateHasher.Hash(straight));
        resumed.EventSeq.ShouldBe(straight.EventSeq);
    }

    [Fact]
    public void A_missing_column_falls_back_to_its_default()
    {
        var world = NewCamp(42);
        Run(world, 2_000);
        var image = SaveCodec.Capture(world);
        var people = image.Tables.Single(t => t.Table == SaveCodec.PeopleTable);
        people.Columns.RemoveAll(c => c.Name == "needs");
        people.Columns.Add(new ColumnBlock { Name = "from_the_future", LayoutVersion = 1, ElementSize = 4, Data = new byte[people.RowCount * 4] });

        var restored = SaveCodec.Restore(image, out var warnings);

        restored.People.Count.ShouldBe(24);
        restored.People.Needs.ToArray().ShouldAllBe(n => n.Satiety == 100 && n.Hydration == 100);
        restored.People.Transforms.ToArray().ShouldBe(world.People.Transforms.ToArray());
        warnings.ShouldContain(w => w.Contains("Missing column people.needs"));
        warnings.ShouldContain(w => w.Contains("Dropped unknown column people.from_the_future"));
    }

    [Fact]
    public void A_torn_log_tail_is_recovered()
    {
        var path = Path.Combine(_dir, "inputs.fslog");
        using (var log = InputLogFile.OpenOrCreate(path))
        {
            for (var i = 1; i <= 10; i++) { log.Append(new CommandEnvelope(i, i, CommandSource.Dev, new SetDayLength(30))); }
        }

        var intact = new FileInfo(path).Length;
        using (var f = new FileStream(path, FileMode.Open)) { f.SetLength(intact - 3); }   // crash mid-record

        using (var reopened = InputLogFile.OpenOrCreate(path))
        {
            reopened.Existing.Count.ShouldBe(9);
            reopened.TruncatedBytes.ShouldBeGreaterThan(0);
            reopened.Append(new CommandEnvelope(10, 10, CommandSource.Dev, new SpawnPerson("Late", 1, 2)));
        }

        var all = InputLogFile.ReadAll(path);
        all.Count.ShouldBe(10);
        all[^1].Payload.ShouldBe(new SpawnPerson("Late", 1, 2));
    }

    [Fact]
    public void Persisted_struct_layouts_match_their_layout_versions()
    {
        // If this fails, a persisted struct changed: bump its LayoutVersion in SaveCodec.PeopleColumns
        // (and register a column migration once saves must stay compatible, 20 §9.5), then update here.
        var expected = new Dictionary<string, (int Version, string Fingerprint)>
        {
            ["core"] = (1, "BirthGameMinute:Int64@0|Sex:Byte@8|LifeStage:Byte@9|Flags:UInt32@12"),
            ["transform"] = (1, "X:Single@0|Y:Single@4|Z:Single@8|Yaw:Single@12"),
            ["needs"] = (1, "Satiety:Single@0|Hydration:Single@4|Energy:Single@8|Warmth:Single@12|Social:Single@16|Comfort:Single@20|Safety:Single@24|Purpose:Single@28|Status:Single@32"),
            ["lod"] = (2, "Tier:LodTier@0|LastUpdateGameMs:Int64@8|Embodied:Boolean@16|FarSinceStep:Int64@24"),
            ["wander"] = (1, "HomeX:Single@0|HomeZ:Single@4|TargetX:Single@8|TargetZ:Single@12|PauseUntilStep:Int64@16|HasTarget:Boolean@24"),
            ["attributes"] = (1, "Strength:Single@0|Endurance:Single@4|Dexterity:Single@8|Perception:Single@12|Intellect:Single@16|Charisma:Single@20"),
            ["personality"] = (1, "Curiosity:Byte@0|Diligence:Byte@1|Sociability:Byte@2|Warmth:Byte@3|Volatility:Byte@4|Values:ValueBlock@5|Culture:UInt16@14|Profession:UInt16@16|Traits:UInt64@24"),
            ["emotions"] = (1, "Anger:Single@0|Fear:Single@4|Grief:Single@8|Joy:Single@12|Shame:Single@16|Jealousy:Single@20|AngerTarget:EntityId@24|FearSource:EntityId@32|JealousyTarget:EntityId@40|ShameAudience:EntityId@48|UpdatedGameMs:Int64@56"),
            ["mood"] = (1, "Value:Single@0|Smoothed:Single@4"),
            ["activity"] = (1, "Action:Int16@0|Phase:Byte@2|Level:ActivityLevel@3|Flags:Byte@4|StartedGameMs:Int64@8|EndGameMs:Int64@16|NextDecideGameMs:Int64@24|Score:Single@32|TargetX:Single@36|TargetZ:Single@40"),
            ["skill_progress"] = (1, "Xp:Single@0|DayXp:Single@4|Day:UInt16@8|LastPracticeDay:UInt16@10|RustDay:UInt16@12|Rust:Byte@14"),   // M2-04, 28 per row
            ["attribute_training"] = (1, "Strength:Single@0|Endurance:Single@4|Dexterity:Single@8|Perception:Single@12|Intellect:Single@16|Charisma:Single@20"),
            ["worn"] = (1, "Under:Int16@0|Torso:Int16@2|Legs:Int16@4|Feet:Int16@6|Cloak:Int16@8|Head:Int16@10|Hands:Int16@12|Reserved:Int16@14"),   // M2-05a
            ["body"] = (1, "Wetness:Single@0|Hypothermia:Single@4"),   // M2-05a
            ["stamina"] = (1, "Value:Single@0|Spent:Single@4|LastSpendStep:Int64@8|WindedUntilStep:Int64@16|GaitUntilStep:Int64@24|Gait:Byte@32|Reserved0:Byte@33|Reserved1:Byte@34|Reserved2:Byte@35"),   // M2-05b
            ["vitals"] = (1, "Blood:Single@0|Bruise:Single@4|Pain:Single@8|Health:Single@12|DownedSinceMin:Int64@16|StableSinceMin:Int64@24|State:VitalState@32|Cause:VitalCause@33|Reserved0:Byte@34|Reserved1:Byte@35"),   // M2-06a
        };
        var types = new Dictionary<string, Type>
        {
            ["core"] = typeof(PersonCore), ["transform"] = typeof(Transform), ["needs"] = typeof(Needs),
            ["lod"] = typeof(LodState), ["wander"] = typeof(WanderState),
            ["attributes"] = typeof(Attributes), ["personality"] = typeof(Personality), ["emotions"] = typeof(Emotions), ["mood"] = typeof(Mood),
            ["activity"] = typeof(ActivityState), ["skill_progress"] = typeof(SkillProgress), ["attribute_training"] = typeof(AttributeTraining),
            ["worn"] = typeof(Worn), ["body"] = typeof(Body), ["stamina"] = typeof(Stamina), ["vitals"] = typeof(FeudalSim.Sim.Health.Vitals),
        };

        // Every persisted struct column is fingerprinted here (id and the per-row byte columns excepted).
        SaveCodec.PeopleColumns.Select(c => c.Name).Except(["id", "skill_levels", "skill_aptitude"]).ShouldBe(types.Keys, ignoreOrder: true);

        foreach (var (name, type) in types)
        {
            var spec = SaveCodec.PeopleColumns.Single(c => c.Name == name);
            var fingerprint = string.Join("|", type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => $"{f.Name}:{f.FieldType.Name}@{Marshal.OffsetOf(type, f.Name)}"));
            (spec.LayoutVersion, fingerprint).ShouldBe(expected[name], $"column '{name}'");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); }
    }
}
