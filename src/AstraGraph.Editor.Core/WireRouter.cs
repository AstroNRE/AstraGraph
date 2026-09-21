using System;
using System.Collections.Generic;

namespace AstraGraph.Editor.Core;

public readonly record struct BezierCurve(
    CanvasPoint Start,
    CanvasPoint Control1,
    CanvasPoint Control2,
    CanvasPoint End)
{
    public CanvasPoint Evaluate(float t)
    {
        var clampedT = Math.Clamp(t, 0f, 1f);
        var u = 1f - clampedT;
        var tt = clampedT * clampedT;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * clampedT;

        var x = (uuu * Start.X) + (3f * uu * clampedT * Control1.X) + (3f * u * tt * Control2.X) + (ttt * End.X);
        var y = (uuu * Start.Y) + (3f * uu * clampedT * Control1.Y) + (3f * u * tt * Control2.Y) + (ttt * End.Y);

        return new CanvasPoint(x, y);
    }
}

public static class WireRouter
{
    public static BezierCurve CalculateCubicBezier(CanvasPoint start, CanvasPoint end)
    {
        var dx = MathF.Abs(end.X - start.X);
        var curvature = MathF.Max(dx * 0.5f, 40.0f);

        // Control point 1 extends right from output pin
        var c1 = new CanvasPoint(start.X + curvature, start.Y);

        // Control point 2 extends left into input pin
        var c2 = new CanvasPoint(end.X - curvature, end.Y);

        return new BezierCurve(start, c1, c2, end);
    }

    public static List<CanvasPoint> SamplePoints(BezierCurve curve, int segments = 24)
    {
        var clampedSegments = Math.Max(2, segments);
        var points = new List<CanvasPoint>(clampedSegments + 1);

        for (var i = 0; i <= clampedSegments; i++)
        {
            var t = (float)i / clampedSegments;
            points.Add(curve.Evaluate(t));
        }

        return points;
    }

    public static float DistanceToWire(CanvasPoint point, BezierCurve curve, int sampleSegments = 24)
    {
        var samples = SamplePoints(curve, sampleSegments);
        var minDistanceSq = float.MaxValue;

        for (var i = 0; i < samples.Count - 1; i++)
        {
            var p1 = samples[i];
            var p2 = samples[i + 1];
            var distSq = DistanceSquaredToSegment(point, p1, p2);
            if (distSq < minDistanceSq)
            {
                minDistanceSq = distSq;
            }
        }

        return MathF.Sqrt(minDistanceSq);
    }

    private static float DistanceSquaredToSegment(CanvasPoint p, CanvasPoint a, CanvasPoint b)
    {
        var l2 = DistanceSquared(a, b);
        if (l2 < 0.0001f) return DistanceSquared(p, a);

        var t = Math.Clamp(((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2, 0f, 1f);
        var projection = new CanvasPoint(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        return DistanceSquared(p, projection);
    }

    private static float DistanceSquared(CanvasPoint a, CanvasPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }
}
