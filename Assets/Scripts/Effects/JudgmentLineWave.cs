using System.Collections.Generic;
using UnityEngine;

namespace Effects
{
    /// <summary>
    /// 打到的時候，判定線在**被打到的那一格**掀起一束光線，往兩側跑開。
    ///
    /// **高度是力度，顏色是判定。** 兩件事各管一樣，所以一眼就分得開：
    /// 這條線講你彈得多用力，判定光柱講你打得多準。
    /// </summary>
    /// <remarks>
    /// **為什麼要這個。** 判定線原本就有反應 —— <see cref="JudgmentLineGlow"/>
    /// 每一次打擊都會被叫到，把整條線的 <c>_EmissionColor</c> 閃一下。問題是
    /// <c>TriggerGlow()</c> 連鍵道參數都沒有：不管打第 3 格還是第 25 格，整條線
    /// 一模一樣地亮。**一個每次都一樣的反應不帶任何資訊**，眼睛幾幀之內就會把它
    /// 當成背景過濾掉，於是那條線讀起來是死的。所以要補的不是「一個反應」，
    /// 是「讓反應有位置」。
    ///
    /// **為什麼是幾何不是亮度。** 亮度閃一下正是上面那個已經證明會變壁紙的做法。
    /// 畫面上現在所有東西講的都是「光」—— 力度色場、導引線、魔法陣、光柱、踏板的
    /// 光牆 —— **沒有任何一個在講「形狀」**。那個頻道是空的，這是它不和別人打架
    /// 最硬的保證。
    ///
    /// **形狀是一束光線，不是一片平滑的土堆。** 光線是釘在固定位置的（每格鍵道
    /// 三根），波從它們之間掃過去，被掃到的那幾根站起來 —— 讀起來是「這條線本來
    /// 就是光做的，被打了一下」，而不是「有個東西從線裡跑出來」。每一根的高度
    /// 帶一點固定的隨機（<see cref="RayJitter"/>），所以它是一束而不是一把梳子。
    ///
    /// 所有光線共用一份網格，所以同時幾顆都是一次繪製：和弦不會因為加法混合疊成
    /// 一片白，那是這個畫面上最實際的干擾形式。
    ///
    /// **為什麼波要跑開。** 撞擊點本身被判定光柱佔住（它的底部就在那裡，而且
    /// PremiumJudgmentGlow 是 Queue=Overlay + ZTest Always，無條件蓋在上面）。
    /// 波往兩側跑，跑到的地方沒有東西 —— 衝突自己解掉了。而「從哪裡擴散出去」
    /// 本身就說明了撞擊點在哪，所以讓開並沒有讓它少說什麼。
    /// 這也是 <see cref="SpeedShare"/> 不能調慢的原因：波必須在光柱亮到最滿之前
    /// 離開那個 x，不然它根本沒有機會被看到。
    ///
    /// 一份網格畫完所有還活著的波，所以同時幾顆都是一次繪製（和
    /// <see cref="LaneStrikeCue"/> 同一個做法）。
    /// </remarks>
    [DefaultExecutionOrder(600)]
    public sealed class JudgmentLineWave : MonoBehaviour
    {
        /// <summary>同時最多疊幾道。超過就丟掉最舊的。</summary>
        private const int Capacity = 24;

        /// <summary>一格鍵道站幾根光線。</summary>
        private const int RaysPerLane = 3;

        /// <summary>一根光線多寬，以鍵道寬為單位。</summary>
        /// <remarks>
        /// 上限是間距（1/3 格 = 1.25 世界單位）：貼到那個數字相鄰兩根就黏成一片，
        /// 又變回土堆了。0.17 格 ≈ 0.64 世界單位，**佔空比約一半** —— 看得出是
        /// 一根一根的，但每一根有足夠的實體。
        ///
        /// 試過 0.07（佔空比 21%），太細了：讀起來是一排針而不是光。
        /// 0.24（72%）則反過來，和弦的時候整排糊成一塊，數不出按了幾顆。
        /// 高度對寬度大約 7:1 是這兩件事之間的位置。
        /// </remarks>
        private const float RayWidthLanes = 0.17f;

