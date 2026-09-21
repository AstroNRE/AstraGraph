using System.Numerics;

namespace AstraGraph.Editor.Core;

public readonly record struct CanvasPoint(float X, float Y)
{
    public static CanvasPoint Zero => new(0f, 0f);

    public static CanvasPoint operator +(CanvasPoint a, CanvasPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static CanvasPoint operator -(CanvasPoint a, CanvasPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static CanvasPoint operator *(CanvasPoint a, float scalar) => new(a.X * scalar, a.Y * scalar);
    public static CanvasPoint operator /(CanvasPoint a, float scalar) => new(a.X / scalar, a.Y / scalar);

    public Vector2 ToVector2() => new(X, Y);
    public static CanvasPoint FromVector2(Vector2 v) => new(v.X, v.Y);
}

public readonly record struct CanvasRect(float X, float Y, float Width, float Height)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(CanvasPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public bool IntersectsWith(CanvasRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

public sealed class CanvasViewport
{
    private float _zoom = 1.0f;

    public float PanX { get; set; }
    public float PanY { get; set; }

    public float Zoom
    {
        get => _zoom;
        set => _zoom = Math.Clamp(value, 0.2f, 3.0f);
    }

    public CanvasPoint ScreenToWorld(CanvasPoint screenPoint)
    {
        var worldX = (screenPoint.X - PanX) / Zoom;
        var worldY = (screenPoint.Y - PanY) / Zoom;
        return new CanvasPoint(worldX, worldY);
    }

    public CanvasPoint WorldToScreen(CanvasPoint worldPoint)
    {
        var screenX = worldPoint.X * Zoom + PanX;
        var screenY = worldPoint.Y * Zoom + PanY;
        return new CanvasPoint(screenX, screenY);
    }

    public static CanvasPoint SnapToGrid(CanvasPoint point, float gridSize = 16.0f)
    {
        if (gridSize <= 0f) return point;
        var snappedX = MathF.Round(point.X / gridSize) * gridSize;
        var snappedY = MathF.Round(point.Y / gridSize) * gridSize;
        return new CanvasPoint(snappedX, snappedY);
    }
}
