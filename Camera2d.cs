using Godot;

public partial class Camera2d : Camera2D
{
    [Export] private float _smoothSpeed = 5.0f;
    [Export] private float _minZoom = 0.1f;
    [Export] private float _maxZoom = 3.0f;
    [Export] private float _padding = 200.0f;
    
    private bool _isInitialized = false;
    
    public override void _Process(double delta)
    {
        var nodes = GetTree().GetNodesInGroup("bodies");
        if (nodes.Count == 0)
        {
            return;
        }

        var minPos = new Vector2(float.MaxValue, float.MaxValue);
        var maxPos = new Vector2(float.MinValue, float.MinValue);
        var validCount = 0;

        foreach (var node in nodes)
        {
            if (node is not Body body)
            {
                continue;
            }
            var pos = body.GlobalPosition;
            minPos.X = Mathf.Min(minPos.X, pos.X);
            minPos.Y = Mathf.Min(minPos.Y, pos.Y);
            maxPos.X = Mathf.Max(maxPos.X, pos.X);
            maxPos.Y = Mathf.Max(maxPos.Y, pos.Y);
            validCount++;
        }
        
        if (validCount == 0)
        {
            return;
        }
        
        var center = (minPos + maxPos) * 0.5f;
        
        var boxWidth = (maxPos.X - minPos.X) + _padding * 2.0f;
        var boxHeight = (maxPos.Y - minPos.Y) + _padding * 2.0f;
        var viewportSize = GetViewportRect().Size;
        if (viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            return;
        }
        
        var zoomX = viewportSize.X / boxWidth;
        var zoomY = viewportSize.Y / boxHeight;
        var targetZoomValue = Mathf.Min(zoomX, zoomY);
        targetZoomValue = Mathf.Clamp(targetZoomValue, _minZoom, _maxZoom);
        
        if (!_isInitialized)
        {
            GlobalPosition = center;
            Zoom = new Vector2(targetZoomValue, targetZoomValue);
            _isInitialized = true;
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(center, (float)delta * _smoothSpeed);
        
        var currentZoom = Zoom.X;
        var smoothedZoom = Mathf.Lerp(currentZoom, targetZoomValue, (float)delta * _smoothSpeed);
        Zoom = new Vector2(smoothedZoom, smoothedZoom);
    }
}
