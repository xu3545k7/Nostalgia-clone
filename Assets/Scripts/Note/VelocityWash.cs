using UnityEngine;

/// <summary>
/// 鋪在跑道上的一層淡淡的光，告訴玩家接下來哪裡要彈強、哪裡要彈弱。
/// </summary>
/// <remarks>
/// **只講一件事：譜面在這裡要求強還是弱。**
///
/// 上一版在同一片色場裡講了三件事：連續的強弱程度（−1..+1）、踏板（變暖、加飽和、
/// 提亮），還有欄杆外側的踏板溢出。三種訊號疊在同一塊地板上，玩家得先拆開才讀得
/// 懂，而踏板本來就有自己的紫色踏板音符。現在踏板不進色場，強弱也只分三種：
///
/// * 強（<see cref="VelocityBands.Band.Loud"/>）→ 紅
/// * 弱（<see cref="VelocityBands.Band.Soft"/>）→ 藍
/// * 普通 → 什麼都不畫
///
/// 和判定用的是同一個分界：演奏會模式只評被標成強／弱的音符，地板上亮起來的也正
/// 好就是那些地方。
///
/// **為什麼放在軌道上、而且分欄。** 強弱是樂句的性質，一段強奏整片亮起來比幾百顆
/// 音符各自掛一個記號好讀。這條軌道的 x 就是音高，所以左右分成十欄：右手旋律唱出
/// 來、左手伴奏壓著的時候，兩邊各亮各的，不會互相平均成什麼都沒有。
///
/// **網格不動，只改頂點色。** 每一幀照時鐘去問「這個位置對應的歌曲時間是強是
/// 弱」。捲動幾何需要和音符同一個時鐘、速度、錨點，三者任一變了就會漂；顏色是每幀
/// 從時鐘讀的，漂不了。
/// </remarks>
public sealed class VelocityWash : MonoBehaviour
{
    /// <summary>色場沿著歌曲的取樣間隔（ms）。</summary>
    private const float StepMs = 50f;

    /// <summary>一顆音符的光往前後各延伸多久（ms）。</summary>
    private const float SpreadMs = 300f;

    /// <summary>鍵盤寬度分成幾欄。十欄，每欄略小於一個八度。</summary>
    private const int Columns = 10;

    /// <summary>隔壁欄滲過來的比例。只為了讓欄與欄之間沒有直線，不是為了把光傳出去。</summary>
    private const float Bleed = 0.3f;

    /// <summary>沿跑道的切片數。越多越平滑，成本不變。</summary>
    private const int Sections = 96;

    /// <summary>色場蓋住多長的跑道，以秒計。</summary>
    private const float ReachSeconds = 2.6f;

    /// <summary>最濃的程度。它是光，不是油漆。</summary>
    private const float MaxAlpha = 0.26f;

    /// <summary>弱的比強的淡：輕聲應該看起來是「光比較少」，不是同樣強度的另一種顏色。</summary>
    private const float SoftShare = 0.62f;

    // 兩者都壓暗：暗地板上疊亮色會把地板往白推，看起來像浮在軌道上的霧；暗色是把地
    // 板染深，顏色才像屬於地板。紅往洋紅偏、不往橘偏，離踏板的琥珀色越遠越好。
    private static readonly Color Loud = new Color(0.74f, 0.055f, 0.045f, 1f);
    private static readonly Color Soft = new Color(0.045f, 0.235f, 0.80f, 1f);

    /// <summary>−1 弱 .. 0 無 .. +1 強，索引為 slot * Columns + column。</summary>
    private float[] field;
    private int slots;

    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private Vector3[] vertices;
    private Color[] colours;

    private Transform judgmentLine;
    private NoteSpawner spawner;
    private Chart pending;
    private bool reported;

