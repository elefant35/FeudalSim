namespace FeudalSim.Sim.World;

/// <summary>Bits of <see cref="PersonCore.Flags"/>.</summary>
public static class PersonFlags
{
    /// <summary>The player's own character: a person like any other (parity), but chosen for by the human, never by the AI (21 §16).</summary>
    public const uint Player = 1;
}

/// <summary>Identity and life facts (rules owned by 16). Names live in a parallel column.</summary>
public struct PersonCore
{
    public long BirthGameMinute;
    public byte Sex;   // 0 = male, 1 = female (M2-FP4: from the given name, see CultureDef.FemaleNames)
    public byte LifeStage;
    public uint Flags;
}

/// <summary>Position (metres; X east, −Z north, +Y up) and facing.</summary>
public struct Transform
{
    public float X, Y, Z, Yaw;
}

/// <summary>11 §9.2 worn clothing: one item handle per slot (−1 = none). Slots follow <see cref="Content.WearSlot"/> (M2-05a).</summary>
public struct Worn
{
    public const int SlotCount = 7;
    public short Under, Torso, Legs, Feet, Cloak, Head, Hands, Reserved;

    public static Worn None => new() { Under = -1, Torso = -1, Legs = -1, Feet = -1, Cloak = -1, Head = -1, Hands = -1, Reserved = -1 };

    public readonly short Slot(int slot) => slot switch { 0 => Under, 1 => Torso, 2 => Legs, 3 => Feet, 4 => Cloak, 5 => Head, _ => Hands };

    public void Set(int slot, short item)
    {
        switch (slot)
        {
            case 0: Under = item; break;
            case 1: Torso = item; break;
            case 2: Legs = item; break;
            case 3: Feet = item; break;
            case 4: Cloak = item; break;
            case 5: Head = item; break;
            default: Hands = item; break;
        }
    }
}

/// <summary>
/// 18 §2.3 / 11 §3.1 Stamina (M2-05b): the seconds-scale exertion pool, simulated at LOD0 only. <c>Spent</c> carries the
/// part of 100 spent toward the next Energy point; the gait and its expiry come from the embodiment (the player's moves).
/// </summary>
public struct Stamina
{
    public float Value, Spent;
    public long LastSpendStep, WindedUntilStep, GaitUntilStep;
    public byte Gait, Reserved0, Reserved1, Reserved2;
}

/// <summary>11 §9.3–9.4 exposure state: Wetness 0–100 and Hypothermia 0–100 (M2-05a).</summary>
public struct Body
{
    public float Wetness, Hypothermia;
}

/// <summary>
/// 11 §10.3 nutrition (M2-07b): Satiety eaten per group, decayed with a 6-day time constant up to <see cref="AtMin"/>
/// (so the shares are a rolling 6-day mix). Updated lazily when someone eats.
/// </summary>
public struct Diet
{
    public float Staple, Protein, Fresh;
    public long AtMin;

    /// <summary>11 §10.5: Satiety drawn from the camp's store on <see cref="StoreDay"/> (rations cap it).</summary>
    public float StoreSatToday;
    public int StoreDay;
}

/// <summary>Needs, 0–100 where 100 = fully satisfied (canon §10.5).</summary>
public struct Needs
{
    public float Satiety, Hydration, Energy, Warmth;
    public float Social, Comfort, Safety, Purpose, Status;

    public static Needs Full => new()
    {
        Satiety = 100, Hydration = 100, Energy = 100, Warmth = 100,
        Social = 100, Comfort = 100, Safety = 100, Purpose = 100, Status = 100,
    };
}

/// <summary>
/// The six attributes (canon §10.1), internal float values (12 §3.2: display = clamp(round(·), 1, 10); checks use the
/// unrounded value). Generated from Potential N(5, 1.4) clamped 2–8; Training, AgeMod, InjuryMod and TempMod come later.
/// </summary>
public struct Attributes
{
    public float Strength, Endurance, Dexterity, Perception, Intellect, Charisma;
}

