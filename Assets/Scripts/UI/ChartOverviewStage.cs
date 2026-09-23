using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 譜面檢視器的舞台：把整首譜用**真的音符物件**排在 Z 軸上，用一台正交攝影機從
/// 正上方拍成一張 RenderTexture 給 UI 顯示。
/// </summary>
/// <remarks>
/// **為什麼是真的物件而不是重畫。** 音符的圖、材質、寬度規則、斷奏的寶石、滑奏的
/// 連結、長條的水晶身體加金框，全都長在 <see cref="NoteController"/> 和它的 shader
/// 裡。在 UI 上重畫一次的話，遊戲改了外觀、檢視器就會對不上 —— 而檢視器的用處正
/// 是「先看一眼等一下要打的東西」，對不上就沒有意義。
///
/// **為什麼不直接轉主攝影機。** 場上的音符只活在判定線前後約兩秒，判定線自己還會
/// 每幀跟著攝影機角度重算。這裡改成自己一座舞台、自己一台攝影機，主攝影機完全不動。
///
/// **隔離。** 舞台上的東西放在專用的 ChartPreview 圖層，舞台攝影機只拍這一層，主攝
/// 影機把這一層剔掉 —— 所以在遊戲中開檢視器，預覽的音符不會混進正在打的軌道裡。
///
/// **投影矩陣是自己給的。** 正交攝影機的 size 會同時決定橫向和縱向；這裡橫向要固
/// 定等於軌道寬度（音符的位置才對得上鍵盤），縱向要能縮放（看幾秒）。所以直接寫
/// <see cref="Camera.projectionMatrix"/>，兩軸各自給。
/// </remarks>
[DisallowMultipleComponent]
public sealed class ChartOverviewStage : MonoBehaviour
{
    /// <summary>專用圖層。舞台只拍這一層，主攝影機只剔這一層。</summary>
    public const string PreviewLayerName = "ChartPreview";

    private const int MaxNotes = 4000;          // 超長的譜面就畫到這裡為止
    private const int SpawnBudgetPerFrame = 48; // 一幀最多生幾顆，避免開啟時卡一下
    private const float NoteThicknessPixels = 13f;  // 音符在畫面上有多厚

    /// <summary>舞台整個搬到世界下方這麼遠的地方。</summary>
    /// <remarks>
    /// 只靠圖層隔離不夠保險：圖層名稱沒被 Unity 重新載入、或主攝影機剛好在那一刻
    /// 還沒出現，舞台就會出現在遊戲畫面裡（實測是判定線下方多出一片洋紅的預覽軌
    /// 道）。搬到主攝影機的遠裁面之外，就算圖層那一層失效也看不到。
    /// </remarks>
    private static readonly Vector3 StageOffset = new Vector3(0f, -6000f, 0f);

    private Camera stageCamera;
    private RenderTexture target;
    private Transform noteRoot;
    private Transform trackClone;
    private readonly List<NoteController> spawned = new List<NoteController>();
    private Coroutine buildRoutine;
    private int previewLayer = -1;
    private int mainCameraMaskBackup;
    private float appliedThickness;
    private float censusTimer;
    private Material trackMaterial;
    private Mesh quadMesh;

    /// <summary>底板用了什麼材質。畫面上看不出來的時候拿它來查。</summary>
    public string TrackMaterialInfo { get; private set; } = "(未建立)";
    private Camera maskedCamera;

    public RenderTexture Target => target;
    public float SpeedWorldUnits { get; private set; } = 30f;
    public float JudgmentZ { get; private set; }
    public float TrackWidth { get; private set; } = 105f;
    public int BuiltNotes => spawned.Count;
    public int TotalNotes { get; private set; }

    /// <summary>生失敗的顆數和第一個原因。少了音符的時候，這兩個值就是答案。</summary>
    public int FailedNotes { get; private set; }
    public string FirstFailure { get; private set; }