    /// <summary>把譜面讀成色場。沒有力度資料的譜面什麼都不畫。</summary>
    public void Prepare(Chart chart, NoteSpawner noteSpawner, Transform judgment)
    {
        spawner = noteSpawner;
        judgmentLine = judgment;
        field = null;
        pending = chart;
        reported = false;

        // 一般模式不畫：那裡不評強弱，這片色場就只是一層會動的顏色蓋在譜面上。
        // 音符外殼（NoteController.UpdateVelocityHalo）用的是同一條規則。
        if (!RecitalWanted()) { Hide(); return; }

        // 分界是 GameManager 解析譜面時設定的，和這裡誰先誰後不保證。還沒好就先擱
        // 著，LateUpdate 會再試。
        if (!VelocityBands.Measured || chart == null || chart.notes == null) { Hide(); return; }

        int lastMs = 0;
        for (int i = 0; i < chart.notes.Count; i++)
        {
            NoteData note = chart.notes[i];
            if (note != null && note.startTime > lastMs) lastMs = note.startTime;
        }
        if (lastMs <= 0) { Hide(); return; }

        float trackWidth = PianoVisualLayout.ResolveTrackWidth(
            spawner != null ? spawner.trackTransform : null);
        float half = Mathf.Max(0.0001f, trackWidth * 0.5f);

        // 最後一顆音符之後多留一段，讓它的光有地方淡掉，而不是在資料的盡頭被切斷。
        slots = Mathf.CeilToInt((lastMs + SpreadMs) / StepMs) + 2;
        var signed = new float[slots * Columns];
        var presence = new float[slots * Columns];

        for (int i = 0; i < chart.notes.Count; i++)
        {
            NoteData note = chart.notes[i];
            if (note == null || note.velocity <= 0) continue;

            int slot = Mathf.Clamp(Mathf.RoundToInt(note.startTime / StepMs), 0, slots - 1);
            float centreX = PianoVisualLayout.ResolveCenterX(note, trackWidth);
            float across = Mathf.Clamp01((centreX + half) / (half * 2f));
            int column = Mathf.Clamp((int)(across * Columns), 0, Columns - 1);
            int cell = slot * Columns + column;

            // 普通的音符也要算進「這裡有東西」：一段強奏裡夾著幾顆普通的，顏色該被
            // 它們沖淡，而不是當它們不存在。
            presence[cell] += 1f;
            VelocityBands.Band band = VelocityBands.Classify(note.velocity);
            if (band == VelocityBands.Band.Loud) signed[cell] += 1f;
            else if (band == VelocityBands.Band.Soft) signed[cell] -= 1f;
        }

        field = Spread(signed, presence, slots);
        BuildMesh(trackWidth);
    }

    /// <summary>
    /// 每一顆被標記的音符在時間上攤開成一個三角形的光，疊起來就是色場。
    /// </summary>
    /// <remarks>
    /// **分母有下限 1，這是「會被清掉」的關鍵。**
    ///
    /// 上一版用的是純粹的加權平均：強弱總和 ÷ 權重總和。只要窗口裡還有任何一顆音
    /// 符，不管它離多遠、權重多小，平均出來都是那顆音符的**全強度** —— 所以一顆強
    /// 音的紅光會完整地撐滿前後 300ms 和隔壁欄，直到有別的音符進來蓋掉它，或是它
    /// 離開窗口時突然歸零。音符變少，顏色卻不會變淡。
    ///
    /// 分母至少是 1 之後：
    ///
    /// * 密集的樂段（權重總和 ≥ 1）→ 還是平均，強音夾普通音會被沖淡。
    /// * 稀疏的地方（權重總和 &lt; 1）→ 等於直接加總，離音符越遠越淡，沒有音符就是 0。
    ///
    /// 一顆單獨的強音因此是一團在它自己位置最亮、往前後自然淡掉的光。
    /// </remarks>
    private static float[] Spread(float[] signed, float[] presence, int slotCount)
    {
        int reach = Mathf.Max(1, Mathf.RoundToInt(SpreadMs / StepMs));
        var result = new float[slotCount * Columns];

        for (int slot = 0; slot < slotCount; slot++)
        {
            for (int column = 0; column < Columns; column++)
            {
                float total = 0f;
                float weight = 0f;
                int from = Mathf.Max(0, slot - reach);
                int to = Mathf.Min(slotCount - 1, slot + reach);
                for (int s = from; s <= to; s++)
                {
                    float timeWeight = 1f - Mathf.Abs(s - slot) / (float)(reach + 1);
                    for (int c = column - 1; c <= column + 1; c++)
                    {
                        if (c < 0 || c >= Columns) continue;
                        int cell = s * Columns + c;
                        if (presence[cell] <= 0f) continue;
                        float w = timeWeight * (c == column ? 1f : Bleed);
                        total += signed[cell] * w;
                        weight += presence[cell] * w;
                    }
                }
                result[slot * Columns + column] = Mathf.Clamp(total / Mathf.Max(1f, weight), -1f, 1f);
            }
        }
        return result;
    }

