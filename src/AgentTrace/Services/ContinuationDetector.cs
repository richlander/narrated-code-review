namespace AgentTrace.Services;

/// <summary>
/// Detects "This session is being continued from a previous conversation..." preambles
/// in user messages and extracts the substantive user request after the preamble.
/// </summary>
public static class ContinuationDetector
{
    private const string ContinuationPrefix = "This session is being continued";

    /// <summary>
    /// Parses a user message to detect continuation preambles.
    /// </summary>
    public static ContinuationInfo Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith(ContinuationPrefix, StringComparison.Ordinal))
            return new ContinuationInfo(false, 0, null);

        // The preamble typically ends with a line that starts the user's actual request.
        // Look for the last paragraph break — the substantive content follows it.
        // Pattern: preamble text...\n\n<user's actual request>
        var lastDoubleNewline = text.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (lastDoubleNewline >= 0 && lastDoubleNewline < text.Length - 2)
        {
            var substantive = text[(lastDoubleNewline + 2)..].TrimEnd();
            if (substantive.Length > 0)
                return new ContinuationInfo(true, lastDoubleNewline + 2, substantive);
        }

        return new ContinuationInfo(true, text.Length, null);
    }

    /// <summary>
    /// Returns true if the text is a continuation preamble.
    /// </summary>
    public static bool IsContinuation(string? text)
        => !string.IsNullOrEmpty(text) && text.StartsWith(ContinuationPrefix, StringComparison.Ordinal);
}

/// <summary>
/// Result of parsing a user message for continuation preamble.
/// </summary>
public record ContinuationInfo(bool IsContinuation, int PreambleCharCount, string? SubstantiveContent);
