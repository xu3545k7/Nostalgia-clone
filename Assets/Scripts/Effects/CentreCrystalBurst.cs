using System.Collections.Generic;
using UnityEngine;

namespace Effects
{
    /// <summary>
    /// The crystal breaking: a cross of light at the centre and four shards
    /// thrown out along the axes, gone in a quarter of a second.
    /// </summary>
    /// <remarks>
    /// **Why it breaks at all.** The crystal is a target. Once the note is
    /// judged the target has been used, and a target that simply vanishes with
    /// its note leaves the player nothing to confirm they hit *it* rather than
    /// merely the note. Shattering is the receipt.
    ///
    /// **Why four shards on the axes and not a spray.** A spray is confetti: it
    /// says something happened, not what. Four pieces leaving along the axes
    /// re-draw the diamond's own geometry coming apart, so the burst reads as
    /// that object breaking rather than as a generic effect played on top of it.
    ///
    /// **Why it is over so fast.** In a dense passage these fire several times a
    /// second. Anything that outlives the note it belongs to turns the lane into
    /// soup -- 0.26s means it is finished before the next note reaches the line
    /// even at speed.
    ///
    /// **Why pooled sprites and not a ParticleSystem.** Five renderers per burst
    /// with exact positions, driven by one Update over a list. A particle system
    /// would need its own material and its own emission plumbing to do the same
    /// four deterministic pieces, and could not be told to sort against the note
    /// it came from.
    /// </remarks>
    [AddComponentMenu("Effects/Centre Crystal Burst")]
    public sealed class CentreCrystalBurst : MonoBehaviour
    {
        private const int PoolSize = 12;
        private const float Life = 0.42f;
        private const float FlashLife = 0.15f;
        /// <summary>How far a shard travels, in multiples of the crystal's size.</summary>
        private const float Reach = 2.6f;
        /// <summary>A shard's size, as a fraction of the crystal it came from.</summary>
        private const float ShardSize = 0.62f;
        /// <summary>
        /// Speed decay. High: nearly all the travel happens in the first frames
        /// and the rest of the life is a slow drift.
        /// </summary>
        /// <remarks>
        /// 這是整個特效的個性所在。破裂是瞬間的，碎片離開得很快；之後它們慢下來
        /// 才是眼睛真正讀到「有東西飛出去了」的那一段 —— 等速飛出去只會看到一團
        /// 東西閃過，看不出它在飛。和魔法陣那個特效同一套：先炸開，再滑行。
        /// </remarks>
        private const float Drag = 16f;

        private static CentreCrystalBurst instance;
        private static Material overlayMaterial;

        /// <summary>
        /// The material that puts the shards over the track instead of inside it.
        /// </summary>
        /// <remarks>
        /// The burst is a camera-facing billboard standing in a track that
        /// slopes away, so its lower half is behind the track surface in depth
        /// and an ordinary sprite gets its bottom eaten. The seal solves this
        /// with an overlay shader; the shards use their own copy of the same
        /// idea. Falling back to Sprites/Default keeps them visible (just
        /// clipped) if the shader ever fails to build.
        /// </remarks>
        private static Material Overlay
        {
            get
            {
                if (overlayMaterial != null) return overlayMaterial;
                Shader shader = Shader.Find("Nostalgia/CrystalShardOverlay")
                                ?? Shader.Find("Sprites/Default");
                if (shader == null) return null;
                overlayMaterial = new Material(shader)
                {
                    name = "Centre Crystal Overlay (Runtime)",
                    renderQueue = 4200,
                    hideFlags = HideFlags.DontSave,
                };
                if (overlayMaterial.HasProperty("_Glow")) overlayMaterial.SetFloat("_Glow", 1.55f);
                return overlayMaterial;
            }
        }

        /// <summary>
        /// A sprite's own world size at scale 1. Sizes here are given in world
        /// units, so every scale has to be divided by this -- otherwise changing
        /// the texture's resolution silently changes how big everything is.
        /// </summary>
        private static float SpriteExtent(Sprite sprite)
        {
            float size = sprite != null ? sprite.bounds.size.y : 1f;
            return size > 0.0001f ? size : 1f;
        }

        private sealed class Piece
        {
            public GameObject Object;
            public Transform Transform;
            public SpriteRenderer Flash;
            public SpriteRenderer[] Shards;
            public Vector3[] Directions;
            public float Age;
            public float Size;
            public bool Active;
        }

        private readonly List<Piece> pool = new List<Piece>(PoolSize);

        private static readonly Vector3[] Axes =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right,
        };

        public static CentreCrystalBurst EnsureCreated()
        {
            if (instance != null) return instance;
            var host = new GameObject("CentreCrystalBurst");
            instance = host.AddComponent<CentreCrystalBurst>();
            DontDestroyOnLoad(host);
            return instance;
        }

