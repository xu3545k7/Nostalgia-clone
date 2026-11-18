using System.Collections.Generic;
using UnityEngine;

namespace Effects
{
    /// <summary>
    /// 從判定線往譜面方向鋪出去的一小段提示，躺在被按到的那一個鍵道上。
    /// </summary>
    /// <remarks>
    /// **為什麼放在軌道上。** 玩家的眼睛在軌道上，不在鍵盤上 —— 鍵盤在畫面最下緣，
    /// 而他正在讀的東西從上面掉下來。把回饋只放在鍵盤上，等於放在他不看的地方。
    ///
    /// **兩種提示刻意用兩種材質。**
    ///
    /// * 按鍵：淡綠色、不發光，按下去就出現、不等判定。它只確認「機器收到了」，
    ///   不該和打擊特效搶注意力；不發光的東西在泛光底下不會擴散，所以它永遠只佔
    ///   自己那一格的寬度。
    /// * 誤觸：紅色、加法混合、明顯更長，而且在判定線上**那一格**多一圈紅光暈。
    ///   它要在餘光裡就被接到，因為玩家不會特地去找自己按錯了哪一鍵。
    ///
    /// **兩種提示的長度各有自己的一列設定**（<c>StrikeCueHeight</c> 和
    /// <c>MistouchCueHeight</c>）。原本誤觸那一條完全不跟著調，理由是「它是警報，
    /// 玩家不該能把它調到看不見」—— 理由對，解法錯：兩條回答的是不同的問題
    /// （「機器收到了嗎」和「你按錯了」），把確認調到剛好的長度，完全不代表警報
    /// 也剛好。警報的保證現在由判定線上那圈光暈接手（<see cref="FaultHalo"/>，
    /// 不吃任何設定），所以帶子的長度可以放手交給玩家。
    ///
    /// 一份網格畫完所有還活著的提示，所以無論同時有幾個都是一次繪製。
    /// </remarks>
    [DefaultExecutionOrder(600)]
    public sealed class LaneStrikeCue : MonoBehaviour
    {
        /// <summary>同時最多留幾個。超過就蓋掉最舊的 —— 排隊等著顯示的回饋沒有意義。</summary>
        private const int Capacity = 24;

        private const float HitSeconds = 0.30f;
        private const float WrongSeconds = 0.55f;

        /// <summary>紅光暈的半寬，以**那一格自己的寬度**為單位。</summary>
        /// <remarks>
        /// 比一格寬是刻意的：光暈要溢出那一格，才讀得出是「光」而不是「隔壁那
        /// 一格也亮了」。一格以內的話它和按鍵提示長得一樣寬，兩件事就混在一起了。
        /// </remarks>
        private const float HaloLanes = 1.6f;

        /// <summary>紅光暈半寬的下限，佔軌道寬的比例。</summary>
        /// <remarks>
        /// 88 鍵模式下一格只有一般模式的三分之一寬，<see cref="HaloLanes"/> 乘完
        /// 還是一條細線 —— 而警報的大小不該由玩家選了哪個顯示模式決定。
        /// </remarks>
        private const float HaloMinShare = 0.035f;

        /// <summary>紅光暈從判定線往前後各散多遠，**以音符高為單位**。</summary>
        /// <remarks>
        /// 軌道是躺著的：同一個世界長度橫向大約是縱向的 2.5 倍像素。所以橫向的
        /// 半寬用鍵道寬算（<see cref="HaloLanes"/>），縱向這一軸用音符高算 ——
        /// 拿軌道寬的比例來量縱向距離，就是踏板光暈那個「一片厚霧」的來源。
        ///
        /// 1.1 個音符高（預設 2.2 個世界單位）往前後各散，配上約三格鍵道的寬度，
        /// 畫面上是一道**壓扁的**紅光。那正是要的：它要讀成「判定線在這裡紅了」，
        /// 而不是「軌道上多了一顆紅球」。前後對稱，所以它不帶方向性，不會被誤讀
        /// 成「往哪一邊」。
        ///
        /// 跟著音符高走還有一個好處：玩家把音符調厚，光暈也跟著厚 —— 它量的是
        /// 「比旁邊那些東西厚多少」，而不是一個絕對的距離。
        /// </remarks>
        private const float HaloDepthNotes = 1.1f;

