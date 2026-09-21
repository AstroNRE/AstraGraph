using System.Collections.Generic;
using AstraGraph.Editor.Core;

namespace AstraGraph.Editor.UI;

public readonly record struct CanvasColor(byte R, byte G, byte B, byte A = 255)
{
    public static CanvasColor Black => new(0, 0, 0);
    public static CanvasColor White => new(255, 255, 255);
    public static CanvasColor Gray => new(128, 128, 128);
    public static CanvasColor DarkGray => new(45, 45, 48);
    public static CanvasColor LightGray => new(200, 200, 200);
    public static CanvasColor Blue => new(0, 122, 204);
    public static CanvasColor Green => new(106, 153, 85);
    public static CanvasColor Red => new(209, 105, 105);
    public static CanvasColor Yellow => new(220, 220, 100);
    public static CanvasColor Orange => new(214, 157, 133);
}

public interface ICanvasRenderer
{
    void DrawLine(CanvasPoint startPoint, CanvasPoint endPoint, CanvasColor color, float thickness = 1f);
    void DrawBezier(BezierCurve curve, CanvasColor color, float thickness = 2f);
    void DrawRectangle(CanvasRect rect, CanvasColor color, float thickness = 1f);
    void FillRectangle(CanvasRect rect, CanvasColor color);
    void DrawCircle(CanvasPoint center, float radius, CanvasColor color);
    void FillCircle(CanvasPoint center, float radius, CanvasColor color);
    void DrawText(string text, CanvasPoint position, CanvasColor color, float fontSize = 12f);
}

public sealed class MockCanvasRenderer : ICanvasRenderer
{
    public List<string> DrawCalls { get; } = [];

    public void DrawLine(CanvasPoint startPoint, CanvasPoint endPoint, CanvasColor color, float thickness = 1)
    {
        DrawCalls.Add($"Line: ({startPoint.X},{startPoint.Y})->({endPoint.X},{endPoint.Y})");
    }

    public void DrawBezier(BezierCurve curve, CanvasColor color, float thickness = 2)
    {
        DrawCalls.Add($"Bezier: ({curve.Start.X},{curve.Start.Y})->({curve.End.X},{curve.End.Y})");
    }

    public void DrawRectangle(CanvasRect rect, CanvasColor color, float thickness = 1)
    {
        DrawCalls.Add($"Rect: ({rect.X},{rect.Y},{rect.Width},{rect.Height})");
    }

    public void FillRectangle(CanvasRect rect, CanvasColor color)
    {
        DrawCalls.Add($"FillRect: ({rect.X},{rect.Y},{rect.Width},{rect.Height})");
    }

    public void DrawCircle(CanvasPoint center, float radius, CanvasColor color)
    {
        DrawCalls.Add($"Circle: ({center.X},{center.Y}), r={radius}");
    }

    public void FillCircle(CanvasPoint center, float radius, CanvasColor color)
    {
        DrawCalls.Add($"FillCircle: ({center.X},{center.Y}), r={radius}");
    }

    public void DrawText(string text, CanvasPoint position, CanvasColor color, float fontSize = 12)
    {
        DrawCalls.Add($"Text: '{text}' at ({position.X},{position.Y})");
    }
}