        /// <summary>
        /// Breaks a crystal at <paramref name="position"/>.
        /// </summary>
        /// <param name="size">The crystal's world size, so the shards scale with it.</param>
        public static void Play(Vector3 position, float size, Quaternion rotation,
            int sortingLayerId, int sortingOrder, bool rightHand)
        {
            if (size <= 0.0001f) return;
            CentreCrystalBurst burst = EnsureCreated();
            if (burst == null) return;
            burst.Emit(position, size, rotation, sortingLayerId, sortingOrder, rightHand);
        }

        private void Emit(Vector3 position, float size, Quaternion rotation,
            int sortingLayerId, int sortingOrder, bool rightHand)
        {
            Piece piece = Take();
            if (piece == null) return;

            piece.Transform.SetPositionAndRotation(position, rotation);
            piece.Age = 0f;
            piece.Size = size;
            piece.Active = true;
            piece.Object.SetActive(true);

            // 判定的那一瞬間，這一格上還有光柱、符文和打擊光在燒。排在音符自己
            // 的上面幾層還是會被蓋掉 —— 碎片要排得夠高才看得到。
            piece.Flash.sortingLayerID = sortingLayerId;
            piece.Flash.sortingOrder = sortingOrder + 40;
            Sprite shardSprite = CentreCrystal.Shard(rightHand);
            for (int i = 0; i < piece.Shards.Length; i++)
            {
                piece.Shards[i].sprite = shardSprite;
                piece.Shards[i].sortingLayerID = sortingLayerId;
                piece.Shards[i].sortingOrder = sortingOrder + 41;
            }

            Step(piece, 0f);
        }

        private Piece Take()
        {
            for (int i = 0; i < pool.Count; i++)
                if (!pool[i].Active) return pool[i];
            return pool.Count < PoolSize ? Build() : pool[0];
        }

        private Piece Build()
        {
            var root = new GameObject("Burst");
            root.transform.SetParent(transform, false);
            root.SetActive(false);

            var piece = new Piece
            {
                Object = root,
                Transform = root.transform,
                Shards = new SpriteRenderer[Axes.Length],
                Directions = new Vector3[Axes.Length],
            };

            var flashObject = new GameObject("Flash");
            flashObject.transform.SetParent(root.transform, false);
            piece.Flash = flashObject.AddComponent<SpriteRenderer>();
            piece.Flash.sprite = CentreCrystal.Cross;
            piece.Flash.sharedMaterial = Overlay;

            for (int i = 0; i < Axes.Length; i++)
            {
                var shard = new GameObject("Shard" + i);
                shard.transform.SetParent(root.transform, false);
                // 左右兩片轉九十度，尖端才是朝著飛出去的方向。
                bool horizontal = Mathf.Abs(Axes[i].x) > 0.5f;
                shard.transform.localRotation = Quaternion.Euler(0f, 0f, horizontal ? 90f : 0f);
                piece.Shards[i] = shard.AddComponent<SpriteRenderer>();
                piece.Shards[i].sharedMaterial = Overlay;
                piece.Directions[i] = Axes[i];
            }

            pool.Add(piece);
            return piece;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < pool.Count; i++)
            {
                Piece piece = pool[i];
                if (!piece.Active) continue;
                piece.Age += dt;
                if (piece.Age >= Life)
                {
                    piece.Active = false;
                    piece.Object.SetActive(false);
                    continue;
                }
                Step(piece, piece.Age);
            }
        }

        private static void Step(Piece piece, float age)
        {
            float t = Mathf.Clamp01(age / Life);

            // 碎片：一開始就有全速，之後被阻力拖住 —— 破裂是瞬間的事，之後只是
            // 碎塊在滑行。
            float travel = piece.Size * Reach * (1f - Mathf.Exp(-Drag * age)) / (1f - Mathf.Exp(-Drag * Life));
            float shrink = Mathf.Lerp(ShardSize, ShardSize * 0.45f, t);
            float fade = t < 0.42f ? 1f : 1f - (t - 0.42f) / 0.58f;
            fade *= fade;

            for (int i = 0; i < piece.Shards.Length; i++)
            {
                SpriteRenderer shard = piece.Shards[i];
                shard.transform.localPosition = piece.Directions[i] * travel;
                shard.transform.localScale = Vector3.one
                    * (piece.Size * shrink / SpriteExtent(shard.sprite) / CentreCrystal.StoneFill);
                // 顏色已經烤在碎片的圖裡（左右手各一張），這裡只管淡出。
                shard.color = new Color(1f, 1f, 1f, fade);
            }

            // 十字光：比碎片更短命，只在最前面那幾格出現。
            float flash = Mathf.Clamp01(age / FlashLife);
            float flashFade = 1f - flash;
            piece.Flash.transform.localScale = Vector3.one
                * (piece.Size * Mathf.Lerp(0.6f, 3.2f, Mathf.Sqrt(flash))
                   / SpriteExtent(piece.Flash.sprite));
            piece.Flash.color = new Color(0.78f, 0.68f, 1f, flashFade * flashFade * 0.95f);
        }
    }
}
