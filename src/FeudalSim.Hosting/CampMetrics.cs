using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Hosting;

/// <summary>One game day of 21 §19 camp metrics (headless, policy-only).</summary>
public sealed record CampDay(
    double IdleRate, double LowNeedShare, double MoodMean, double BreakingShare, double Divergence,
    double Food, double Firewood, double FireShare, double FriendsPerPerson = 0, double EnemiesPerPerson = 0, double MeanOpinion = 0,
    double MeanWarmth = 0, double ColdShare = 0, double FreezingShare = 0, double WarmingShare = 0, double MeanWetness = 0, double MinWarmth = 100, double MaxHypothermia = 0);

/// <summary>
/// Samples a utility-AI world once per game minute and summarizes each game day per 21 §19: idle rate (share of awake
/// time resting or idling), need health (agent-hours with any physical need &lt; 15), mean smoothed mood, share of
/// agent-days in the breaking band (&lt; −60), behavior divergence (mean pairwise normalized L1 distance between
/// same-profession agents' daily activity histograms), plus camp stocks and how much of the day the fire burned.
/// Read-only: never changes the world.
/// </summary>
public sealed class CampMetrics
{
    private readonly int _actions;
    private readonly int _idle, _rest, _sleep;
    private long _lastMinute = -1;
    private int _minutes, _awake, _idleMinutes, _lowNeed, _agentMinutes, _fireMinutes, _cold, _freezing, _warming;
    private double _warmthSum, _wetSum, _minWarmth = 100, _maxHypo;
    private readonly int _warmUp;
    private double _moodSum;
    private int _moodN;
    private float[][] _hist = [];
    private double[] _dayMood = [];

    public CampMetrics(ContentDatabase content)
    {
        _actions = content.Actions.Count;
        int H(string id) => ContentDatabase.HandleOf(content.Actions, id, a => a.Id);
        (_idle, _rest, _sleep, _warmUp) = (H("action.idle"), H("action.rest"), H("action.sleep"), H("action.warm_up"));
    }

    public List<CampDay> Days { get; } = [];

    public void Sample(SimWorld world)
    {
        var minute = world.Clock.GameMs / 60_000;
        if (minute == _lastMinute || world.Camp.Active == 0) { return; }
        _lastMinute = minute;
        var p = world.People;
        if (_hist.Length != p.Count)
        {
            _hist = [.. Enumerable.Range(0, p.Count).Select(_ => new float[_actions + 1])];
            _dayMood = new double[p.Count];
        }

        _minutes++;
        if (world.Camp.FireFuelMin > 0f) { _fireMinutes++; }
        for (var i = 0; i < p.Count; i++)
        {
            if (world.IsPlayer(i)) { continue; }   // settlers only: the player's character is not on the AI
            var a = p.Activity[i].Action;
            _hist[i][a < 0 ? _actions : a]++;
            _agentMinutes++;
            var n = p.Needs[i];
            if (n.Satiety < 15f || n.Hydration < 15f || n.Energy < 15f || n.Warmth < 15f) { _lowNeed++; }   // any physical need (21 §19)
            _warmthSum += n.Warmth;
            _wetSum += p.Body[i].Wetness;
            if (n.Warmth < 40f) { _cold++; }
            _minWarmth = Math.Min(_minWarmth, n.Warmth);
            _maxHypo = Math.Max(_maxHypo, p.Body[i].Hypothermia);
            if (n.Warmth < 25f) { _freezing++; }
            if (a != _sleep)
            {
                _awake++;
                if (a == _idle || a == _rest) { _idleMinutes++; }
                if (a == _warmUp && a >= 0) { _warming++; }
            }

            _dayMood[i] += p.Mood[i].Smoothed;
            _moodSum += p.Mood[i].Smoothed;
            _moodN++;
        }
    }

