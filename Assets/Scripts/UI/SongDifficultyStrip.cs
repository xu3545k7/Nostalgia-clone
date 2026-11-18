using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The plaque under the carousel: the five charts a song can carry, at what
/// level, the best grade earned on each, and the record on its hardest chart.
/// </summary>
/// <remarks>
/// **Why all five are always shown.** A row that changes width and membership
/// with every song cannot be read at a glance -- the eye has to re-find each
/// tier before it can compare. Fixed slots mean the third stone is always
/// Expert, and a song that has no Expert says so with a question mark instead of
/// by silently closing the gap.
///
/// **Why tiers and not levels.** The carousel already colours things by level.
/// This answers a different question -- which charts exist -- and a song's charts
/// are named, not numbered. Anything outside the known names is its own tier,
/// because in this library those are always the odd, usually harder, one-off
/// charts.
/// </remarks>
public sealed class SongDifficultyStrip : MonoBehaviour
{
    private const string ObjectName = "~SongDifficultyStrip";
    private const int TierCount = 5;
    private const float GemWidth = 96f;
    private const float GemHeight = 106f;
    private const float Spacing = 26f;
    private const float RankSize = 38f;
    private const float RecordHeight = 44f;
    /// <summary>分數靠右對齊到這裡，等第圖標接在後面。</summary>
    private const float ScoreEdge = 196f;
    /// <summary>How far the plaque's slanted ends cut in, as a fraction of its height.</summary>
    /// <summary>端頭留白。原本是 `height * Bevel * 2`，斜切沒了但寬度必須一模一樣。</summary>
    /// <remarks>
    /// 761.2 這個數字是算式的錨：`SideStripEdge`（476.5）和 `SideStripBottom`
    /// （461.2）是它的函數，而演奏會那兩塊牌的寬度又是從 `SideStripEdge` 推出來的。
    /// 把盤子縮到 628 每少一單位半寬，兩塊牌就少 0.85 單位 —— 所以這裡保留 133.2，
    /// 只是把它從「斜切吃掉的」改成「端頭留的呼吸」。
    /// </remarks>
    private const float EndRoom = 133.2f;

    /// <summary>
    /// The named charts a song can carry, in the order they are shown -- which
    /// is also how ornate the binding of a card showing that tier gets.
    /// </summary>
    public enum Tier { Normal, Hard, Expert, Real, Special }

    private sealed class Slot
    {
        public GemGraphic gem;
        public TextMeshProUGUI level;
        public RectTransform root;
        public Button button;
        public GemGlow glow;
    }

    private readonly List<Slot> slots = new List<Slot>(TierCount);
    private readonly Dictionary<Tier, SongSelectionManager.SongOption> charts =
        new Dictionary<Tier, SongSelectionManager.SongOption>();
    private RectTransform row;
    private TextMeshProUGUI record;
    private RawImage recordRank;
    private Tier selected = Tier.Normal;
    private SongBookDressing dressing;

    /// <summary>
    /// 玩家挑的難度階。整個選歌畫面共用一份 —— 三張卡、書本的顏色和房間的燈
    /// 都是同一個選擇的結果，各記各的就會互相矛盾。
    /// </summary>
    private static Tier preferred = Tier.Normal;
    private static bool hasPreference;
    private static bool preferenceLoaded;

    /// <summary>每一張在場的卡片牌，換偏好時要一起換。</summary>
    private static readonly List<SongDifficultyStrip> live = new List<SongDifficultyStrip>(4);

    /// <summary>難度階被改掉了（點寶石、或真的選了一個難度去玩）。</summary>
    public static event System.Action PreferenceChanged;

    private static readonly Dictionary<Tier, SongSelectionManager.SongOption> scratch =
        new Dictionary<Tier, SongSelectionManager.SongOption>();

    /// <summary>
    /// Hangs the plaque under a song card, as a child of it.
    /// </summary>
    /// <remarks>
    /// A child rather than something that chases the card each frame: chasing is
    /// always a frame late, and while the carousel is scrolling that frame is
    /// visible as the plaque dragging behind the book. As a child it comes out of
    /// the same transform the card does, so the slide, the scale and the fade all
    /// carry over for free and it can never come adrift.
    /// </remarks>
    public static SongDifficultyStrip Attach(RectTransform card)
    {
        if (card == null) return null;
        Transform existing = card.Find(ObjectName);
        if (existing != null) return existing.GetComponent<SongDifficultyStrip>();

        float content = TierCount * GemWidth + (TierCount - 1) * Spacing;
        float height = GemHeight + RankSize + RecordHeight + 34f;
        float width = content + EndRoom + 44f;

        var stripObject = new GameObject(ObjectName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(PlaqueGraphic), typeof(SongDifficultyStrip));
        stripObject.layer = card.gameObject.layer;

        RectTransform rect = stripObject.GetComponent<RectTransform>();
        rect.SetParent(card, false);
        // 掛在卡片的下緣往下：錨點在卡底、支點在牌面頂端。
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -20f);
        rect.sizeDelta = new Vector2(width, height);

        // 兩層：場（有紙紋、有光）＋ 金工（沒有貼圖）。見 PlaqueGraphic.Build。
        PlaqueGraphic plaque = PlaqueGraphic.Build(rect, PlaqueShape.Cradle);