    /// <summary>場景裡有幾顆音符不是這座舞台生的（孤兒），以及第一顆住在哪。</summary>
    public int OrphanNotes { get; private set; }
    public string FirstOrphan { get; private set; }
    public bool IsBuilding => buildRoutine != null;

    public static ChartOverviewStage Create()
    {
        // 先掃掉任何還活著的舊舞台。它們理論上會自我銷毀，但只要有一顆漏掉，畫面
        // 上就會多出一整份譜面的音符 —— 而且看起來就像「多出來、遊戲裡沒有的音」。
        var stale = FindObjectsByType<ChartOverviewStage>(FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < stale.Length; i++)
        {
            if (stale[i] != null) DestroyImmediate(stale[i].gameObject);
        }

        var go = new GameObject("ChartOverviewStage");
        go.hideFlags = HideFlags.DontSave;
        return go.AddComponent<ChartOverviewStage>();
    }

    private void Awake()
    {
        previewLayer = LayerMask.NameToLayer(PreviewLayerName);
        // 圖層沒設好就退回 TransparentFX（1）。寧可畫錯一層，也不要整個看不到。
        if (previewLayer < 0) previewLayer = 1;
        gameObject.layer = previewLayer;

        noteRoot = new GameObject("Notes").transform;
        noteRoot.SetParent(transform, false);
        noteRoot.gameObject.layer = previewLayer;
        // DontSave 一定要整棵掛上：play mode 當掉時，Unity 還原場景會把沒有這個旗
        // 標的執行期物件留在編輯場景裡 —— 那正是「洋紅色的 TrackPreview 在 Hierarchy
        // 找不到、重開場景也還在」的成因。
        noteRoot.gameObject.hideFlags = HideFlags.DontSave;

        BuildCamera();
    }

    /// <summary>檢視器不在了就自我了斷。</summary>
    /// <remarks>
    /// 舞台上有上千顆音符和一台相機。只要有任何一條路徑忘了收（例如在檢視器開著的
    /// 時候直接開始遊戲），它就會一路活到遊戲裡 —— 實測就是判定線後方多出一片預覽
    /// 軌道。與其把每一條路徑都補齊，不如讓它自己確認「我還該不該在」。
    /// </remarks>
    private void Update()
    {
        var viewer = ChartOverviewViewer.Instance;
        if (viewer == null || !viewer.IsOpen)
        {
            Destroy(gameObject);
            return;
        }

        // 每半秒點名一次：場景裡的音符有幾顆不是這座舞台生的。
        //
        // 「清了還在」而且「只有 tap 留下」代表那些東西不在 Notes 底下 —— 點名會
        // 直接告訴我們它們掛在誰身上，不用再猜。
        censusTimer -= Time.unscaledDeltaTime;
        if (censusTimer <= 0f)
        {
            censusTimer = 0.5f;
            TakeCensus();
        }
    }

    /// <summary>點名用圖層認人，不是用元件。</summary>
    /// <remarks>
    /// 殘留的音符身上不一定還有 NoteController（元件被銷毀、GameObject 卻留著的路
    /// 徑是存在的），用元件去找就會什麼都找不到 —— 可是它照樣畫在畫面上。舞台是唯
    /// 一會用 ChartPreview 圖層的東西，所以「這一層上不屬於現任舞台的」就是殘留，
    /// 而且這個判準和它身上還剩什麼元件無關。
    /// </remarks>
    private void TakeCensus()
    {
        var all = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int orphans = 0;
        string first = null;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer note = all[i];
            if (note == null || note.gameObject.layer != previewLayer) continue;
            if (note.transform.IsChildOf(transform)) continue;
            orphans++;
            if (first == null)
            {
                string path = note.name;
                for (Transform t = note.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                first = $"{path} @ {note.transform.position}";
            }
            // 順手收掉。畫面上看得到的殘留沒有留著的理由。
            Transform top = note.transform;
            while (top.parent != null) top = top.parent;
            Destroy(top.gameObject);
        }
        OrphanNotes = orphans;
        FirstOrphan = first;
    }