    /// <summary>Closes the current game day (call at each day boundary).</summary>
    public CampDay EndDay(SimWorld world)
    {
        var p = world.People;
        var breaking = 0;
        var settlers = 0;
        for (var i = 0; i < _dayMood.Length; i++)
        {
            if (i < p.Count && world.IsPlayer(i)) { continue; }
            settlers++;
            if (_minutes > 0 && _dayMood[i] / _minutes < -60) { breaking++; }
        }

        var day = new CampDay(
            _awake == 0 ? 0 : _idleMinutes / (double)_awake,
            _agentMinutes == 0 ? 0 : _lowNeed / (double)_agentMinutes,
            _moodN == 0 ? 0 : _moodSum / _moodN,
            settlers == 0 ? 0 : breaking / (double)settlers,
            Divergence(p),
            world.Camp.Food, world.Camp.Firewood,
            _minutes == 0 ? 0 : _fireMinutes / (double)_minutes,
            SocialCounts(world).Friends, SocialCounts(world).Enemies, SocialCounts(world).Opinion,
            _agentMinutes == 0 ? 0 : _warmthSum / _agentMinutes, _agentMinutes == 0 ? 0 : _cold / (double)_agentMinutes,
            _agentMinutes == 0 ? 0 : _freezing / (double)_agentMinutes, _awake == 0 ? 0 : _warming / (double)_awake,
            _agentMinutes == 0 ? 0 : _wetSum / _agentMinutes, _minWarmth, _maxHypo);
        Days.Add(day);
        (_minutes, _awake, _idleMinutes, _lowNeed, _agentMinutes, _fireMinutes, _moodSum, _moodN) = (0, 0, 0, 0, 0, 0, 0, 0);
        (_cold, _freezing, _warming, _warmthSum, _wetSum, _minWarmth, _maxHypo) = (0, 0, 0, 0, 0, 100, 0);
        foreach (var h in _hist) { Array.Clear(h); }
        Array.Clear(_dayMood);
        return day;
    }

    private double Divergence(PersonTable p)
    {
        if (_hist.Length < 2) { return 0; }
        var total = 0.0;
        var pairs = 0;
        for (var a = 0; a < _hist.Length; a++)
        {
            for (var b = a + 1; b < _hist.Length; b++)
            {
                if (p.Personality[a].Profession != p.Personality[b].Profession) { continue; }   // same role only
                float sa = _hist[a].Sum(), sb = _hist[b].Sum();
                if (sa == 0 || sb == 0) { continue; }
                var l1 = 0.0;
                for (var k = 0; k < _hist[a].Length; k++) { l1 += Math.Abs((_hist[a][k] / sa) - (_hist[b][k] / sb)); }
                total += l1 / 2;   // normalized to 0–1
                pairs++;
            }
        }

        return pairs == 0 ? 0 : total / pairs;
    }

    /// <summary>21 §19 social network: mean friends / enemies per person (tags), and mean opinion over edges. Read-only.</summary>
    private static (double Friends, double Enemies, double Opinion) SocialCounts(SimWorld world)
    {
        var n = Math.Max(1, world.People.Count);
        int friends = 0, enemies = 0, edges = 0;
        double opinion = 0;
        foreach (var ((holder, other), e) in world.Relationships.Edges)
        {
            if ((e.Tags & Sim.Social.RelTags.Friend) != 0) { friends++; }
            if ((e.Tags & Sim.Social.RelTags.Enemy) != 0) { enemies++; }
            opinion += world.Relationships.Opinion(new Sim.Core.EntityId(holder), new Sim.Core.EntityId(other));
            edges++;
        }

        return (friends / (double)n, enemies / (double)n, edges == 0 ? 0 : opinion / edges);
    }

    /// <summary>Share of directed edges passing each 16 §4.12 Friend gate: Op ≥ 30, F ≥ 30, T ≥ 40, all three. Read-only.</summary>
    public static double[] FriendGates(SimWorld world)
    {
        double op = 0, f = 0, t = 0, all = 0, n = 0;
        var ops = new List<float>();
        foreach (var ((holder, other), e) in world.Relationships.Edges)
        {
            var opinion = world.Relationships.Opinion(new Sim.Core.EntityId(holder), new Sim.Core.EntityId(other));
            ops.Add(opinion);
            var o = opinion >= 30f;
            bool ff = e.Familiarity >= 30f, tt = e.Trust >= 40f;
            op += o ? 1 : 0; f += ff ? 1 : 0; t += tt ? 1 : 0; all += o && ff && tt ? 1 : 0; n++;
        }

        ops.Sort();
        return n == 0 ? [0, 0, 0, 0, 0, 0, 0] : [op / n, f / n, t / n, all / n, ops[ops.Count / 2], ops[(int)(0.9 * (ops.Count - 1))], ops[^1]];
    }

    /// <summary>21 §19 task failure for the run (activities failed / started).</summary>
    public static double TaskFailure(SimWorld world)
        => world.Systems.OfType<ActivitySystem>().FirstOrDefault() is { Started: > 0 } a ? a.Abandoned / (double)a.Started : 0;
}
