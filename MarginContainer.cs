using Godot;

public partial class MarginContainer : Godot.MarginContainer
{
    public override void _Ready()
    {
        GetViewport().SizeChanged += ApplySafeArea;
        ApplySafeArea();
    }

    private void ApplySafeArea()
    {
        var safeArea = DisplayServer.GetDisplaySafeArea();
        var windowPosition = DisplayServer.WindowGetPosition();
        var windowSize = DisplayServer.WindowGetSize();

        if (windowSize.X <= 0 || windowSize.Y <= 0)
        {
            return;
        }

        var viewportRect = GetViewportRect();
        var xScale = viewportRect.Size.X / windowSize.X;
        var yScale = viewportRect.Size.Y / windowSize.Y;

        if (safeArea.Position == Vector2.Zero &&
            (safeArea.Size == Vector2.Zero || safeArea.Size == windowSize))
        {
            SetMargins(0, 0, 0, 0);
            return;
        }

        const int minPadding = 16;
        var safeAreaLeft = Mathf.Max(0, safeArea.Position.X - windowPosition.X);
        var safeAreaTop = Mathf.Max(0, safeArea.Position.Y - windowPosition.Y);
        var safeAreaRight = Mathf.Min(
            windowSize.X,
            safeArea.Position.X + safeArea.Size.X - windowPosition.X);
        var safeAreaBottom = Mathf.Min(
            windowSize.Y,
            safeArea.Position.Y + safeArea.Size.Y - windowPosition.Y);

        if (safeAreaRight <= safeAreaLeft || safeAreaBottom <= safeAreaTop)
        {
            safeAreaLeft = 0;
            safeAreaTop = 0;
            safeAreaRight = windowSize.X;
            safeAreaBottom = windowSize.Y;
        }

        var marginLeft = Mathf.RoundToInt(safeAreaLeft * xScale);
        var marginTop = Mathf.RoundToInt(safeAreaTop * yScale);
        var marginRight = Mathf.RoundToInt((windowSize.X - safeAreaRight) * xScale);
        var marginBottom = Mathf.RoundToInt((windowSize.Y - safeAreaBottom) * yScale);

        SetMargins(
            marginLeft > 0 ? Mathf.Max(minPadding, marginLeft) : 0,
            marginTop > 0 ? Mathf.Max(minPadding, marginTop) : 0,
            marginRight > 0 ? Mathf.Max(minPadding, marginRight) : 0,
            marginBottom > 0 ? Mathf.Max(minPadding, marginBottom) : 0);
    }

    private void SetMargins(int left, int top, int right, int bottom)
    {
        AddThemeConstantOverride("margin_left", left);
        AddThemeConstantOverride("margin_top", top);
        AddThemeConstantOverride("margin_right", right);
        AddThemeConstantOverride("margin_bottom", bottom);
    }
}
