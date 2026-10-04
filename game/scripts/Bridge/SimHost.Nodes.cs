using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// M2-FP3: the island's nodes around the player (<see cref="World.NodeDressing"/>), their states from the sim, and the
/// look-at verbs on them: [E] on a standing tree fells it (13 §9.6 `recipe.fell_tree`, an axe in your kit; the stages
/// resolve as an NPC's would, one after another as the work time passes), [E] on a plant or bush in season gathers it
/// (11 §8.2 `Forage`: what you think you picked may not be what it is). Results come back as subtitles.
/// </summary>
public partial class SimHost
{
    private World.NodeDressing? _nodes;
    private Task? _statesPending;
    private bool _statesDirty;
    private int _fellRecipe = -1;
    private (ulong Process, int Stage) _lastWorked;
    private Vector2? _fellingAt;
    private double _lastAwayNote = -100;
    private Vector2 _coverAt = new(float.NaN, float.NaN);
    private int _windSecond = -1;

    private void InitNodes()
    {
        if (_island is null || _content is null) { return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _nodes = new World.NodeDressing(this, _island, _content);
        _fellRecipe = ContentDatabase.HandleOf(_content.Recipes, "recipe.fell_tree", r => r.Id);
        GD.Print($"SimHost: nodes — {_content.Nodes.Count} types, {_nodes.ArtTypes} with art ({sw.Elapsed.TotalMilliseconds:F0} ms)");
    }

    /// <summary>Per frame: rebuild around the player when they cross a chunk (or move 12 m, for the ground cover), ask the sim for states.</summary>
    private void UpdateNodes(RenderSnapshot snap)
    {
        if (_nodes is null) { return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var crossed = _nodes.Update(_player.X, _player.Y);
        if (!crossed && !(_coverAt.DistanceTo(_player) < 12f)) { _nodes.Refresh(_player.X, _player.Y); crossed = true; }
        if (crossed)
        {
            _coverAt = _player;
            _statesDirty = true;
            if (sw.Elapsed.TotalMilliseconds > 8) { GD.Print($"SimHost: nodes rebuilt in {sw.Elapsed.TotalMilliseconds:F1} ms ({_nodes.Instances} nodes)"); }
        }

        if (_statesDirty && _statesPending is not { IsCompleted: false }) { RequestStates(); }

        // Look-at candidates: nodes within reach.
        _lookables.Clear();
        foreach (var n in _nodes.Near)
        {
            if (new Vector2(n.At.X, n.At.Z).DistanceTo(_player) > ReachM + 1f) { continue; }
            var def = _content!.Nodes[n.Type];
            var height = def.Kind switch { NodeKind.Tree => n.State == World.NodeDressing.Felled ? 0.4f : 1.4f, NodeKind.Bush => 0.8f, _ => 0.2f };
            _lookables.Add(new LookTarget(def.Kind.ToString().ToLowerInvariant(), ((ulong)(uint)n.Chunk << 20) | (uint)n.Index, NodeLabel(def, n.Size, n.State), n.At + new Vector3(0, height, 0)));
        }

        AutoWork(snap);
        if ((int)_clock != _windSecond) { _windSecond = (int)_clock; World.NodeDressing.SetWind(snap.Weather.WindMs); }
    }

    private void RequestStates()
    {
        _statesDirty = false;
        var chunks = _nodes!.ChunksAround(_player.X, _player.Y).Select(c => (c, _nodes.CountIn(c))).ToList();
        var (px, pz) = (_player.X, _player.Y);
        _statesPending = _runner!.Invoke(w =>
        {
            var states = new List<(int, int, byte)>();
            foreach (var (c, count) in chunks)
            {
                for (var k = 0; k < count; k++) { states.Add((c, k, w.NodeDeltas.State(c, k))); }
            }

            return states;
        }).ContinueWith(t => { if (t.IsCompletedSuccessfully) { _pendingStates = t.Result; } }, TaskScheduler.Default);
    }

    private List<(int, int, byte)>? _pendingStates;

    /// <summary>Applies states that arrived from the sim thread (on the main thread).</summary>
    private void ApplyStates()
    {
        if (_pendingStates is { } states && _nodes is not null)
        {
            _pendingStates = null;
            _nodes.SetStates(states, _player.X, _player.Y);
        }
    }

    private static string NodeLabel(NodeDef def, byte size, byte state)
    {
        var name = def.Name.ToLowerInvariant();
        return def.Kind switch
        {
            NodeKind.Tree when state == World.NodeDressing.Felled => $"{name} stump",
            NodeKind.Tree => size switch { 0 => $"{name} sapling", 1 => $"young {name}", 3 => $"old {name}", _ => name },
            _ when state == World.NodeDressing.Harvested => $"{name} (picked over)",
            _ => name,
        };
    }

    /// <summary>The verb for a look target (shown as the prompt; [E] does it).</summary>
    private string LookPromptFor(LookTarget t)
    {
        if (t.Kind == "person") { return $"[E] talk to {t.Name}   ·   [T] take from them"; }
        var (chunk, index) = ((int)(t.Id >> 20), (int)(t.Id & 0xFFFFF));
        var near = _nodes?.Near.FirstOrDefault(n => n.Chunk == chunk && n.Index == index);
        if (near is not { } n) { return t.Name; }
        var def = _content!.Nodes[n.Type];
        return def.Kind switch
        {
            NodeKind.Tree when n.State == 0 && n.Size >= 1 => $"[E] fell the {t.Name}",
            NodeKind.Tree => t.Name,
            _ when def.Forage is not null && n.State == 0 => $"[E] gather {t.Name}",
            _ => t.Name,
        };
    }

    /// <summary>[E]: talk to a person, fell a tree, gather a plant.</summary>
    private void Interact()
    {
        if (_look is not { } t) { return; }
        if (t.Kind == "person") { TryTalk(); return; }
        var (chunk, index) = ((int)(t.Id >> 20), (int)(t.Id & 0xFFFFF));
        var near = _nodes?.Near.FirstOrDefault(n => n.Chunk == chunk && n.Index == index);
        if (near is not { } n) { return; }
        var def = _content!.Nodes[n.Type];
        var player = _runner!.Snapshots.ReadLatest().PlayerId;
        if (def.Kind == NodeKind.Tree && n.State == 0 && n.Size >= 1)
        {
            _runner.Submit(CommandSource.Player, new StartProcess(player, "recipe.fell_tree", SiteChunk: chunk, SiteIndex: index));
            _fellingAt = new Vector2(n.At.X, n.At.Z);
            Say($"You set about felling the {t.Name}.");
        }
        else if (def.Forage is not null && n.State == 0)
        {
            _runner.Submit(CommandSource.Player, new Forage(player, chunk, index));
        }
    }

    /// <summary>
    /// Felling plays out without a minigame (yet): each stage is worked as an NPC would (m = NaN) as soon as the last one's
    /// work time is up.
    /// </summary>
    private void AutoWork(RenderSnapshot snap)
    {
        if (snap.PlayerProcess == 0 || snap.ProcessRecipe != _fellRecipe || snap.ProcessState != (byte)Sim.Crafting.ProcessState.Active) { return; }
        if (snap.GameMs / Sim.Time.SimClock.MsPerGameMinute < snap.ProcessBusyUntilMin) { return; }
        if (_lastWorked == (snap.PlayerProcess, snap.ProcessStage)) { return; }
        if (_fellingAt is { } site && site.DistanceTo(_player) > 4.5f)   // the sim's 4 m reach (13 §4): walk away and the work waits
        {
            if (_clock - _lastAwayNote > 6) { _lastAwayNote = _clock; Say("The tree waits — go back to it to keep felling."); }
            return;
        }

        _lastWorked = (snap.PlayerProcess, snap.ProcessStage);
        GD.Print($"SimHost: work stage {snap.ProcessStage} of process {snap.PlayerProcess}");
        _runner!.Submit(CommandSource.Player, new WorkStage(snap.PlayerProcess, float.NaN));
    }

    private void Say(string line)
    {
        _lines.Add((_clock, line));
        GD.Print($"SimHost: say “{line}”");
    }

    /// <summary>Node-related events: states changed (re-query), and what the player gathered or felled.</summary>
    private bool OnNodeEvent(object payload)
    {
        switch (payload)
        {
            case Sim.Events.Foraged f:
                _statesDirty = true;
                if (f.Worker == _runner!.Snapshots.ReadLatest().PlayerId)
                {
                    _foraged++;
                    var seen = f.Seen >= 0 ? _content!.Items[f.Seen].Name : _content!.Items[f.Item].Name;
                    Say($"You gather {f.Qty} × {seen.ToLowerInvariant()}.");
                }

                return true;
            case Sim.Events.ProcessCompleted c when c.Worker == _runner!.Snapshots.ReadLatest().PlayerId && !Knapping:
                _statesDirty = true;
                _felled++;
                Say($"Done: {_content!.Items[c.Item].Name.ToLowerInvariant()} (quality {c.Q}).");
                return true;
            case Sim.Events.ProcessCompleted:
                _statesDirty = true;
                return false;
            case Sim.Events.CommandRejected r when r.Reason.StartsWith("Forage", StringComparison.Ordinal) || (r.Reason.StartsWith("StartProcess", StringComparison.Ordinal) && !Knapping):
                Say(r.Reason[(r.Reason.IndexOf(':') + 1)..].Trim());
                return true;
        }

        return false;
    }
}