    /// <summary>一條帶子：軌道兩側各一個頂點，中間 <see cref="Columns"/> 個。</summary>
    private void BuildMesh(float trackWidth)
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "VelocityWash", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            var filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = VelocityHalo.Material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            // 在音符和軌道之間。它是地上的光，不是空中的東西。
            meshRenderer.sortingOrder = -50;
        }

        float half = trackWidth * 0.5f;
        float reach = Mathf.Max(1f, (spawner != null ? spawner.speed : 30f) * ReachSeconds);

        int perRow = Columns + 2;
        int rows = Sections + 1;
        vertices = new Vector3[rows * perRow];
        colours = new Color[rows * perRow];

        for (int r = 0; r < rows; r++)
        {
            float z = reach * r / Sections;
            int v = r * perRow;
            vertices[v] = new Vector3(-half, 0f, z);
            for (int c = 0; c < Columns; c++)
                vertices[v + 1 + c] = new Vector3(-half + trackWidth * (c + 0.5f) / Columns, 0f, z);
            vertices[v + perRow - 1] = new Vector3(half, 0f, z);
        }

        int strips = perRow - 1;
        var triangles = new int[Sections * strips * 6];
        int t = 0;
        for (int r = 0; r < Sections; r++)
        {
            int a = r * perRow;
            int b = a + perRow;
            for (int s = 0; s < strips; s++)
            {
                triangles[t++] = a + s;
                triangles[t++] = b + s;
                triangles[t++] = b + s + 1;
                triangles[t++] = a + s;
                triangles[t++] = b + s + 1;
                triangles[t++] = a + s + 1;
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors = colours;
        mesh.triangles = triangles;
        mesh.bounds = new Bounds(new Vector3(0f, 0f, reach * 0.5f), new Vector3(trackWidth, 1f, reach));
        meshRenderer.enabled = true;
    }

    private void Hide()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
    }

    /// <summary>演奏會模式才畫。設定隨時可改，所以每一幀都重問。</summary>
    private static bool RecitalWanted()
    {
        try
        {
            var settings = SettingsManager.Instance;
            return settings != null && settings.RecitalModeInPlay;
        }
        catch { return false; }
    }

    private void LateUpdate()
    {
        using (HitchProbe.Measure("velocityWash")) LateUpdateCore();
    }

    private void LateUpdateCore()
    {
        // 歌曲進行中把演奏會模式關掉（例如在設定的譜面預覽裡切換），色場要跟著收
        // 掉。以前只在載入譜面時問一次，關掉之後那片顏色會一直留在軌道上。
        if (!RecitalWanted())
        {
            Hide();
            return;
        }

        if (field == null)
        {
            if (pending == null || !VelocityBands.Measured) return;
            Prepare(pending, spawner, judgmentLine);
            if (field == null) return;
        }
        if (mesh == null || meshRenderer == null) return;
        meshRenderer.enabled = true;

        Conductor conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
        if (conductor == null) return;

        float songMs = (float)conductor.effectiveSongPosition;
        float speed = Mathf.Max(0.01f, spawner != null ? spawner.speed : 30f);
        float reach = speed * ReachSeconds;

        if (judgmentLine != null)
        {
            // 稍微高於音符的平面：低於會沉進軌道地板被深度測試切掉。前後關係交給
            // sortingOrder，那個和高度無關。
            transform.position = new Vector3(
                spawner != null && spawner.trackTransform != null
                    ? spawner.trackTransform.position.x : transform.position.x,
                judgmentLine.position.y + 0.005f,
                judgmentLine.position.z);
        }

        int perRow = Columns + 2;
        int rows = vertices.Length / perRow;

        for (int r = 0; r < rows; r++)
        {
            float z = reach * r / Sections;
            float ms = songMs + z / speed * 1000f;

            // 遠端收掉，近端也收一點：色場沒有起點和終點，只有濃淡。
            float distance = r / (float)Sections;
            float fade = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.62f, 1f, distance))
                       * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.06f, distance));

            int v = r * perRow;
            for (int c = 0; c < Columns; c++)
            {
                float level = Sample(ms, c);
                bool loud = level >= 0f;
                float strength = Mathf.Abs(level) * fade * MaxAlpha * (loud ? 1f : SoftShare);
                Color band = loud ? Loud : Soft;
                colours[v + 1 + c] = new Color(band.r, band.g, band.b, strength);
            }

            // 軌道邊緣沿用最外側那一欄的顏色，色場才填滿整條軌道。
            colours[v] = colours[v + 1];
            colours[v + perRow - 1] = colours[v + Columns];
        }

        mesh.colors = colours;

        if (!reported)
        {
            reported = true;
            Debug.Log($"[VelocityWash] pos={transform.position} reach={reach:F1} speed={speed:F1} " +
                $"rows={rows} cols={Columns} slots={slots} order={meshRenderer.sortingOrder} material=" +
                $"{(meshRenderer.sharedMaterial != null ? "ok" : "MISSING")}");
        }
    }

    /// <summary>
    /// 某個歌曲時間、某一欄的強弱。資料範圍以外是 0。
    /// </summary>
    /// <remarks>
    /// 以前在資料範圍外會**沿用邊界那一格的值**：前奏期間（歌曲時間 &lt; 0）整條跑
    /// 道都讀第一格，第一顆是強音的話，開場幾秒整條都是紅的。範圍外現在是空的，
    /// 開頭那一格往前在一個光暈的長度內淡出。
    /// </remarks>
    private float Sample(float ms, int column)
    {
        if (field == null || slots <= 0) return 0f;
        if (ms < 0f) return field[column] * Mathf.Clamp01(1f + ms / SpreadMs);
        float slot = ms / StepMs;
        int i = (int)slot;
        if (i >= slots - 1) return 0f;
        float a = field[i * Columns + column];
        float b = field[(i + 1) * Columns + column];
        return Mathf.Lerp(a, b, slot - i);
    }
}