    private void OnDestroy()
    {
        // 這個舊台是被 Destroy(gameObject) 直接掉的（ChartOverviewViewer），Clear()
        // 不一定跑得到；而生音符的協程是分幀做的，被打斷時它尾巴那行
        // 「把覆寫清掉」永遠不會執行。静態跨場景活著，於是選曲畫面那份譜
        // 就一路進到遊戲裡，滑奧全部去別份譜找下一個節點。
        NoteController.PreviewChartOverride = null;
        RestoreMainCameraMask();
        if (target != null)
        {
            if (stageCamera != null) stageCamera.targetTexture = null;
            target.Release();
            Destroy(target);
            target = null;
        }
    }

    private void BuildCamera()
    {
        var camGo = new GameObject("StageCamera");
        camGo.transform.SetParent(transform, false);
        camGo.layer = previewLayer;
        camGo.hideFlags = HideFlags.DontSave;
        stageCamera = camGo.AddComponent<Camera>();
        stageCamera.orthographic = true;
        stageCamera.cullingMask = 1 << previewLayer;
        stageCamera.clearFlags = CameraClearFlags.SolidColor;
        // 軌道的底色：和遊戲裡那條深色跑道同一個調，不是純黑。
        stageCamera.backgroundColor = new Color(0.045f, 0.040f, 0.055f, 1f);
        stageCamera.nearClipPlane = 0.1f;
        stageCamera.farClipPlane = 400f;
        stageCamera.allowHDR = false;
        stageCamera.allowMSAA = false;
        stageCamera.useOcclusionCulling = false;
        // URP：明講這是一台 Base 相機、不要後處理。不設定的話它會沿用專案的
        // volume 與後處理堆疊，那在一台只拍一層的離屏相機上沒有意義，也是渲染出
        // 問題時最難查的一段。
        var urp = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        urp.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
        urp.renderPostProcessing = false;
        urp.renderShadows = false;
        urp.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
        urp.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
        // URP 不支援手動呼叫 Camera.Render()，所以攝影機自己開著、直接渲染到
        // RenderTexture；有 targetTexture 就不會畫到螢幕上。檢視器關掉時再關它。
        stageCamera.enabled = false;
        stageCamera.depth = -50f;
        camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 正上方往下看
    }

    /// <summary>主攝影機不要拍到舞台。開一次、關的時候還原。</summary>
    private void ExcludeFromMainCamera()
    {
        Camera main = Camera.main;
        if (main == null || maskedCamera == main) return;
        RestoreMainCameraMask();
        maskedCamera = main;
        mainCameraMaskBackup = main.cullingMask;
        main.cullingMask &= ~(1 << previewLayer);
    }

    private void RestoreMainCameraMask()
    {
        if (maskedCamera == null) return;
        maskedCamera.cullingMask = mainCameraMaskBackup;
        maskedCamera = null;
    }

    // ------------------------------------------------------------------
    // 建立
    // ------------------------------------------------------------------

    /// <summary>把整首譜排上舞台。會分幾幀做完。</summary>
    public void Build(Chart chart)
    {
        Clear();
        if (chart == null || chart.notes == null || chart.notes.Count == 0) return;

        NoteSpawner spawner = ResolveSpawner();
        SpeedWorldUnits = spawner != null && spawner.speed > 0.01f ? spawner.speed : 30f;
        TrackWidth = PianoVisualLayout.ResolveTrackWidth(
            spawner != null ? spawner.trackTransform : null);
        // 音符自己也是這樣找判定線的（NoteController.Initialize），跟著讀同一個值，
        // 舞台的座標才會和音符對得上。
        GameObject line = GameObject.Find("JudgmentLine");
        JudgmentZ = line != null ? line.transform.position.z : 0f;

        ExcludeFromMainCamera();
        CloneTrack(spawner, chart);

        TotalNotes = Mathf.Min(chart.notes.Count, MaxNotes);
        buildRoutine = StartCoroutine(BuildRoutine(chart, spawner));
    }