        /// <summary>每一根高度的隨機下限（上限是 1）。</summary>
        /// <remarks>
        /// 固定的偽亂數，不是每幀重擲 —— 重擲的話整束光線會在原地發抖。
        /// 沒有這個抖動，一束等高的光線讀起來是一把梳子。
        /// </remarks>
        private const float RayJitter = 0.52f;

        /// <summary>光線之間那層薄光有多高，相對於光線本身。</summary>
        /// <remarks>
        /// **光線不能站在全黑上面。** 一根一根孤立的亮條讀起來是一排物件；
        /// 它們之間墊一層連續的薄光之後，整束才是「一片光，裡面有比較亮的幾道」。
        /// 這也是參考圖裡那團藍光的樣子 —— 有線條，但線條泡在霧裡。
        ///
        /// 薄光用的是**同一條包絡**，只是矮、而且不帶每根各自的抖動（抖動是用來
        /// 分出一根一根的，薄光要的正好相反：連續）。
        /// </remarks>
        private const float HazeHeight = 0.38f;

        /// <summary>那層薄光有多淡，相對於光線。</summary>
        /// <remarks>
        /// 這是加法混合，所以薄光會**疊在光線底下**一起累加。太亮的話光線的根部
        /// 全部燒成一片白，等於把剛剛加寬加高的那些線又糊掉了。
        /// </remarks>
        private const float HazeAlpha = 0.46f;

        /// <summary>薄光的取樣密度：每個光線間距切幾段。</summary>
        /// <remarks>
        /// 薄光是連續的，所以它的取樣要比光線密 —— 照光線的間距（1/3 格）取樣的話，
        /// 一個波峰只分到 2.7 個點，畫出來的是折線不是霧。
        /// </remarks>
        private const int HazePerPitch = 3;

        /// <summary>光線往外倒多少，相對於它自己的高度。</summary>
        /// <remarks>離撞擊點越遠倒得越多，所以整束是從撞擊點往外被吹開的。</remarks>
        private const float RayLean = 0.26f;

        /// <summary>一道滿強度的波有多高，佔軌道寬的比例。</summary>
        /// <remarks>
        /// **這裡用軌道寬是對的**，而踏板光暈那裡用軌道寬是錯的 —— 差別在軸。
        /// 那邊量的是沿軌道（世界 z）的距離，被透視壓成約 12 px/世界單位；
        /// 這裡量的是站起來的高度（世界 y），在畫面上約 27 px/世界單位，和橫向的
        /// 30 px 同一個量級。所以橫向的尺在這一軸上可以直接用。
        ///
        /// 0.042 × 105 ≈ 4.4 個世界單位，和踏板的光牆同高（pedalBeamShare 也是
        /// 0.042）。**和那道牆同高是可以的，因為質量完全不同** —— 那是一整片實心
        /// 的光幕，這是一束寬 0.26 的細線，同樣的高度佔掉的畫面差一個量級。
        ///
        /// 走到這個數字之前試過兩版，都太矮：
        /// * 0.014（高 1.5、波峰寬 4.7，比例 1:3）—— 低矮的土丘，不是撞擊。
        /// * 0.020 配 0.11 格的線寬 —— 已經是光線了，但高度只有間距的 1.6 倍，
        ///   讀起來是一把小梳子。**光線要像光線，高度對寬度的比例是關鍵**：
        ///   現在是 4.4 比 0.26，約 17:1。
        /// </remarks>
        private const float PeakShare = 0.042f;

        /// <summary>和弦疊加後的上限，相對於 <see cref="PeakShare"/>。</summary>
        /// <remarks>
        /// 八顆同時打，疊加會把整條線掀起來。夾的時機是**疊加之後**，不是每一道
        /// 各自夾 —— 各自夾的話和弦和單音長得一模一樣，那就白疊了。
        /// 夾法要在一道以內完全不作用（單音必須剛好是 PeakShare，不能被軟夾縮水），
        /// 超過之後才指數地收斂。
        /// </remarks>
        private const float ChordCeiling = 1.8f;

