using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Crafting.Minigames;
using Godot;

namespace FeudalSim.Game.UI;

/// <summary>
/// M2-11b: the knapping bench (13 §7.3–7.4, §8.1). It plays the sim's own seeded primitives — the nodules, targets and
/// path the sim would replay — and sends each stage's calibrated m through <c>WorkStage</c>; the sim resolves it with
/// 12's <c>Resolve()</c>. Choose: click a nodule, then click its rim where you'd strike. Rough out / thin: move along the
/// edge, Q/E set the platform angle, hold the left button to charge, release to strike (6 each). Pressure: hold the
/// left button and trace the serrations left to right. Esc auto-resolves the stage as an NPC would.
/// </summary>
public partial class KnapView : Control
{
    public Action<StateCommand>? Submit;
    public Func<RenderSnapshot>? Snapshot;
    public Action<double>? SetSpeed;
    public MinigameDef? Curve;
    public bool Autoplay;   // --autotest-knap: scripted attentive inputs

    private static readonly string[] StageNames = ["Choose a nodule", "Rough out", "Thin", "Pressure-flake"];
    private ulong _process;
    private int _stage = -1;
    private bool _awaiting;
    private double _stageStart, _clock, _restoreSpeed = 1;
    private float[] _nodules = [];
    private int _chosen = -1;
    private readonly List<Strike> _strikes = [];
    private readonly List<string> _flakes = [];
    private float _angle = 70f, _charge;
    private bool _charging;
    private readonly float[] _path = new float[Knapping.TracePoints];
    private int _traced;
    private string _status = "", _result = "";
    private FeudalSim.Sim.Core.Rng _hand = new(11);

    public bool Finished { get; private set; }

    public void Begin(ulong process)
    {
        (_process, _stage, _awaiting, Finished, _result) = (process, -1, false, false, "");
        Visible = true;
        GrabFocus();
    }

    /// <summary>Stage and result events from the sim (13 §7.4 result card).</summary>
    public void OnEvent(object payload)
    {
        switch (payload)
        {
            case FeudalSim.Sim.Events.StageResolved r when r.Process == _process:
                _status = $"{StageNames[Math.Min(r.Stage, 3)]}: {(FeudalSim.Sim.Skills.Outcome)r.Outcome} · PS {r.Ps:F0}{(float.IsNaN(r.M) ? " (auto)" : $" · m {r.M:+0.00;-0.00}")}";
                break;
            case FeudalSim.Sim.Events.ProcessCompleted c when c.Process == _process:
                var grade = FeudalSim.Sim.Items.Quality.GradeOf(c.Q);
                _result = $"Flint knife — Q {c.Q} ({grade}){(c.Flaws != 0 ? " · flawed" : "")}. [Esc] close";
                (Finished, _stage) = (true, 4);
                SetSpeed?.Invoke(_restoreSpeed);
                break;
            case FeudalSim.Sim.Events.ProcessRuined u when u.Process == _process:
                _result = "The blade snapped — you keep a few flakes. [Esc] close";
                (Finished, _stage) = (true, 4);
                SetSpeed?.Invoke(_restoreSpeed);
                break;
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_process == 0 || Finished || Snapshot?.Invoke() is not { } snap) { QueueRedraw(); return; }
        var minute = snap.GameMs / FeudalSim.Sim.Time.SimClock.MsPerGameMinute;
        var busy = snap.PlayerProcess == _process && minute < snap.ProcessBusyUntilMin;
        if (_awaiting)
        {
            if (busy || snap.PlayerProcess != _process || snap.ProcessStage <= _stage) { QueueRedraw(); return; }
            _awaiting = false;
            SetSpeed?.Invoke(_restoreSpeed);
        }

        if (snap.PlayerProcess == _process && !busy && snap.ProcessStage != _stage && snap.ProcessStage < 4) { StartStage(snap.ProcessStage, snap); }
        if (_charging) { _charge = Math.Min(1f, _charge + (float)(delta / 1.2)); }
        if (Autoplay && !_awaiting && _stage is >= 0 and < 4) { AutoPlay(snap); }
        QueueRedraw();
    }

    private Feel Feel(RenderSnapshot snap) => new(snap.StageGrip, snap.StageDex);

    private void StartStage(int stage, RenderSnapshot snap)
    {
        (_stage, _stageStart, _chosen, _traced, _charge, _charging) = (stage, Time.GetTicksMsec() / 1000.0, -1, 0, 0f, false);
        _strikes.Clear();
        _flakes.Clear();
        if (stage == 0) { _nodules = Knapping.Nodules(snap.WorldSeed, _process); }
    }