        /// <summary>光暈這一層比判定線高多少（host 自己已經 +0.02）。</summary>
        /// <remarks>
        /// 小節線是**不透明、會寫深度**的平面，就躺在音符那一層（判定線 +0.1）。
        /// 擺在它底下的話，每一條小節線掃過判定線都會把光暈切掉一塊 —— 看起來
        /// 像閃爍的 bug。+0.12 在它上面（和踏板那一層同一個理由，見
        /// PedalNoteRenderer.NoteLayerYOffset）。
        /// </remarks>
        private const float HaloLift = 0.10f;

        private struct Cue
        {
            public float Lane;        // 鍵道中心的世界 x
            public float Width;       // 鍵道寬度
            public float Until;       // 收掉的時刻（unscaled）
            public float Life;        // 總長度，用來算剩餘比例
            public bool Wrong;
        }

        private static LaneStrikeCue instance;

        private readonly List<Cue> cues = new List<Cue>(Capacity);
        private MeshRenderer hitRenderer;
        private MeshRenderer wrongRenderer;
        private MeshRenderer haloRenderer;
        private Mesh hitMesh;
        private Mesh wrongMesh;
        private Mesh haloMesh;

        private readonly List<Vector3> vertices = new List<Vector3>(512);
        private readonly List<Color> colours = new List<Color>(512);
        private readonly List<Vector2> uv = new List<Vector2>(512);
        private readonly List<int> triangles = new List<int>(768);

        public static LaneStrikeCue EnsureCreated()
        {
            if (instance != null) return instance;
            var host = new GameObject("LaneStrikeCue");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<LaneStrikeCue>();
            return instance;
        }

        /// <summary>
        /// 按下去了。低、短、不發光。
        /// </summary>
        /// <remarks>
        /// **這是按鍵提示，不是打擊提示。** 它在鍵被按下的那一刻出現，不等判定
        /// —— 玩家需要知道的第一件事是「機器收到我了」，而那件事和「這一下有沒
        /// 有打中音符」是兩回事，發生的時間也不同（判定要等一個操作幀）。
        ///
        /// 綁在判定上的話，沒打中的按鍵完全沒有反應，那正是「鍵盤壞了」的感覺
        /// —— 而在 Hardcore 模式下那一下連聲音都沒有。
        /// </remarks>
        public static void Press(int lane) => Show(lane, false);

        /// <summary>按錯了。長、久、紅色發光，判定線上還多一圈紅光暈。</summary>
        public static void Wrong(int lane) => Show(lane, true);

        private static void Show(int lane, bool wrong)
        {
            try
            {
                float trackWidth = PianoVisualLayout.ResolveTrackWidth(null);
                if (trackWidth <= 0.001f) return;

                // 用一顆臨時的 NoteData 去問位置，而不是自己算 —— 鍵道到 x 的
                // 換算在 88 鍵模式和一般模式底下是兩套（一個看音高、一個看鍵道）。
                //
                // **而且要把實際按到的音高填進去。** 少了它，88 鍵模式下這顆探針
                // 會被當成「沒有音高」，於是拿到一般模式的 28 格寬度 —— 位置偏
                // 掉、寬度變成三倍，相鄰兩格直接黏成一片，左右的縫就消失了。
                PianoKeysound.DescribeLaneInput(lane, out int pitch, out _);
                var probe = new NoteData { startLane = lane, endLane = lane };
                if (pitch >= 0) probe.pitch = pitch;

                float centre = PianoVisualLayout.ResolveCenterX(probe, trackWidth);
                // 寬度用**音符自己的**那一個，所以提示和音符的縫對得齊 —— 兩套
                // 算式各留各的縫，看起來就是兩個不相干的東西各自對不準。
                float width = PianoVisualLayout.ResolveVisualWidth(probe, trackWidth);

                LaneStrikeCue view = EnsureCreated();
                if (view.cues.Count >= Capacity) view.cues.RemoveAt(0);
                float life = wrong ? WrongSeconds : HitSeconds;
                view.cues.Add(new Cue
                {
                    Lane = centre,
                    Width = width,
                    Until = Time.unscaledTime + life,
                    Life = life,
                    Wrong = wrong,
                });
            }
            catch { }
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            for (int i = cues.Count - 1; i >= 0; i--)
                if (cues[i].Until <= now) cues.RemoveAt(i);

            if (hitRenderer == null) Build();
            if (hitRenderer == null || wrongRenderer == null || haloRenderer == null) return;

            if (cues.Count == 0)
            {
                hitRenderer.enabled = false;
                wrongRenderer.enabled = false;
                if (haloRenderer != null) haloRenderer.enabled = false;
                return;
            }

            if (!TryPlaceAtJudgmentLine()) return;

            float trackWidth = PianoVisualLayout.ResolveTrackWidth(null);
            Fill(false, trackWidth, now);
            Upload(hitMesh, hitRenderer);
            Fill(true, trackWidth, now);
            Upload(wrongMesh, wrongRenderer);
            FaultHalo(trackWidth, now);
            Upload(haloMesh, haloRenderer);
        }

