namespace QuickDescribe.Services;

/// <summary>What describing a picture gave: the description or why there isn't one, and the text read from it or why
/// it couldn't be read.</summary>
public sealed record Result(string? Description, string? DescriptionCode, string? DescriptionProblem,
    IReadOnlyList<string>? Lines, string? TextProblem)
{
    public const string TextHeading = "Text in the picture:";

    /// <summary>The description code for a PC that can't describe at all (not a Copilot+ PC): the result is the text alone.</summary>
    public const string TextOnlyCode = "text_only";

    /// <summary>Said with the first result on such a PC, once ever (Settings.TextOnlySaid), in place of a "No description"
    /// line with every result.</summary>
    public const string TextOnlyNote =
        "This PC can't describe pictures: Windows' image description needs a Copilot+ PC. Quick Describe reads the text in them instead.";

    /// <summary>Only the text, because this PC can't describe.</summary>
    public bool TextOnly => DescriptionCode == TextOnlyCode;

    /// <summary>Whether this result should say <see cref="TextOnlyNote"/>: the first text-only result, until the note has
    /// been heard.</summary>
    public bool SaysTextOnly(bool alreadySaid) => TextOnly && !alreadySaid;

    /// <summary>What goes in the Result box: the description first, then the text, each with a plain line when it's missing.
    /// On a PC that can't describe, just the text, after <see cref="TextOnlyNote"/> when <paramref name="sayTextOnly"/>.</summary>
    public string BoxText(bool sayTextOnly = false)
    {
        var parts = new List<string>();
        if (DescriptionPart(sayTextOnly) is { } description) parts.Add(description);
        if (Lines is { Count: > 0 }) parts.Add(TextHeading + Environment.NewLine + string.Join(Environment.NewLine, Lines));
        else parts.Add(TextProblem is null ? "No text found." : $"Text couldn't be read: {TextProblem}");
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    /// <summary>What's spoken when it's done: the description (or why there isn't one) and how much text there is, not the
    /// text itself, which can be a whole screen's worth. The Result box has it, to read at the person's own pace.</summary>
    public string Announcement(bool sayTextOnly = false)
    {
        var count = Lines?.Count ?? 0;
        var text = count switch
        {
            0 when TextProblem is not null => "Text couldn't be read.",
            0 => "No text found.",
            1 => "1 line of text follows.",
            _ => $"{count} lines of text follow.",
        };
        return DescriptionPart(sayTextOnly) is { } description ? $"{description} {text}" : text;
    }

    private string? DescriptionPart(bool sayTextOnly)
    {
        if (!string.IsNullOrWhiteSpace(Description)) return Description.Trim();
        return DescriptionCode switch
        {
            TextOnlyCode => sayTextOnly ? TextOnlyNote : null,
            // Common with screens (Windows' model declines pictures that are mostly text): the text is the useful part.
            "too_much_text" => "No description: Windows' AI doesn't describe pictures with this much text.",
            "not_installed" or "not_supported" or "disabled_by_user" or "unavailable" =>
                "No description: Windows' AI isn't available on this PC. Help, Check Windows AI says why.",
            _ => $"No description: {DescriptionProblem ?? "Windows couldn't describe this picture."}",
        };
    }
}
