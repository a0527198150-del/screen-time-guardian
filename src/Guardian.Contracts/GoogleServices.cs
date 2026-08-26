namespace ScreenTimeGuardian.Contracts;

/// <summary>
/// Catalog of the Google service keys the browser extension understands.
/// Keys are the identifiers stored on <see cref="GoogleAccountRule.Services"/>
/// and reported by the extension; labels are what the control panel shows.
/// Keep the keys in sync with SERVICE_HOSTS in extension/background.js.
/// </summary>
public static class GoogleServices
{
    /// <summary>Every service key, in display order.</summary>
    public static readonly string[] All =
    {
        "gmail", "drive", "docs", "calendar", "chat", "meet", "photos",
        "search", "youtube", "gemini", "maps", "translate", "keep",
        "news", "finance", "groups", "one"
    };

    /// <summary>Human readable Hebrew label per service key.</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["gmail"] = "Gmail ✉️",
        ["drive"] = "Drive 🗂️",
        ["docs"] = "Docs / Sheets / Slides 📄",
        ["calendar"] = "Calendar 📅",
        ["chat"] = "Chat 💬",
        ["meet"] = "Meet 🎥",
        ["photos"] = "Photos 🖼️",
        ["search"] = "חיפוש 🔍",
        ["youtube"] = "YouTube ▶️",
        ["gemini"] = "Gemini ✨",
        ["maps"] = "Maps 🗺️",
        ["translate"] = "Translate 🌐",
        ["keep"] = "Keep 📝",
        ["news"] = "News 📰",
        ["finance"] = "Finance 📈",
        ["groups"] = "Groups 👥",
        ["one"] = "Google One 💾"
    };

    public static string Label(string key) =>
        Names.TryGetValue(key, out var label) ? label : key;
}
