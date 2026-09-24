namespace AstraGraph.UI.Logic;

/// <summary>
/// Small binding expressions: a state name, a literal, or string concatenation with +.
/// </summary>
public static class UiExpression
{
    public static bool TryEvaluate(string expression, IReadOnlyDictionary<string, object?> state, out object? value, out string? error)
    {
        value = null;
        error = null;
        var text = expression.Trim();
        if (text.Length == 0)
        {
            error = "Expression is empty.";
            return false;
        }

        if (text.Contains('+', StringComparison.Ordinal))
        {
            var combined = "";
            foreach (var part in text.Split('+'))
            {
                if (!TryEvaluate(part, state, out var piece, out error))
                {
                    value = null;
                    return false;
                }

                combined += piece?.ToString();
            }

            value = combined;
            return true;
        }

        if ((text.StartsWith('"') && text.EndsWith('"')) || (text.StartsWith('\'') && text.EndsWith('\'')))
        {
            value = text[1..^1];
            return true;
        }

        if (state.TryGetValue(text, out value))
        {
            return true;
        }

        if (int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            value = number;
            return true;
        }

        if (bool.TryParse(text, out var flag))
        {
            value = flag;
            return true;
        }

        error = $"Expression references missing state '{text}'.";
        return false;
    }
}
