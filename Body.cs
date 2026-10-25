using Godot;

public partial class Body : Node2D
{
    [Export] public float Mass { get; set; } = 1000.0f;

    [Export] public Vector2 Velocity { get; set; } = Vector2.Zero;

	public Vector2 Acceleration { get; set; } = Vector2.Zero;
    
    private Line2D _trail;
    private const int MaxTrailPoints = 400;
    
	public override void _Ready()
	{
		_trail = new()
		{
			Width = 2.0f,
			DefaultColor = Colors.Cyan,
			TopLevel = true,
		};
		AddChild(_trail);
	}
    
    public void UpdateTrail()
    {
	    if (_trail == null)
	    {
		    return;
	    }
	    _trail.AddPoint(GlobalPosition);
	    if (_trail.GetPointCount() > MaxTrailPoints)
	    {
		    _trail.RemovePoint(0);
	    }
    }

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, 8.0f, Colors.White);
	}
}
