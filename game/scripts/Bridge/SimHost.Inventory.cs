using FeudalSim.Sim.Commands;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// M2-07b: what you carry ([I]), as you believe it to be (a mislabelled stack shows its label, 11 §8.2), with Satiety per
/// unit for food. ↑/↓ choose, [Enter] eats one (an <c>Eat</c> command; the sim decides what it truly was), [I] or [Esc]
/// closes. The list refreshes after every bite.
/// </summary>
public partial class SimHost
{
    private PanelContainer? _invPage;
    private Label? _invText;
    private List<(string Id, string Name, int Qty, float Sat)> _invItems = [];
    private int _invSelected;

    private bool InventoryOpen => _invPage?.Visible == true;

    private void InitInventory()
    {
        _invPage = new PanelContainer { Position = new Vector2(12, 120), Size = new Vector2(440, 360), Visible = false };
        _invText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        _invPage.AddChild(_invText);
        GetNode<CanvasLayer>("Overlay").AddChild(_invPage);
    }

    private void ToggleInventory()
    {
        if (_invPage is null) { return; }
        if (_invPage.Visible) { _invPage.Visible = false; return; }
        RefreshInventory(show: true);
    }

    private void RefreshInventory(bool show)
    {
        _ = _runner!.Invoke(w =>
        {
            var items = new List<(string, string, int, float)>();
            foreach (var s in w.Inventory.Of(w.PlayerId))
            {
                var seen = w.Content.Items[s.Seen];
                items.Add((seen.Id, seen.Name, s.Qty, seen.Food?.Sat ?? -1f));
            }

            return items.GroupBy(i => i.Item1).Select(g => (g.Key, g.First().Item2, g.Sum(i => i.Item3), g.First().Item4)).ToList();
        }).ContinueWith(t => _ui.Enqueue(() =>
        {
            _invItems = t.Result;
            _invSelected = Math.Clamp(_invSelected, 0, Math.Max(0, _invItems.Count - 1));
            DrawInventory();
            if (show) { _invPage!.Visible = true; }
        }), TaskScheduler.Default);
    }

    private void DrawInventory()
    {
        var lines = new List<string> { "WHAT YOU CARRY   ↑↓ choose · [Enter] eat · [D] put one down · [I] close" };
        for (var k = 0; k < _invItems.Count; k++)
        {
            var (_, name, qty, sat) = _invItems[k];
            lines.Add($"{(k == _invSelected ? "▸ " : "   ")}{name} × {qty}{(sat >= 0 ? $"   ({sat:0} Sat each)" : "")}");
        }

        if (_invItems.Count == 0) { lines.Add("(nothing)"); }
        _invText!.Text = string.Join("\n", lines);
    }

    /// <summary>Keys while the panel is open; true if used.</summary>
    private bool InventoryKey(Key key)
    {
        if (!InventoryOpen) { return false; }
        switch (key)
        {
            case Key.Up: _invSelected = Math.Max(0, _invSelected - 1); DrawInventory(); return true;
            case Key.Down: _invSelected = Math.Min(Math.Max(0, _invItems.Count - 1), _invSelected + 1); DrawInventory(); return true;
            case Key.Enter or Key.KpEnter when _invItems.Count > 0 && _invItems[_invSelected].Sat >= 0:
                _runner!.Submit(CommandSource.Player, new Eat(_runner.Snapshots.ReadLatest().PlayerId, _invItems[_invSelected].Id));
                return true;
            case Key.D when _invItems.Count > 0:
                _runner!.Submit(CommandSource.Player, new Drop(_runner.Snapshots.ReadLatest().PlayerId, _invItems[_invSelected].Id, 1));
                return true;
            case Key.Escape or Key.I: _invPage!.Visible = false; return true;
        }

        return false;
    }

    /// <summary>What eating did (or why not), as a subtitle; the panel refreshes.</summary>
    private bool OnEatEvent(object payload)
    {
        switch (payload)
        {
            case Sim.Events.Ate a when a.Eater == _runner!.Snapshots.ReadLatest().PlayerId:
                Say($"You eat the {_content!.Items[a.Seen].Name.ToLowerInvariant()} (+{a.Sat:0} Satiety).");
                _eaten++;
                if (InventoryOpen) { RefreshInventory(show: false); }
                return true;
            case Sim.Events.CommandRejected r when r.Reason.StartsWith("Eat:", StringComparison.Ordinal):
                Say(r.Reason[4..].Trim());
                return true;
        }

        return false;
    }
}