        SongDifficultyStrip strip = stripObject.GetComponent<SongDifficultyStrip>();
        strip.row = rect;
        strip.Build(content);
        return strip;
    }

    private void Build(float content)
    {
        for (int i = 0; i < TierCount; i++)
        {
            Slot slot = CreateSlot();
            slot.root.anchoredPosition = new Vector2(
                -content * 0.5f + GemWidth * 0.5f + i * (GemWidth + Spacing),
                RecordHeight + 38f);
            slot.gem.Caption = TierName((Tier)i);
            Tier tier = (Tier)i;
            slot.button.onClick.AddListener(() =>
            {
                UiSfx.Select();
                Select(tier);
            });
            slots.Add(slot);
        }

        // 圖示和分數當成一組置中，而不是各自置中 —— 各自置中的話，分數位數一變，
        // 圖示就會跟著跳。
        var bandObject = new GameObject("RecordBand", typeof(RectTransform));
        bandObject.layer = gameObject.layer;
        RectTransform band = bandObject.GetComponent<RectTransform>();
        band.SetParent(row, false);
        band.anchorMin = band.anchorMax = new Vector2(0.5f, 0f);
        band.pivot = new Vector2(0.5f, 0f);
        band.sizeDelta = new Vector2(ScoreEdge + 14f + RecordHeight * 1.6f, RecordHeight);
        band.anchoredPosition = new Vector2(0f, 20f);

        var rankObject = new GameObject("RecordRank", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(RawImage));
        rankObject.layer = gameObject.layer;
        RectTransform rankRect = rankObject.GetComponent<RectTransform>();
        rankRect.SetParent(band, false);
        // 分數在前、圖標在後。分數靠右對齊到一個固定的邊界，圖標接在那個邊界後面
        // —— 位數從六位變七位時，動的是分數的左端，圖標不會跟著跑。
        rankRect.anchorMin = rankRect.anchorMax = new Vector2(0f, 0.5f);
        rankRect.pivot = new Vector2(0f, 0.5f);
        rankRect.sizeDelta = new Vector2(RecordHeight, RecordHeight);
        rankRect.anchoredPosition = new Vector2(ScoreEdge + 14f, 0f);
        recordRank = rankObject.GetComponent<RawImage>();
        recordRank.raycastTarget = false;
        recordRank.enabled = false;

        var recordObject = new GameObject("Record", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        recordObject.layer = gameObject.layer;
        RectTransform recordRect = recordObject.GetComponent<RectTransform>();
        recordRect.SetParent(band, false);
        recordRect.anchorMin = new Vector2(0f, 0f);
        recordRect.anchorMax = new Vector2(1f, 1f);
        recordRect.offsetMin = Vector2.zero;
        recordRect.offsetMax = new Vector2(ScoreEdge - band.sizeDelta.x, 0f);

        record = recordObject.GetComponent<TextMeshProUGUI>();
        record.fontSize = 34f;
        record.fontStyle = FontStyles.Bold;
        record.alignment = TextAlignmentOptions.MidlineRight;
        record.color = new Color(0.94f, 0.88f, 0.70f, 1f);
        record.raycastTarget = false;
        // 數字要工整：關掉字距調整，改用固定的字元間距。比例字體會把 1 排得比
        // 8 窄，分數每跳一次數字就左右扭一下 —— 均勻的間距讀起來才像一組數據。
        List<UnityEngine.TextCore.OTL_FeatureTag> features =
            new List<UnityEngine.TextCore.OTL_FeatureTag>(record.fontFeatures);
        if (features.Remove(UnityEngine.TextCore.OTL_FeatureTag.kern))
            record.fontFeatures = features;
        record.characterSpacing = 7f;
    }

    /// <summary>Refreshes the plaque for the song now under the cursor.</summary>
    public void Show(SongSelectionManager.SongOption song)
    {
        charts.Clear();
        Collect(song, charts);
        if (song?.difficultyVariants != null)
            foreach (SongSelectionManager.SongOption variant in song.difficultyVariants)
                Collect(variant, charts);

        // 換歌時停在玩家自己挑的那一階，不是這首歌最高的那一階 —— 一個練
        // Expert 的人捲過整個曲庫，看到的應該一直是 Expert。這首沒有那一階
        // 才退而求其次，挑最靠近的。
        selected = Resolve(charts);

        Refresh();
    }

    private void Select(Tier tier)
    {
        if (!charts.ContainsKey(tier)) return;
        Remember(tier);
        // 點下去改的是整個畫面的難度，不只是這一張卡：三張卡、書本的裝飾和
        // 房間的燈都要一起換。
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (live[i] == null) { live.RemoveAt(i); continue; }
            live[i].selected = Resolve(live[i].charts, out _);
            live[i].Refresh();
        }
        PreferenceChanged?.Invoke();
    }

    private void OnEnable()
    {
        if (!live.Contains(this)) live.Add(this);
    }

    private void OnDisable()
    {
        live.Remove(this);
    }

    /// <summary>
    /// The tier to show for a song: the player's own, or the nearest this song
    /// actually has.
    /// </summary>
    /// <remarks>
    /// 距離相同時取比較簡單的那一階。挑錯方向的代價不對稱：多給一階難度只是
    /// 讓人以為自己打得動，少給一階只是保守。
    /// </remarks>
    private static Tier Resolve(Dictionary<Tier, SongSelectionManager.SongOption> available,
        out SongSelectionManager.SongOption chart)
    {
        chart = null;
        if (available.Count == 0) return Tier.Normal;

        LoadPreference();
        if (!hasPreference)
        {
            // 還沒挑過任何難度：維持舊行為，指著這首最高的那一階。
            Tier top = Tier.Normal;
            int best = -1;
            foreach (KeyValuePair<Tier, SongSelectionManager.SongOption> pair in available)
            {
                if (pair.Value.difficultyLevel <= best) continue;
                best = pair.Value.difficultyLevel;
                top = pair.Key;
            }
            available.TryGetValue(top, out chart);
            return top;
        }

        if (available.TryGetValue(preferred, out chart)) return preferred;

        Tier nearest = Tier.Normal;
        int distance = int.MaxValue;
        foreach (KeyValuePair<Tier, SongSelectionManager.SongOption> pair in available)
        {
            int gap = Mathf.Abs((int)pair.Key - (int)preferred);
            if (gap > distance) continue;
            if (gap == distance && (int)pair.Key > (int)nearest) continue;
            distance = gap;
            nearest = pair.Key;
        }
        available.TryGetValue(nearest, out chart);
        return nearest;
    }

    private Tier Resolve(Dictionary<Tier, SongSelectionManager.SongOption> available)
    {
        return Resolve(available, out _);
    }

    /// <summary>
    /// The chart a song shows under the player's difficulty choice -- what the
    /// book's colour, the room light and the record line are all about.
    /// </summary>
    public static SongSelectionManager.SongOption PreferredChart(
        SongSelectionManager.SongOption song)
    {
        if (song == null) return null;
        scratch.Clear();
        Collect(song, scratch);
        if (song.difficultyVariants != null)
            foreach (SongSelectionManager.SongOption variant in song.difficultyVariants)
                Collect(variant, scratch);
        Resolve(scratch, out SongSelectionManager.SongOption chart);
        return chart;
    }

    /// <summary>
    /// Records that the player chose this chart, so every later song opens on
    /// the same tier.
    /// </summary>
    public static void RememberChoice(SongSelectionManager.SongOption chart)
    {
        if (chart == null || chart.difficultyLevel <= 0) return;
        Remember(Classify(chart.difficultyName));
    }

    private static void Remember(Tier tier)
    {
        LoadPreference();
        if (hasPreference && preferred == tier) return;
        preferred = tier;
        hasPreference = true;
        try { SettingsManager.Instance?.SetPreferredDifficultyTier((int)tier); } catch { }
    }

    private static void LoadPreference()
    {
        if (preferenceLoaded) return;
        preferenceLoaded = true;
        int stored = -1;
        try
        {
            stored = SettingsManager.Instance != null
                ? SettingsManager.Instance.PreferredDifficultyTier : -1;
        }
        catch { stored = -1; }
        if (stored < 0 || stored >= TierCount) return;
        preferred = (Tier)stored;
        hasPreference = true;
    }

    private void Refresh()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            charts.TryGetValue((Tier)i, out SongSelectionManager.SongOption chart);
            Fill(slots[i], (Tier)i, chart, (Tier)i == selected);
        }

        Dress();
        ShowRecord();
    }

    /// <summary>
    /// Puts the card this plaque hangs from into the chosen tier's colours.
    /// </summary>
    /// <remarks>
    /// 從這裡驅動而不是從輪播：這塊牌子本來就是「現在看的是哪一階」的唯一
    /// 事實來源，卡片的裝幀跟著它走，兩者就不可能講不同的話。
    /// </remarks>
    private void Dress()
    {
        if (dressing == null)
            dressing = SongBookDressing.Attach(transform.parent as RectTransform);
        if (dressing == null) return;
        bool present = charts.ContainsKey(selected);
        dressing.Show(TierColour(selected), (int)selected, present);
    }

    /// <summary>
    /// The line under the stones: the score on this song's hardest chart.
    /// </summary>
    /// <remarks>
    /// The hardest rather than the selected one, because this whole plaque is
    /// about the song and not about a chart -- and the hardest is the number a
    /// player quotes when asked how far they have got with a song.
    /// </remarks>
    private void ShowRecord()
    {
        if (record == null) return;

        charts.TryGetValue(selected, out SongSelectionManager.SongOption chart);
        int score = chart != null ? LocalScoreRecords.GetBestScore(chart) : 0;

        // 等第只用圖示，不再寫出字母 —— 同一件事說兩次。
        Texture2D texture = score > 0
            ? LocalScoreRecords.LoadRankTexture(LocalScoreRecords.GetRank(score))
            : null;
        if (recordRank != null)
        {
            recordRank.texture = texture;
            recordRank.enabled = texture != null;
            if (texture != null)
            {
                // 等第圖不是正方形（A+ 和 B+ 都比其他寬），塞進固定的方框會被壓扁。
                // 高度固定、寬度跟著原圖的長寬比走。
                float aspect = texture.width / (float)Mathf.Max(1, texture.height);
                recordRank.rectTransform.sizeDelta = new Vector2(RecordHeight * aspect, RecordHeight);
            }
        }

        record.text = score > 0 ? score.ToString("N0") : Localize.T("無紀錄", "无纪录", "No record");
        record.color = score > 0
            ? new Color(0.96f, 0.90f, 0.72f, 1f)
            : new Color(0.72f, 0.66f, 0.56f, 0.72f);
        ClassicalBookUITheme.ApplyLocalizedFont(record);
    }

    /// <summary>
    /// Files a chart under its tier, keeping the harder one when two collide.
    /// </summary>
    private static void Collect(SongSelectionManager.SongOption chart,
        Dictionary<Tier, SongSelectionManager.SongOption> into)
    {
        if (chart == null || chart.difficultyLevel <= 0) return;
        Tier tier = Classify(chart.difficultyName);
        if (into.TryGetValue(tier, out SongSelectionManager.SongOption held)
            && held.difficultyLevel >= chart.difficultyLevel) return;
        into[tier] = chart;
    }

    /// <summary>
    /// Master and Real are the same slot: a song has one top chart, whichever
    /// word it chose for it.
    /// </summary>
    /// <summary>難度名屬於哪一階。練習室要照同一套規則分，不能自己再猜一次。</summary>
    public static Tier TierOf(string difficultyName) => Classify(difficultyName);

    /// <summary>那一階的顏色。唯一一份配色仍然在 DifficultyVisualPalette。</summary>
    public static Color ColourOf(Tier tier) => TierColour(tier);

    private static Tier Classify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Tier.Normal;
        string lowered = name.ToLowerInvariant();
        if (lowered.Contains("normal") || name.Contains("一般") || name.Contains("普通")) return Tier.Normal;
        if (lowered.Contains("hard") || name.Contains("困難")) return Tier.Hard;
        if (lowered.Contains("expert") || name.Contains("專家")) return Tier.Expert;
        if (lowered.Contains("real") || lowered.Contains("master")) return Tier.Real;
        return Tier.Special;
    }

    private static Color TierColour(Tier tier)
    {
        switch (tier)
        {
            // 唯一一份配色在 DifficultyVisualPalette
            case Tier.Normal: return DifficultyVisualPalette.Green;
            case Tier.Hard: return DifficultyVisualPalette.Yellow;
            case Tier.Expert: return DifficultyVisualPalette.Red;
            default: return DifficultyVisualPalette.Violet;
        }
    }

    private static string TierName(Tier tier)
    {
        switch (tier)
        {
            case Tier.Normal: return "NORMAL";
            case Tier.Hard: return "HARD";
            case Tier.Expert: return "EXPERT";
            case Tier.Real: return "REAL";
            default: return "SPECIAL";
        }
    }

    private static void Fill(Slot slot, Tier tier, SongSelectionManager.SongOption chart, bool selected)
    {
        bool present = chart != null;
        // 沒有的難度不是留白：留白讀起來像「還沒載入」，問號讀起來是「這首沒有」。
        slot.gem.Tint = present ? TierColour(tier) : new Color(0.42f, 0.40f, 0.42f, 1f);
        slot.gem.Muted = !present;
        slot.level.text = present ? chart.difficultyLevel.ToString() : "?";
        // 等級數字不跟著石身壓暗。TMP 把 faceColor 乘上頂點色，所以調低這裡的 alpha
        // 會連那圈灰描邊一起淡掉 —— 而描邊正是數字壓在寶石亮面上還讀得到的唯一原因。
        // 「現在看的是哪一格」石身自己的亮度已經講完了，數字不必再說一次。
        // 問號是例外：它不是一個等級，用同一個「沒選到」的量收起來，才不會搶戲。
        slot.level.color = new Color(1f, 1f, 1f, present ? 1f : GemGraphic.UnselectedAlpha);
        if (slot.button != null) slot.button.interactable = present;
        // 選中／滑過的亮度交給 GemGlow 補間。資料在這一行就已經是對的了，動的只有畫面。
        if (slot.glow != null) slot.glow.Set(present, selected);
    }

    private Slot CreateSlot()
    {
        var slotObject = new GameObject("Gem", typeof(RectTransform));
        slotObject.layer = gameObject.layer;
        RectTransform root = slotObject.GetComponent<RectTransform>();
        root.SetParent(row, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.sizeDelta = new Vector2(GemWidth, GemHeight);

        var gemObject = new GameObject("Stone", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(GemGraphic));
        gemObject.layer = gameObject.layer;
        RectTransform gemRect = gemObject.GetComponent<RectTransform>();
        gemRect.SetParent(root, false);
        gemRect.anchorMin = gemRect.anchorMax = new Vector2(0.5f, 1f);
        gemRect.pivot = new Vector2(0.5f, 1f);
        gemRect.sizeDelta = new Vector2(GemWidth, GemHeight);
        GemGraphic gem = gemObject.GetComponent<GemGraphic>();
        // 寶石自己就是按鈕。牌面底板不吃射線，所以點擊只會落在石頭上。
        gem.raycastTarget = true;
        Button button = gemObject.AddComponent<Button>();
        // transition 維持 None：Selectable 的過場是去寫 targetGraphic.color，而寶石
        // 的每一面都是自己在 OnPopulateMesh 裡算出來的，color 對它沒有意義。手感由
        // GemGlow 出。
        button.transition = Selectable.Transition.None;
        GemGlow glow = gemObject.AddComponent<GemGlow>();

        // 白字、灰色細框，壓在寶石中央。細框是必要的：寶石有亮有暗，純白數字
        // 在亮面上會消失。
        var levelObject = new GameObject("Level", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        levelObject.layer = gameObject.layer;
        RectTransform levelRect = levelObject.GetComponent<RectTransform>();
        levelRect.SetParent(gemRect, false);
        levelRect.anchorMin = new Vector2(0f, 0.26f);
        levelRect.anchorMax = new Vector2(1f, 0.90f);
        levelRect.offsetMin = Vector2.zero;
        levelRect.offsetMax = Vector2.zero;
        var level = levelObject.GetComponent<TextMeshProUGUI>();
        level.fontSize = 40f;
        level.fontStyle = FontStyles.Bold;
        level.alignment = TextAlignmentOptions.Center;
        level.color = Color.white;
        level.outlineColor = new Color(0.36f, 0.36f, 0.40f, 1f);
        level.outlineWidth = 0.16f;
        level.raycastTarget = false;

        return new Slot { root = root, gem = gem, level = level, button = button, glow = glow };
    }
}

/// <summary>
/// 這四塊面板的底：一套共用的金屬，四種不同的家具。
/// </summary>
/// <remarks>
/// **為什麼不再是同一個六角形。** 同一個平頂六角形本來同時當玩家徽章、演奏會左右
/// 兩塊牌、以及五顆寶石底下這塊盤子；而遊戲裡音符、力度光暈、踏板和弦光、中央水晶
/// 也全是六角形。使用者的話是「不要都六角形」—— 這不是配色問題，是整套形狀語彙塌
/// 到同一個字上了。
///
/// **共用的是物理，不是家具。** 四種輪廓共用：同一個金色、同一組三層填色、同一條
/// 14→15.6 的內金線、同一圈 1 單位的羽化裙擺（專案沒開 MSAA，硬邊會爬階梯）。不同
/// 的只有那一圈點。所以它們看得出是同一種金屬打的，又絕不會認錯是哪一塊。
///
/// **為什麼六角形在版面上也該走。** 演奏會那塊牌的 `Pad` 被迫從 26 加到 38，病根不
/// 是內縮的算法（真正的多邊形 offset 只賺回 2.5 單位），是那條**跑滿全高的斜邊**：
/// 在最上和最下那一行字的高度上，內縮 14 的那圈金線已經沿著斜邊退到 x≈35。方角或
/// 是只在一端做文章的輪廓完全不花這筆錢，四條直邊上的金線恆在 15.6。
/// </remarks>
internal enum PlaqueShape
{
    /// <summary>銅牌：左端整個是半圓，右端方 —— 圓頭方尾。玩家徽章。</summary>
    Badge,
    /// <summary>灣口牌：外端是半橢圓的鼻子，裝訂端方。演奏會左（演奏者）。</summary>
    Bay,
    /// <summary>印刷卡：四角方切，只在角上斜掉 4 —— 裁刀沒有半徑。演奏會右（評分）。</summary>
    Card,
    /// <summary>搖籃：平頂讓寶石，性格全在分數帶以下那道相切的四分之一橢圓。難度列。</summary>
    Cradle
}

[RequireComponent(typeof(CanvasRenderer))]
internal sealed class PlaqueGraphic : MaskableGraphic
{
    internal const float RuleOuter = 14f;     // 內金線：外緣
    internal const float RuleInner = 15.6f;   // 內金線：內緣
    private const float Nick = 4f;            // 印刷卡的角，45°

    /// <summary>光從左上來。和這個專案其他每一處的受光方向同一邊（約 123°）。</summary>
    internal static readonly Vector2 Key = new Vector2(-0.55f, 0.84f).normalized;

    /// <summary>對角漸層的總對比。0.13 表示左上 ×1.18、右下 ×0.82。</summary>
    private const float Throw = 0.13f;

    /// <summary>場鋪的是哪一種材質，放大幾倍。tint 0.10 下給到 8-bit 上約 4.2 級。</summary>
    /// <remarks>
    /// 皮不是紙。紙要 gain 5 才夠（它的標準差只有 0.0215），而那個倍率會把抄紙的簾紋
    /// 放大成一條條看得見的**橫帶** —— 牌面因此有了方向，而一塊平放的托盤不該有。皮
    /// 的標準差是 0.0443、而且是各向同性的（鵝卵石般的粒面，不是線），gain 2.5 就到
    /// 同樣的對比。順帶讓這幾塊牌和結算那本書是同一種材料。
    /// </remarks>
    private const ClassicalBookUITheme.Surface GrainKind = ClassicalBookUITheme.Surface.Leather;
    private const float GrainGain = 2.5f;

    /// <summary>牌面的底色。</summary>
    /// <remarks>
    /// **0.17 不是 0.10。** 0.10 的牌子在一整片亮著的書和舞台燈旁邊讀起來是挖在畫面
    /// 上的**洞**，不是放在上面的物件；而且那個亮度下不管貼圖怎麼放大，絕對調變都只
    /// 有 ±5 級，在感知門檻以下 —— 使用者連續兩次說「沒看到質感」就是這件事。提到
    /// 0.17 之後同樣的相對調變變成 ±9.4 級，皮紋才真的看得見。色相不動（G/R = 0.55、
    /// B/R = 0.35）。
    ///
    /// 上限在哪：最亮的一角是 `0.17 × 1.818（flat lift）× 1.13（光）× 0.891（貼圖最
    /// 大值）= 0.31`，離夾白還很遠。
    /// </remarks>
    [SerializeField] private Color field = new Color(0.17f, 0.0935f, 0.0595f, 0.93f);
    [SerializeField] private Color gilt = new Color(0.92f, 0.78f, 0.44f, 1f);
    [SerializeField] private PlaqueShape profile = PlaqueShape.Card;
    [SerializeField] private float nose = 75f;      // Bay 的鼻子有多深
    [SerializeField] private float cradleRx = 200f; // Cradle 底部那道橢圓
    [SerializeField] private float cradleRy = 64f;

    private PlaqueGildingGraphic gilding;


    /// <summary>
    /// 在 <paramref name="host"/> 上建一塊牌：場一層、金工一層。
    /// </summary>
    /// <remarks>
    /// **為什麼一定要兩層。** 貼圖會乘到整個 mesh 上，而金色 0.92 乘上均值 0.55 的紙
    /// 紋是 0.51 —— 金子會發黑。場要貼圖、金工不能有，所以它們不能是同一個 graphic。
    /// 代價是多一個 draw call，換來的是那 16.9 萬個像素不再是平塗。
    /// </remarks>
    public static PlaqueGraphic Build(RectTransform host, PlaqueShape shape)
    {
        if (host == null) return null;
        PlaqueGraphic plate = host.GetComponent<PlaqueGraphic>();
        if (plate == null) plate = host.gameObject.AddComponent<PlaqueGraphic>();
        plate.profile = shape;
        plate.raycastTarget = false;

        Transform existing = host.Find("~Gilding");
        if (existing == null)
        {
            var giltObject = new GameObject("~Gilding", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(PlaqueGildingGraphic));
            giltObject.layer = host.gameObject.layer;
            var giltRect = (RectTransform)giltObject.transform;
            giltRect.SetParent(host, false);
            giltRect.anchorMin = Vector2.zero;
            giltRect.anchorMax = Vector2.one;
            giltRect.offsetMin = Vector2.zero;
            giltRect.offsetMax = Vector2.zero;
            existing = giltObject.transform;
        }
        plate.gilding = existing.GetComponent<PlaqueGildingGraphic>();
        plate.gilding.raycastTarget = false;
        plate.gilding.Bind(plate);
        plate.SetVerticesDirty();
        return plate;
    }

    public override Texture mainTexture
    {
        get
        {
            Texture grain = ClassicalBookUITheme.DarkSurfaceTexture(GrainKind, GrainGain);
            return grain != null ? grain : base.mainTexture;
        }
    }

    public PlaqueShape Profile
    {
        get => profile;
        set
        {
            if (profile == value) return;
            profile = value;
            SetVerticesDirty();
            if (gilding != null) gilding.SetVerticesDirty();
        }
    }

    /// <summary><see cref="PlaqueShape.Bay"/> 的鼻子深度。要等於肖像的中心 x 才同心。</summary>
    public float Nose
    {
        get => nose;
        set
        {
            if (Mathf.Approximately(nose, value)) return;
            nose = value;
            SetVerticesDirty();
            if (gilding != null) gilding.SetVerticesDirty();
        }
    }

    internal Color Gilt => gilt;

    /// <summary>
    /// 要掛投影的話掛在這裡，不要掛在場上。
    /// </summary>
    /// <remarks>
    /// uGUI 的 `Shadow` 複製的是**它所在那個 graphic 的網格**。掛在場上的話，複製到的
    /// 是內縮 5 的那一圈，投影會整個躲在牌子自己底下 —— 只有金工那一層畫到真正的外
    /// 輪廓。
    /// </remarks>
    internal GameObject OutlineTarget => gilding != null ? gilding.gameObject : gameObject;

    /// <summary>金工那一層要用同一條環，所以環的產生器對它開放。</summary>
    internal Vector2[] RingFor(Rect r, float inset) => Ring(r, inset);

    /// <summary>
    /// 場。兩層暗色的填，鋪著放大過的紙紋，再壓一道從左上來的對角漸層。
    /// </summary>
    /// <remarks>
    /// **為什麼要做這件事。** 難度盤是 761×222 —— 16.9 萬個像素，過去是一片**平塗**，
    /// 而上面坐著五顆有明暗刻面的石頭。牌面本身沒有任何材質可言，所以不管周圍的東西
    /// 做得多細，這一塊永遠是畫面上最假的地方。
    ///
    /// 紙紋走 `ClassicalBookUITheme` 那條放大路徑：tint 0.10 會把紙的 0.0215 標準差壓
    /// 成 8-bit 上的 0.55 級（跨不到一個色階），gain 5 之後是 4.3 級。頂點色要乘上
    /// `DarkSurfaceLift`，平均亮度才和放大之前一樣。
    ///
    /// 對角漸層是逐頂點的，而扇形是從中心發散的 —— 中心那一點拿 1.0、外環每一點拿自
    /// 己的值，插值出來就是一整片連續的漸層，零額外頂點。
    /// </remarks>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 8f || r.height <= 8f) return;

        // 這塊牌**本來是純色**，不是鋪著貼圖的面 —— 所以補的是 1/pivot 不是
        // 均值/pivot。用錯的話整塊會暗 19%，而牌面上還坐著要讀的字。
        float lift = ClassicalBookUITheme.DarkSurfaceFlatLift;
        Texture grain = mainTexture;
        float tile = grain != null ? grain.width : 256f;

        Color recess = new Color(field.r * 0.6f, field.g * 0.6f, field.b * 0.6f, 0.98f);
        LitFan(vh, r, Ring(r, 5f), recess, lift, tile);
        LitFan(vh, r, Ring(r, 9f), field, lift, tile);
    }

    private static void LitFan(VertexHelper vh, Rect r, Vector2[] points, Color colour,
        float lift, float tile)
    {
        Color based = new Color(colour.r * lift, colour.g * lift, colour.b * lift, colour.a);
        int first = vh.currentVertCount;
        AddLit(vh, r, r.center, based, tile);
        foreach (Vector2 point in points) AddLit(vh, r, point, based, tile);
        for (int i = 0; i < points.Length; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % points.Length);
    }

    private static void AddLit(VertexHelper vh, Rect r, Vector2 position, Color colour, float tile)
    {
        float nx = r.width > 1f ? (position.x - r.center.x) / (r.width * 0.5f) : 0f;
        float ny = r.height > 1f ? (position.y - r.center.y) / (r.height * 0.5f) : 0f;
        float lit = 1f + Throw * (Key.x * nx + Key.y * ny);
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = new Color(colour.r * lit, colour.g * lit, colour.b * lit, colour.a);
        vertex.uv0 = new Vector2((position.x - r.xMin) / tile, (position.y - r.yMin) / tile);
        vh.AddVert(vertex);
    }

    // ── 四種輪廓 ──────────────────────────────────────────────────────

    private Vector2[] Ring(Rect r, float inset)
    {
        switch (profile)
        {
            case PlaqueShape.Badge: return BadgeRing(r, inset);
            case PlaqueShape.Card: return CardRing(r, inset);
            // 這兩種的造型只在一端（或只在一條邊）上，四條直邊維持筆直，所以真正的
            // 多邊形 offset 在每一條邊上量到的垂直距離都剛好是 inset —— 金線恆在
            // 15.6，不會像斜邊那樣沿著邊往內退。那正是六角形做不到的事。
            default: return Silhouette.Offset(Ring0(r), r.center, -inset);
        }
    }

    private Vector2[] Ring0(Rect r)
    {
        return profile == PlaqueShape.Bay ? BayRing(r) : CradleRing(r);
    }

    /// <summary>圓頭方尾。頭的半徑隨內縮變小但**圓心不動**，所以每一層的相切都還在。</summary>
    /// <remarks>
    /// 圓心固定是關鍵：若把整個輪廓等比內縮，頭和三條直邊在接點會錯開一格，每一層
    /// 之間就會露出一條細縫（這個檔案的六角形因為 `cut` 用的是全高而不是內縮後的半
    /// 高，已經有這個毛病的輕症版）。
    /// </remarks>
    private static Vector2[] BadgeRing(Rect r, float inset)
    {
        float radius = r.height * 0.5f - inset;
        float hx = r.xMin + r.height * 0.5f + 1f;      // 430×116 上是 59，和肖像同心
        float cy = r.center.y;
        float top = r.yMax - inset;
        float bottom = r.yMin + inset;
        float right = r.xMax - inset;

        var points = new Vector2[17];
        points[0] = new Vector2(hx, top);
        points[1] = new Vector2(right, top);
        points[2] = new Vector2(right, bottom);
        points[3] = new Vector2(hx, bottom);
        for (int i = 1; i < 14; i++)
        {
            float angle = Mathf.Deg2Rad * (-90f - 180f * i / 14f);
            points[3 + i] = new Vector2(hx + radius * Mathf.Cos(angle), cy + radius * Mathf.Sin(angle));
        }
        return points;
    }

    /// <summary>外端半橢圓的鼻子，裝訂端方切。</summary>
    private Vector2[] BayRing(Rect r)
    {
        float bx = Mathf.Clamp(nose, 8f, r.width * 0.45f);
        float rx = bx;
        float ry = r.height * 0.5f;
        float cx = r.xMin + bx;
        float cy = r.center.y;

        var points = new System.Collections.Generic.List<Vector2>(17)
        {
            new Vector2(cx, r.yMin), new Vector2(r.xMax, r.yMin),
            new Vector2(r.xMax, r.yMax), new Vector2(cx, r.yMax)
        };
        // 90° → 270°：從上緣繞過最左邊回到下緣。兩端的點和上面那兩個重合，所以跳過。
        for (int i = 1; i < 14; i++)
        {
            float angle = Mathf.Deg2Rad * (90f + 180f * i / 14f);
            points.Add(new Vector2(cx + rx * Mathf.Cos(angle), cy + ry * Mathf.Sin(angle)));
        }
        return points.ToArray();
    }

    /// <summary>方切，四角各斜掉 4。裁刀沒有半徑，這就是整句設計。</summary>
    private static Vector2[] CardRing(Rect r, float inset)
    {
        float x0 = r.xMin + inset, x1 = r.xMax - inset;
        float y0 = r.yMin + inset, y1 = r.yMax - inset;
        float n = Mathf.Min(Nick, Mathf.Min(x1 - x0, y1 - y0) * 0.25f);
        return new[]
        {
            new Vector2(x0 + n, y1), new Vector2(x1 - n, y1),
            new Vector2(x1, y1 - n), new Vector2(x1, y0 + n),
            new Vector2(x1 - n, y0), new Vector2(x0 + n, y0),
            new Vector2(x0, y0 + n), new Vector2(x0, y1 - n)
        };
    }

    /// <summary>平頂讓寶石，底下兩側掃上去的一道相切橢圓。</summary>
    /// <remarks>
    /// `cradleRy` 不能再小：這道掃弧的最小曲率半徑是 ry²/rx，在 64/200 是 20.5，還在
    /// 內金線被侵蝕的那 15.6 之上。降到 56 就是 15.7，金線會開始起尖點。
    /// </remarks>
    private Vector2[] CradleRing(Rect r)
    {
        float rx = Mathf.Min(cradleRx, r.width * 0.3f);
        float ry = Mathf.Min(cradleRy, r.height * 0.32f);
        // 18 不是 10。頂上兩個圓角的半徑必須大於內金線的 15.6 —— `Silhouette.Offset`
        // 是逐點的斜接，把一段半徑 r 的弧往內推超過 r，相鄰頂點就會交叉，那一圈線在
        // 兩個角上各折 3 條邊。實測 16 還會折 2 條，18 才乾淨。
        const float f = 18f;
        var points = new System.Collections.Generic.List<Vector2>(26);
        // 左下的掃弧：180° → 270°，從 (xMin, yMin+ry) 到 (xMin+rx, yMin)
        for (int i = 0; i <= 8; i++)
        {
            float angle = Mathf.Deg2Rad * (180f + 90f * i / 8f);
            points.Add(new Vector2(r.xMin + rx + rx * Mathf.Cos(angle),
                                   r.yMin + ry + ry * Mathf.Sin(angle)));
        }
        // 右下鏡像。從 i=8 起算，那一點是 (xMax-rx, yMin) —— 兩道掃弧之間的底邊必須
        // 是平的，少了它底緣會從 x=rx 直接斜到 x=W-0.8rx，弧和弧之間出現一道折。
        for (int i = 8; i >= 0; i--)
        {
            float angle = Mathf.Deg2Rad * (180f + 90f * i / 8f);
            points.Add(new Vector2(r.xMax - rx - rx * Mathf.Cos(angle),
                                   r.yMin + ry + ry * Mathf.Sin(angle)));
        }
        // 上面兩個角只給 10 的圓角：頂緣必須是平的，五顆石頭坐在上面。
        points.Add(new Vector2(r.xMax, r.yMax - f));
        for (int i = 1; i < 3; i++)
        {
            float angle = Mathf.Deg2Rad * (90f * i / 3f);
            points.Add(new Vector2(r.xMax - f + f * Mathf.Cos(angle), r.yMax - f + f * Mathf.Sin(angle)));
        }
        points.Add(new Vector2(r.xMax - f, r.yMax));
        points.Add(new Vector2(r.xMin + f, r.yMax));
        for (int i = 1; i < 3; i++)
        {
            float angle = Mathf.Deg2Rad * (90f + 90f * i / 3f);
            points.Add(new Vector2(r.xMin + f + f * Mathf.Cos(angle), r.yMax - f + f * Mathf.Sin(angle)));
        }
        points.Add(new Vector2(r.xMin, r.yMax - f));
        return points.ToArray();
    }

    private static void Fan(VertexHelper vh, Vector2 centre, Vector2[] points, Color colour)
    {
        int first = vh.currentVertCount;
        AddVertex(vh, centre, colour);
        foreach (Vector2 point in points) AddVertex(vh, point, colour);
        for (int i = 0; i < points.Length; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % points.Length);
    }

    private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color colour)
    {
        int index = vh.currentVertCount;
        AddVertex(vh, p0, colour);
        AddVertex(vh, p1, colour);
        AddVertex(vh, p2, colour);
        AddVertex(vh, p3, colour);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}