        /// <summary>波峰每秒跑多遠，佔軌道寬的比例。</summary>
        /// <remarks>
        /// **這個值是算出來的，不是調出來的。** 判定光柱的 riseDuration 是
        /// 0.055 秒，而光柱無條件蓋在波上面（Overlay + ZTest Always）。所以波峰
        /// 必須在 55 毫秒內離開光柱底部所佔的範圍，否則它整個生命裡最亮的那一段
        /// 完全不會被看到。
        ///
        /// 要清掉的距離 = 光柱半寬（半格鍵道，1.88）+ 波峰半寬
        /// （<see cref="CrestShare"/> 的一半，1.68）≈ 3.6 世界單位。
        /// 3.6 / 0.055 ≈ 65 單位/秒 ≈ 軌道寬的 0.62。
        ///
        /// 改 riseDuration 或 CrestShare 的話這個數字要重算。
        /// </remarks>
        private const float SpeedShare = 0.62f;

        /// <summary>波峰本身有多寬，佔軌道寬的比例。</summary>
        /// <remarks>
        /// 0.032 × 105 ≈ 3.4，約 **0.9 格鍵道** —— 要比鍵道間距窄。
        /// 先前的 0.045（1.26 格）比間距還寬，於是和弦的每一顆會互相糊成一條
        /// 長堤；收到 0.9 格之後五顆和弦畫出來是**五個分得開的鼓包**，
        /// 玩家數得出來剛剛按了幾顆。那是這個特效唯一能比光柱多說的事。
        /// </remarks>
        private const float CrestShare = 0.032f;

        /// <summary>往外跑多遠就剩一半高，佔軌道寬的比例。</summary>
        private const float SpreadShare = 0.22f;

        /// <summary>一道波活多久。</summary>
        private const float LifeSeconds = 0.42f;

        /// <summary>同一格在這段時間內只收一次。</summary>
        /// <remarks>
        /// 打擊特效有兩條路徑都會叫到這裡（<see cref="HitParticleManager"/> 和
        /// ParticleEffectPlayer 的 PlayClassicalAccent），而它們是同一顆音符 ——
        /// 不擋的話同一下會疊成兩倍高。和弦不受影響：不同鍵道的 x 差得夠遠。
        /// </remarks>
        private const float DedupeSeconds = 0.05f;
        private const float DedupeShare = 0.015f;

        private struct Wave
        {
            public float X;          // 撞擊點的世界 x
            public float Start;      // unscaled 時刻
            public float Amplitude;  // 世界單位，帶正負號
            public Color Tint;
        }

        private static JudgmentLineWave instance;

        private readonly List<Wave> waves = new List<Wave>(Capacity);
        private MeshRenderer view;
        private Mesh mesh;

        private readonly List<Vector3> vertices = new List<Vector3>(256);
        private readonly List<Color> colours = new List<Color>(256);
        private readonly List<Vector2> uv = new List<Vector2>(256);
        private readonly List<int> triangles = new List<int>(768);

        public static JudgmentLineWave EnsureCreated()
        {
            if (instance != null) return instance;
            // 自己開一個 host 而不是掛到場上現有的物件：find-or-create 在
            // gameplay root 關著的時候會建出替身，把設定好的那一份擠掉。
            var host = new GameObject("JudgmentLineWave");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<JudgmentLineWave>();
            return instance;
        }

        /// <summary>
        /// 這一下打在 <paramref name="worldX"/>，照**力度**掀一束光線。
        /// </summary>
        /// <remarks>
        /// **高度由力度決定，不是由判定決定。** 判定只決定顏色。
        ///
        /// 這件事反過來做過一版（PERFECT 高、GOOD 矮），那是錯的：判定準不準已經
        /// 由判定光柱和判定字在講，再用高度講一次只是同一句話說三遍。而力度
        /// —— 這個遊戲整個在講的東西 —— 在畫面上只有力度色場那一層很淡的底色
        /// 在代表。把它接到一個會動的東西上，玩家才看得到自己的強弱。
        ///
        /// 顏色自己去拿判定色（和光柱同一套）。兩條打擊路徑手上的顏色不一樣
        /// —— 一個是手色、一個是主題色 —— 誰先到就決定顏色的話，同一種判定會
        /// 隨路徑變色。從這裡統一拿，那件事就不可能發生。
        /// </remarks>
        public static void Strike(float worldX, int lane, NoteData note, JudgmentResult result)
        {
            Color tint;
            try { tint = NoteJudgementMeshManager.EnsureCreated().BaseColorFor(result); }
            catch { tint = Color.white; }
            // 失誤往**下**凹。方向相反是免費的資訊，絕對不會看錯成打中。
            float strength = result == JudgmentResult.Miss ? -0.34f : ResolveStrength(lane, note);
            Strike(worldX, strength, tint);
        }

