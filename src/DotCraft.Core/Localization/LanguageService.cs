namespace DotCraft.Localization;

/// <summary>
/// Supported languages
/// </summary>
public enum Language
{
    Chinese,
    English
}

/// <summary>
/// Service for managing language settings and localization
/// </summary>
public sealed class LanguageService(Language language = Language.Chinese)
{
    private Language _currentLanguage = language;

    public Language CurrentLanguage
    {
        get => _currentLanguage;
        set => _currentLanguage = value;
    }

    /// <summary>
    /// Toggle between Chinese and English
    /// </summary>
    public Language ToggleLanguage()
    {
        _currentLanguage = _currentLanguage == Language.Chinese ? Language.English : Language.Chinese;
        return _currentLanguage;
    }

    /// <summary>
    /// Get localized string based on current language
    /// </summary>
    public string GetString(string zh, string en)
    {
        return _currentLanguage == Language.Chinese ? zh : en;
    }
}