    private IEnumerator BuildRoutine(Chart chart, NoteSpawner spawner)
    {
        GameObject prefab = spawner != null ? spawner.notePrefab2D : null;
        if (prefab == null)
        {
            buildRoutine = null;
            yield break;
        }

        // 滑奏要靠這份譜找下一個節點；選曲畫面的 GameManager 手上還沒有譜。
        NoteController.PreviewChartOverride = chart;
        Vector3 noteScale = spawner != null ? ResolveNoteScale(spawner) : new Vector3(1f, 0.1f, 0.2f);

        int budget = 0;
        for (int i = 0; i < TotalNotes; i++)
        {
            NoteData note = chart.notes[i];
            if (note == null) continue;

            GameObject instance = Instantiate(prefab, noteRoot);
            instance.hideFlags = HideFlags.DontSave;
            instance.transform.localScale = noteScale;
            SetLayerRecursively(instance.transform, previewLayer);
            var controller = instance.GetComponent<NoteController>();
            if (controller == null)
            {
                Destroy(instance);
                continue;
            }

            try
            {
                controller.ConfigureStaticPreview(note, spawner, SpeedWorldUnits, StageOffset);
                if (appliedThickness > 0.0001f) controller.SetPreviewThickness(appliedThickness);
                // 生出來的子物件（RuntimeSprite、HoldTail、寶石）也要在專用圖層上。
                SetLayerRecursively(instance.transform, previewLayer);
                spawned.Add(controller);
            }
            catch (System.Exception error)
            {
                FailedNotes++;
                if (string.IsNullOrEmpty(FirstFailure))
                    FirstFailure = $"{error.GetType().Name}: {error.Message}";
                Destroy(instance);
            }

            if (++budget >= SpawnBudgetPerFrame)
            {
                budget = 0;
                yield return null;
            }
        }

        NoteController.PreviewChartOverride = null;
        buildRoutine = null;
    }

    /// <summary>NoteSpawner 生音符前會做的縮放補正，這裡照抄一次。</summary>
    private Vector3 ResolveNoteScale(NoteSpawner spawner)
    {
        Vector3 parentScale = noteRoot != null ? noteRoot.lossyScale : Vector3.one;
        Vector3 basis = spawner.defaultNoteScale;
        return new Vector3(
            parentScale.x != 0f ? basis.x / parentScale.x : basis.x,
            parentScale.y != 0f ? basis.y / parentScale.y : basis.y,
            parentScale.z != 0f ? basis.z / parentScale.z : basis.z);
    }

