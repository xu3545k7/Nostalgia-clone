using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在開打之前先把每個材質畫一次，讓 shader 在那時候編譯，而不是在第一顆音上。
/// </summary>
/// <remarks>
/// 量出來的症狀非常乾淨：開頭 15 秒有 4 格超過 17ms（最糟 34.7ms、判定時鐘被拉開
/// 92ms），之後整首歌 max 一直是 7.0ms、一次都沒有超標，而且前後 GC 次數一樣。
/// 「只在最前面發生固定幾次、之後永遠不再發生、不是 GC」就是**第一次繪製才建立
/// 算繪管線**的指紋——這台跑 D3D12，每個材質第一次送出繪製時才建 PSO。
///
/// 這也解釋了為什麼把物件建立搬到載入時沒有用：預熱建的是 GameObject 和材質，
/// 但 PSO 是**畫下去**才建的，材質放在池子裡沒被畫過就等於沒暖。
///
/// 所以這裡真的去畫：把每個材質用一個次像素大小的四邊形送進主相機一次。次像素
/// 是為了看不見，但仍然在視錐內——被剔除掉就不會產生繪製呼叫，也就不會建 PSO。
///
/// 專案的 Graphics Settings 裡 Preloaded Shaders 是空的（<c>m_PreloadedShaders: []</c>），
/// 所以在此之前完全沒有任何預熱。
/// </remarks>
[DefaultExecutionOrder(-9000)]
public class GameplayShaderWarmup : MonoBehaviour
{
    /// <summary>同一批材質重畫幾格。一格通常就夠，多給幾格是為了保險。</summary>
    private const int WarmFrames = 3;

    /// <summary>四邊形放在近裁面外一點點，確保不會被裁掉。</summary>
    private const float DistanceFromCamera = 0.05f;

    /// <summary>邊長。夠小到看不見，又不是零——零面積可能被驅動整批丟掉。</summary>
    private const float WarmScale = 0.0002f;

