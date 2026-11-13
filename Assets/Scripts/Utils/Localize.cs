using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 依照玩家選的語言挑字串。
/// </summary>
/// <remarks>
/// 這個模式本來散在各處：<c>RuntimeSongLibraryPanel</c> 有一份自己的私有 <c>T</c>，
/// <c>SimpleCarouselSettings</c> 則是一路 <c>simplified ? … : zh ? … : …</c> 寫在
/// 每個字串上。同一個判斷寫第三遍就該收攏，否則新加的 UI 很容易忘記翻譯——
/// 載入頁和結算的「返回選曲」原本就是漏掉的。
///
/// 繁、簡、英三種刻意寫在同一行：這個專案的字串量小，寫在一起最好讀，也最不容易漏
/// 掉其中一種。
///
/// **日文和韓文走查表，不走第四、第五個參數。** 把 <c>T</c> 擴成五個參數要改 138 處
/// 呼叫、散在 16 個檔，而且譯文會被寫死在程式裡 —— 改一個錯字就得重編。改成拿<b>繁體
/// 那一串當鍵</b>去查 <c>Resources/Locale/&lt;code&gt;.json</c>：呼叫端一行都不用動，
/// 要加第六種語言也只是多一個檔。
///
/// 實測 124 處三字面值的呼叫裡只有 105 組不同的繁體字串，撞鍵的兩組（和弦、設定）
/// 兩邊意思一樣，所以繁體字串當鍵是夠用的。
///
/// **查不到就退英文，不是退繁體。** 翻譯漏掉時，日文介面裡冒出一句繁體中文會像是壞
/// 掉；英文則是這個 UI 本來就支援的語言，看起來只是沒翻到。
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
        switch (language)
        {
            case AppLanguage.English: return english;
            case AppLanguage.SimplifiedChinese: return simplified;
            case AppLanguage.Japanese:
            case AppLanguage.Korean:
                string found = Lookup(language, traditional);
                return found ?? english;
            default: return traditional;
        }
    }

    /// <summary>
    /// 帶參數的字串：格式字串本身照 <see cref="T"/> 挑，再 <c>string.Format</c>。
    /// </summary>
    /// <remarks>
    /// 以前這種字串只能寫成內插（<c>$"距普通 {n:N0}"</c>），而內插的結果是**算出來的**，
    /// 當不了查表的鍵 —— 日韓永遠查不到。更糟的是那種寫法連英文都沒得挑，中文會直接
    /// 印在英文介面上。專案裡本來就有 14 處是這樣（結算、設定面板的數值列）。
    ///
    /// 改成把洞留在格式字串裡（<c>"距普通 {0:N0}"</c>）：繁體那一串照樣是鍵，而且譯文
    /// 自己決定數字擺哪裡 —— 日文「普通まで {0:N0}」這種語序也寫得出來。
    ///
    /// 譯文的洞寫錯（少一個、或寫成 <c>{1}</c>）會丟 <c>FormatException</c>。那是譯文
    /// 的問題，不該讓畫面整個掛掉，所以接起來退回沒填的格式字串 —— 看得出哪裡壞了。
    /// </remarks>
    public static string F(string traditional, string simplified, string english,
        params object[] args)
    {
        string format = T(traditional, simplified, english);
        if (args == null || args.Length == 0) return format;
        try { return string.Format(format, args); }
        catch (System.FormatException) { return format; }
    }

    /// <summary>
    /// 繁體原文當鍵，**所有**其他語言都查表（包含簡體和英文）。
    /// </summary>
    /// <remarks>
    /// <see cref="T"/> 是把三種語言寫在呼叫端，字串量小的時候最好讀。但專案裡還有
    /// 約 450 條寫死的繁體中文（考程、曲庫面板、練習室、匯入訊息…），全部改成 T 等於
    /// 要在 C# 裡手打 1350 個字串 —— 打錯一個字只有那一句壞掉，而且從中文介面看不出來。
    ///
    /// 這裡只包一層：<c>"評分明細"</c> → <c>S("評分明細")</c>。譯文全部進
    /// <c>Resources/Locale/&lt;code&gt;.json</c>，由腳本照索引產生、對帳，和日韓本來就在用
    /// 的是同一組檔案、同一組鍵。多一種語言只是多一個檔。
    ///
    /// **退路分兩種。** 簡體查不到退繁體（看得懂，只是字體不同）；英日韓查不到先退英文、
    /// 再退繁體 —— 日文介面裡冒出繁體中文像壞掉，英文只像沒翻到。
    /// </remarks>
    public static string S(string traditional)
    {
        if (string.IsNullOrEmpty(traditional)) return traditional;
        AppLanguage language = Current;
        if (language == AppLanguage.TraditionalChinese) return traditional;
        string found = Lookup(language, traditional);
        if (found != null) return found;
        if (language == AppLanguage.SimplifiedChinese) return traditional;
        return Lookup(AppLanguage.English, traditional) ?? traditional;
    }

    /// <summary>帶參數的 <see cref="S"/>。洞寫在格式字串裡：<c>S("{0} 級")</c>。</summary>
    /// <remarks>
    /// 內插字串（<c>$"{n} 級"</c>）算出來的結果當不了鍵，所以這種句子一定要先把洞
    /// 留在格式字串裡才翻得了。譯文的洞寫錯會丟 <c>FormatException</c>，接起來退回
    /// 沒填的格式字串 —— 畫面不會掛，而且看得出哪一條壞了。
    /// </remarks>
    public static string SF(string traditional, params object[] args)
    {
        string format = S(traditional);
        if (args == null || args.Length == 0) return format;
        try { return string.Format(format, args); }
        catch (System.FormatException) { return format; }
    }

    /// <summary>語言檔的檔名（<c>Resources/Locale/&lt;code&gt;.json</c>）。</summary>
    public static string CodeOf(AppLanguage language)
    {
        switch (language)
        {
            case AppLanguage.Japanese: return "ja";
            case AppLanguage.Korean: return "ko";
            case AppLanguage.SimplifiedChinese: return "zh-Hans";
            case AppLanguage.English: return "en";
            default: return "zh-Hant";
        }
    }

    /// <summary>這個語言有沒有譯文檔。沒有的話選單上不該出現它。</summary>
    public static bool HasTable(AppLanguage language) => Table(language) != null;

    /// <summary>這個語言的字串是查表來的（日、韓），不是寫在呼叫端的。</summary>
    public static bool UsesTable(AppLanguage language) =>
        language == AppLanguage.Japanese || language == AppLanguage.Korean;

    /// <summary>
    /// 拿繁體字串當鍵查**目前語言**的譯文；查不到、或目前語言不查表，都回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 給那些「字串不是用 <see cref="T"/> 組出來的」地方用。設定畫面就是這樣：
    /// 110 處直接寫 <c>simplified ? 簡 : zh ? 繁 : 英</c>，而 <c>zh</c> 的定義是
    /// 「不是英文」—— 日文和韓文也落在繁體那一支。結果就是日文介面的設定頁整頁
    /// 繁體中文。把那 110 處逐一改寫成 <c>T(...)</c> 風險太高，改成**算完之後拿
    /// 算出來的繁體再查一次表**：同一張 json、同一組鍵，呼叫端只多一層。
    ///
    /// 回 <c>null</c> 而不是回原字串，呼叫端才分得出「沒翻到」和「翻出來就是這樣」。
    /// </remarks>
    public static string Translate(string traditional)
    {
        AppLanguage language = Current;
        return UsesTable(language) ? Lookup(language, traditional) : null;
    }

    /// <summary>
    /// 同上，但先試 <c>context + "|" + traditional</c> 這個鍵。
    /// </summary>
    /// <remarks>
    /// 同一串繁體在不同畫面意思不一樣時才需要：「關閉」在標題列是 Close（閉じる）、
    /// 在設定的下拉裡是 Off（オフ）；「一般」在難度是 Normal（ノーマル）、在設定的
    /// 分組是 General（一般）。整個專案目前只有這兩組。
    /// </remarks>
    public static string Translate(string context, string traditional)
    {
        AppLanguage language = Current;
        if (!UsesTable(language)) return null;
        return Lookup(language, context + "|" + traditional)
            ?? Lookup(language, traditional);
    }

    private static readonly Dictionary<AppLanguage, Dictionary<string, string>> tables =
        new Dictionary<AppLanguage, Dictionary<string, string>>();

    private static string Lookup(AppLanguage language, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        Dictionary<string, string> table = Table(language);
        if (table == null) return null;
        return table.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)
            ? value : null;
    }

    /// <summary>
    /// 讀一次就留著。讀失敗也留著（留一張空表），否則每一個字串都會再試一次檔案。
    /// </summary>
    private static Dictionary<string, string> Table(AppLanguage language)
    {
        if (tables.TryGetValue(language, out Dictionary<string, string> held)) return held;

        Dictionary<string, string> table = null;
        try
        {
            TextAsset asset = Resources.Load<TextAsset>("Locale/" + CodeOf(language));
            if (asset != null && !string.IsNullOrEmpty(asset.text))
                table = Parse(asset.text);
        }
        catch
        {
            table = null;
        }
        tables[language] = table;
        return table;
    }

    /// <summary>
    /// 把 <c>{"鍵":"值"}</c> 讀成字典。
    /// </summary>
    /// <remarks>
    /// 自己剖而不是用 <c>JsonUtility</c>：它不吃任意鍵的字典，要嘛為 105 個鍵各宣告一
    /// 個欄位、要嘛把檔案改成陣列包物件 —— 兩個都讓譯者難改。這裡只需要認得字串對字串
    /// 的平面物件，連巢狀都不用。
    /// </remarks>
    private static Dictionary<string, string> Parse(string json)
    {
        var table = new Dictionary<string, string>();
        int at = 0;
        while (true)
        {
            string key = NextString(json, ref at);
            if (key == null) break;
            int colon = json.IndexOf(':', at);
            if (colon < 0) break;
            at = colon + 1;
            string value = NextString(json, ref at);
            if (value == null) break;
            table[key] = value;
        }
        return table;
    }

    private static string NextString(string json, ref int at)
    {
        while (at < json.Length && json[at] != '"') at++;
        if (at >= json.Length) return null;
        at++;
        var text = new StringBuilder();
        while (at < json.Length)
        {
            char c = json[at++];
            if (c == '"') return text.ToString();
            if (c != '\\') { text.Append(c); continue; }
            if (at >= json.Length) break;
            char escaped = json[at++];
            switch (escaped)
            {
                case 'n': text.Append('\n'); break;
                case 't': text.Append('\t'); break;
                case 'r': text.Append('\r'); break;
                case 'u':
                    if (at + 4 <= json.Length &&
                        int.TryParse(json.Substring(at, 4),
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out int code))
                    {
                        text.Append((char)code);
                        at += 4;
                    }
                    break;
                default: text.Append(escaped); break;
            }
        }
        return null;
    }

    /// <summary>設定還沒建立時退回繁體中文，跟 SettingsManager 的預設一致。</summary>
    public static AppLanguage Current => SettingsManager.Instance != null
        ? SettingsManager.Instance.CurrentLanguage
        : AppLanguage.TraditionalChinese;
}