        /// <summary>這一下彈了多用力，換算成一束光線的高度（1 = 滿）。</summary>
        /// <remarks>
        /// 先問**玩家實際送進來的力度**（<c>PianoKeysound.DescribeLaneInput</c>，
        /// 和 PRECISE 判定問的是同一個來源）。問不到才退回譜面寫的那一顆 ——
        /// 滑鼠和鍵盤沒有力度，那時候至少還有譜面的強弱可以照。
        ///
        /// 映射把 0.15～0.95 攤到 0.26～1.0：真正用到的力度不會貼著 0 和 127，
        /// 直接線性對應的話整首曲子的光線高度會擠在中間一小段，強弱就看不出來了。
        /// </remarks>
        private static float ResolveStrength(int lane, NoteData note)
        {
            float v = -1f;
            try
            {
                if (lane >= 0)
                {
                    PianoKeysound.DescribeLaneInput(lane, out _, out float played, out _);
                    if (played >= 0f) v = played;
                }
            }
            catch { }
            if (v < 0f && note != null && note.velocity > 0) v = note.velocity / 127f;
            // 什麼都問不到：給一個中間值，不要讓線完全沒反應。
            if (v < 0f) return 0.62f;
            return Mathf.Lerp(0.26f, 1f, Mathf.Clamp01(Mathf.InverseLerp(0.15f, 0.95f, v)));
        }

        /// <summary>直接指定強度（正的往上、負的往下），1 = 一道滿的。</summary>
        public static void Strike(float worldX, float strength, Color tint)
        {
            try
            {
                if (Mathf.Abs(strength) < 0.01f) return;
                float trackWidth = PianoVisualLayout.ResolveTrackWidth(null);
                if (trackWidth <= 0.001f) return;

                JudgmentLineWave view = EnsureCreated();
                float now = Time.unscaledTime;
                float sameLane = trackWidth * DedupeShare;
                for (int i = view.waves.Count - 1; i >= 0; i--)
                {
                    Wave w = view.waves[i];
                    if (now - w.Start < DedupeSeconds && Mathf.Abs(w.X - worldX) <= sameLane)
                        return;
                }

                if (view.waves.Count >= Capacity) view.waves.RemoveAt(0);
                view.waves.Add(new Wave
                {
                    X = worldX,
                    Start = now,
                    Amplitude = strength * trackWidth * PeakShare,
                    Tint = tint,
                });
            }
            catch { }
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            for (int i = waves.Count - 1; i >= 0; i--)
                if (now - waves[i].Start >= LifeSeconds) waves.RemoveAt(i);

            if (view == null) Build();
            if (view == null) return;

            if (waves.Count == 0)
            {
                view.enabled = false;
                return;
            }

            if (!TryPlaceAtJudgmentLine()) return;
            Fill(PianoVisualLayout.ResolveTrackWidth(null), now);
            Upload();
        }

        /// <summary>
        /// 每幀重新問判定線在哪 —— 它的高度是玩家可以改的設定，記一次然後一直用，
        /// 會在他調整的當下就對不上。
        /// </summary>
        private bool TryPlaceAtJudgmentLine()
        {
            var line = GameObject.Find("JudgmentLine");
            if (line == null) return false;
            Vector3 at = line.transform.position;
            transform.position = new Vector3(0f, at.y + 0.02f, at.z);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            return true;
        }

        /// <summary>某一個 x 上，所有還活著的波加起來是什麼。</summary>
        private struct Sample
        {
            /// <summary>未夾的總和，帶正負號。</summary>
            public float Height;
            public Color Tint;
            /// <summary>貢獻最大的那一道在哪、離多遠 —— 決定光線往哪邊倒。</summary>
            public float DomX;
            public float DomDist;
            public bool Any;
        }

