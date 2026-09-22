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
    /// * 誤觸：紅色、加法混合、明顯更長。它要在餘光裡就被接到，因為玩家不會特地去
    ///   找自己按錯了哪一鍵。
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
        private Mesh hitMesh;
        private Mesh wrongMesh;

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

        /// <summary>按錯了。高、久、紅色發光。</summary>
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
            if (hitRenderer == null || wrongRenderer == null) return;

            if (cues.Count == 0)
            {
                hitRenderer.enabled = false;
                wrongRenderer.enabled = false;
                return;
            }

            if (!TryPlaceAtJudgmentLine()) return;

            float trackWidth = PianoVisualLayout.ResolveTrackWidth(null);
            Fill(false, trackWidth, now);
            Upload(hitMesh, hitRenderer);
            Fill(true, trackWidth, now);
            Upload(wrongMesh, wrongRenderer);
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
            // 而它躺下來之後那個量就是長度）。誤觸那一條**不跟著調** —— 它是警
            // 報，不是裝飾，玩家不該能把它調到看不見。
            float scale = 1f;
            try
            {
                var settings = SettingsManager.Instance;
                if (settings != null) scale = settings.StrikeCueHeight;
            }
            catch { }
            if (!wrong && scale <= 0.001f) return;

            float reach = trackWidth * (wrong ? 0.34f : 0.26f * scale);

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
            hitMesh.MarkDynamic();
            wrongMesh.MarkDynamic();

            // 打中的那一份**不發光**：平的頂點色 shader，泛光碰不到它，所以它永遠只
            // 佔自己那一格的寬度。誤觸走加法，才接得到餘光。
            hitRenderer = Attach("Hit", hitMesh, "Nostalgia/FlatUnlitVertexColour");
            wrongRenderer = Attach("Wrong", wrongMesh, "Nostalgia/FlatUnlitAdditive");
        }

        private MeshRenderer Attach(string name, Mesh mesh, string shaderName)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            host.hideFlags = HideFlags.DontSave;
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Shader shader = Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find("Sprites/Default");
            // **畫在音符底下。**
            //
            // 音符是 Transparent（3000 起跳）的精靈，所以這裡壓到 2990：提示被
            // 音符蓋住是對的 —— 它是襯在底下的一層確認，不是蓋在譜面上的東西。
            // 3040 那個值會讓它壓過長條，變成畫面上最搶眼的東西。
            renderer.sharedMaterial = new Material(shader)
            {
                name = name + " (Runtime)",
                hideFlags = HideFlags.DontSave,
                renderQueue = 2990,
            };
            renderer.enabled = false;
            return renderer;
        }
    }
}
