using Godot;

public partial class SidebarController : Control
{
    [Export] private Control _sidebarPanel;
    [Export] private Button _toggleButton;
    [Export] private float _animationDuration = 0.3f;
    
    [Export] private Label _energyLabel;
    [Export] private Label _momentumLabel;
    
    [Export] private SpinBox _numberOfBodiesSpinBox;
    [Export] private SpinBox _gravitationalConstantSpinBox;
    [Export] private SpinBox _softeningFactorSpinBox;
    [Export] private CheckButton _pullBackCheckButton;
    [Export] private Button _applyAndResetButton;

    private bool _isOpen;
    private float _sidebarWidth = 320.0f;
    private Tween _tween;
    
    private ThreeBody _threeBody;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        if (_sidebarPanel != null)
        {
            _sidebarWidth = _sidebarPanel.CustomMinimumSize.X > 0 ? _sidebarPanel.CustomMinimumSize.X : 320.0f;
            _sidebarPanel.Position = new Vector2(GetHiddenPanelPosition(), 0);
            _sidebarPanel.Visible = false;
        }

        if (_toggleButton != null)
        {
            _toggleButton.Text = "显示";
            _toggleButton.Pressed += OnTogglePressed;
            _toggleButton.Position = new Vector2(0, _toggleButton.Position.Y);
        }
        
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

                _threeBody.ResetSimulation();
            };
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
        
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed &&
            mouseEvent.ButtonIndex == MouseButton.Left)
        {
            var mousePos = mouseEvent.Position;

            var panelRect = _sidebarPanel.GetGlobalRect();
            var toggleRect = _toggleButton.GetGlobalRect();

            if (!panelRect.HasPoint(mousePos) && !toggleRect.HasPoint(mousePos))
            {
                ToggleSidebar(false);
            }
        }
    }
    
    private void UpdateEnergyAndMomentum()
    {
        var nodes = GetTree().GetNodesInGroup("bodies");
        if (nodes.Count == 0)
        {
            return;
        }

        var totalKineticEnergy = 0.0f;
        var totalMomentum = Vector2.Zero;

        foreach (var node in nodes)
        {
            if (node is not Body body)
            {
                continue;
            }

            var speedSquared = body.Velocity.LengthSquared();
            totalKineticEnergy += 0.5f * body.Mass * speedSquared;
            totalMomentum += body.Velocity * body.Mass;
        }

        _energyLabel.Text = $"总动能: {totalKineticEnergy:F2}";
        _momentumLabel.Text = $"总动量: ({totalMomentum.X:F2}, {totalMomentum.Y:F2})";
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
