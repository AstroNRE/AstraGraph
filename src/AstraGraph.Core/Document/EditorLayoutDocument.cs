namespace AstraGraph.Core;

/// <summary>
/// Visual comment box annotation on the graph editor canvas.
/// Does NOT affect compilation or semantic hash.
/// </summary>
public sealed class CommentBoxDocument : IEquatable<CommentBoxDocument>
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; } = 200;

    public double Height { get; init; } = 100;

    public string ColorHex { get; init; } = "#333333";

    public bool Equals(CommentBoxDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id &&
               Title == other.Title &&
               Text == other.Text &&
               X.Equals(other.X) &&
               Y.Equals(other.Y) &&
               Width.Equals(other.Width) &&
               Height.Equals(other.Height) &&
               ColorHex == other.ColorHex;
    }

    public override bool Equals(object? obj) => Equals(obj as CommentBoxDocument);

    public override int GetHashCode() => HashCode.Combine(Id, Title, X, Y, Width, Height);
}

/// <summary>
/// Stores UI layout information (node positions, comments, zoom).
/// Completely excluded from SemanticHash calculation.
/// </summary>
public sealed class EditorLayoutDocument : IEquatable<EditorLayoutDocument>
{
    public Dictionary<string, NodePosition> NodePositions { get; init; } = [];

    public List<CommentBoxDocument> Comments { get; init; } = [];

    public double ViewportX { get; init; }

    public double ViewportY { get; init; }

    public double Zoom { get; init; } = 1.0;

    public bool Equals(EditorLayoutDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ViewportX.Equals(other.ViewportX) &&
               ViewportY.Equals(other.ViewportY) &&
               Zoom.Equals(other.Zoom) &&
               Comments.SequenceEqual(other.Comments) &&
               NodePositions.OrderBy(k => k.Key).SequenceEqual(other.NodePositions.OrderBy(k => k.Key));
    }

    public override bool Equals(object? obj) => Equals(obj as EditorLayoutDocument);

    public override int GetHashCode() => HashCode.Combine(ViewportX, ViewportY, Zoom);
}

public readonly record struct NodePosition(double X, double Y);