    /// <summary>Ends the stage: m from the shipped curve (or NaN to auto-resolve) → WorkStage; time-lapse at 8× while the labor runs.</summary>
    private void Commit(float raw, bool catastrophic, bool auto = false)
    {
        var snap = Snapshot!.Invoke();
        var m = auto || Curve is null ? float.NaN : Calibration.M(Curve.Stages[_stage], Feel(snap).Band, raw, catastrophic);
        Submit?.Invoke(new WorkStage(_process, m, (float)((Time.GetTicksMsec() / 1000.0) - _stageStart)));   // real seconds played (13 §3.1)
        _awaiting = true;
        _status = "…the work goes on (time passes)";
        SetSpeed?.Invoke(8);
    }

    public void SetRestoreSpeed(double speed) => _restoreSpeed = speed;

    public override void _GuiInput(InputEvent e)
    {
        if (_stage is < 0 or > 3 || _awaiting || Snapshot?.Invoke() is not { } snap) { return; }
        var size = Size;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when _stage == 0 && b.Pressed:
                var at = b.Position;
                for (var k = 0; k < _nodules.Length; k++)
                {
                    var c = NodulePos(k, size);
                    var d = at.DistanceTo(c);
                    if (_chosen < 0 && d < 55f) { _chosen = k; return; }
                    if (_chosen == k && d is > 40f and < 80f)
                    {
                        var mark = (Mathf.Atan2(at.Y - c.Y, at.X - c.X) + Mathf.Pi) / Mathf.Tau;
                        Commit(Knapping.ChooseScore(Feel(snap), _nodules, k, mark, snap.WorldSeed, _process), false);
                        return;
                    }
                }

                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when _stage is 1 or 2:
                if (b.Pressed) { (_charging, _charge) = (true, 0f); }
                else if (_charging) { Strike(EdgeFraction(b.Position.X, size), snap); }
                break;
            case InputEventMouseMotion mm when _stage == 3 && (mm.ButtonMask & MouseButtonMask.Left) != 0:
                var x = EdgeFraction(mm.Position.X, size);
                while (_traced < _path.Length && x >= (_traced + 0.5f) / _path.Length)
                {
                    _path[_traced++] = Mathf.Clamp(1f - ((mm.Position.Y - (size.Y * 0.35f)) / (size.Y * 0.4f)), 0f, 1f);
                }

                if (_traced == _path.Length) { Commit(Knapping.TraceStage(Feel(snap), _path, snap.WorldSeed, _process), false); }
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Q }:
                _angle -= 2f;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.E }:
                _angle += 2f;
                break;
        }
    }

    private void Strike(float point, RenderSnapshot snap)
    {
        _charging = false;
        var k = _strikes.Count;
        var s = new Strike(point, _angle, _charge);
        _strikes.Add(s);
        var lip = _flakes.Count > 0 && _flakes[^1] == "step" ? 0.85f : 1f;
        var (result, _) = Knapping.Hit(Feel(snap), Knapping.Target(snap.WorldSeed, _process, _stage, k), s, lip, snap.WorldSeed, _process, _stage, k);
        _flakes.Add(result switch { FlakeResult.Clean => "clean", FlakeResult.StepHinge => "step", FlakeResult.Poor => "poor", _ => "SNAP" });
        if (result == FlakeResult.Snap || _strikes.Count == Knapping.StrikesPerStage)
        {
            var (raw, cat) = Knapping.StrikeStage(Feel(snap), _chosen >= 0 ? _nodules[_chosen] : 0.65f, [.. _strikes], snap.WorldSeed, _process, _stage);
            Commit(raw, cat);
        }
    }

    /// <summary>Esc: auto-resolve this stage at the player's E (13 §7.5) — or close the finished card.</summary>
    public bool Escape()
    {
        if (Finished) { Visible = false; _process = 0; return true; }
        if (_stage is >= 0 and < 4 && !_awaiting) { Commit(0f, false, auto: true); return true; }
        return false;
    }

    private void AutoPlay(RenderSnapshot snap)
    {
        var feel = Feel(snap);
        var (raw, cat) = BotPlayer.Attentive.Play(feel, _stage, snap.WorldSeed, _process, _nodules.Length > 0 ? _nodules.Max() : 0.65f, ref _hand);
        Commit(raw, cat);
    }

    private static Vector2 NodulePos(int k, Vector2 size) => new(size.X * (0.2f + (0.2f * k)), size.Y * 0.5f);

    private static float EdgeFraction(float x, Vector2 size) => Mathf.Clamp((x - 60f) / (size.X - 120f), 0f, 1f);

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.08f, 0.07f, 0.06f, 0.99f));
        var font = ThemeDB.FallbackFont;
        void Text(string s, Vector2 at, int px = 18, Color? c = null) => DrawString(font, at, s, HorizontalAlignment.Left, -1, px, c ?? new Color(1f, 0.95f, 0.85f));
        Text($"Knapping · {(_stage is >= 0 and < 4 ? StageNames[_stage] : "")}", new Vector2(20, 32), 22);
        Text(_status, new Vector2(20, 60), 16, new Color(0.85f, 0.85f, 0.7f));
        if (_result.Length > 0) { Text(_result, new Vector2(20, size.Y - 24), 20, new Color(0.6f, 1f, 0.6f)); return; }
        var flint = new Color(0.45f, 0.47f, 0.52f);
        switch (_stage)
        {
            case 0:
                Text(_chosen < 0 ? "Click a nodule to pick it up and tap it." : "Click its rim where you'd strike the first flake.", new Vector2(20, 90), 16);
                for (var k = 0; k < _nodules.Length; k++)
                {
                    var c = NodulePos(k, size);
                    DrawCircle(c, 50f, k == _chosen ? flint.Lightened(0.25f) : flint);
                    DrawArc(c, 50f, 0, Mathf.Tau, 48, new Color(0.2f, 0.2f, 0.22f), 3f);
                    if (k == _chosen) { Text($"rings {(_nodules[k] > 0.75f ? "clear" : _nodules[k] > 0.5f ? "dull" : "flat")}", c + new Vector2(-30, 75), 15); }
                }

                break;
            case 1 or 2:
                var y = size.Y * 0.55f;
                DrawLine(new Vector2(60, y), new Vector2(size.X - 60, y), flint.Lightened(0.2f), 6f);
                var next = _strikes.Count;
                if (next < Knapping.StrikesPerStage && Snapshot?.Invoke() is { } s)
                {
                    var t = Knapping.Target(s.WorldSeed, _process, _stage, next);
                    var tx = 60 + (t.Point * (size.X - 120));
                    DrawLine(new Vector2(tx, y - 18), new Vector2(tx, y + 18), new Color(0.9f, 0.7f, 0.3f, 0.6f), 2f);   // where the flake wants to come off
                    var bar = new Rect2(size.X - 60, size.Y * 0.2f, 18, size.Y * 0.5f);
                    DrawRect(bar, new Color(0.2f, 0.2f, 0.2f));
                    DrawRect(new Rect2(bar.Position.X, bar.End.Y - (bar.Size.Y * _charge), bar.Size.X, bar.Size.Y * _charge), new Color(0.9f, 0.5f, 0.2f));
                    DrawLine(new Vector2(bar.Position.X - 4, bar.End.Y - (bar.Size.Y * t.Force)), new Vector2(bar.End.X + 4, bar.End.Y - (bar.Size.Y * t.Force)), new Color(1f, 0.85f, 0.4f), 2f);
                }

                var mx = Mathf.Clamp(GetLocalMousePosition().X, 60, size.X - 60);
                var dir = Vector2.FromAngle(Mathf.DegToRad(-_angle));
                DrawLine(new Vector2(mx, y), new Vector2(mx, y) + (dir * 70f), new Color(0.8f, 0.8f, 0.9f), 3f);
                Text($"angle {_angle:F0}° (Q/E) · hold to charge, release to strike · strikes {_strikes.Count}/{Knapping.StrikesPerStage} · {string.Join(" ", _flakes)}", new Vector2(20, 90), 16);
                break;
            case 3:
                Text("Hold the button and trace the serrations, left to right.", new Vector2(20, 90), 16);
                if (Snapshot?.Invoke() is { } st)
                {
                    for (var k = 0; k < Knapping.TracePoints; k++)
                    {
                        var px = 60 + ((k + 0.5f) / Knapping.TracePoints * (size.X - 120));
                        var py = (size.Y * 0.35f) + ((1f - Knapping.TraceTarget(st.WorldSeed, _process, k)) * size.Y * 0.4f);
                        DrawCircle(new Vector2(px, py), 4f, k < _traced ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.9f, 0.7f, 0.3f));
                    }
                }

                break;
        }

        Text("[Esc] let your hands do it (auto-resolve)", new Vector2(20, size.Y - 24), 14, new Color(0.7f, 0.7f, 0.7f));
    }
}
