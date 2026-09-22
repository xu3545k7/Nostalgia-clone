using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Adds a restrained, camera-space layer of floating gold dust and gives the
/// existing URP image a warm concert-hall finish. It is created at runtime so
/// old scenes and song data do not need to be migrated.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassicalAtmosphere : MonoBehaviour
{
    private static Material particleMaterial;
    private static Material gradientSkyboxMaterial;

    private struct DustLayer
    {
        public ParticleSystem system;
        public Color startLow;
        public Color startHigh;
        public Gradient lifetime;
    }

    private static readonly System.Collections.Generic.List<DustLayer> dustLayers =
        new System.Collections.Generic.List<DustLayer>();
    private static float appliedHueShift;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<ClassicalAtmosphere>() != null) return;
        new GameObject("Classical Atmosphere").AddComponent<ClassicalAtmosphere>();
    }

    private IEnumerator Start()
    {
        Camera camera = null;
        while (camera == null)
        {
            camera = Camera.main;
            yield return null;
        }

        transform.SetParent(camera.transform, false);
        CreateGradientBackdrop(camera);
        float depth = Mathf.Max(camera.nearClipPlane + 10f, camera.farClipPlane * 0.88f);
        float height = 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = height * camera.aspect;
        float perspectiveScale = Mathf.Max(1f, depth / 35f);

        // 塵埃只存在於光柱裡。光柱本身畫在 UI 背景層（見 StageLightRig），這裡
        // 只放粒子；兩邊讀同一份 StageLightLayout，所以錐體位置一定對得上。
        // 一道光柱一組發射器就夠：十一道再各分兩層就是二十二個粒子系統，
        // 拿一個更寬的尺寸範圍換掉那一半的開銷。
        StageBeam[] beams = StageLightLayout.Build(width / height);
        float halfHeight = height * 0.5f;
        for (int i = 0; i < beams.Length; i++)
        {
            CreateDustLayer("Gold Dust " + i, 5.5f * beams[i].strength,
                0.025f * perspectiveScale, 0.12f * perspectiveScale, 9f, 0.30f,
                depth, beams[i], halfHeight);
        }

        CreateWarmGrade();
    }

    /// <summary>
    /// One tier of motes, emitted inside a single beam rather than across the
    /// whole frame.
    /// </summary>
    /// <remarks>
    /// A box the size of the frustum spreads the same budget of particles over
    /// the entire screen, where each one is an isolated speck. Dust is only
    /// legible as dust where a light picks it out, so the emitter is the cone
    /// itself: same count, concentrated where the eye has a reason to look.
    /// </remarks>
    private void CreateDustLayer(string layerName, float rate, float minSize,
        float maxSize, float lifetime, float alpha, float depth, StageBeam beam, float halfHeight)
    {
        var go = new GameObject(layerName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(beam.apex.x * halfHeight,
                                                 beam.apex.y * halfHeight, depth);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, beam.tiltDegrees);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime * 1.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.07f);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.maxParticles = Mathf.CeilToInt(rate * lifetime * 1.35f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.82f, 0.50f, 0.16f, alpha * 0.45f),
            new Color(1.00f, 0.82f, 0.42f, alpha));

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        // ConeVolume 才會填滿整個錐體；Cone 只在底面那一圈生。
        shape.shapeType = ParticleSystemShapeType.ConeVolume;
        // 繞 X 轉 90 度把預設的 +Z 發射方向轉成 -Y，也就是往下照。
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.radius = beam.topRadius * halfHeight;
        shape.length = beam.length * halfHeight;
        shape.angle = Mathf.Atan2(beam.baseRadius - beam.topRadius, beam.length) * Mathf.Rad2Deg;

        // 塵埃在光柱裡是慢慢往下沉的，不是靜止的。
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        // 三軸都要明寫。只設 y 會讓 x/z 留在單常數模式，Unity 就會拒收：
        // "Particle Velocity curves must all be in the same mode"。
        velocity.x = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.08f, -0.02f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.005f, 0.005f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        noise.frequency = 0.18f;
        noise.scrollSpeed = 0.08f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, .72f, .28f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .18f), new GradientAlphaKey(.7f, .72f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        // Background ambience must never cover notes, hit meshes or UI.
        renderer.sortingOrder = -200;
        renderer.material = GetParticleMaterial();
        ps.Play();

        dustLayers.Add(new DustLayer
        {
            system = ps,
            startLow = new Color(0.82f, 0.50f, 0.16f, alpha * 0.45f),
            startHigh = new Color(1.00f, 0.82f, 0.42f, alpha),
            lifetime = gradient,
        });
        // 這一層是新建的，還沒吃到目前的染色。
        ApplyDustHue(appliedHueShift, true);
    }

    /// <summary>
    /// 把金色塵埃沿色相環轉幾度。調性驅動的背景顏色是靠這個被看見的——
    /// 天空盒暗到幾乎全黑（亮度 0.1 上下），真正在畫面上有面積的是這些粒子。
    /// </summary>
    /// <remarks>
    /// 轉的是**出廠顏色**而不是當下顏色，所以反覆呼叫不會越轉越遠。
    /// 飽和度和明度都不動：金色是這個遊戲的識別，調性只該讓它偏，不該讓它變成另一種東西。
    /// </remarks>
    public static void ApplyDustHue(float hueShiftDegrees, bool force = false)
    {
        if (!force && Mathf.Approximately(appliedHueShift, hueShiftDegrees)) return;
        appliedHueShift = hueShiftDegrees;

        for (int i = dustLayers.Count - 1; i >= 0; i--)
        {
            var layer = dustLayers[i];
            if (layer.system == null) { dustLayers.RemoveAt(i); continue; }

            var main = layer.system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                ShiftHue(layer.startLow, hueShiftDegrees),
                ShiftHue(layer.startHigh, hueShiftDegrees));

            var colorModule = layer.system.colorOverLifetime;
            var keys = layer.lifetime.colorKeys;
            var shifted = new GradientColorKey[keys.Length];
            for (int k = 0; k < keys.Length; k++)
                shifted[k] = new GradientColorKey(ShiftHue(keys[k].color, hueShiftDegrees), keys[k].time);
            var gradient = new Gradient();
            gradient.SetKeys(shifted, layer.lifetime.alphaKeys);
            colorModule.color = gradient;
        }
    }

    private static Color ShiftHue(Color source, float degrees)
    {
        Color.RGBToHSV(source, out float h, out float s, out float v);
        // 白色（飽和度 0）轉色相不會有任何變化，直接留著——塵埃出生時就是白的。
        if (s <= 0.001f) return source;
        Color shifted = Color.HSVToRGB(Mathf.Repeat(h + degrees / 360f, 1f), s, v);
        shifted.a = source.a;
        return shifted;
    }

    private void CreateGradientBackdrop(Camera camera)
    {
        // A skybox is guaranteed to render behind every chart object regardless
        // of the song's coordinate range. Never use a camera-facing opaque quad here.
        if (gradientSkyboxMaterial == null)
        {
            Shader shader = Shader.Find("Custom/ClassicalGradientSkybox");
            if (shader == null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.008f, 0.012f);
                return;
            }
            gradientSkyboxMaterial = new Material(shader) { name = "Classical Burgundy Gradient (Runtime)" };
        }
        RenderSettings.skybox = gradientSkyboxMaterial;
        camera.clearFlags = CameraClearFlags.Skybox;
    }

    private static Material GetParticleMaterial()
    {
        if (particleMaterial != null) return particleMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        particleMaterial = new Material(shader) { name = "Classical Gold Dust (Runtime)" };
        Texture2D softDisc = CreateSoftDisc();
        if (particleMaterial.HasProperty("_BaseMap")) particleMaterial.SetTexture("_BaseMap", softDisc);
        if (particleMaterial.HasProperty("_MainTex")) particleMaterial.SetTexture("_MainTex", softDisc);
        MakeTransparent(particleMaterial, false);
        particleMaterial.renderQueue = 3100;
        return particleMaterial;
    }

    /// <summary>
    /// Switches a URP particle material to a blended surface, by hand.
    /// </summary>
    /// <remarks>
    /// URP declares <c>_SrcBlend = One</c>, <c>_DstBlend = Zero</c> and
    /// <c>_ZWrite = On</c> as the shader defaults, and only the material
    /// inspector's ShaderGUI ever rewrites them. Setting <c>_Surface</c> and
    /// the keyword from script -- which is what this file did -- changes what
    /// the material claims to be while it still renders fully opaque, so every
    /// falloff painted into a texture or a vertex colour is thrown away. That
    /// is why the beams came out as flat gold bars with a rectangle on top.
    /// </remarks>
    private static void MakeTransparent(Material material, bool additive)
    {
        var source = UnityEngine.Rendering.BlendMode.SrcAlpha;
        var destination = additive
            ? UnityEngine.Rendering.BlendMode.One
            : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", additive ? 2f : 0f);
        material.SetFloat("_SrcBlend", (float)source);
        material.SetFloat("_DstBlend", (float)destination);
        material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)destination);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
    }

    private static Texture2D CreateSoftDisc()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Soft Gold Disc" };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + .5f) / size * 2f - 1f;
            float dy = (y + .5f) / size * 2f - 1f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
            pixels[y * size + x] = new Color(1f, .78f, .35f, a);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void CreateWarmGrade()
    {
        var volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.weight = 1f;
        volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

        var bloom = volume.profile.Add<Bloom>(true);
        // 1.05 was low enough that ordinary overlaps bloomed. The playfield is
        // full of additive overlays, and where two of them cross, their sum can
        // pass the threshold even though neither is close to it alone: the
        // judgment line contributes 0.94 and a track border 0.35, and the two
        // points where the borders meet the line came out at 1.29 — two small
        // crossings that bloom turned into balls of light parked at the bottom
        // corners of the track, matching no object in the scene.
        //
        // 1.30 leaves everything meant to glow glowing (hit effects land near
        // 1.8, a pressed pedal cue near 3.6) and stops incidental sums.
        bloom.threshold.Override(1.30f);
        bloom.intensity.Override(0.32f);
        bloom.scatter.Override(0.62f);

        var color = volume.profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(-0.06f);
        color.contrast.Override(8f);
        color.colorFilter.Override(new Color(1f, 0.94f, 0.84f));
        color.saturation.Override(-4f);

        var vignette = volume.profile.Add<Vignette>(true);
        vignette.color.Override(new Color(0.06f, 0.018f, 0.008f));
        vignette.intensity.Override(0.24f);
        vignette.smoothness.Override(0.72f);
    }
}