    /// <summary>鋪一塊軌道當底，用軌道自己的材質。</summary>
    /// <remarks>
    /// 不複製整顆軌道物件。Instantiate 會把上面的腳本一起複製、而且複製出來的當下
    /// 就跑 Awake —— 那些腳本會去動共用材質、註冊全域狀態，複本被銷毀時又把別人的
    /// 東西一起帶走（實測：關掉檢視器之後鍵盤下方變成洋紅色的「找不到材質」）。這
    /// 裡只借它的 sharedMaterial，自己鋪一塊面片，乾淨得多。
    /// </remarks>
    private void CloneTrack(NoteSpawner spawner, Chart chart)
    {
        Transform source = spawner != null ? spawner.trackTransform : null;
        if (source == null) return;

        Renderer sourceRenderer = source.GetComponentInChildren<Renderer>(true);
        Material sourceMaterial = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;

        // 不用 GameObject.CreatePrimitive：它會去載內建管線的 Default-Material（在
        // URP 底下就是那片洋紅），還附帶一顆 Collider。自己組一片四邊形，連內建資源
        // 都不碰。
        GameObject clone = new GameObject("TrackPreview", typeof(MeshFilter), typeof(MeshRenderer));
        clone.transform.SetParent(transform, false);
        clone.hideFlags = HideFlags.DontSave;
        clone.GetComponent<MeshFilter>().sharedMesh = GetOrCreateQuad();
        var renderer = clone.GetComponent<MeshRenderer>();
        if (sourceMaterial != null && sourceMaterial.shader != null && sourceMaterial.shader.isSupported)
        {
            trackMaterial = new Material(sourceMaterial) { hideFlags = HideFlags.DontSave };
            TrackMaterialInfo = sourceMaterial.name + " / " + sourceMaterial.shader.name;
        }
        else
        {
            // 連軌道的材質都不能用就退回一個保證畫得出來的深色 —— 寧可素一點，也
            // 不要一片洋紅。
            Shader fallback = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (fallback == null)
            {
                // 沒有任何能用的 shader：不要鋪。new Material(null) 畫出來就是洋紅。
                TrackMaterialInfo = "沒有可用的底板 shader，略過底板";
                Destroy(clone);
                return;
            }
            trackMaterial = new Material(fallback) { hideFlags = HideFlags.DontSave };
            trackMaterial.color = new Color(0.08f, 0.07f, 0.09f, 1f);
            TrackMaterialInfo = "fallback " + (fallback != null ? fallback.name : "null") +
                " (來源 " + (sourceMaterial != null ? sourceMaterial.name : "null") + ")";
        }
        renderer.sharedMaterial = trackMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        SetLayerRecursively(clone.transform, previewLayer);

        // 整首攤開有多長，軌道就拉多長（原本只有 1000 單位）。
        float lengthWorld = ((chart.music_finish_time_msec > 0
            ? chart.music_finish_time_msec
            : 240000f) / 1000f) * SpeedWorldUnits;
        // 軌道從判定線稍前一點開始，往譜面的方向鋪到結尾之後 —— 不是以曲長的一半
        // 為中心。置中的話它會往判定線後方多伸出半條，在遊戲畫面裡就是鍵盤底下那
        // 一片多出來的東西。
        float startZ = JudgmentZ - 20f;
        float endZ = JudgmentZ + lengthWorld + 60f;
        float spanZ = Mathf.Max(1f, endZ - startZ);
        // 自己的四邊形是 1×1、躺在 XZ 平面上，所以縮放直接就是「多寬 × 多長」。
        clone.transform.localScale = new Vector3(TrackWidth, 1f, spanZ);
        Vector3 position = source.position + StageOffset;
        position.z = (startZ + endZ) * 0.5f;
        clone.transform.position = position;
        trackClone = clone.transform;
    }