/// <summary>
/// 牌子的金工：外緣、羽化的裙擺、內金線。**不能有貼圖。**
/// </summary>
/// <remarks>
/// 它和 <see cref="PlaqueGraphic"/> 是同一塊牌的兩層。拆開的唯一理由是貼圖會乘到整個
/// mesh 上：金色 0.92 乘上均值 0.55 的紙紋是 0.51，金子會發黑。場需要那張紙紋，金工
/// 不能碰它，所以它們不能共用一個 `CanvasRenderer`。
///
/// 它是子物件所以畫在場之後 —— 內金線必須壓在場上面，而外緣那圈金和場沒有重疊。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class PlaqueGildingGraphic : MaskableGraphic
{
    private const float RuleOuter = PlaqueGraphic.RuleOuter;
    private const float RuleInner = PlaqueGraphic.RuleInner;

    private PlaqueGraphic plate;

    internal void Bind(PlaqueGraphic owner)
    {
        plate = owner;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (plate == null) plate = GetComponentInParent<PlaqueGraphic>();
        if (plate == null) return;
        Rect r = rectTransform.rect;
        if (r.width <= 8f || r.height <= 8f) return;

        Color gilt = plate.Gilt;
        Vector2 centre = r.center;
        Vector2[] rim = plate.RingFor(r, 0f);

        // 外輪廓羽化。專案沒開 MSAA（PC_RPAsset 的 m_MSAA: 1），而輪播每一幀都在改卡
        // 片的縮放 —— 任何一條不是水平也不是垂直的邊只要是硬邊，就會一路爬階梯。實心
        // 收進半格、外面補一圈 alpha 收到 0 的裙擺，大小不變，邊卻是連續的。
        Vector2[] outer = Silhouette.Offset(rim, centre, -0.5f);
        Vector2[] skirt = Silhouette.Offset(rim, centre, 0.5f);
        Color edge = new Color(gilt.r, gilt.g, gilt.b, 0.95f);

        // 外緣那一圈金：從實心邊界到場的上緣（內縮 5），是一條帶不是一整片 —— 場已
        // 經自己畫了，不需要在它底下再鋪一層。
        //
        // **每一段都照自己的法線受光。** 這是整塊牌上唯一高對比的東西：場的 tint 是
        // 0.10，紋理再怎麼放大絕對變化也只有 ±5 級；金是 0.92，同樣的相對調變是
        // ±38 級。一整圈同色的金讀起來是印上去的色塊，有方向的金才讀成打過的金屬。
        //
        // 法線直接從已經算好的兩圈拿：`skirt − outer` 就是那個頂點的向外方向。
        var shade = new float[outer.Length];
        for (int i = 0; i < outer.Length; i++)
        {
            Vector2 n = skirt[i] - outer[i];
            n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector2.up;
            // 0.68–1.06 不是 0.72–1.20：金是 0.92，乘上 1.20 是 1.10，最亮的那一段會被
            // 夾成純白 —— 高光反而變成一塊沒有形狀的平面。1.06 讓它停在 0.975。
            shade[i] = 0.68f + 0.38f * Mathf.Clamp01(Vector2.Dot(n, PlaqueGraphic.Key) * 0.5f + 0.5f);
        }
        Vector2[] inner5 = plate.RingFor(r, 5f);
        for (int i = 0; i < outer.Length; i++)
        {
            int next = (i + 1) % outer.Length;
            Color a = Tone(edge, shade[i]);
            Color b = Tone(edge, shade[next]);
            // 內緣比外緣暗一階：金帶是一個有厚度的斜面，不是一條線。
            AddQuad(vh, outer[i], outer[next], inner5[next], inner5[i],
                a, b, Tone(edge, shade[next] * 0.82f), Tone(edge, shade[i] * 0.82f));
            Color clearA = new Color(a.r, a.g, a.b, 0f);
            Color clearB = new Color(b.r, b.g, b.b, 0f);
            AddQuad(vh, outer[i], outer[next], skirt[next], skirt[i], a, b, clearB, clearA);
        }

        Hairline(vh, r, centre, rim, gilt);
    }

    // ── 內金線 ────────────────────────────────────────────────────────

    /// <summary>
    /// 兩條規線之間留一段暗場，邊框才有厚度。
    /// </summary>
    /// <remarks>
    /// 只有 <see cref="PlaqueShape.Bay"/> 和 <see cref="PlaqueShape.Cradle"/> 的金線
    /// 是輪廓的真正 offset —— 它們的直邊夠多，offset 在每一條邊上都剛好是 15.6。
    ///
    /// <see cref="PlaqueShape.Card"/> 用**純矩形**：它的角是 45° 斜切，offset 到 15.6
    /// 會把線帶到 Rect 外面去。而且矩形的金線正是讓 `Pad` 從 38 降到 20 的那一步。
    ///
    /// <see cref="PlaqueShape.Badge"/> 的是兩件：一個**左邊開口**的規線框（刻版的邊
    /// 線碰到浮雕就停），加上一圈繞著頭的圓 —— 同一條線彎過去當肖像的 bezel。順手修
    /// 掉一個出貨中的缺陷：六角形的金線目前壓過 78 的肖像 1.3 單位，只是被印章蓋住
    /// 才看不到；改成半徑 42.4 之後對 39 的肖像有 3.4 的真淨空。
    /// </remarks>
    private void Hairline(VertexHelper vh, Rect r, Vector2 centre, Vector2[] rim, Color gilt)
    {
        Color line = new Color(gilt.r, gilt.g, gilt.b, 0.55f);
        PlaqueShape profile = plate.Profile;
        if (profile == PlaqueShape.Card)
        {
            Band(vh, Rectangle(r, RuleOuter), Rectangle(r, RuleInner), line);
            return;
        }
        if (profile == PlaqueShape.Badge)
        {
            float hx = r.xMin + r.height * 0.5f + 1f;
            float cy = r.center.y;
            // 規線框：上、下、右三段，左邊讓給圓。x 從頭的右側 49 起算。
            float left = hx + 49f;
            Strip(vh, left, r.xMax - RuleInner, r.yMax - RuleInner, r.yMax - RuleOuter, line);
            Strip(vh, left, r.xMax - RuleInner, r.yMin + RuleOuter, r.yMin + RuleInner, line);
            Strip(vh, r.xMax - RuleInner, r.xMax - RuleOuter,
                r.yMin + RuleOuter, r.yMax - RuleOuter, line);
            Band(vh, Circle(hx, cy, 44f, 24), Circle(hx, cy, 42.4f, 24), line);
            return;
        }
        Band(vh, Silhouette.Offset(rim, centre, -RuleOuter),
            Silhouette.Offset(rim, centre, -RuleInner), line);
    }

    private static Vector2[] Rectangle(Rect r, float inset)
    {
        return new[]
        {
            new Vector2(r.xMin + inset, r.yMax - inset), new Vector2(r.xMax - inset, r.yMax - inset),
            new Vector2(r.xMax - inset, r.yMin + inset), new Vector2(r.xMin + inset, r.yMin + inset)
        };
    }

    private static Vector2[] Circle(float cx, float cy, float radius, int segments)
    {
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            points[i] = new Vector2(cx + radius * Mathf.Cos(angle), cy + radius * Mathf.Sin(angle));
        }
        return points;
    }

    private static void Band(VertexHelper vh, Vector2[] outer, Vector2[] inner, Color colour)
    {
        int count = Mathf.Min(outer.Length, inner.Length);
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            AddQuad(vh, outer[i], outer[next], inner[next], inner[i], colour);
        }
    }

    private static void Strip(VertexHelper vh, float x0, float x1, float y0, float y1, Color colour)
    {
        AddQuad(vh, new Vector2(x0, y1), new Vector2(x1, y1),
            new Vector2(x1, y0), new Vector2(x0, y0), colour);
    }

    private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Color colour)
    {
        AddQuad(vh, p0, p1, p2, p3, colour, colour, colour, colour);
    }

    private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Color c0, Color c1, Color c2, Color c3)
    {
        int index = vh.currentVertCount;
        AddVertex(vh, p0, c0);
        AddVertex(vh, p1, c1);
        AddVertex(vh, p2, c2);
        AddVertex(vh, p3, c3);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    /// <summary>把一個顏色乘上受光量，alpha 不動。</summary>
    private static Color Tone(Color colour, float lit)
    {
        return new Color(colour.r * lit, colour.g * lit, colour.b * lit, colour.a);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }

}