        private Sample SampleAt(float x, float now, float speed, float crest, float spread)
        {
            Sample o = default;
            o.DomX = x;
            float r = 0f, g = 0f, b = 0f, a = 0f, weight = 0f, domWeight = 0f;

            for (int i = 0; i < waves.Count; i++)
            {
                Wave w = waves[i];
                float age = now - w.Start;
                if (age < 0f) continue;
                float d = Mathf.Abs(x - w.X);
                // 波峰在 d = speed * age，所以 age = 0 時它正好在撞擊點本身。
                float u = (d - speed * age) / crest;
                // 3 個波峰寬之外什麼都沒有：前面還沒走到、後面已經走過去了。
                if (u > 3f || u < -3f) continue;
                float life = Mathf.Clamp01(1f - age / LifeSeconds);
                float h = w.Amplitude * Mathf.Exp(-u * u)
                        * (1f / (1f + d / spread))   // 攤得越開越低
                        * life * life;
                o.Height += h;
                float ww = Mathf.Abs(h);
                r += w.Tint.r * ww; g += w.Tint.g * ww; b += w.Tint.b * ww;
                a += w.Tint.a * ww; weight += ww;
                // 倒的方向由**貢獻最大的那一道**決定。兩道波同時掃到同一根的時候，
                // 用平均會讓它站直，那反而是三者之中最難看的一個。
                if (ww > domWeight) { domWeight = ww; o.DomX = w.X; o.DomDist = d; }
            }

            if (weight <= 1e-5f) return o;
            o.Any = true;
            o.Tint = new Color(r / weight, g / weight, b / weight, a / weight);
            return o;
        }

        private void Fill(float trackWidth, float now)
        {
            vertices.Clear();
            colours.Clear();
            uv.Clear();
            triangles.Clear();
            if (trackWidth <= 0.001f) return;

            float peak = trackWidth * PeakShare;
            float ceiling = peak * ChordCeiling;
            float speed = trackWidth * SpeedShare;
            float crest = Mathf.Max(0.001f, trackWidth * CrestShare);
            float spread = Mathf.Max(0.001f, trackWidth * SpreadShare);
            float half = trackWidth * 0.5f;

            float laneWidth = trackWidth / PianoVisualLayout.LegacyLaneCount;
            float pitch = Mathf.Max(0.01f, laneWidth / RaysPerLane);
            float rayHalf = laneWidth * RayWidthLanes * 0.5f;
            int count = Mathf.CeilToInt(trackWidth / pitch);

            // ---- 第一層：光線之間那片連續的薄光 ----
            //
            // 先畫，因為它是墊在底下的。加法混合其實不在乎順序，但讀程式的人在乎。
            float hazeStep = pitch / Mathf.Max(1, HazePerPitch);
            int hazeCount = Mathf.CeilToInt(trackWidth / hazeStep);
            int hazeAt = vertices.Count;
            int emitted = 0;
            for (int i = 0; i <= hazeCount; i++)
            {
                float x = -half + i * hazeStep;
                Sample sm = SampleAt(x, now, speed, crest, spread);
                float h = sm.Any
                    ? SoftCeiling(sm.Height, peak, ceiling) * HazeHeight
                    : 0f;
                Color tint = sm.Tint;
                tint.a *= HazeAlpha
                        * Mathf.Pow(Mathf.Clamp01(Mathf.Abs(h) / (peak * HazeHeight)), 0.6f);

                // 不跳過沒有波的那幾段 —— 薄光必須是**連續**的一條，中間缺一塊
                // 就會看到接縫。沒有波的地方高度是 0，那一格自然是退化的，
                // 不會畫出任何像素。
                vertices.Add(new Vector3(x, 0f, 0f));
                colours.Add(tint);
                uv.Add(new Vector2(0f, 0f));
                vertices.Add(new Vector3(x, h, 0f));
                colours.Add(tint);
                uv.Add(new Vector2(0f, 1f));
                emitted++;
            }
            for (int i = 0; i < emitted - 1; i++)
            {
                int p = hazeAt + i * 2;
                int q = p + 2;
                triangles.Add(p); triangles.Add(p + 1); triangles.Add(q + 1);
                triangles.Add(p); triangles.Add(q + 1); triangles.Add(q);
            }

            // ---- 第二層：站在薄光裡的那一根一根 ----
            for (int ray = 0; ray <= count; ray++)
            {
                float x = -half + ray * pitch;
                Sample sm = SampleAt(x, now, speed, crest, spread);
                if (!sm.Any) continue;

                float height = SoftCeiling(sm.Height, peak, ceiling)
                             * (RayJitter + (1f - RayJitter) * Hash01(ray));
                if (Mathf.Abs(height) < peak * 0.02f) continue;

                Color tint = sm.Tint;
                // 矮的光線要淡，不是「一樣亮但比較矮」—— 高度和亮度一起走，
                // 強弱才讀得出來；只動高度的話弱音和強音看起來一樣響，只是矮一點。
                tint.a *= Mathf.Pow(Mathf.Clamp01(Mathf.Abs(height) / peak), 0.6f);

                float lean = Mathf.Sign(x - sm.DomX) * Mathf.Abs(height) * RayLean
                           * Mathf.Clamp01(sm.DomDist / spread);
                Ray(x, rayHalf * (0.72f + 0.56f * Hash01(ray * 7 + 3)), height, lean, tint);
            }
        }