    /// <summary>躺在 XZ 平面上的 1×1 四邊形，法線朝上。</summary>
    private Mesh GetOrCreateQuad()
    {
        if (quadMesh != null) return quadMesh;
        quadMesh = new Mesh { name = "ChartOverviewQuad", hideFlags = HideFlags.DontSave };
        quadMesh.SetVertices(new List<Vector3>
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
        });
        quadMesh.SetUVs(0, new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
        });
        quadMesh.SetNormals(new List<Vector3>
        {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up
        });
        quadMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        quadMesh.RecalculateBounds();
        return quadMesh;
    }

    private static NoteSpawner ResolveSpawner()
    {
        if (NoteSpawner.Instance != null) return NoteSpawner.Instance;
        if (GameManager.Instance != null && GameManager.Instance.NoteSpawner != null)
            return GameManager.Instance.NoteSpawner;
        return FindFirstObjectByType<NoteSpawner>(FindObjectsInactive.Include);
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
    }

    public void Clear()
    {
        if (buildRoutine != null)
        {
            StopCoroutine(buildRoutine);
            buildRoutine = null;
        }
        NoteController.PreviewChartOverride = null;
        // 掃整棵子樹，不是只掃 spawned 名單。
        //
        // 名單裡存的是 NoteController；只要有任何一條路徑把那個元件弄掉（音符自己
        // 的 ReleaseOrDestroy 就會），名單裡那一格就變成 null，GameObject 卻還活
        // 著 —— 那就是一顆再也沒人收的音符，下一次開別的譜面時它還在畫面上。
        if (noteRoot != null)
        {
            for (int i = noteRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = noteRoot.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }
        }
        spawned.Clear();
        TotalNotes = 0;
        FailedNotes = 0;
        FirstFailure = null;
        appliedThickness = 0f;
        if (trackClone != null)
        {
            Destroy(trackClone.gameObject);
            trackClone = null;
        }
        if (trackMaterial != null)
        {
            Destroy(trackMaterial);
            trackMaterial = null;
        }
        if (quadMesh != null)
        {
            Destroy(quadMesh);
            quadMesh = null;
        }
        RestoreMainCameraMask();
        if (stageCamera != null) stageCamera.enabled = false;
    }

    // ------------------------------------------------------------------
    // 拍照
    // ------------------------------------------------------------------

    /// <summary>
    /// 把「播放頭在 viewMs、畫面高度等於 visibleMs 毫秒」這個視窗拍成一張圖。
    /// </summary>
    /// <param name="playheadShare">播放頭在畫面上的高度比例（0 = 最底下）。</param>
    public RenderTexture Render(float viewMs, float visibleMs, float playheadShare,
        int pixelWidth, int pixelHeight)
    {
        if (stageCamera == null) return null;
        EnsureTarget(pixelWidth, pixelHeight);
        if (target == null) return null;

        float halfHeightWorld = Mathf.Max(0.01f, (visibleMs / 1000f) * SpeedWorldUnits * 0.5f);
        // 音符在畫面上維持固定厚度：縮放變了就換算成新的世界厚度重設一次。
        ApplyNoteThickness(halfHeightWorld * 2f / Mathf.Max(1, pixelHeight) * NoteThicknessPixels);
        float halfWidthWorld = Mathf.Max(0.01f, TrackWidth * 0.5f);
        float playheadZ = JudgmentZ + (viewMs / 1000f) * SpeedWorldUnits;
        float centreZ = playheadZ + halfHeightWorld * (1f - 2f * Mathf.Clamp01(playheadShare));

        stageCamera.transform.position = new Vector3(0f, 120f, centreZ) + StageOffset;
        stageCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        // 橫向要剛好是整條軌道、縱向要跟著縮放走。
        //
        // 不覆寫 projectionMatrix：URP 會因為那顆自訂矩陣讓整條管線出問題（實測是
        // 連主畫面都變成洋紅的錯誤材質）。改成設 aspect —— 正交相機的水平半寬就是
        // orthographicSize × aspect，同樣能把兩軸分開，而且走的是引擎本來就支援的路。
        stageCamera.ResetProjectionMatrix();
        stageCamera.orthographicSize = halfHeightWorld;
        stageCamera.aspect = halfWidthWorld / halfHeightWorld;
        stageCamera.targetTexture = target;
        stageCamera.enabled = true;
        return target;
    }

    /// <summary>把每一顆音符的厚度設成同一個世界值（只在真的變了的時候跑）。</summary>
    private void ApplyNoteThickness(float worldThickness)
    {
        if (worldThickness <= 0.0001f) return;
        if (Mathf.Abs(worldThickness - appliedThickness) < appliedThickness * 0.04f) return;
        appliedThickness = worldThickness;
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null) spawned[i].SetPreviewThickness(worldThickness);
        }
    }

    private void EnsureTarget(int width, int height)
    {
        width = Mathf.Clamp(width, 64, 4096);
        height = Mathf.Clamp(height, 64, 4096);
        if (target != null && target.width == width && target.height == height) return;

        if (target != null)
        {
            if (stageCamera != null) stageCamera.targetTexture = null;
            target.Release();
            Destroy(target);
        }
        target = new RenderTexture(width, height, 24,
            RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Default)
        {
            name = "ChartOverviewTarget",
            antiAliasing = 1,
            useMipMap = false,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        target.Create();
    }
}