/// <summary>12 §3.3 training by use, per attribute (−1 … +2); added to the generated potential in checks.</summary>
public struct AttributeTraining
{
    public float Strength, Endurance, Dexterity, Perception, Intellect, Charisma;
}

/// <summary>
/// Per person per skill (12 §5): XP toward the next level, today's XP (the 150/day cap), the day it counts for, the last
/// day practised (rust grace) and Rust (a temporary penalty; the level itself never drops, 12 §5.6).
/// </summary>
public struct SkillProgress
{
    public float Xp, DayXp;
    public ushort Day, LastPracticeDay, RustDay;   // RustDay: the last day rust was evaluated (idempotent across save/load)
    public byte Rust;
}

/// <summary>The nine values (canon §10.4), importance 0–100.</summary>
public struct ValueBlock
{
    public byte Family, Wealth, Status, Honor, Tradition, Faith, Fairness, Freedom, Loyalty;
}

/// <summary>
/// Personality and origin (canon §10.4, 21 §3): five facets (0–100, mean 50, SD 15), nine values, homeland culture
/// and profession as content handles, and the trait set as a bitset over trait handles. NB: <c>Values.Status</c> is how
/// much the person cares about standing; <c>Needs.Status</c> is how satisfied that care is.
/// </summary>
public struct Personality
{
    public const ushort None = 0xFFFF;

    public byte Curiosity, Diligence, Sociability, Warmth, Volatility;
    public ValueBlock Values;
    public ushort Culture, Profession;
    public ulong Traits;

    public readonly bool HasTrait(int handle) => (Traits & (1UL << handle)) != 0;
}

/// <summary>
/// Emotions (canon §10.5), 0–100, decaying with half-lives (21 §6.2) evaluated lazily from <c>UpdatedGameMs</c>; the
/// targets name who an emotion is about.
/// </summary>
public struct Emotions
{
    public float Anger, Fear, Grief, Joy, Shame, Jealousy;
    public Core.EntityId AngerTarget, FearSource, JealousyTarget, ShameAudience;
    public long UpdatedGameMs;
}

/// <summary>Mood (canon §10.5): −100…+100 composite (21 §6.4) and its one-game-hour EMA.</summary>
public struct Mood
{
    public float Value, Smoothed;
}

/// <summary>
/// The current activity (21 §3 ActivityState, M1 form): the chosen action (content handle, −1 none), travel or perform
/// phase, the activity level for need decay, flags for the psychology system, and timing in game-ms.
/// </summary>
public struct ActivityState
{
    public const byte Asleep = 1, Interacting = 2, Purposeful = 4;

    /// <summary>In a conversation (21 §14.4: <c>action.converse</c> holds the NPC) and waiting on its own decision points (§14.6).</summary>
    public const byte Conversing = 8, Deliberating = 16;

    public short Action;
    public byte Phase;   // 0 travelling to the place, 1 performing
    public Content.ActivityLevel Level;
    public byte Flags;
    public long StartedGameMs, EndGameMs, NextDecideGameMs;
    public float Score, TargetX, TargetZ;

    public readonly bool Has(byte flag) => (Flags & flag) != 0;
}

public enum LodTier : byte { Lod0, Lod1, Lod2, Lod3, Lod0Battle }

/// <summary>Simulation level of detail (canon §8.2).</summary>
public struct LodState
{
    public LodTier Tier;
    public long LastUpdateGameMs;

    /// <summary>LOD0 only: false from promotion until the client's first body report (pending embodiment).</summary>
    public bool Embodied;

    /// <summary>LOD0 only: the step at which the person was first seen beyond the demotion radius (0 = not far).</summary>
    public long FarSinceStep;
}

/// <summary>M0 toy behavior state for <c>WanderSystem</c>; replaced by the NPC AI in M1.</summary>
public struct WanderState
{
    public float HomeX, HomeZ, TargetX, TargetZ;
    public long PauseUntilStep;
    public bool HasTarget;
}