        /// <summary>一根站在判定線上的光線。</summary>
        /// <remarks>
        /// 三排 x 兩層，中間那一排 uv.x = 0。**中間那一排是必要的**：shader 的橫向
        /// 衰減是 1 - uv.x²，只有左右兩排的話兩邊都是 1，整根被算成透明。
        /// 這個坑在這個專案裡已經踩過兩次（見 LaneStrikeCue.Strip、
        /// PedalNoteRenderer 的光暈）。
        ///
        /// uv.y 0 -> 1 是往上淡出，交給 shader 逐像素算 —— 頂點色只能線性內插，
        /// 輪廓會是硬邊，而硬邊會被眼睛讀成一個有形狀的物件，不是光。
        /// </remarks>
        private void Ray(float x, float halfWidth, float height, float lean, Color tint)
        {
            int at = vertices.Count;
            for (int col = 0; col < 3; col++)
            {
                float dx = (col - 1) * halfWidth;
                float across = col == 1 ? 0f : 1f;
                vertices.Add(new Vector3(x + dx, 0f, 0f));
                colours.Add(tint);
                uv.Add(new Vector2(across, 0f));
                vertices.Add(new Vector3(x + dx + lean, height, 0f));
                colours.Add(tint);
                uv.Add(new Vector2(across, 1f));
            }
            for (int col = 0; col < 2; col++)
            {
                int p = at + col * 2;
                int q = p + 2;
                triangles.Add(p); triangles.Add(p + 1); triangles.Add(q + 1);
                triangles.Add(p); triangles.Add(q + 1); triangles.Add(q);
            }
        }

        /// <summary>同一根光線每次都要拿到同一個值，所以是雜湊不是亂數。</summary>
        private static float Hash01(int i)
        {
            uint h = (uint)i * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFFFFu) / (float)0xFFFFFF;
        }

        /// <summary>
        /// 一道以內原封不動，超過才收斂到 <paramref name="ceiling"/>。
        /// </summary>
        private static float SoftCeiling(float value, float knee, float ceiling)
        {
            float a = Mathf.Abs(value);
            if (a <= knee) return value;
            float room = Mathf.Max(0.001f, ceiling - knee);
            float over = knee + room * (1f - Mathf.Exp(-(a - knee) / room));
            return value < 0f ? -over : over;
        }

        private void Upload()
        {
            mesh.Clear();
            if (triangles.Count == 0)
            {
                view.enabled = false;
                return;
            }
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            view.enabled = true;
        }

        private void Build()
        {
            mesh = new Mesh { name = "JudgmentLineWave", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();

            var host = new GameObject("Wave");
            host.transform.SetParent(transform, false);
            host.hideFlags = HideFlags.DontSave;
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Shader shader = Shader.Find("Nostalgia/FlatUnlitAdditive");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            renderer.sharedMaterial = new Material(shader)
            {
                name = "JudgmentLineWave (Runtime)",
                hideFlags = HideFlags.DontSave,
                // 判定線是 Transparent+30（3030），光柱是 Overlay（4000）。
                // 夾在中間：波要蓋過它自己掀起來的那條線，但撞擊點那一段要讓光柱
                // 蓋住 —— 波是從光柱後面冒出來往外跑的，順序本身就在說這件事。
                renderQueue = 3040,
            };
            renderer.enabled = false;
            view = renderer;
        }
    }
}
