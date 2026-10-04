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
    private int _drank;

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

        // Look-at candidates: ground piles and nodes within reach.
        _lookables.Clear();
        foreach (var p in _piles)
        {
            if (new Vector2(p.X, p.Z).DistanceTo(_player) > Sim.World.PileStore.ReachM - 0.5f) { continue; }
            var top = p.Goods[0];
            _lookables.Add(new LookTarget("pile", p.Id, $"{top.Name.ToLowerInvariant()} ×{top.Qty}{(p.Goods.Count > 1 ? $" (+{p.Goods.Count - 1} more)" : "")}", new Vector3(p.X, Ground(p.X, p.Z) + 0.3f, p.Z)));
        }

        foreach (var n in _nodes.Near)
        {
            var def = _content!.Nodes[n.Type];
            // Within the sim's reach, less a step of position lag: plants 3 m (Foraging.ReachM), trees 4 m (Processes.SiteProblem).
            var reach = def.Kind == NodeKind.Tree ? ReachM : Sim.Crafting.Foraging.ReachM - 0.5f;
            if (new Vector2(n.At.X, n.At.Z).DistanceTo(_player) > reach) { continue; }
            var height = def.Kind switch { NodeKind.Tree => n.State == World.NodeDressing.Felled ? 0.4f : 1.4f, NodeKind.Bush => 0.8f, _ => 0.2f };
            _lookables.Add(new LookTarget(def.Kind.ToString().ToLowerInvariant(), ((ulong)(uint)n.Chunk << 20) | (uint)n.Index, NodeLabel(def, n.Size, n.State), n.At + new Vector3(0, height, 0)));
        }

        AutoWork(snap);
        _waterHere = WaterHere();
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

            var piles = new List<PileView>();   // M2-08: ground piles within 200 m
            foreach (var (id, p) in w.Piles.All)
            {
                if (((p.X - px) * (p.X - px)) + ((p.Z - pz) * (p.Z - pz)) > 200f * 200f) { continue; }
                var goods = w.Inventory.Of(new Sim.Core.EntityId(id)).GroupBy(s => s.Seen).Select(g => (w.Content.Items[g.Key].Id, w.Content.Items[g.Key].Name, g.Sum(s => s.Qty))).ToList();
                if (goods.Count > 0) { piles.Add(new PileView(id, p.X, p.Z, goods)); }
            }

            return (states, piles);
        }).ContinueWith(t => { if (t.IsCompletedSuccessfully) { (_pendingStates, _pendingPiles) = t.Result; } }, TaskScheduler.Default);
    }

    private List<(int, int, byte)>? _pendingStates;
    private List<PileView>? _pendingPiles;
    private List<PileView> _piles = [];
    private readonly Dictionary<ulong, Node3D> _pileNodes = [];

    /// <summary>A ground pile as the client shows it: where, and what is in it (as seen).</summary>
    private sealed record PileView(ulong Id, float X, float Z, List<(string Item, string Name, int Qty)> Goods);
    private string? _waterHere;

    /// <summary>What water the player stands by (the sim decides; this only names it for the prompt): camp water, a brook, the sea.</summary>
    private string? WaterHere()
    {
        if (_island is null) { return null; }
        var g = _island.Map.Grid;
        if (new Vector2(_campRecord.WaterX, _campRecord.WaterZ).DistanceTo(_player) <= Sim.Survival.Water.ReachM - 0.5f) { return "the brook"; }
        for (var a = 0; a < 8; a++)
        {
            var (ox, oz) = (Mathf.Cos(a * Mathf.Pi / 4) * 2.5f, Mathf.Sin(a * Mathf.Pi / 4) * 2.5f);
            if (Sim.Survival.Water.KindAt(g, _player.X + ox, _player.Y + oz) is { } k) { return k switch { "lake" => "the lake", "river" => "the river", "spring" => "the spring", _ => "the stream" }; }
        }

        return _island.HeightAt(_player.X, _player.Y) < 0.6f && Hosting.CampAnchor.Cell(g, _player.X, _player.Y) is var c and >= 0 && g.CoastDistM[c] < 12f ? "the sea" : null;
    }

    /// <summary>Applies states that arrived from the sim thread (on the main thread).</summary>
    private void ApplyStates()
    {
        if (_pendingStates is { } states && _nodes is not null)
        {
            _pendingStates = null;
            _nodes.SetStates(states, _player.X, _player.Y);
        }

        if (_pendingPiles is { } piles)
        {
            _pendingPiles = null;
            ShowPiles(piles);
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
        if (t.Kind == "pile") { return $"[E] pick up: {t.Name}"; }
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

    /// <summary>[E]: talk to a person, fell a tree, gather a plant — or, looking at nothing by water, drink.</summary>
    private void Interact()
    {
        if (_look is null && _waterHere is not null)
        {
            _runner!.Submit(CommandSource.Player, new Drink(_runner.Snapshots.ReadLatest().PlayerId));
            return;
        }

        if (_look is not { } t) { return; }
        if (t.Kind == "person") { TryTalk(); return; }
        if (t.Kind == "pile" && _piles.FirstOrDefault(p => p.Id == t.Id) is { } pile)
        {
            _runner!.Submit(CommandSource.Player, new PickUp(_runner.Snapshots.ReadLatest().PlayerId, new Sim.Core.EntityId(pile.Id), pile.Goods[0].Item, 1));
            return;
        }

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
            case Sim.Events.ItemsMoved m:
                _statesDirty = true;
                if (InventoryOpen) { RefreshInventory(show: false); }
                if (m.To == _runner!.Snapshots.ReadLatest().PlayerId) { Say($"You pick up {m.Qty} × {_content!.Items[m.Item].Name.ToLowerInvariant()}."); }
                return true;
            case Sim.Events.CommandRejected r when r.Reason.StartsWith("PickUp:", StringComparison.Ordinal) || r.Reason.StartsWith("Drop:", StringComparison.Ordinal):
                Say(r.Reason[(r.Reason.IndexOf(':') + 1)..].Trim());
                return true;
            case Sim.Events.Drank d when d.Drinker == _runner!.Snapshots.ReadLatest().PlayerId:
                _drank++;
                Say(d.Source == "sea" ? "Salt water — it only makes you thirstier." : $"You drink from the {(d.Source == "camp" ? "brook" : d.Source)} (+{d.Hydration:0} Hydration).");
                return true;
            case Sim.Events.CommandRejected r when r.Reason.StartsWith("Drink:", StringComparison.Ordinal):
                Say(r.Reason[6..].Trim());
                return true;
            case Sim.Events.CommandRejected r when r.Reason.StartsWith("Forage", StringComparison.Ordinal) || (r.Reason.StartsWith("StartProcess", StringComparison.Ordinal) && !Knapping):
                Say(r.Reason[(r.Reason.IndexOf(':') + 1)..].Trim());
                return true;
        }

        return false;
    }

    /// <summary>Draws the piles: the first item's prop model (the art's <c>props/&lt;item&gt;.glb</c>), else a graybox crate.</summary>
    private void ShowPiles(List<PileView> piles)
    {
        _piles = piles;
        var live = piles.Select(p => p.Id).ToHashSet();
        foreach (var gone in _pileNodes.Keys.Where(k => !live.Contains(k)).ToList()) { _pileNodes[gone].QueueFree(); _pileNodes.Remove(gone); }
        foreach (var p in piles)
        {
            var key = p.Goods[0].Item;
            if (_pileNodes.TryGetValue(p.Id, out var existing) && existing.HasMeta("item") && existing.GetMeta("item").AsString() == key) { continue; }
            existing?.QueueFree();
            var name = key.StartsWith("item.", StringComparison.Ordinal) ? key[5..] : key;
            var path = $"res://assets/props/{(name == "firewood" ? "firewood_bundle" : name)}.glb";
            Node3D node = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path).Instantiate<Node3D>()
                : new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.4f, 0.6f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("6a4a30") } };
            World.CampDressing.DropVertexColour(node);
            node.SetMeta("item", key);
            node.Position = new Vector3(p.X, Ground(p.X, p.Z), p.Z);
            AddChild(node);
            _pileNodes[p.Id] = node;
        }
    }
}
