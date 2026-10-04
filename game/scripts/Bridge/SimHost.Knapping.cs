using FeudalSim.Game.UI;
using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>M2-11b: [K] knaps a flint knife at the bench (13 §8.1) — the panel plays the sim's seeded primitives.</summary>
public partial class SimHost
{
    private KnapView? _knap;
    private bool _autotestKnap, _openKnap;
    private double _knapAutotestAt = -1;

    private void InitKnapping()
    {
        _knap = new KnapView
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -460, OffsetRight = 460, OffsetTop = -230, OffsetBottom = 230,
            Visible = false, FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop,
            Curve = _content!.Minigame("minigame.knapping"), Autoplay = _autotestKnap,
            Submit = c => _runner!.Submit(CommandSource.Player, c),
            Snapshot = () => _runner!.Snapshots.ReadLatest(),
            SetSpeed = scale => this.SetSpeed(scale),   // the bench's time-lapse (13 §3.1)
        };
        GetNode("Overlay").AddChild(_knap);
        if (_autotestKnap || _openKnap) { _knapAutotestAt = Time.GetTicksMsec() / 1000.0 + 2.0; }   // wall clock: the bench's time-lapse speeds Godot's delta
    }

    private bool Knapping => _knap?.Visible == true;

    private void TryKnap()
    {
        if (_knap is null || Knapping || _dialogue?.Conversation is not null) { return; }
        _knap.SetRestoreSpeed(_timeScale);
        var snap = _runner!.Snapshots.ReadLatest();
        if (snap.PlayerProcess != 0) { _knap.Begin(snap.PlayerProcess); return; }   // pick up work in progress
        _runner.Submit(CommandSource.Player, new StartProcess(snap.PlayerId, "recipe.flint_knife"));
    }

    /// <summary>Process events go to the bench; the player's own start opens it.</summary>
    private bool OnKnapEvent(object payload)
    {
        if (_knap is null) { return false; }
        switch (payload)
        {
            case FeudalSim.Sim.Events.ProcessStarted s when s.Worker == _runner!.Snapshots.ReadLatest().PlayerId && _content!.Recipes[s.Recipe].Id == "recipe.flint_knife":
                _knap.Begin(s.Process);
                return true;
            case FeudalSim.Sim.Events.StageResolved or FeudalSim.Sim.Events.ProcessCompleted or FeudalSim.Sim.Events.ProcessRuined when Knapping:
                _knap.OnEvent(payload);
                if (_autotestKnap && _knap.Finished) { FinishKnapAutotest(payload); }
                return true;
            case FeudalSim.Sim.Events.CommandRejected r when r.Reason.StartsWith("StartProcess", StringComparison.Ordinal) || r.Reason.StartsWith("WorkStage", StringComparison.Ordinal):
                GD.Print($"SimHost: {r.Reason}");
                if (_autotestKnap) { GD.Print("SimHost: KNAP AUTOTEST FAIL"); GetTree().Quit(1); }
                return true;
        }

        return false;
    }

    private void UpdateKnapAutotest()
    {
        if (!_autotestKnap && !_openKnap) { return; }
        var now = Time.GetTicksMsec() / 1000.0;
        if (_knapAutotestAt > 0 && now >= _knapAutotestAt) { _knapAutotestAt = -1; TryKnap(); }
        if (!_autotestKnap) { return; }   // --open-knap (dev): just open the bench, e.g. for `--shot`
        if (now > 120) { GD.Print("SimHost: KNAP AUTOTEST FAIL (timeout)"); GetTree().Quit(1); }
    }

    private void FinishKnapAutotest(object payload)
    {
        var ok = payload is FeudalSim.Sim.Events.ProcessCompleted or FeudalSim.Sim.Events.ProcessRuined;
        GD.Print($"SimHost: knapped → {(payload is FeudalSim.Sim.Events.ProcessCompleted c ? $"Q {c.Q}" : "ruined (snapped)")}");
        GD.Print($"SimHost: KNAP AUTOTEST {(ok ? "PASS" : "FAIL")}");
        QuitCleanly();
    }
}
