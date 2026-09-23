using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Effects
{
    /// <summary>
    /// 讓背景的顏色跟著音樂的調性走：底色來自封面，色相隨調性中心偏移。
    /// </summary>
    /// <remarks>
    /// **為什麼不跟拍子閃。** 踏板那邊已經下過同樣的結論：踏板 90% 時間踩著，所以
    /// 常態在幾秒內就變成新的背景、不再攜帶資訊。週期性閃爍是同一件事，而且它就閃在
    /// 判定線那條全畫面最擠的帶子旁邊。這裡改成每 6 秒上下換一次（見
    /// <see cref="HarmonyTimeline"/> 的實測），慢到不會搶戲，但和音樂真的翻頁的時刻對齊。
    ///
    /// 顏色是**從時鐘推出來的**，不是靠事件推進：拉時間軸、暫停、倒帶顏色都不會跑掉。
    /// 和踏板視覺同一個做法。
    /// </remarks>
    [DefaultExecutionOrder(50)]
    public class BackgroundHarmonyDriver : MonoBehaviour
    {
        private const string RootName = "~BackgroundHarmonyDriver";

        public enum BackdropMode
        {
            /// <summary>只染色現有的封面背景（最保守，看得到但不改變原本的畫面結構）。</summary>
            TintCover = 0,
            /// <summary>把封面背景換成漸層色場。封面仍然留在角落的 songImage。</summary>
            ReplaceWithGradient = 1,
            /// <summary>完全不碰 UI，只驅動天空盒（封面若蓋滿畫面就等於看不到）。</summary>
            SkyboxOnly = 2,
        }

        [Tooltip("背景要怎麼吃這套配色。看不到效果時先確認這裡不是 SkyboxOnly。")]
        [SerializeField] private BackdropMode mode = BackdropMode.TintCover;

        [Tooltip("五度圈每走一步轉幾度色相。10 度時實測整首歌的偏移中位 20 度、最大 60 度。")]
        [SerializeField, Range(0f, 30f)] private float degreesPerFifth = 10f;

        [Tooltip("換色的滑行時間（秒）。要慢到讀起來是『房間變色了』而不是『閃了一下』。")]
        [SerializeField, Range(0.1f, 6f)] private float glideSeconds = 2.5f;

        [Tooltip("染色模式下，封面被染的程度。0 = 原樣，1 = 完全變成調性色。")]
        [SerializeField, Range(0f, 1f)] private float tintStrength = 0.45f;

        // 軌道玻璃化的值**不放在這裡**。這個物件是執行時才生出來的，Inspector 上的欄位
        // 既找不到、每次播放又重置——等於做了一個誰都調不到的旋鈕。真正的家在
        // SettingsManager（存檔、有 UI、和其他視覺設定放在一起），這裡只負責套用。
        private float trackGlass;
        private float glassFloorOpacity = 0.55f;

        // 材質屬性 ID。名稱對應 Nostalgia/Player View Marble Track 的 Properties。
        private static readonly int SkyBottomId = Shader.PropertyToID("_SkyBottom");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_SkyHorizon");
        private static readonly int SkyTopId = Shader.PropertyToID("_SkyTop");
        private static readonly int GlassId = Shader.PropertyToID("_Glass");
        private static readonly int GlassMaxId = Shader.PropertyToID("_GlassMax");
        private static readonly int JudgmentZId = Shader.PropertyToID("_JudgmentZ");
        private static readonly int CoverTexId = Shader.PropertyToID("_CoverTex");
        private static readonly int CoverRectId = Shader.PropertyToID("_CoverRect");
        private static readonly int CoverAmountId = Shader.PropertyToID("_CoverAmount");
        private static readonly int CoverScreenSpaceId = Shader.PropertyToID("_CoverScreenSpace");

        private static BackgroundHarmonyDriver Instance;

        private readonly List<HarmonyTimeline.Segment> segments = new List<HarmonyTimeline.Segment>();
        private int tonic;
        private BackgroundPalette.Sky basePalette = BackgroundPalette.Fallback;
        private float harmonyStrength = 1f;
        private float currentOffsetDegrees;
        private float targetOffsetDegrees;

        private Texture coverTexture;
        private bool coverIsVideo;
        private Texture lastRejectedCover;
        private float lastCoverRetryTime = float.NegativeInfinity;
        private readonly Vector3[] coverCorners = new Vector3[4];
        private Vector4 coverRect;
        private Vector4 pushedCoverRect;

        private RawImage backdrop;
        private bool backdropCaptured;
        private Texture originalBackdropTexture;
        private Color originalBackdropColor = Color.white;
        private Texture2D gradientTexture;
        private Material skyboxInstance;
        private GameBackgroundManager cachedBackground;
        private ConcertHallBackdrop hall;
        private AspectFillRawImage libraryRoom;
        private RenderTexture blurredCover;
        private Texture blurredFrom;
        private StageLightRig lightRig;
        private float lastHallCheckTime = float.NegativeInfinity;

        private readonly List<Renderer> trackRenderers = new List<Renderer>();
        private float appliedGlass = -1f;
        private float appliedGlassFloor = -1f;
        private float lastSearchTime = float.NegativeInfinity;
        private bool warnedNoTrack;

        /// <summary>遊戲畫面在不在。不在的時候找不到軌道是正常的。</summary>
        private static bool GameplayVisible()
        {
            var game = GameManager.Instance;
            return game != null && game.gameplayRoot != null
                && game.gameplayRoot.activeInHierarchy;
        }
        private Transform cachedJudgmentLine;
        private float appliedJudgmentZ = float.NaN;

        /// <summary>拿到唯一的一份，場上沒有就生一個。</summary>
        public static BackgroundHarmonyDriver GetOrCreate()
        {
            if (Instance != null) return Instance;

            var existing = FindAnyObjectByType<BackgroundHarmonyDriver>();
            if (existing != null)
            {
                Instance = existing;
                return Instance;
            }

            // AddComponent 會當場跑 Awake，Instance 在那裡被設起來。
            new GameObject(RootName).AddComponent<BackgroundHarmonyDriver>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreBackdrop();
            if (Instance == this) Instance = null;
            if (gradientTexture != null) Destroy(gradientTexture);
            if (skyboxInstance != null) Destroy(skyboxInstance);
        }

        /// <summary>換譜面時重算。cover 為 null 就沿用上一次的底色。</summary>
        public void Rebuild(Chart chart, Texture cover)
        {
            segments.Clear();
            currentOffsetDegrees = 0f;
            targetOffsetDegrees = 0f;

            // 換歌就把封面歸零重找。不歸零的話 RetryCoverIfMissing 會因為
            // coverTexture 還留著上一首的而直接跳過，整首歌都用著別人的封面。
            coverTexture = null;
            coverIsVideo = false;
            lastRejectedCover = null;
            lastCoverRetryTime = float.NegativeInfinity;
            AcceptCover(cover);

            if (chart != null && chart.notes != null)
            {
                segments.AddRange(HarmonyTimeline.Build(chart.notes, chart.beat_timings));
                float endMs = chart.music_finish_time_msec > 0
                    ? chart.music_finish_time_msec
                    : (segments.Count > 0 ? segments[segments.Count - 1].startMs : 0f);
                tonic = HarmonyTimeline.Tonic(segments, endMs);
            }
            else
            {
                tonic = 0;
            }

            Debug.Log($"[BackgroundHarmony] 調性段落 {segments.Count} 段，主音 pc={tonic}，" +
                      $"底色 hue={basePalette.hue:F2} sat={basePalette.saturation:F2}，模式 {mode}");
            Apply(BrightenedBase());
        }

        /// <summary>
        /// 收下一張候選封面，純黑的佔位圖會被擋掉。
        /// </summary>
        /// <remarks>
        /// `GameBackgroundManager` 在封面還沒載好時會把一張 1×1 的
        /// `GameBackgroundManager_Black` 塞進 backgroundImage。載入譜面的當下抓到的
        /// 往往就是它，於是玻璃化的軌道被整片塗黑 —— 看起來完全像「透明度沒作用」。
        /// </remarks>
        private void AcceptCover(Texture cover)
        {
            if (cover == null) return;
            // 影片走的是 VideoPlayer 的 RenderTexture，直接收下不取樣。
            // 兩個理由：它隨時可能剛好停在一格黑畫面，用亮度判定會被當成佔位圖擋掉；
            // 而且 FromCover 是 Blit + ReadPixels，會讓 GPU 停一下——影片的貼圖是
            // 輪替的緩衝區，每換一次就取樣一次等於每幀都在停。配色沿用原本那組。
            if (cover is RenderTexture)
            {
                coverTexture = cover;
                coverIsVideo = true;
                return;
            }
            var sampled = BackgroundPalette.FromCover(cover);
            if (!BackgroundPalette.IsUsableCover(sampled))
            {
                coverTexture = null;                 // 玻璃退回透出那片天空
                coverIsVideo = false;
                Debug.Log($"[BackgroundHarmony] 略過封面「{cover.name}」（平均亮度 " +
                          $"{sampled.sourceLuminance:F3}，判定為佔位用的純色圖）");
                return;
            }
            basePalette = sampled;
            coverTexture = cover;
            coverIsVideo = false;
        }

        /// <summary>
        /// 影片背景：換成螢幕座標取樣，並算出影片那張圖在螢幕上的位置。
        /// </summary>
        /// <remarks>
        /// 軌道 UV 取樣對靜態曲繪是對的（圖順著跑道往遠處延伸，讀起來就是背景的
        /// 延伸），但影片那樣做就變成「影片播在軌道上」，跟著跑道一起動。改成螢幕
        /// 座標之後，軌道上取到的正好是它擋住的那幾個畫素，和它周圍的背景接得起來，
        /// 才是使用者要的「透明的軌道疊在背景影片上」。
        ///
        /// 影片是等比例縮放進那塊 RawImage 的，左右會留黑邊，所以不能假設滿版——
        /// 直接量 RawImage 的四個角。
        /// </remarks>
        private void UpdateCoverRect()
        {
            if (!coverIsVideo) return;
            var manager = CachedBackground();
            var img = manager != null ? manager.backgroundImage : null;
            if (img == null) return;
            var canvas = img.canvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            img.rectTransform.GetWorldCorners(coverCorners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, coverCorners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(cam, coverCorners[2]);
            coverRect = CoverRectFor(bottomLeft, topRight);
            // 只有真的變了才推（開始播放、改解析度、換影片比例）。`renderer.materials`
            // 每次呼叫都會配一個陣列，不能每幀來一次。
            if ((coverRect - pushedCoverRect).sqrMagnitude > 1e-10f) PushCoverRect();
        }

        /// <summary>
        /// 影片在螢幕上的矩形 → shader 的取樣係數。uv = 畫素座標 * xy + zw。
        /// </summary>
        /// <remarks>純算術，抽出來是為了驗得起來（見 BackgroundHarmonyTests）。</remarks>
        public static Vector4 CoverRectFor(Vector2 bottomLeft, Vector2 topRight)
        {
            float w = Mathf.Max(1f, topRight.x - bottomLeft.x);
            float h = Mathf.Max(1f, topRight.y - bottomLeft.y);
            return new Vector4(1f / w, 1f / h, -bottomLeft.x / w, -bottomLeft.y / h);
        }

        private void PushCoverRect()
        {
            pushedCoverRect = coverRect;
            for (int i = 0; i < trackRenderers.Count; i++)
            {
                var r = trackRenderers[i];
                if (r == null) continue;
                var mats = r.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null || !mat.HasProperty(CoverScreenSpaceId)) continue;
                    mat.SetFloat(CoverScreenSpaceId, coverIsVideo ? 1f : 0f);
                    mat.SetVector(CoverRectId, coverRect);
                }
            }
        }

        private GameBackgroundManager CachedBackground()
        {
            if (cachedBackground == null) cachedBackground = FindAnyObjectByType<GameBackgroundManager>();
            return cachedBackground;
        }

        /// <summary>
        /// 影片播放中要一直盯著那張貼圖：VideoPlayer 換 RenderTexture 時
        /// <see cref="RetryCoverIfMissing"/> 不會動（它只在沒有封面時才找）。
        /// </summary>
        private void FollowVideoTexture()
        {
            var manager = CachedBackground();
            var img = manager != null ? manager.backgroundImage : null;
            var tex = img != null && img.isActiveAndEnabled ? img.texture : null;
            if (tex is RenderTexture && tex != coverTexture)
            {
                AcceptCover(tex);
                appliedGlass = -1f;                  // 逼下一幀把新貼圖推到軌道上
            }
            else if (coverIsVideo && (tex == null || !(tex is RenderTexture)))
            {
                // 影片收掉了（換歌、播完），別讓軌道繼續顯示已經失效的 RenderTexture。
                coverTexture = null;
                coverIsVideo = false;
                appliedGlass = -1f;
            }
        }

        private void Update()
        {
            // 玻璃化和歌曲進度無關，所以放在 conductor 檢查之前——不然在選曲畫面
            // 或還沒開始播的時候拉滑桿，會完全沒有反應。
            ApplyTrackGlass();
            ApplyTrackHidden();
            ApplyTrackJudgmentClip();
            FollowVideoTexture();
            RetryCoverIfMissing();
            UpdateCoverRect();
            UpdateBackdropBlur();
            UpdateConcertHall();

            var game = GameManager.Instance;
            Conductor conductor = game != null ? game.Conductor : null;
            if (conductor == null) return;

            int index = HarmonyTimeline.IndexAt(segments, conductor.renderSongPosition);
            targetOffsetDegrees = index >= 0
                ? HarmonyTimeline.HueOffsetDegrees(segments[index].pitchClass, tonic,
                                                   degreesPerFifth * harmonyStrength)
                : 0f;

            // 指數滑行。用 renderSongPosition 的變化量而不是 Time.deltaTime 會更「跟著歌」，
            // 但暫停時滑行也該停，deltaTime 在暫停時本來就是 0，所以這樣就對了。
            float k = glideSeconds > 0.01f ? 1f - Mathf.Exp(-Time.deltaTime / glideSeconds) : 1f;
            float next = Mathf.Lerp(currentOffsetDegrees, targetOffsetDegrees, k);

            // 亮度和色相是兩件事：調性說「現在在哪個調」，密度說「現在到哪了」。
            // 兩個都要能單獨觸發重畫，不然色相停下來之後起伏就跟著卡住。
            if (Mathf.Abs(next - currentOffsetDegrees) < 0.01f && Mathf.Abs(next - targetOffsetDegrees) < 0.01f)
                return;                                   // 已經到位，不用重畫
            currentOffsetDegrees = next;
            Apply(BackgroundPalette.ShiftHue(BrightenedBase(), currentOffsetDegrees));
        }

        /// <summary>
        /// 還沒拿到可用封面時，每秒回頭問一次。
        /// </summary>
        /// <remarks>
        /// 封面是非同步載入的，`LoadChartData` 當下 backgroundImage 上通常還是黑底。
        /// 只在載譜時抓一次的話，整首歌就再也沒有封面可用了。
        /// </remarks>
        private void RetryCoverIfMissing()
        {
            if (coverTexture != null) return;
            if (Time.unscaledTime - lastCoverRetryTime < 1f) return;
            lastCoverRetryTime = Time.unscaledTime;

            var manager = FindAnyObjectByType<GameBackgroundManager>();
            var candidate = manager != null && manager.backgroundImage != null
                ? manager.backgroundImage.texture : null;
            // 背景上掛著的可能已經是我們自己模糊過的那一張，那不是新封面。
            if (candidate == blurredCover) candidate = blurredFrom;
            if (candidate == null || candidate == lastRejectedCover) return;
            lastRejectedCover = candidate;
            AcceptCover(candidate);
            if (coverTexture != null)
            {
                appliedGlass = -1f;                  // 逼下一幀把新封面推到軌道上
            }
        }

        /// <summary>
        /// Softens the cover behind the hall so the room reads as depth.
        /// </summary>
        /// <remarks>
        /// The backdrop is a photograph and everything in front of it is drawn
        /// vector art, which is what made the frame look flat: a sharp photo
        /// sits in the same plane as the architecture over it. Blurred, it stops
        /// competing for the eye and starts reading as the far wall.
        ///
        /// Done on the GPU as a chain of halving blits -- bilinear filtering is
        /// the box filter, so the blur costs a handful of blits and no shader.
        /// Down four steps then back up two: stopping at the bottom leaves the
        /// upscale to the RawImage, where a single bilinear stretch shows its
        /// grid.
        ///
        /// The sharp texture is kept for the track's reflection, which is a
        /// mirror and should not be soft.
        /// </remarks>
        private void UpdateBackdropBlur()
        {
            if (!EnsureBackdrop()) return;
            // 影片每一幀都在變，模糊它等於每一幀重跑整條鏈。
            if (coverIsVideo || IsVideoPlaying()) return;

            Texture source = backdrop.texture;
            if (source == null || source == blurredCover) return;
            if (source == gradientTexture) return;

            blurredCover = BuildBlur(source, blurredCover);
            if (blurredCover == null) return;
            blurredFrom = source;
            backdrop.texture = blurredCover;
        }

        private static RenderTexture BuildBlur(Texture source, RenderTexture reuse)
        {
            if (source.width < 8 || source.height < 8) return null;

            int width = Mathf.Max(8, source.width / 2);
            int height = Mathf.Max(8, source.height / 2);
            if (reuse != null && (reuse.width != width || reuse.height != height))
            {
                reuse.Release();
                Destroy(reuse);
                reuse = null;
            }

            if (reuse == null)
            {
                reuse = new RenderTexture(width, height, 0, RenderTextureFormat.Default)
                {
                    name = "BackdropBlur",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                reuse.Create();
            }

            RenderTexture current = RenderTexture.GetTemporary(width, height);
            current.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, current);

            for (int step = 0; step < 4; step++)
            {
                RenderTexture next = RenderTexture.GetTemporary(
                    Mathf.Max(4, current.width / 2), Mathf.Max(4, current.height / 2));
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, next);
                RenderTexture.ReleaseTemporary(current);
                current = next;
            }

            for (int step = 0; step < 2; step++)
            {
                RenderTexture next = RenderTexture.GetTemporary(
                    Mathf.Min(width, current.width * 2), Mathf.Min(height, current.height * 2));
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, next);
                RenderTexture.ReleaseTemporary(current);
                current = next;
            }

            Graphics.Blit(current, reuse);
            RenderTexture.ReleaseTemporary(current);
            return reuse;
        }

        /// <summary>
        /// Keeps the auditorium attached, tinted and out of the way of a video.
        /// </summary>
        /// <remarks>
        /// Driven from Update rather than from Apply: Apply only runs when a
        /// colour actually moves, so a backdrop that appears a few seconds into
        /// the song would miss its one chance to be found.
        /// </remarks>
        private void UpdateConcertHall()
        {
            if (!EnsureBackdrop()) return;
            if (hall == null)
            {
                hall = ConcertHallBackdrop.Attach(backdrop);
                if (hall == null) return;
            }

            // 廳堂留給演奏會模式：那座鍍金的大廳本來就是「上台」的樣子。一般
            // 模式接的是選歌畫面那個房間 —— 從選曲走進遊玩，房間不換。
            bool recital = false;
            try { recital = SettingsManager.Instance != null && SettingsManager.Instance.RecitalModeInPlay; }
            catch { recital = false; }

            if (!recital && libraryRoom == null)
                libraryRoom = AttachLibraryRoom(hall.transform.parent,
                    hall.transform.GetSiblingIndex() + 1);

            Color roomColour = BackgroundPalette.ShiftHue(BrightenedBase(), currentOffsetDegrees).horizon;
            hall.Tint = roomColour;

            // 燈架畫在廳堂的正前方。它和塵埃共用 StageLightLayout，所以光柱和
            // 粒子的錐體是同一組座標，不是兩邊各自對出來的。
            if (lightRig == null) lightRig = StageLightRig.Attach(hall.transform);
            if (lightRig != null) lightRig.Tint = roomColour;

            // MV 本身就是背景，別在上面蓋一座廳堂。這個查詢會掃場景，所以問慢一點。
            if (Time.unscaledTime - lastHallCheckTime < 0.4f) return;
            lastHallCheckTime = Time.unscaledTime;
            bool visible = !IsVideoPlaying();
            if (hall.enabled != (visible && recital)) hall.enabled = visible && recital;
            // 舞台燈屬於舞台。一般模式那個房間沒有聚光燈，光柱和它裡面的塵埃
            // 一起關掉。
            if (lightRig != null && lightRig.enabled != (visible && recital))
                lightRig.enabled = visible && recital;
            if (libraryRoom != null && libraryRoom.enabled != (visible && !recital))
                libraryRoom.enabled = visible && !recital;
        }

        /// <summary>
        /// The song library's own room, put behind the chart for normal play.
        /// </summary>
        /// <remarks>
        /// Same photograph, same fill: walking from the shelf into the song
        /// should not change the room you are standing in. It is laid over the
        /// blurred cover at four fifths alpha, the way the hall's walls are, so
        /// the artwork still tints the room instead of vanishing behind it.
        /// </remarks>
        private static AspectFillRawImage AttachLibraryRoom(Transform parent, int siblingIndex)
        {
            if (parent == null) return null;
            Transform existing = parent.Find("LibraryRoomBackdrop");
            if (existing != null) return existing.GetComponent<AspectFillRawImage>();

            var roomObject = new GameObject("LibraryRoomBackdrop", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(AspectFillRawImage));
            roomObject.layer = parent.gameObject.layer;
            RectTransform rect = roomObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(siblingIndex);

            AspectFillRawImage room = roomObject.GetComponent<AspectFillRawImage>();
            room.texture = Resources.Load<Texture2D>("UI/SongSelectionClassicalRoom");
            room.color = new Color(1f, 1f, 1f, 0.82f);
            room.raycastTarget = false;
            room.UpdateUVRect();
            return room;
        }

        private void Apply(BackgroundPalette.Sky sky)
        {
            ApplySkybox(sky);
            ApplyTrackSky(sky);

            // 真正在畫面上看得見的是 ClassicalAtmosphere 的金色塵埃 —— 天空盒暗到
            // 幾乎全黑，改它的色相幾乎讀不出來。粒子才是有面積的那一層，所以調性
            // 的偏移主要靠它被看見。
            ClassicalAtmosphere.ApplyDustHue(currentOffsetDegrees);

            if (mode == BackdropMode.SkyboxOnly) return;

            if (!EnsureBackdrop()) return;
            if (IsVideoPlaying())
            {
                // MV 就是背景，別在上面塗東西。
                RestoreBackdropVisualsOnly();
                return;
            }

            if (mode == BackdropMode.TintCover)
            {
                Color tint = Color.Lerp(originalBackdropColor, sky.horizon * 4f, tintStrength);
                tint.a = originalBackdropColor.a;
                backdrop.color = tint;
            }
            else
            {
                if (backdrop.texture != gradientTexture) backdrop.texture = BuildGradient(sky);
                else WriteGradient(sky);
                backdrop.color = Color.white;
            }
        }

        /// <summary>
        /// 底色，亮度跟著軌道玻璃化程度提高。
        /// </summary>
        /// <remarks>
        /// 實心軌道時背景只是氣氛，維持原本近乎全黑的亮度；軌道一透明，那片天空就變成
        /// 玩家真正在看的東西，還維持 0.1 的亮度等於「透明了也只看到黑色」。所以兩者
        /// 綁在一起，而不是再多一個要使用者自己配平的旋鈕。
        /// </remarks>
        private BackgroundPalette.Sky BrightenedBase()
        {
            return BackgroundPalette.WithBrightness(basePalette, 1f + trackGlass * 1.5f);
        }

        /// <summary>調性變色的強度倍率。0 = 關掉，1 = 校準值，2 = 誇張。</summary>
        /// <remarks>
        /// 之所以需要一個能拉到 2 的倍率：實測整首歌的色相偏移中位只有 10 度，
        /// 那是**刻意**的（調性沒變就不該變色），但也因此第一次看的人會以為壞掉。
        /// 拉到 2 確認它真的在動，再往回收，比「相信它有在動」好。
        /// </remarks>
        public void SetHarmonyStrength(float strength)
        {
            harmonyStrength = Mathf.Clamp(strength, 0f, 2f);
        }

        /// <summary>設定玻璃化程度。由 SettingsManager 呼叫，滑桿一動就會看到。</summary>
        /// <remarks>
        /// 軌道可能還沒生出來（換歌、場景剛起來），所以這裡只記值，真正的套用在
        /// <see cref="ApplyTrackGlass"/>，它每幀檢查一次值有沒有變。找不到軌道時
        /// 會清掉快取重找，而不是一次找不到就永遠放棄——踏板渲染器踩過這個坑。
        /// </remarks>
        public void SetTrackGlass(float glass, float floorOpacity)
        {
            trackGlass = Mathf.Clamp01(glass);
            glassFloorOpacity = Mathf.Clamp(floorOpacity, 0.2f, 1f);
            trackRenderers.Clear();          // 換歌後軌道可能是新的物件
            appliedGlass = -1f;              // 逼下一幀重套
        }

        /// <summary>軌道整個不畫（拋物線模式）。玻璃是「透出背景」，這個是真的不存在。</summary>
        private bool trackHidden;
        private bool appliedTrackHidden;

        public void SetTrackHidden(bool hidden)
        {
            if (trackHidden == hidden) return;
            trackHidden = hidden;
            trackRenderers.Clear();          // 重找，順便讓下一幀重套
            appliedTrackHidden = !hidden;
            appliedGlass = -1f;
        }

        /// <summary>
        /// 把「不畫軌道」推到軌道的 Renderer 上。
        /// </summary>
        /// <remarks>
        /// 用 `renderer.enabled` 而不是把材質調成透明：軌道的 shader 是不透明的，
        /// 調 alpha 只會變黑（玻璃那條路就是這樣才要推天空色）。整個關掉最乾淨，
        /// 而且切回傾斜模式時原封不動地開回來。
        /// </remarks>
        private void ApplyTrackHidden()
        {
            if (appliedTrackHidden == trackHidden && trackRenderers.Count > 0) return;
            if (trackRenderers.Count == 0)
            {
                if (Time.unscaledTime - lastSearchTime < 0.5f) return;
                lastSearchTime = Time.unscaledTime;
                CollectTrackRenderers();
            }
            if (trackRenderers.Count == 0) return;
            for (int i = 0; i < trackRenderers.Count; i++)
            {
                var r = trackRenderers[i];
                if (r != null) r.enabled = !trackHidden;
            }
            appliedTrackHidden = trackHidden;
        }

        /// <summary>
        /// 把玻璃化程度推到軌道材質上。
        /// </summary>
        /// <remarks>
        /// 用 `renderer.material`（會自動產生實例）而不是 sharedMaterial —— 後者會把
        /// 專案裡的 .mat 資產本身改掉，在編輯器裡按停止之後髒檔案還留著。
        ///
        /// 只有值真的變了才寫，因為這是每幀跑的。
        /// </remarks>
        private void ApplyTrackGlass()
        {
            if (Mathf.Approximately(appliedGlass, trackGlass) &&
                Mathf.Approximately(appliedGlassFloor, glassFloorOpacity)) return;

            if (trackRenderers.Count == 0)
            {
                // FindObjectsByType 掃全場景很貴，而「還沒生出來」可能持續好幾秒。
                if (Time.unscaledTime - lastSearchTime < 0.5f) return;
                lastSearchTime = Time.unscaledTime;
                CollectTrackRenderers();
            }
            // 找不到軌道就**不要**記成已套用。場景還沒起來、換歌中途都會暫時找不到，
            // 記成已套用的話就再也不會重試了，而那種失敗是完全無聲的。
            if (trackRenderers.Count == 0) return;

            for (int i = 0; i < trackRenderers.Count; i++)
            {
                var r = trackRenderers[i];
                if (r == null) continue;
                var mats = r.materials;                 // 實例化，不動共用資產
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    if (!mat.HasProperty(GlassId)) continue;
                    mat.SetFloat(GlassId, trackGlass);
                    // 「玻璃透背景」現在的意思是「最多讓多少天空取代大理石」。
                    mat.SetFloat(GlassMaxId, 1f - glassFloorOpacity + 0.45f);
                }
            }
            appliedGlass = trackGlass;
            appliedGlassFloor = glassFloorOpacity;
            // **一定要在這裡重推一次**：軌道的 _Sky*/_Cover* 只有 Apply() 會寫，而
            // Apply() 平常只在色相移動時才跑。少了這一行，剛找到的軌道材質會一直
            // 停在 shader 的預設值（那組近乎全黑的暗紅）——看起來就是「拉了透明度
            // 只是變黑」。
            Apply(BackgroundPalette.ShiftHue(BrightenedBase(), currentOffsetDegrees));
            // 這一行是給「調了沒反應」時看的：三個數字各自對應一種失敗。
            // Renderer=0 → 抓不到軌道；封面=無 → 玻璃只會透出那片近乎全黑的天空；
            // 地平線色很暗 → 亮度沒推上去。
            var check = BackgroundPalette.ShiftHue(BrightenedBase(), currentOffsetDegrees);
            Debug.Log($"[BackgroundHarmony] 玻璃化 {trackGlass:P0}　軌道 Renderer {trackRenderers.Count} 個　" +
                      $"封面 {(coverTexture != null ? coverTexture.name : "無")}" +
                      $"{(coverIsVideo ? "（影片，螢幕座標）" : "")}　" +
                      $"地平線色 {check.horizon}　亮度 ×{check.brightness:F1}");
        }

        /// <summary>
        /// 玻璃化時把 playfield 的家具整組移到天空盒後面。
        /// </summary>
        /// <remarks>
        /// **這是「透明卻看不到背景」的真正原因。** URP 的順序是「不透明 → 天空盒 →
        /// 透明」：軌道原本在 queue 2000（不透明段）就畫完並寫了深度，天空盒根本不會
        /// 畫到那些像素，所以就算把 alpha 調下去，它混合的對象是**相機的清除色**而不是
        /// 背景 —— 看起來就是「還有一層底色」。而且 `Track_BackGround.mat` 上有
        /// `m_CustomRenderQueue: 2000`，材質的 queue 覆寫會直接蓋掉 shader 的 tag，
        /// 改 shader 的 "Queue"= 完全沒有作用。
        ///
        /// 2500 是 URP 的分界（Transparent 從 2501 起算）。整組 +600 一起搬，是為了
        /// 保住它們原本的相對順序（軌道 2005 &lt; 踏板 2009 &lt; 導引線 2010），
        /// 不然導引線會跑到軌道底下被玻璃吃掉。
        ///
        /// 只在 glass &gt; 0 時搬；關掉就原封不動放回去，所以預設路徑完全沒被動到。
        /// </remarks>
        private void CollectTrackRenderers()
        {
            trackRenderers.Clear();
            appliedJudgmentZ = float.NaN;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    var shader = mats[m] != null ? mats[m].shader : null;
                    if (shader == null) continue;
                    // **只有軌道**。踏板音符和導引線的材質碰不得：PedalNoteRenderer
                    // 每幀往自己那份材質寫 _TMin/_TMax，而 `renderer.materials` 會把它
                    // 換成新實例，那些寫入就全部落到一份沒在用的材質上——踏板會整個消失。
                    if (shader.name == "Nostalgia/Player View Marble Track")
                    {
                        trackRenderers.Add(r);
                        break;
                    }
                }
            }
            // 只在**遊戲畫面已經開著**的時候才抱怨。選歌畫面裡根本沒有軌道，
            // 那不是故障；每五秒喊一次「找不到軌道」只是把真正的問題淹掉。
            //
            // 找到之後把旗標放掉：換一首歌軌道會是新的物件，那時候要是真的壞了
            // 還是要講。
            if (trackRenderers.Count > 0)
            {
                warnedNoTrack = false;
            }
            else if (!warnedNoTrack && GameplayVisible())
            {
                warnedNoTrack = true;
                Debug.LogWarning("[BackgroundHarmony] 找不到用 Nostalgia/Player View Marble Track " +
                                 "的 Renderer，軌道玻璃化無效。");
            }
        }

        private void ApplyTrackJudgmentClip()
        {
            if (cachedJudgmentLine == null)
            {
                GameObject line = GameObject.Find("JudgmentLine");
                cachedJudgmentLine = line != null ? line.transform : null;
            }
            if (cachedJudgmentLine == null) return;

            if (trackRenderers.Count == 0)
                CollectTrackRenderers();
            if (trackRenderers.Count == 0) return;

            float judgmentZ = cachedJudgmentLine.position.z;
            if (!float.IsNaN(appliedJudgmentZ) &&
                Mathf.Abs(appliedJudgmentZ - judgmentZ) <= 0.0001f)
                return;

            for (int i = 0; i < trackRenderers.Count; i++)
            {
                Renderer trackRenderer = trackRenderers[i];
                if (trackRenderer == null) continue;
                Material[] materials = trackRenderer.materials;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];
                    if (material != null && material.HasProperty(JudgmentZId))
                        material.SetFloat(JudgmentZId, judgmentZ);
                }
            }
            appliedJudgmentZ = judgmentZ;
        }

        /// <summary>天空的三段顏色同時推給軌道 —— 玻璃化是靠它自己畫出同一片天空。</summary>
        private void ApplyTrackSky(BackgroundPalette.Sky sky)
        {
            for (int i = 0; i < trackRenderers.Count; i++)
            {
                var r = trackRenderers[i];
                if (r == null) continue;
                var mats = r.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null || !mat.HasProperty(SkyHorizonId)) continue;
                    mat.SetColor(SkyBottomId, sky.bottom);
                    mat.SetColor(SkyHorizonId, sky.horizon);
                    mat.SetColor(SkyTopId, sky.top);
                    // 有封面就讓玻璃透出封面；沒有就維持那片天空。
                    if (mat.HasProperty(CoverAmountId))
                    {
                        if (coverTexture != null) mat.SetTexture(CoverTexId, coverTexture);
                        mat.SetFloat(CoverAmountId, coverTexture != null ? 1f : 0f);
                    }
                    if (mat.HasProperty(CoverScreenSpaceId))
                    {
                        mat.SetFloat(CoverScreenSpaceId, coverIsVideo ? 1f : 0f);
                        mat.SetVector(CoverRectId, coverRect);
                    }
                }
            }
        }

        private void ApplySkybox(BackgroundPalette.Sky sky)
        {
            Material sky_ = RenderSettings.skybox;
            if (sky_ == null) return;
            if (skyboxInstance == null || RenderSettings.skybox != skyboxInstance)
            {
                // 複製一份再改，否則會把 .mat 資產本身改掉（編輯器裡會留下髒檔案）。
                skyboxInstance = new Material(sky_);
                RenderSettings.skybox = skyboxInstance;
            }
            if (skyboxInstance.HasProperty("_BottomColor")) skyboxInstance.SetColor("_BottomColor", sky.bottom);
            if (skyboxInstance.HasProperty("_HorizonColor")) skyboxInstance.SetColor("_HorizonColor", sky.horizon);
            if (skyboxInstance.HasProperty("_TopColor")) skyboxInstance.SetColor("_TopColor", sky.top);
        }

        private bool EnsureBackdrop()
        {
            if (backdrop != null) return true;
            var manager = FindAnyObjectByType<GameBackgroundManager>();
            if (manager == null || manager.backgroundImage == null) return false;
            backdrop = manager.backgroundImage;
            if (!backdropCaptured)
            {
                originalBackdropTexture = backdrop.texture;
                originalBackdropColor = backdrop.color;
                backdropCaptured = true;
            }
            return true;
        }

        private bool IsVideoPlaying()
        {
            var manager = FindAnyObjectByType<GameBackgroundManager>();
            var player = manager != null ? manager.videoPlayer : null;
            return player != null && player.isPlaying;
        }

        private void RestoreBackdropVisualsOnly()
        {
            if (backdrop == null || !backdropCaptured) return;
            backdrop.color = originalBackdropColor;
            if (mode == BackdropMode.ReplaceWithGradient && backdrop.texture == gradientTexture)
                backdrop.texture = originalBackdropTexture;
        }

        private void RestoreBackdrop()
        {
            RestoreBackdropVisualsOnly();
            backdrop = null;
            backdropCaptured = false;
        }

        /// <summary>
        /// 縱向漸層貼圖。1 像素寬就夠——RawImage 會橫向拉開，而漸層本來就只沿 Y 變化。
        /// </summary>
        private Texture2D BuildGradient(BackgroundPalette.Sky sky)
        {
            if (gradientTexture == null)
            {
                gradientTexture = new Texture2D(1, 128, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = "BackgroundHarmonyGradient",
                };
            }
            WriteGradient(sky);
            return gradientTexture;
        }

        private void WriteGradient(BackgroundPalette.Sky sky)
        {
            if (gradientTexture == null) return;
            int height = gradientTexture.height;
            var pixels = new Color32[height];
            for (int y = 0; y < height; y++)
            {
                // 0 = 畫面底部（判定線那端）→ 最暗；地平線帶在偏下方，上緣再收暗。
                float t = height <= 1 ? 0f : (float)y / (height - 1);
                Color c = t < 0.35f
                    ? Color.Lerp(sky.bottom, sky.horizon, t / 0.35f)
                    : Color.Lerp(sky.horizon, sky.top, (t - 0.35f) / 0.65f);
                pixels[y] = c;
            }
            gradientTexture.SetPixels32(pixels);
            gradientTexture.Apply(false);
        }
    }
}