        /// <summary>
        /// 把兩份網格擺到判定線上。
        /// </summary>
        /// <remarks>
        /// 每幀重新問：判定線的高度是玩家可以改的設定，而這個提示貼著它才有意義 ——
        /// 記一次然後一直用，會在他調高度的當下就對不上。
        /// </remarks>
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

        private void Fill(bool wrong, float trackWidth, float now)
        {
            vertices.Clear();
            colours.Clear();
            uv.Clear();
            triangles.Clear();

            // 長度可調（設定名是「高度」，因為玩家調的是「這個提示有多大」，
            // 而它躺下來之後那個量就是長度）。**兩條各吃自己那一列** —— 為什麼
            // 分開，見本類別的說明。
            float scale = 1f;
            try
            {
                var settings = SettingsManager.Instance;
                if (settings != null)
                    scale = wrong ? settings.MistouchCueHeight : settings.StrikeCueHeight;
            }
            catch { }
            if (scale <= 0.001f) return;

            // 誤觸原本的長度（0.34）是在「不跟著調」的前提下訂的，所以它就是
            // scale = 1 的樣子：預設值不動，調過的人才會看到差別。
            float reach = trackWidth * (wrong ? 0.34f : 0.26f) * scale;

            // **從判定線本身長出來，不留空隙。**
            //
            // 上一版在這裡留了 2.2% 軌道寬的縫，那是誤解：「要有縫隙」講的是提示
            // 和**音符**之間，而那個分離已經由 renderQueue 2990（畫在音符底下）
            // 和 y 方向的抬高做到了。
            //
            // 按鍵提示的意義就是「判定線上這一格被按了」。它離開那條線就失去了
            // 錨點 —— 漂在軌道中間的一段綠色，讀不出它在指哪裡。
            //
            // 誤觸那一條留一點點，純粹是為了和按鍵那一條錯開；同時按到又判成誤
            // 觸的時候，兩條完全重疊會只看得到上面那一條。
            float gap = wrong ? trackWidth * 0.010f : 0f;

            for (int i = 0; i < cues.Count; i++)
            {
                Cue cue = cues[i];
                if (cue.Wrong != wrong) continue;

                float left = Mathf.Clamp01((cue.Until - now) / Mathf.Max(0.001f, cue.Life));
                // 誤觸開頭幾乎不衰減，後段才退 —— 它要在發生的那一下就被看見。
                // 按鍵的則是平滑地來、平滑地走：它只是確認，不是警報。
                float fade = wrong ? Mathf.Sqrt(left) : left * left;
                // 伸出來的那一下比較快，收回去比較慢。
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - left) / 0.25f));

                // 按鍵是**淡綠色** —— 和鍵盤上被按下的那一顆同一個顏色。同一件
                // 事在兩個地方發生，就該是同一個顏色，玩家不必學第二套對照。
                Color colour = wrong
                    ? new Color(2.6f, 0.16f, 0.12f, 0.85f * fade)
                    : new Color(0.52f, 1.00f, 0.62f, 0.60f * fade);