/// <summary>
/// One difficulty stone: a white hexagonal setting with a cut hexagonal gem in
/// it, and the tier's name across the bottom.
/// </summary>
/// <remarks>
/// **Why it is cut into facets.** A single flat hexagon of any colour reads as a
/// sticker. What makes a stone a stone is that neighbouring faces catch the
/// light differently, so the crown is drawn as six separate facets around a
/// table, each shaded by which way it tilts against a light from the upper left.
/// The table itself is the flattest and most even face, which is where the level
/// number sits.
///
/// **Why the white hexagon is drawn first and full size.** It is not a border,
/// it is the setting the stone is mounted in -- on the dark plaque a coloured
/// hexagon alone floats, and the ring of white is what puts it on something.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class GemGraphic : MaskableGraphic
{
    private static readonly Vector2 Light = new Vector2(-0.5f, 0.87f);

    /// <summary>
    /// 沒被選到的那幾顆有多暗。整塊牌子只有這一個「沒選到」的量。
    /// </summary>
    /// <remarks>
    /// 以前石身寫 0.58、等級數字另外寫 0.5，兩個數字在講同一件事卻不一樣；改一邊
    /// 就會忘記另一邊。名字的 0.55 也一併收進來 —— 差那 0.03 沒有人分得出來，但多
    /// 一個數字就多一個會走樣的地方。
    /// </remarks>
    internal const float UnselectedAlpha = 0.58f;

    /// <summary>這首歌沒有的那一階：照樣畫出來，但是整顆收起來。</summary>
    internal const float MutedAlpha = 0.40f;

    private Color tint = new Color(0.62f, 0.32f, 0.84f, 1f);
    private string caption = string.Empty;
    private bool muted;
    private float glow = 1f;
    private TextMeshProUGUI captionLabel;

    /// <summary>
    /// 這顆石頭有多亮：0 是沒被選到、1 是現在選的那一階，滑過時再往上一點。
    /// </summary>
    /// <remarks>
    /// Dimmed rather than outlined: an outline is one more shape on a plaque
    /// that already has five stones and a score, and brightness is the one
    /// channel nothing else here is using.
    ///
    /// **為什麼是浮點數而不是 bool。** 本來是布林值，於是點另一顆寶石時「舊的暗下
    /// 去」和「新的亮起來」發生在同一幀 —— 畫面只是換了一張圖，沒有任何東西把玩家
    /// 剛剛的手指和亮起來的那一顆連起來。改成連續值，交接才補得出來（由
    /// <see cref="GemGlow"/> 驅動）。選取本身仍然是當下就改好的，動的只有畫面。
    ///
    /// 可以大於 1：滑過會再加一點點，那一段剩餘量只餵給星芒 —— alpha 到 1 就飽和
    /// 了，已經選中的那一顆若只看 alpha 就會對滑過毫無反應。
    /// </remarks>
    public float Glow
    {
        get => glow;
        set
        {
            if (Mathf.Approximately(glow, value)) return;
            glow = value;
            SetVerticesDirty();
            ApplyCaption();
        }
    }

    public Color Tint
    {
        get => tint;
        set
        {
            if (tint == value) return;
            tint = value;
            SetVerticesDirty();
            ApplyCaption();
        }
    }

    /// <summary>A tier the song does not have: still drawn, but withdrawn.</summary>
    public bool Muted
    {
        get => muted;
        set
        {
            if (muted == value) return;
            muted = value;
            SetVerticesDirty();
            ApplyCaption();
        }
    }

    public string Caption
    {
        get => caption;
        set
        {
            if (caption == value) return;
            caption = value;
            ApplyCaption();
        }
    }

    /// <summary>
    /// Draws the stone: bezel, then the back of the stone, then the front of it
    /// over the top at part opacity.
    /// </summary>
    /// <remarks>
    /// **Why the pavilion is drawn first.** What makes a cut stone look like a
    /// stone and not a coloured tile is that you can see its *back* through its
    /// front: the pavilion facets behind the table are what produce the dark and
    /// bright wedges inside it. So the back is laid down at full contrast, the
    /// crown goes over it at around half alpha, and the two mix per pixel. That
    /// mixing is the depth -- no amount of shading on a single opaque layer
    /// gets there, which is why the first version read flat however many facets
    /// it had.
    ///
    /// **Why the contrast is extreme.** Adjacent facets on a real brilliant are
    /// near-black against near-white, because each one is a mirror pointing
    /// somewhere different. Mild shading is what glass looks like; this steps
    /// from 55% towards black to 45% towards white between neighbours.
    ///
    /// **Why the facets are not all the same hue.** A little dispersion -- warm
    /// on the facets facing the light, cool on the ones facing away -- is most
    /// of what says "gem" rather than "plastic", and it costs one lerp.
    /// </remarks>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 8f || r.height <= 8f) return;

        float alpha = muted ? MutedAlpha : Mathf.Lerp(UnselectedAlpha, 1f, Mathf.Clamp01(glow));
        Vector2 centre = new Vector2(r.center.x, r.center.y + r.height * 0.06f);
        float halfWidth = r.width * 0.5f;
        float halfHeight = r.height * 0.42f;

        DrawBezel(vh, centre, halfWidth, halfHeight, alpha);

        float girdleW = halfWidth * 0.86f;
        float girdleH = halfHeight * 0.86f;
        Vector2[] girdle = Hexagon(centre, girdleW, girdleH, 0f);
        // 桌面轉 30 度：它的頂點對著腰稜的邊中點，冠部因此是六個風箏面和六個
        // 星形面交錯 —— 明亮式切割的星形圖案就是這樣來的。
        Vector2[] table = Hexagon(centre, girdleW * 0.46f, girdleH * 0.46f, 30f);

        DrawPavilion(vh, centre, girdle, alpha);
        DrawCrown(vh, centre, girdle, table, alpha);
        DrawTable(vh, centre, table, girdleW, alpha);
        DrawGirdle(vh, girdle, alpha);
        DrawSparkle(vh, centre, girdleW, girdleH, alpha);
    }

    /// <summary>
    /// The white metal the stone sits in: a rim, lit from the upper left.
    /// </summary>
    /// <remarks>
    /// 只有最外面這一圈羽化。石頭內部那些切面之間的硬跳接是刻意的 —— 相鄰的面要
    /// 像兩面朝向不同的鏡子，一柔化就變回一塊有漸層的彩色磁磚。會爬階梯的是輪廓，
    /// 不是切面。
    /// </remarks>
    private void DrawBezel(VertexHelper vh, Vector2 centre, float halfWidth, float halfHeight,
        float alpha)
    {
        Vector2[] rim = Hexagon(centre, halfWidth, halfHeight, 0f);
        Vector2[] inner = Hexagon(centre, halfWidth * 0.88f, halfHeight * 0.88f, 0f);
        // 實心收進半格、外面補一條收到透明的裙擺：輪廓的位置和墨量都沒變，但邊是
        // 連續的。
        Vector2[] outer = Silhouette.Offset(rim, centre, -0.5f);
        Vector2[] skirt = Silhouette.Offset(rim, centre, 0.5f);
        Color silver = new Color(0.93f, 0.92f, 0.90f, 1f);

        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            Vector2 edge = (outer[next] - outer[i]).normalized;
            Vector2 outward = new Vector2(-edge.y, edge.x);
            // 金屬只有兩種：對著光的一段是白的，背光的一段幾乎是灰的。
            float facing = Vector2.Dot(outward, Light);
            Color face = Color.Lerp(silver * 0.38f, Color.white, Mathf.Clamp01(facing * 0.5f + 0.5f));
            face.a = 0.97f * alpha;
            Color lip = Color.Lerp(face, silver * 0.62f, 0.5f);
            lip.a = face.a;

            int first = vh.currentVertCount;
            AddVertex(vh, outer[i], face);
            AddVertex(vh, outer[next], face);
            AddVertex(vh, inner[next], lip);
            AddVertex(vh, inner[i], lip);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);

            // 裙擺接的是這一段自己的顏色，六段的明暗才不會在輪廓上錯開。
            Silhouette.Ramp(vh, outer[i], outer[next], skirt[i], skirt[next], face);
        }
    }

    /// <summary>
    /// The back of the stone, at full contrast. Everything else is drawn over it.
    /// </summary>
    private void DrawPavilion(VertexHelper vh, Vector2 centre, Vector2[] girdle, float alpha)
    {
        // 尖底不在正中央：從斜上方看一顆石頭，亭部的尖端會往背光那一側偏。
        Vector2 culet = centre - Light * (Vector2.Distance(girdle[0], centre) * 0.10f);
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            Vector2 middle = (girdle[i] + girdle[next] + culet) / 3f;
            Vector2 outward = (middle - centre).normalized;
            float facing = Vector2.Dot(outward, Light);
            // 相鄰的兩個面必須跳很開 —— 每一個面都是朝著不同方向的鏡子。
            float step = (i % 2 == 0) ? 0.42f : -0.46f;
            Color deep = Facet(facing * 0.5f + step, alpha, 1f);
            Color tip = Facet(facing * 0.3f + step * 0.6f - 0.25f, alpha, 1f);

            int first = vh.currentVertCount;
            AddVertex(vh, girdle[i], deep);
            AddVertex(vh, girdle[next], deep);
            AddVertex(vh, culet, tip);
            vh.AddTriangle(first, first + 1, first + 2);
        }
    }

    /// <summary>The crown facets, laid over the pavilion at part opacity.</summary>
    private void DrawCrown(VertexHelper vh, Vector2 centre, Vector2[] girdle, Vector2[] table,
        float alpha)
    {
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            AddFacet(vh, centre, girdle[i], girdle[next], table[i], alpha, 0f, 0.62f);
            AddFacet(vh, centre, table[(i + 5) % 6], girdle[i], table[i], alpha, 0.26f, 0.48f);
        }
    }

    /// <summary>
    /// The table: the flat top, thin enough to see the pavilion's star in it.
    /// </summary>
    private void DrawTable(VertexHelper vh, Vector2 centre, Vector2[] table, float girdleW,
        float alpha)
    {
        Vector2 spot = centre + Light * (girdleW * 0.14f);
        Color middle = Facet(0.30f, alpha, 0.40f);
        int first = vh.currentVertCount;
        AddVertex(vh, spot, middle);
        for (int i = 0; i < table.Length; i++)
        {
            Vector2 outward = (table[i] - centre).normalized;
            AddVertex(vh, table[i], Facet(Vector2.Dot(outward, Light) * 0.35f - 0.10f, alpha, 0.42f));
        }
        for (int i = 0; i < table.Length; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % table.Length);

        // 桌面上的高光。硬邊、很小、偏在受光側 —— 柔和的一團讀起來是塑膠。
        Vector2 glint = centre + Light * (girdleW * 0.28f);
        Vector2 along = new Vector2(-Light.y, Light.x) * (girdleW * 0.26f);
        Vector2 depth = Light * (girdleW * 0.09f);
        Color hot = new Color(1f, 0.99f, 0.96f, 0.85f * alpha);
        Color clear = new Color(1f, 0.99f, 0.96f, 0f);
        int index = vh.currentVertCount;
        AddVertex(vh, glint - along, clear);
        AddVertex(vh, glint + depth, hot);
        AddVertex(vh, glint + along, clear);
        AddVertex(vh, glint - depth * 0.6f, hot);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    /// <summary>The girdle: a bright arc on the lit side, a dark one opposite.</summary>
    private void DrawGirdle(VertexHelper vh, Vector2[] girdle, float alpha)
    {
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            Vector2 edge = (girdle[next] - girdle[i]).normalized;
            Vector2 outward = new Vector2(-edge.y, edge.x);
            float facing = Vector2.Dot(outward, Light);
            AddEdge(vh, girdle[i], girdle[next], 2.6f,
                facing > 0.15f ? Facet(0.92f, alpha * 0.9f, 1f)
                               : Facet(-0.72f, alpha * 0.8f, 1f));
        }
    }

    /// <summary>
    /// The one hard point of light a polished stone throws back at you.
    /// </summary>
    private void DrawSparkle(VertexHelper vh, Vector2 centre, float girdleW, float girdleH,
        float alpha)
    {
        if (muted) return;
        Vector2 at = centre + new Vector2(Light.x * girdleW * 0.62f, Light.y * girdleH * 0.62f);
        // 星芒吃的是沒夾過的 glow：滑過已經選中的那一顆時，alpha 早就到頂了，能動的
        // 只剩這道光 —— 長一點、亮一點，就是「你指著的是這顆」的全部回饋。
        // 一定要用 LerpUnclamped：Mathf.Lerp 會把 t 夾在 0..1，而這裡整個設計就
        // 是「glow 可以超過 1，超出的那一段只餵給星芒」—— 用夾過的版本等於滑過已
        // 經選中的那一顆時一個像素都不會變。
        float arm = girdleW * Mathf.LerpUnclamped(0.26f, 0.42f, glow);
        Color hot = new Color(1f, 1f, 0.98f,
            Mathf.Clamp01(Mathf.LerpUnclamped(0.5f, 0.9f, glow)) * alpha);
        Color clear = new Color(1f, 1f, 0.98f, 0f);

        AddSpike(vh, at, new Vector2(arm, 0f), arm * 0.10f, hot, clear);
        AddSpike(vh, at, new Vector2(0f, arm * 0.8f), arm * 0.10f, hot, clear);
    }

    private static void AddSpike(VertexHelper vh, Vector2 at, Vector2 arm, float width,
        Color hot, Color clear)
    {
        Vector2 normal = new Vector2(-arm.y, arm.x).normalized * width;
        int first = vh.currentVertCount;
        AddVertex(vh, at - arm, clear);
        AddVertex(vh, at + normal, hot);
        AddVertex(vh, at + arm, clear);
        AddVertex(vh, at - normal, hot);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    /// <summary>
    /// A facet's colour: the stone's own hue pushed towards white or black, with
    /// a little dispersion either way.
    /// </summary>
    private Color Facet(float amount, float alpha, float opacity)
    {
        float k = Mathf.Clamp(amount, -1f, 1f);
        // 受光的面偏暖、背光的面偏冷。這一點色散就是「寶石」和「塑膠」的差別。
        Color hue = k >= 0f
            ? Color.Lerp(tint, new Color(1f, 0.94f, 0.86f, 1f), 0.18f)
            : Color.Lerp(tint, new Color(0.62f, 0.72f, 1f, 1f), 0.22f);
        Color target = k >= 0f ? Color.white : new Color(0.04f, 0.02f, 0.06f, 1f);
        Color mixed = Color.Lerp(hue, target, Mathf.Abs(k));
        mixed.a = 0.97f * alpha * opacity;
        return mixed;
    }

    /// <summary>
    /// One crown facet, shaded by the way it tilts against the light.
    /// </summary>
    /// <remarks>
    /// The tilt is taken from the facet's own centroid relative to the stone's,
    /// which is what makes neighbouring faces differ -- and neighbouring faces
    /// differing is the entire difference between a cut stone and a coloured
    /// hexagon. <paramref name="bias"/> separates the two families of facet so
    /// the star pattern reads even where two of them face the same way.
    /// </remarks>
    private void AddFacet(VertexHelper vh, Vector2 centre, Vector2 a, Vector2 b, Vector2 c,
        float alpha, float bias, float opacity)
    {
        Vector2 middle = (a + b + c) / 3f;
        Vector2 outward = (middle - centre).normalized;
        float facing = Vector2.Dot(outward, Light) * 0.75f + bias;

        int first = vh.currentVertCount;
        AddVertex(vh, a, Facet(facing, alpha, opacity));
        AddVertex(vh, b, Facet(facing, alpha, opacity));
        AddVertex(vh, c, Facet(facing * 0.45f + 0.20f, alpha, opacity * 0.82f));
        vh.AddTriangle(first, first + 1, first + 2);
    }

    /// <summary>A pointy-top hexagon, optionally rotated about its centre.</summary>
    private static Vector2[] Hexagon(Vector2 centre, float halfWidth, float halfHeight, float degrees)
    {
        var points = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = (90f + degrees + i * 60f) * Mathf.Deg2Rad;
            points[i] = new Vector2(centre.x + Mathf.Cos(a) * halfWidth,
                                    centre.y + Mathf.Sin(a) * halfHeight);
        }

        return points;
    }

    private static void Fan(VertexHelper vh, Vector2 centre, Vector2[] points, Color middle, Color rim)
    {
        int first = vh.currentVertCount;
        AddVertex(vh, centre, middle);
        foreach (Vector2 point in points) AddVertex(vh, point, rim);
        for (int i = 0; i < points.Length; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % points.Length);
    }

    private static void AddEdge(VertexHelper vh, Vector2 from, Vector2 to, float width, Color colour)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude < 1e-4f) return;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width;
        Color clear = new Color(colour.r, colour.g, colour.b, 0f);

        int index = vh.currentVertCount;
        AddVertex(vh, from, colour);
        AddVertex(vh, to, colour);
        AddVertex(vh, to - normal, clear);
        AddVertex(vh, from - normal, clear);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    private void ApplyCaption()
    {
        if (captionLabel == null)
        {
            var go = new GameObject("Tier", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            go.layer = gameObject.layer;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(rectTransform, false);
            rect.anchorMin = new Vector2(0f, 0.0f);
            rect.anchorMax = new Vector2(1f, 0.20f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            captionLabel = go.GetComponent<TextMeshProUGUI>();
            captionLabel.fontSize = 11f;
            captionLabel.fontStyle = FontStyles.Bold;
            captionLabel.alignment = TextAlignmentOptions.Center;
            captionLabel.characterSpacing = 3f;
            captionLabel.raycastTarget = false;
        }

        // 亮度每一幀都在動，字卻幾乎不變：照樣指派會讓 TMP 每一幀重排一次版面。
        if (captionLabel.text != caption) captionLabel.text = caption;
        Color ink = Color.Lerp(tint, Color.white, 0.86f);
        ink.a = muted ? MutedAlpha : Mathf.Lerp(UnselectedAlpha, 1f, Mathf.Clamp01(glow));
        captionLabel.color = ink;
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}

/// <summary>
/// 一顆寶石的亮度：被選中、被滑過，以及兩者之間的那一段。
/// </summary>
/// <remarks>
/// **為什麼手感不放在 Button 上。** `Selectable` 的每一種 transition 都要有東西可
/// 以換：`ColorTint` 寫的是 `targetGraphic.color`，`SpriteSwap` 要圖，`Animation`
/// 要 Animator。寶石的每一個切面都是 `OnPopulateMesh` 當場算出來的，`color` 對它
/// 完全沒有作用 —— 所以那顆 Button 的 transition 維持 `None`，亮度自己管。
///
/// **為什麼要補一段過場。** 點另一顆石頭時，舊的暗下去和新的亮起來本來發生在同一
/// 幀：畫面只是換了一張圖，沒有任何東西把玩家剛剛按下的位置和亮起來的那一顆連起
/// 來。180ms 的交叉淡入就是那條線；滑過的 120ms 則短得多，因為那一段是在回應手，
/// 不是在說明狀態。
/// </remarks>
internal sealed class GemGlow : UIBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>選取交接的長度。</summary>
    private const float SelectSeconds = 0.18f;

    /// <summary>滑過的長度。比選取短 —— 回應手的動作慢了就像卡住。</summary>
    private const float HoverSeconds = 0.12f;

    /// <summary>
    /// 滑過在選取之上再加的亮度。看得出「這顆點得下去」，又不會被誤讀成已經選中。
    /// </summary>
    private const float HoverLift = 0.18f;

    /// <summary>
    /// 一段有終點的緩出。
    /// </summary>
    /// <remarks>
    /// 存的是**畫出來的值**，換方向時從它接著走。存目標值的話，一段還沒走完就被反
    /// 轉（在五顆石頭之間連點就會）會當場跳一格。
    ///
    /// 剩餘時間用倒數而不是累加：struct 的預設值全是 0，用累加的話「0」同時代表
    /// 「剛開始」和「沒在動」，一顆還沒被設定過的石頭會自己空轉一段。
    /// </remarks>
    private struct Ease
    {
        public float value;
        private float from;
        private float target;
        private float left;
        private float span;

        public void Snap(float to)
        {
            value = from = target = to;
            left = 0f;
            span = 0f;
        }

        /// <summary>補間停在半路時直接跳到終點。</summary>
        public void Settle()
        {
            if (left > 0f) Snap(target);
        }

        public void Aim(float to, float seconds)
        {
            // 已經停在那裡、或已經正往那裡走，就不要重新起算 —— 每一幀重設的話補間
            // 永遠停在第一格，看起來像卡死。
            if (Mathf.Approximately(left > 0f ? target : value, to)) return;
            from = value;
            target = to;
            span = Mathf.Max(0.0001f, seconds);
            left = span;
        }

        /// <summary>往前走一格，回傳這一格有沒有動。</summary>
        public bool Step(float delta)
        {
            if (left <= 0f) return false;
            left -= delta;
            if (left <= 0f)
            {
                // 明確收在終點，而不是無止境地趨近。
                value = target;
                left = 0f;
                return true;
            }

            float t = 1f - left / span;
            value = Mathf.Lerp(from, target, 1f - Mathf.Pow(1f - t, 3f));
            return true;
        }
    }

    private GemGraphic gem;
    private Ease select;
    private Ease hover;
    private bool available = true;
    private bool hovering;
    private bool settled;

    /// <summary>
    /// 現找而不是在建的時候綁進來：側邊那兩張卡是 Instantiate 複製出去的，執行期綁
    /// 上的參照在複製品上會全部是 null，然後這裡第一行就靜靜地跳出去。
    /// </summary>
    private GemGraphic Stone => gem != null ? gem : (gem = GetComponent<GemGraphic>());

    /// <summary>
    /// 這一格是不是這首歌有的難度、以及是不是現在選的那一階。
    /// </summary>
    /// <remarks>
    /// 第一次不補間：牌子一出現就該已經是對的，淡進來會讓人以為畫面還沒載完。
    /// </remarks>
    public void Set(bool present, bool chosen)
    {
        available = present;
        float lit = present && chosen ? 1f : 0f;
        if (settled)
        {
            select.Aim(lit, SelectSeconds);
        }
        else
        {
            settled = true;
            select.Snap(lit);
        }

        // 這首沒有的難度按不下去，就不要亮得像按得下去；反過來，游標本來就停在這一
        // 格、換一首之後這一階才出現的話，亮度要補上 —— 游標沒動，uGUI 不會再送一
        // 次 OnPointerEnter。
        if (!present) hovering = false;
        hover.Aim(hovering ? 1f : 0f, HoverSeconds);
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = available;
        if (hovering) hover.Aim(1f, HoverSeconds);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        hover.Aim(0f, HoverSeconds);
    }

    /// <summary>
    /// 卡片重新出現時直接歸位。
    /// </summary>
    /// <remarks>
    /// 物件被停用的期間 Update 不會跑，補間就停在半路。再出現時要講的是「現在是哪
    /// 一階」這個事實，不是把那半段演完。
    /// </remarks>
    protected override void OnEnable()
    {
        base.OnEnable();
        select.Settle();
        hover.Snap(0f);
        Apply();
    }

    /// <summary>
    /// 被收起來時把滑過的亮度歸零。
    /// </summary>
    /// <remarks>
    /// 物件被停用時 uGUI 不會補送 OnPointerExit —— 不自己清的話，下次這張卡再出
    /// 現，會有一顆無緣無故亮著的石頭，而游標根本不在上面。
    /// </remarks>
    protected override void OnDisable()
    {
        base.OnDisable();
        hovering = false;
        hover.Snap(0f);
        Apply();
    }

    private void Update()
    {
        float delta = Time.unscaledDeltaTime;
        bool moved = select.Step(delta);
        // 兩段都要走完，所以是 |= 不是 ||：後者會在前一段還在動時跳過這一段。
        moved |= hover.Step(delta);
        if (moved) Apply();
    }

    private void Apply()
    {
        GemGraphic stone = Stone;
        if (stone != null) stone.Glow = select.value + hover.value * HoverLift;
    }
}

/// <summary>
/// 把一個多邊形的**外輪廓**羽化掉的兩個工具。
/// </summary>
/// <remarks>
/// 專案沒開 MSAA（PC_RPAsset 的 m_MSAA: 1），而選歌輪播每一幀都在改卡片的縮放，所
/// 以硬邊的斜線會整條爬階梯 —— 階梯比線本身還顯眼。BPM 圖（ChartBpmGraphic）和練
/// 習室的曲線（PracticeRoomScreen.AddLine）早就是這樣畫的，這裡只是把同一招用在封
/// 閉的形狀上：實心往內收半格、外面補一圈 alpha 收到 0 的裙擺，輪廓的位置和墨量都
/// 不變，但邊是連續的。
/// </remarks>
internal static class Silhouette
{
    /// <summary>
    /// 把一圈點沿著外法線推出 <paramref name="distance"/>（負數是往內縮）。
    /// </summary>
    /// <remarks>
    /// 不是把半徑加上一個值就算了：這裡的六角形被壓扁過（寬高不一樣），等比放大在
    /// 每一條邊上量到的垂直距離都不同，裙擺會一邊粗一邊細。
    ///
    /// 朝外是哪一邊用中心點判斷，不看繞向 —— 這個檔案裡兩個六角形一個順時針一個逆
    /// 時針，寫死法線方向的話其中一個的裙擺會長到形狀裡面去，把邊緣啃掉一圈。
    /// </remarks>
    internal static Vector2[] Offset(Vector2[] ring, Vector2 centre, float distance)
    {
        var moved = new Vector2[ring.Length];
        for (int i = 0; i < ring.Length; i++)
        {
            Vector2 back = EdgeNormal(ring[(i + ring.Length - 1) % ring.Length], ring[i], centre);
            Vector2 ahead = EdgeNormal(ring[i], ring[(i + 1) % ring.Length], centre);
            Vector2 bisector = back + ahead;
            if (bisector.sqrMagnitude < 1e-6f)
            {
                moved[i] = ring[i] + ahead * distance;
                continue;
            }

            bisector.Normalize();
            // 轉角要推得比邊上遠一點（除以夾角的餘弦），兩條邊各自量到的垂直距離才
            // 都剛好是 distance；直接推 distance 的話，角會被削掉一小塊。
            float lean = Mathf.Max(0.35f, Vector2.Dot(bisector, ahead));
            moved[i] = ring[i] + bisector * (distance / lean);
        }

        return moved;
    }

    /// <summary>實心的外緣接上一條收到透明的帶子。</summary>
    internal static void Ramp(VertexHelper vh, Vector2 fromInner, Vector2 toInner,
        Vector2 fromOuter, Vector2 toOuter, Color colour)
    {
        Color clear = new Color(colour.r, colour.g, colour.b, 0f);
        int first = vh.currentVertCount;
        Add(vh, fromInner, colour);
        Add(vh, toInner, colour);
        Add(vh, toOuter, clear);
        Add(vh, fromOuter, clear);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    private static Vector2 EdgeNormal(Vector2 from, Vector2 to, Vector2 centre)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude < 1e-6f) return Vector2.zero;
        Vector2 normal = new Vector2(direction.y, -direction.x).normalized;
        if (Vector2.Dot(normal, (from + to) * 0.5f - centre) < 0f) normal = -normal;
        return normal;
    }

    private static void Add(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
