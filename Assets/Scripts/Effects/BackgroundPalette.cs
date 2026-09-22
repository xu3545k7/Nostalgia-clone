using UnityEngine;

namespace Effects
{
    /// <summary>
    /// 從曲繪抽出背景用的配色。
    /// </summary>
    /// <remarks>
    /// 97 份註冊譜面全部都有封面，只有 9 份有 MV —— 所以「從封面長出背景」是唯一
    /// 能一次覆蓋整個曲庫、又不用畫任何新素材的做法。
    ///
    /// 抽的是**色相**，不是整張圖的平均色。平均色永遠是灰的（明暗互相抵消），而封面
    /// 的識別度來自它的色相。所以統計時用飽和度×明度加權：接近黑白的像素本來就沒有
    /// 顏色可言，不該參與投票。
    ///
    /// 明度刻意壓在 shader 原本的預設值附近（bottom 0.012 / horizon 0.095 / top 0.018）。
    /// 背景不是要變亮，是要變得「屬於這首歌」——判定線那條帶已經是全畫面最擠的地方，
    /// 背景一亮就開始搶注意力。
    /// </remarks>
    public static class BackgroundPalette
    {
        /// <summary>三段式漸層，對應 ClassicalGradientSkybox 的三個顏色。</summary>
        public struct Sky
        {
            public Color bottom;
            public Color horizon;
            public Color top;
            public float hue;          // 0..1，這首歌的基底色相
            public float saturation;   // 0..1
            public float brightness;   // 相對於「幾乎全黑」那個基準的倍率
            /// <summary>來源圖的平均亮度。判斷「這張圖是不是佔位用的純黑」就靠它。</summary>
            public float sourceLuminance;
        }

        // shader 預設值量出來的明度，維持同一個暗度層級。
        private const float BottomValue = 0.045f;
        private const float HorizonValue = 0.115f;
        private const float TopValue = 0.030f;
        // 封面再鮮豔，背景也只取這個上限——背景不該有前景的飽和度。
        private const float MaxSaturation = 0.55f;
        private const float MinSaturation = 0.18f;

        /// <summary>抽色用的取樣解析度。24×24 就足夠決定色相，而且一次讀回很便宜。</summary>
        private const int SampleSize = 24;

        public static Sky Fallback => Build(0.94f, 0.30f, 1f);   // 原本那個暗紅

        /// <summary>
        /// 從封面算出配色。讀不到就回 <see cref="Fallback"/>。
        /// </summary>
        /// <remarks>
        /// 走 RenderTexture blit 而不是直接 GetPixels：Resources 裡的封面幾乎都沒有
        /// 開 Read/Write，直接讀會丟例外。blit 到一張小 RT 再 ReadPixels 不管匯入
        /// 設定都能用，而且順便把圖縮到 24×24。
        /// </remarks>
        public static Sky FromCover(Texture cover)
        {
            if (cover == null) return Fallback;

            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                rt = RenderTexture.GetTemporary(SampleSize, SampleSize, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(cover, rt);
                RenderTexture.active = rt;
                readback = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, SampleSize, SampleSize), 0, 0, false);
                readback.Apply(false);
                return FromPixels(readback.GetPixels());
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BackgroundPalette] 讀不到封面色：{ex.Message}");
                return Fallback;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readback != null) Object.Destroy(readback);
            }
        }

        /// <summary>像素 → 配色。純函式，測試直接餵陣列進來。</summary>
        public static Sky FromPixels(Color[] pixels)
        {
            if (pixels == null || pixels.Length == 0) return Fallback;

            // 色相是環狀量，不能直接平均（350° 和 10° 的平均是 180°，完全錯的顏色）。
            // 改成把每個像素當成單位圓上的向量相加，再取合向量的角度。
            float x = 0f, y = 0f, satSum = 0f, weightSum = 0f, lumaSum = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                lumaSum += pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
                Color.RGBToHSV(pixels[i], out float h, out float s, out float v);
                float weight = s * v;              // 越接近黑白的像素越沒有發言權
                if (weight <= 0.0001f) continue;
                float radians = h * 2f * Mathf.PI;
                x += Mathf.Cos(radians) * weight;
                y += Mathf.Sin(radians) * weight;
                satSum += s * weight;
                weightSum += weight;
            }

            float luminance = lumaSum / pixels.Length;
            if (weightSum <= 0.0001f)
            {
                // 整張是黑白的（純黑佔位圖就是這一類）。色相沿用預設，但亮度要回報，
                // 呼叫端才知道這張圖不能拿來當背景。
                var grey = Fallback;
                grey.sourceLuminance = luminance;
                return grey;
            }

            float hue = Mathf.Repeat(Mathf.Atan2(y, x) / (2f * Mathf.PI), 1f);
            float saturation = Mathf.Clamp(satSum / weightSum, MinSaturation, MaxSaturation);
            var sky = Build(hue, saturation);
            sky.sourceLuminance = luminance;
            return sky;
        }

        /// <param name="brightness">
        /// 亮度倍率。1 = 原本那個「幾乎全黑」的層級，適合實心軌道。軌道玻璃化之後
        /// 後面露出來的就是這片天空，還維持 0.1 的亮度等於透明了也只看到黑色 ——
        /// 所以由呼叫端跟著玻璃化程度把它拉上來。
        /// </param>
        public static Sky Build(float hue, float saturation, float brightness = 1f)
        {
            hue = Mathf.Repeat(hue, 1f);
            saturation = Mathf.Clamp01(saturation);
            brightness = Mathf.Clamp(brightness, 0.2f, 6f);
            return new Sky
            {
                // 地平線是唯一帶顏色的那一段；上下都壓暗，讓顏色集中在跑道遠端的高度。
                bottom = Color.HSVToRGB(hue, saturation * 0.7f, Mathf.Clamp01(BottomValue * brightness)),
                horizon = Color.HSVToRGB(hue, saturation, Mathf.Clamp01(HorizonValue * brightness)),
                top = Color.HSVToRGB(hue, saturation * 0.5f, Mathf.Clamp01(TopValue * brightness)),
                hue = hue,
                saturation = saturation,
                brightness = brightness,
            };
        }

        /// <summary>把整套配色沿色相環轉幾度。調性偏移就是用這個表現的。</summary>
        public static Sky ShiftHue(Sky sky, float degrees)
        {
            var shifted = Build(sky.hue + degrees / 360f, sky.saturation,
                                sky.brightness <= 0f ? 1f : sky.brightness);
            shifted.sourceLuminance = sky.sourceLuminance;
            return shifted;
        }

        /// <summary>換一個亮度倍率，色相飽和度不動。</summary>
        public static Sky WithBrightness(Sky sky, float brightness)
        {
            var scaled = Build(sky.hue, sky.saturation, brightness);
            scaled.sourceLuminance = sky.sourceLuminance;
            return scaled;
        }

        /// <summary>
        /// 這張圖能不能拿來當背景。
        /// </summary>
        /// <remarks>
        /// `GameBackgroundManager` 在封面還沒載好（或那首歌就是黑底）時會塞一張叫
        /// `GameBackgroundManager_Black` 的 1×1 純黑佔位圖。拿它當封面的話，玻璃化
        /// 的軌道會被整片塗黑——看起來就像「透明度完全沒作用」，而其實是有作用的，
        /// 只是透出來的是我自己畫上去的黑。
        /// </remarks>
        public const float MinUsableLuminance = 0.02f;

        public static bool IsUsableCover(Sky sky) => sky.sourceLuminance >= MinUsableLuminance;
    }
}
