using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
    private const float Bevel = 0.30f;

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
        float width = content + height * Bevel * 2f + 44f;

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

        PlaqueGraphic plaque = stripObject.GetComponent<PlaqueGraphic>();
        plaque.raycastTarget = false;
        plaque.Bevel = Bevel;

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
            slot.button.onClick.AddListener(() => Select(tier));
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
        record.enableKerning = false;
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
        slot.gem.Selected = present && selected;
        slot.level.text = present ? chart.difficultyLevel.ToString() : "?";
        // 沒被選到的也要壓暗，否則「現在看的是哪一格」得靠寶石自己的亮度去猜。
        float ink = present ? (selected ? 1f : 0.5f) : 0.5f;
        slot.level.color = new Color(1f, 1f, 1f, ink);
        if (slot.button != null) slot.button.interactable = present;
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
        button.transition = Selectable.Transition.None;

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

        return new Slot { root = root, gem = gem, level = level, button = button };
    }
}

/// <summary>
/// The hexagonal plate the stones sit on: a dark field with a plain gold border.
/// </summary>
/// <remarks>
/// A border and not ornament. The plaque already carries five coloured stones,
/// five captions, five grades and a score; scrollwork around that is one more
/// thing competing for the same glance. Two clean rules say "this is a panel"
/// and then get out of the way.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class PlaqueGraphic : MaskableGraphic
{
    [SerializeField] private Color field = new Color(0.10f, 0.055f, 0.035f, 0.93f);
    [SerializeField] private Color gilt = new Color(0.92f, 0.78f, 0.44f, 1f);

    private float bevel = 0.30f;

    public float Bevel
    {
        get => bevel;
        set
        {
            if (Mathf.Approximately(bevel, value)) return;
            bevel = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 8f || r.height <= 8f) return;

        Vector2[] outer = Hexagon(r, 0f);
        Vector2[] inner = Hexagon(r, 5f);
        Vector2[] core = Hexagon(r, 9f);

        Fan(vh, r.center, outer, new Color(gilt.r, gilt.g, gilt.b, 0.95f));
        Fan(vh, r.center, inner, new Color(field.r * 0.6f, field.g * 0.6f, field.b * 0.6f, 0.98f));
        Fan(vh, r.center, core, field);

        // 內側再一條細金線。兩條規線之間留一段暗場，邊框才有厚度。
        Vector2[] ruleOuter = Hexagon(r, 14f);
        Vector2[] ruleInner = Hexagon(r, 15.6f);
        for (int i = 0; i < ruleOuter.Length; i++)
            AddQuad(vh, ruleOuter[i], ruleOuter[(i + 1) % ruleOuter.Length],
                ruleInner[(i + 1) % ruleInner.Length], ruleInner[i],
                new Color(gilt.r, gilt.g, gilt.b, 0.55f));
    }

    /// <summary>A flat-top hexagon inset from the rect by <paramref name="inset"/>.</summary>
    private Vector2[] Hexagon(Rect r, float inset)
    {
        float halfWidth = r.width * 0.5f - inset;
        float halfHeight = r.height * 0.5f - inset;
        float cut = Mathf.Min(halfWidth * 0.9f, r.height * bevel);
        return new[]
        {
            new Vector2(r.center.x - halfWidth, r.center.y),
            new Vector2(r.center.x - halfWidth + cut, r.center.y + halfHeight),
            new Vector2(r.center.x + halfWidth - cut, r.center.y + halfHeight),
            new Vector2(r.center.x + halfWidth, r.center.y),
            new Vector2(r.center.x + halfWidth - cut, r.center.y - halfHeight),
            new Vector2(r.center.x - halfWidth + cut, r.center.y - halfHeight),
        };
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

    private Color tint = new Color(0.62f, 0.32f, 0.84f, 1f);
    private string caption = string.Empty;
    private bool muted;
    private bool selected = true;
    private TextMeshProUGUI captionLabel;

    /// <summary>
    /// The stone whose record is on show. The others are turned down.
    /// </summary>
    /// <remarks>
    /// Dimmed rather than outlined: an outline is one more shape on a plaque
    /// that already has five stones and a score, and brightness is the one
    /// channel nothing else here is using.
    /// </remarks>
    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
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

        float alpha = muted ? 0.40f : selected ? 1f : 0.58f;
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
    private void DrawBezel(VertexHelper vh, Vector2 centre, float halfWidth, float halfHeight,
        float alpha)
    {
        Vector2[] outer = Hexagon(centre, halfWidth, halfHeight, 0f);
        Vector2[] inner = Hexagon(centre, halfWidth * 0.88f, halfHeight * 0.88f, 0f);
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
        float arm = girdleW * (selected ? 0.42f : 0.26f);
        Color hot = new Color(1f, 1f, 0.98f, (selected ? 0.9f : 0.5f) * alpha);
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

        captionLabel.text = caption;
        Color ink = Color.Lerp(tint, Color.white, 0.86f);
        ink.a = muted ? 0.40f : selected ? 1f : 0.55f;
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