                // cue.Width 已經是**扣掉縫之後**的音符寬度，所以這裡直接取一半。
                // 再乘一個 0.34 的話等於在已經留好的縫上再縮一次，提示會比音符細
                // 一半以上，看起來不像同一格的東西。
                float half = cue.Width * 0.5f * (wrong ? 1f : 0.92f);
                Strip(cue.Lane, half, gap, reach * grow * (0.4f + 0.6f * fade), colour);
            }
        }

        /// <summary>
        /// 誤觸的那一格，判定線上一圈紅光暈。
        /// </summary>
        /// <remarks>
        /// **為什麼除了帶子還要這個。** 那條紅帶子躺在軌道上、從判定線往譜面
        /// 深處鋪出去，所以它和「音符掉下來的那條跑道」在同一個區域裡，而玩家
        /// 的注意力在那個區域是給音符的。按錯的時候他正在讀的是下一顆音符，紅
        /// 帶子落在他的餘光裡，和一堆正在落下的東西混在一起。
        ///
        /// 判定線不一樣：那是畫面上唯一一條**不動**的線，而不動的東西變色，
        /// 餘光一定接得到。所以警報分兩份 —— 帶子說「是哪一格」，光暈說
        /// 「你按錯了」。
        ///
        /// **為什麼是光暈不是把整條線閃紅。** 整條線一起閃是
        /// <see cref="JudgmentLineGlow"/> 已經證明會變成壁紙的做法（它連鍵道
        /// 參數都沒有，每次都一樣）—— 一個每次都一樣的反應不帶任何資訊。這一圈
        /// 只亮在按錯的那一格上，所以它同時回答了「有錯」和「錯在哪」。
        ///
        /// 兩片共用 <see cref="Strip"/>（第二片的 reach 是負的），所以光暈和帶子
        /// 的形狀永遠不會分岔，而且前後對稱的淡出完全交給 shader 逐像素算。
        /// </remarks>
        private void FaultHalo(float trackWidth, float now)
        {
            vertices.Clear();
            colours.Clear();
            uv.Clear();
            triangles.Clear();

            float noteHeight = 2f;
            try
            {
                var settings = SettingsManager.Instance;
                if (settings != null) noteHeight = settings.NoteVisualHeight;
            }
            catch { }
            float depth = noteHeight * HaloDepthNotes;
            for (int i = 0; i < cues.Count; i++)
            {
                Cue cue = cues[i];
                if (!cue.Wrong) continue;

                float left = Mathf.Clamp01((cue.Until - now) / Mathf.Max(0.001f, cue.Life));
                // 警報是一記重擊，不是一段延續：發生的那一瞬間就滿，然後退掉。
                // 帶子走的是相反的曲線（sqrt，退得很慢），所以它留在那裡當「是
                // 哪一格」的答案，光暈只負責那一下的衝擊。
                float punch = left * left;
                // 加法混合進 framebuffer 的是 rgb × alpha，而 bloom 的門檻是 0.8
                // —— 紅色配到 3.0 才有超出的部分可以被挑出來發光。
                Color colour = new Color(3.0f, 0.18f, 0.13f, 0.90f * punch);

                float half = Mathf.Max(cue.Width * HaloLanes, trackWidth * HaloMinShare);
                Strip(cue.Lane, half, 0f, depth, colour);
                Strip(cue.Lane, half, 0f, -depth, colour);
            }
        }

        /// <summary>
        /// 一條**躺在軌道上**的短帶，從判定線往玩家這一側伸出去。
        /// </summary>
        /// <remarks>
        /// **平行於軌道，不是立起來的。** 立起來的柱子是一個站在軌道上的物件，它
        /// 有自己的體積、會擋到後面的東西、而且會隨鏡頭角度改變形狀；躺著的帶子
        /// 是軌道表面的一部分，它和音符共用同一個平面，所以讀起來是「這一格亮了
        /// 一下」而不是「這裡多了一根東西」。
        ///
        /// 三排 x 兩層，中間那一排 uv.x = 0。中間那一排是必要的：發光的那一份走
        /// 加法 shader，衰減是 1 - uv.x²，只有左右兩排的話兩邊都是 1、整條被算成
        /// 透明。不發光的那一份用不到 uv，但兩份共用同一個產生器，形狀才不會分岔。
        /// </remarks>
        private void Strip(float x, float half, float gap, float reach, Color colour)
        {
            Color far = new Color(colour.r, colour.g, colour.b, 0f);
            int at = vertices.Count;
            for (int col = 0; col < 3; col++)
            {
                float dx = (col - 1) * half;
                float across = col == 1 ? 0f : 1f;
                // 近端貼著判定線（讓開一條縫），遠端往**譜面深處**淡出。
                //
                // 往前而不是往後：譜面是從那個方向來的，所以那是玩家的視線**已
                // 經在看**的地方。往玩家這一側鋪的話，提示落在他剛剛看完、不會
                // 再回頭的區域，等於又放在他不看的地方。
                vertices.Add(new Vector3(x + dx, 0f, gap));
                colours.Add(colour);
                uv.Add(new Vector2(across, 0f));
                vertices.Add(new Vector3(x + dx, 0f, gap + reach));
                colours.Add(far);
                uv.Add(new Vector2(across, 1f));
            }
            for (int col = 0; col < 2; col++)
            {
                int a = at + col * 2;
                int b = a + 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(b + 1);
                triangles.Add(a); triangles.Add(b + 1); triangles.Add(b);
            }
        }

        private void Upload(Mesh mesh, MeshRenderer renderer)
        {
            mesh.Clear();
            if (triangles.Count == 0)
            {
                renderer.enabled = false;
                return;
            }
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            renderer.enabled = true;
        }

        private void Build()
        {
            hitMesh = new Mesh { name = "LaneStrikeHit", hideFlags = HideFlags.DontSave };
            wrongMesh = new Mesh { name = "LaneStrikeWrong", hideFlags = HideFlags.DontSave };
            haloMesh = new Mesh { name = "LaneStrikeHalo", hideFlags = HideFlags.DontSave };
            hitMesh.MarkDynamic();
            wrongMesh.MarkDynamic();
            haloMesh.MarkDynamic();

            // 打中的那一份**不發光**：平的頂點色 shader，泛光碰不到它，所以它永遠只
            // 佔自己那一格的寬度。誤觸走加法，才接得到餘光。
            //
            // 兩條帶子都是 2990：音符是 Transparent（3000 起跳）的精靈，所以帶子
            // 被音符蓋住是對的 —— 它是襯在底下的一層確認，不是蓋在譜面上的東西。
            hitRenderer = Attach("Hit", hitMesh, "Nostalgia/FlatUnlitVertexColour", 2990, 0f);
            wrongRenderer = Attach("Wrong", wrongMesh, "Nostalgia/FlatUnlitAdditive", 2990, 0f);
            // 光暈反過來：它要被看見，而判定線那一條是**畫面上最擠的一道**
            // （3000 音符／3020 長條／3030 判定線／4000 光柱 都在那裡畫）。壓在
            // 底下的東西在那一道上等於不存在，所以 3042 —— 蓋過判定線本身，
            // 讓在 4000 的判定光柱還是蓋在它上面。
            haloRenderer = Attach("Halo", haloMesh, "Nostalgia/FlatUnlitAdditive", 3042, HaloLift);
        }

        private MeshRenderer Attach(string name, Mesh mesh, string shaderName, int queue, float lift)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            if (lift != 0f) host.transform.localPosition = new Vector3(0f, lift, 0f);
            host.hideFlags = HideFlags.DontSave;
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Shader shader = Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find("Sprites/Default");
            // queue 由呼叫端決定 —— 帶子要被音符蓋住，光暈要蓋過判定線，兩件事
            // 在這一道上差很多。理由寫在 Build 的呼叫處。
            renderer.sharedMaterial = new Material(shader)
            {
                name = name + " (Runtime)",
                hideFlags = HideFlags.DontSave,
                renderQueue = queue,
            };
            renderer.enabled = false;
            return renderer;
        }
    }
}
