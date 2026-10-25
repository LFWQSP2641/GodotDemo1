using System.Linq;
using Godot;

public partial class SidebarController : Control
{
    [Export] private Control _sidebarPanel = null!;
    [Export] private Button _toggleButton = null!;
    [Export] private float _animationDuration = 0.3f;

    [Export] private Label _kineticEnergyLabel = null!;
    [Export] private Label _gravitationalPotentialEnergyLabel = null!;
    [Export] private Label _totalEnergyLabel = null!;
    [Export] private Label _momentumLabel = null!;
    [Export] private Label _frameSkippedLabel = null!;

    [Export] private SpinBox _numberOfBodiesSpinBox = null!;
    [Export] private SpinBox _gravitationalConstantSpinBox = null!;
    [Export] private SpinBox _softeningFactorSpinBox = null!;
    [Export] private CheckButton _pullBackCheckButton = null!;
    [Export] private CheckButton _interpolateCheckButton = null!;
    [Export] private Button _applyAndResetButton = null!;

    private bool _isOpen;
    private float _sidebarWidth = 320.0f;
    private Tween? _tween;

    private ThreeBody? _threeBody;

    private int _frameSkipCount;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _sidebarWidth = _sidebarPanel.CustomMinimumSize.X > 0 ? _sidebarPanel.CustomMinimumSize.X : 320.0f;
        _sidebarPanel.Position = new Vector2(GetHiddenPanelPosition(), 0);
        _sidebarPanel.Visible = false;

        _toggleButton.Text = "显示";
        _toggleButton.Pressed += OnTogglePressed;
        _toggleButton.Position = new Vector2(0, _toggleButton.Position.Y);

        _threeBody = GetNode<ThreeBody>("../../../ThreeBody");
        GD.Print($"SidebarController: Found ThreeBody node: {_threeBody != null}");
        if (_threeBody != null)
        {
            _applyAndResetButton.Pressed += () =>
            {
                _threeBody.PauseSimulation();

                _threeBody.NumberOfBodies = (int)_numberOfBodiesSpinBox.Value;
                _threeBody.GravitationalConstant = (float)_gravitationalConstantSpinBox.Value;
                _threeBody.SofteningFactor = (float)_softeningFactorSpinBox.Value;
                _threeBody.EnablePullBack = _pullBackCheckButton.ButtonPressed;
                _threeBody.EnableInterpolation = _interpolateCheckButton.ButtonPressed;

                _threeBody.ResetSimulation();
            };

            _threeBody.FrameSkipped += () => { _frameSkipCount++; };
        }
    }

    public override void _Process(double delta)
    {
        if (!_isOpen)
        {
            return;
        }
        UpdateEnergyAndMomentum();
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isOpen)
        {
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouseEvent)
        {
            var mousePos = mouseEvent.Position;

            var panelRect = _sidebarPanel.GetGlobalRect();
            var toggleRect = _toggleButton.GetGlobalRect();

            if (!panelRect.HasPoint(mousePos) && !toggleRect.HasPoint(mousePos))
            {
                ToggleSidebar(false);

                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void UpdateEnergyAndMomentum()
    {
        var nodes = GetTree().GetNodesInGroup("bodies");
        var bodies = nodes.OfType<Body>().ToList();
        var count = bodies.Count;
        if (count == 0)
        {
            return;
        }

        long totalKineticEnergy = 0;
        long totalGravitationalPotentialEnergy = 0;
        long totalEnergy = 0;
        var totalMomentum = Vector2.Zero;

        for (var i = 0; i < count; i++)
        {
            var body = bodies[i];

            var speedSquared = body.Velocity.LengthSquared();
            totalKineticEnergy += (long)(0.5f * body.Mass * speedSquared);
            totalMomentum += body.Velocity * body.Mass;

            for (var j = i + 1; j < count; j++)
            {
                var otherBody = bodies[j];

                var direction = otherBody.Position - body.Position;
                var distanceSquared = direction.LengthSquared();
                totalGravitationalPotentialEnergy -= (long)(_threeBody!.GravitationalConstant * body.Mass * otherBody.Mass / Mathf.Sqrt(distanceSquared));
            }
        }

        totalEnergy = totalKineticEnergy + totalGravitationalPotentialEnergy;

        _kineticEnergyLabel.Text = $"总动能: {totalKineticEnergy}";
        _gravitationalPotentialEnergyLabel.Text = $"总引力势能: {totalGravitationalPotentialEnergy}";
        _totalEnergyLabel.Text = $"总能量: {totalEnergy}";
        _momentumLabel.Text = $"总动量: ({totalMomentum.X:F2}, {totalMomentum.Y:F2})";
        _frameSkippedLabel.Text = $"帧跳过: {_frameSkipCount}";
    }

    private void OnTogglePressed()
    {
        ToggleSidebar(!_isOpen);
    }

    private float GetHiddenPanelPosition()
    {
        var panelWidth = _sidebarPanel.Size.X > 0 ? _sidebarPanel.Size.X : _sidebarWidth;
        return -panelWidth - _sidebarPanel.GetParentControl().GlobalPosition.X;
    }

    private void ToggleSidebar(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;

        if (_tween != null && _tween.IsRunning())
        {
            _tween.Kill();
        }

        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.SetEase(Tween.EaseType.Out);
        _tween.SetTrans(Tween.TransitionType.Cubic);

        if (_isOpen)
        {
            _toggleButton.Text = "隐藏";
            _sidebarPanel.Visible = true;

            _tween.TweenProperty(_sidebarPanel, "position:x", 0.0f, _animationDuration);
            _tween.TweenProperty(_toggleButton, "position:x", _sidebarWidth, _animationDuration);
        }
        else
        {
            _toggleButton.Text = "显示";

            _tween.TweenProperty(_sidebarPanel, "position:x", GetHiddenPanelPosition(), _animationDuration);
            _tween.TweenProperty(_toggleButton, "position:x", 0.0f, _animationDuration);

            _tween.Chain().TweenCallback(Callable.From(() => _sidebarPanel.Visible = false));
        }
    }
}