    private Mesh quad;
    private readonly List<Material> materials = new List<Material>(128);
    private Coroutine warming;
    private bool sweptForChart;
    private bool sweptForPlay;
    private Chart lastChart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<GameplayShaderWarmup>() != null) return;
        var go = new GameObject("GameplayShaderWarmup");
        DontDestroyOnLoad(go);
        go.AddComponent<GameplayShaderWarmup>();
    }

    private void Awake()
    {
        quad = BuildQuad();
    }

    // ── 不要在這裡加 Shader.WarmupAllShaders() ──────────────────────────
    //
    // 試過了，它在 URP 上會直接讓 Unity 斷言失敗：那個函式會走遍**每一個** shader
    // 的每一個變體，而 URP 的 'Particles/Simple Lit'（49 個 keyword）和
    // 'Particles/Lit'（53 個）屬於不同的 local keyword space，切換過去時
    // LocalKeywordState::Remove 就報 "State comes from an incompatible keyword
    // space"，堆疊停在 WarmupOnePass → WarmupShadersImpl。那是 Unity 自己 warmup
    // 實作在 SRP 下的問題，跟這個專案的材質無關，也沒有參數可以繞過。
    //
    // 而且它本來就是多餘的保險：下面的 DrawMesh 走的是真正的算繪路徑，shader
    // 程式的載入與編譯本來就會在同一次繪製裡發生——正是我們要提前的那一次。

    private void Update()
    {
        if (warming != null) return;

        GameManager game = GameManager.Instance;
        Chart chart = game != null ? game.CurrentChart : null;
        Conductor conductor = game != null ? game.Conductor : null;

        if (chart != lastChart)
        {
            lastChart = chart;
            sweptForChart = false;
            sweptForPlay = false;
        }
        if (chart == null) return;

        // 掃兩次，因為材質是分兩批出現的。
        //
        // 第一次在**譜面剛載好**：音符池和節拍線池這時候才建起來，而先前只等到
        // conductor.isActive 才掃，log 顯示載入造成的那幾格算繪尖峰
        // （113.7 / 57.6 / 52.4 / 64.0ms，全都 scripts=0.0ms）在掃描之前就發生完了。
        //
        // 第二次在**開始播放時**：前奏期間才會出現的東西（背景、踏板音符、
        // 判定線）在第一次掃的時候還不存在。
        if (!sweptForChart)
        {
            sweptForChart = true;
            warming = StartCoroutine(WarmVisibleMaterials());
            return;
        }
        if (!sweptForPlay && conductor != null && conductor.isActive)
        {
            sweptForPlay = true;
            warming = StartCoroutine(WarmVisibleMaterials());
        }
    }

    private IEnumerator WarmVisibleMaterials()
    {
        using (HitchProbe.Measure("warmupCollect")) CollectMaterials();
        if (materials.Count == 0) { warming = null; yield break; }

        for (int frame = 0; frame < WarmFrames; frame++)
        {
            Camera camera = Camera.main;
            if (camera == null) break;

            // 相機正前方、近裁面之外一點點：一定在視錐內，所以不會被剔除。
            Vector3 position = camera.transform.position +
                               camera.transform.forward * (camera.nearClipPlane + DistanceFromCamera);
            Matrix4x4 matrix = Matrix4x4.TRS(position, camera.transform.rotation,
                                             Vector3.one * WarmScale);

            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                // DrawMesh 會走完整條算繪路徑，這正是建立 PSO 需要的；把材質
                // 放在停用的池子裡不會。
                //
                // 逐個包起來：粒子材質預期的是粒子的頂點串流，用普通四邊形畫它
                // 有可能被拒絕。那種材質暖不到就算了，不該讓其他材質也一起沒暖。
                try
                {
                    Graphics.DrawMesh(quad, matrix, material, 0, camera, 0, null, false, false, false);
                }
                catch { }
            }
            yield return null;
        }

        Debug.Log($"GameplayShaderWarmup: drew {materials.Count} gameplay materials before play.");
        materials.Clear();
        warming = null;
    }

    /// <summary>
    /// 蒐集所有已經存在的 Renderer 用到的材質，包含停用的池子。
    /// </summary>
    /// <remarks>
    /// <c>FindObjectsOfTypeAll</c> 而不是 <c>FindObjectsByType</c>：會被畫到的材質
    /// 幾乎都掛在**還沒啟用**的池子物件上（判定光效、擊中粒子、節拍線、音符），
    /// 只找啟用中的物件正好會漏掉所有需要暖的東西。
    ///
    /// 一首歌只掃一次——這個呼叫會走過所有載入的物件，不便宜。
    /// </remarks>
    private void CollectMaterials()
    {
        materials.Clear();
        var seen = new HashSet<Material>();
        Renderer[] renderers;
        try { renderers = Resources.FindObjectsOfTypeAll<Renderer>(); }
        catch { return; }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            // 專案外的資產（編輯器預覽、匯入的 prefab 本體）沒有場景，不會被畫到。
            if (!renderer.gameObject.scene.IsValid()) continue;

            Material[] shared = renderer.sharedMaterials;
            if (shared == null) continue;
            for (int m = 0; m < shared.Length; m++)
            {
                Material material = shared[m];
                if (material == null || material.shader == null) continue;
                if (!seen.Add(material)) continue;
                materials.Add(material);
            }
        }

        // 走訪 Renderer 抓不到還沒被掛上去的材質。Hold 的魔法陣就是這種：承載它
        // 的 SpriteRenderer 要等第一次碰到 Hold 才建出來，所以它是掃描之後唯一
        // 還會第一次被畫到的東西——量到的殘留尖峰正好就落在掃描之後。
        int before = materials.Count;
        try { ParticleEffectPlayer.CollectWarmupMaterials(materials); } catch { }
        for (int i = materials.Count - 1; i >= before; i--)
        {
            // 上面可能已經蒐集過同一個材質，去重。
            if (seen.Add(materials[i])) continue;
            materials.RemoveAt(i);
        }
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "ShaderWarmupQuad", hideFlags = HideFlags.DontSave };
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),   new Vector3(-0.5f, 0.5f, 0f),
        });
        mesh.SetUVs(0, new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
        });
        mesh.SetNormals(new List<Vector3>
        {
            -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward,
        });
        // 頂點色：判定光效那條路徑靠頂點色做漸層，沒有的話暖到的會是另一個變體。
        mesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (quad != null) Destroy(quad);
    }
}
