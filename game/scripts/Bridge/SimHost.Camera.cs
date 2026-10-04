using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// M2-FP2 (canon v0.3.2): first person by default — the camera at the player's eyes, mouse look, WASD relative to where
/// you look — with an over-the-shoulder third-person toggle [V] (wheel sets its distance). The mouse is captured while you
/// walk and freed for the dialogue panel, the knapping bench and the People page; [Tab] frees or recaptures it by hand, a
/// click recaptures it. Interaction is by looking: the nearest thing in a narrow cone in front of the eyes within reach is
/// the target, and the prompt names it ("[E] talk to Rowan"). FP2 targets settlers; M2-FP3 adds trees, plants and rocks.
/// </summary>
public partial class SimHost
{
    public const float EyeHeight = 1.62f, ReachM = 3.5f, LookConeDeg = 14f, MouseSensitivity = 0.0024f;
    private float _yaw, _pitch;
    private bool _firstPerson = true, _mouseFreed;
    private Control? _crosshair;

    /// <summary>What the eyes rest on: a kind, an id and a name for the prompt.</summary>
    public readonly record struct LookTarget(string Kind, ulong Id, string Name, Vector3 At);

    private LookTarget? _look;

    private bool UiOpen => _dialogue?.Conversation is not null || Knapping || _peoplePage?.Visible == true;

    private void InitCamera()
    {
        _crosshair = new ColorRect { Color = new Color(1, 1, 1, 0.8f), Size = new Vector2(4, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        _crosshair.Position -= new Vector2(2, 2);
        GetNode<CanvasLayer>("Overlay").AddChild(_crosshair);
        if (_island is not null && _islandFacing is { } face) { _yaw = face; }   // start facing the wreck and the sea
    }

    /// <summary>The direction you look along, flattened (movement and the body's facing use it).</summary>
    private Vector2 Forward => new(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));

    private Vector2 Right => new(Mathf.Cos(_yaw), -Mathf.Sin(_yaw));

    /// <summary>WASD → a world direction relative to the view (W = where you look).</summary>
    private Vector2 ViewRelative(Vector2 input) => (Forward * -input.Y) + (Right * input.X);

    private void UpdateMouseMode()
    {
        var want = _play && !UiOpen && !_mouseFreed && !_autotestCamp && !_autotestIsland && DisplayServer.GetName() != "headless"
            ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != want) { Input.MouseMode = want; }
    }

    /// <summary>Mouse look and the camera keys; true if the event was used.</summary>
    private bool CameraInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw -= m.Relative.X * MouseSensitivity;
                _pitch = Math.Clamp(_pitch - (m.Relative.Y * MouseSensitivity), -1.45f, 1.45f);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Input.MouseMode != Input.MouseModeEnum.Captured && !UiOpen:
                _mouseFreed = false;
                return true;
            case InputEventMouseButton { Pressed: true } z when z.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                if (!_firstPerson) { _zoom = Math.Clamp(_zoom * (z.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f), 0.4f, 3f); }
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.V } when !UiOpen:
                _firstPerson = !_firstPerson;
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab } when !UiOpen:
                _mouseFreed = !_mouseFreed;
                return true;
        }

        return false;
    }

    /// <summary>Places the camera: at the eyes (first person) or over the right shoulder (third person, kept above the ground).</summary>
    private void UpdateCamera()
    {
        var ground = Ground(_player.X, _player.Y);
        var eye = new Vector3(_player.X, ground + EyeHeight, _player.Y);
        var basis = Basis.FromEuler(new Vector3(_pitch, _yaw, 0));
        if (_firstPerson)
        {
            _camera.GlobalTransform = new Transform3D(basis, eye);
            _camera.Fov = 75;
        }
        else
        {
            var back = basis.Z * (3.2f * _zoom);
            var shoulder = basis.X * 0.55f;
            var at = eye + back + shoulder + new Vector3(0, 0.25f, 0);
            at.Y = Math.Max(at.Y, Ground(at.X, at.Z) + 0.4f);
            _camera.GlobalTransform = new Transform3D(basis, at);
            _camera.Fov = 70;
        }

        _playerBody!.Visible = !_firstPerson;   // the head-hidden body view arrives with the modular character (M2-FP4)
        if (_crosshair is not null) { _crosshair.Visible = _firstPerson && !UiOpen; }
    }

    /// <summary>The thing you look at within reach: smallest angle off the view direction inside the cone (settlers now; nodes in FP3).</summary>
    private LookTarget? FindLookTarget()
    {
        var eye = _camera.GlobalPosition;
        var forward = -_camera.GlobalBasis.Z;
        LookTarget? best = null;
        var bestAngle = Mathf.DegToRad(LookConeDeg);
        foreach (var (id, p) in _people)
        {
            if (p.Body.Rotation.X != 0) { continue; }   // lying down: asleep, down or dead
            var flat = new Vector2(p.Body.Position.X, p.Body.Position.Z).DistanceTo(_player);
            if (flat > ReachM + 0.5f) { continue; }
            var chest = p.Body.Position + new Vector3(0, 1.3f, 0);
            var angle = forward.AngleTo(chest - eye);
            var size = Mathf.Atan2(0.45f, Math.Max(0.5f, eye.DistanceTo(chest)));   // a body's half-width as seen from here
            if (angle - size < bestAngle) { (best, bestAngle) = (new LookTarget("person", id, _names.GetValueOrDefault(id, "them"), chest), angle - size); }
        }

        foreach (var t in _lookables)
        {
            if (new Vector2(t.At.X, t.At.Z).DistanceTo(_player) > ReachM) { continue; }
            var angle = forward.AngleTo(t.At - eye);
            if (angle < bestAngle) { (best, bestAngle) = (t, angle); }
        }

        return best;
    }

    /// <summary>World things you can look at besides people (M2-FP3 fills this from the nodes around you).</summary>
    private readonly List<LookTarget> _lookables = [];

    private float? _islandFacing;

    /// <summary>The prompt for a world thing (M2-FP3 adds the verbs per node kind).</summary>
    private static string LookPrompt(LookTarget t) => t.Name;
}
