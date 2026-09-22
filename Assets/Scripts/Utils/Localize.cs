/// <summary>
/// 依照玩家選的語言挑字串。
/// </summary>
/// <remarks>
/// 這個模式本來散在各處：<c>RuntimeSongLibraryPanel</c> 有一份自己的私有 <c>T</c>，
/// <c>SimpleCarouselSettings</c> 則是一路 <c>simplified ? … : zh ? … : …</c> 寫在
/// 每個字串上。同一個判斷寫第三遍就該收攏，否則新加的 UI 很容易忘記翻譯——
/// 載入頁和結算的「返回選曲」原本就是漏掉的。
///
/// 刻意不做字典或資源檔：這個專案的字串量小，把三種語言寫在同一行反而最好讀，
/// 也最不容易漏掉其中一種。
/// </remarks>
public static class Localize
{
    /// <summary>Picks the string for the current language.</summary>
    /// <param name="traditional">繁體中文，也是設定讀不到時的預設。</param>
    /// <param name="simplified">简体中文。</param>
    /// <param name="english">English.</param>
    public static string T(string traditional, string simplified, string english)
    {
        AppLanguage language = Current;
        if (language == AppLanguage.English) return english;
        if (language == AppLanguage.SimplifiedChinese) return simplified;
        return traditional;
    }

    /// <summary>設定還沒建立時退回繁體中文，跟 SettingsManager 的預設一致。</summary>
    public static AppLanguage Current => SettingsManager.Instance != null
        ? SettingsManager.Instance.CurrentLanguage
        : AppLanguage.TraditionalChinese;
}
