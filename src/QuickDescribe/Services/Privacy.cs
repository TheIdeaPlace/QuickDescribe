namespace QuickDescribe.Services;

/// <summary>What goes in something public (a bug report) or something that might be attached to one (the log).</summary>
public static class Privacy
{
    /// <summary>The user's folder written as %USERPROFILE%, and their Windows user name anywhere else as %USERNAME%.</summary>
    public static string Redact(string text) =>
        Redact(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

    internal static string Redact(string text, string profile, string userName)
    {
        if (profile.Length > 0) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        // As a whole word only: a name like "al" mustn't change "also".
        if (userName.Length > 1)
            text = System.Text.RegularExpressions.Regex.Replace(text, $@"(?<![\w]){System.Text.RegularExpressions.Regex.Escape(userName)}(?![\w])",
                "%USERNAME%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text;
    }
}
