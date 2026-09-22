using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A pooled engraved halo + tapered ray mesh layered over the regular hit particles.</summary>
public sealed class ClassicalHitAccent : MonoBehaviour
{
    private const int PoolSize = 16;
    private static ClassicalHitAccent instance;
    private readonly List<Accent> pool = new List<Accent>(PoolSize);
    private Material material;

    private sealed class Accent
    {
        public GameObject Object;
        public Transform Transform;
        public MeshRenderer Renderer;
        public MaterialPropertyBlock Block;
        public float Start;
        public float Duration;
        public Vector3 EndScale;
        public Color Color;
        public bool Active;
    }

    public static void Play(Vector3 position, Quaternion rotation, float noteWidth, Color color,
        float noteHeight = 1f, float holdLength = 0f)
    {
        EnsureCreated().PlayInternal(position, rotation, noteWidth, color, noteHeight, holdLength);
    }

    private static ClassicalHitAccent EnsureCreated()
    {
        if (instance != null) return instance;
        var go = new GameObject("Classical Hit Accent Pool");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ClassicalHitAccent>();
        return instance;
    }

    private void Awake()
    {
        Shader shader = Shader.Find("Custom/UnlitVertexColor");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        material = new Material(shader) { name = "Classical Hit Filigree (Runtime)", renderQueue = 4000 };
        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }
        for (int i = 0; i < PoolSize; i++) pool.Add(CreateAccent(i));
    }

    private Accent CreateAccent(int index)
    {
        var go = new GameObject("Engraved Hit Halo " + index);
        go.transform.SetParent(transform, false);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildFiligreeMesh();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 32760;
        go.SetActive(false);
        return new Accent { Object = go, Transform = go.transform, Renderer = renderer, Block = new MaterialPropertyBlock() };
    }

    private void PlayInternal(Vector3 position, Quaternion rotation, float noteWidth, Color color,
        float noteHeight, float holdLength)
    {
        Accent accent = null;
        for (int i = 0; i < pool.Count; i++) if (!pool[i].Active) { accent = pool[i]; break; }
        if (accent == null) accent = pool[0];

        float width = Mathf.Clamp(noteWidth, .18f, 12f);
        float enclosedHeight = Mathf.Max(.1f, noteHeight * 1.3f + Mathf.Max(0f, holdLength));
        accent.Start = Time.unscaledTime;
        accent.Duration = .3f;
        accent.EndScale = new Vector3(width * 1.9f, enclosedHeight / .96f, 1f);
        Color warmWhite = new Color(1f, .94f, .72f, 1f);
        Color flareColor = Color.Lerp(color, warmWhite, .84f);
        accent.Color = new Color(flareColor.r * 1.35f, flareColor.g * 1.35f, flareColor.b * 1.2f, 1f);
        Vector3 centredPosition = position + rotation * new Vector3(0f, Mathf.Max(0f, holdLength) * .5f, 0f);
        accent.Transform.SetPositionAndRotation(centredPosition, rotation);
        accent.Transform.localScale = accent.EndScale * .28f;
        accent.Active = true;
        accent.Object.SetActive(true);
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < pool.Count; i++)
        {
            Accent a = pool[i];
            if (!a.Active) continue;
            float t = Mathf.Clamp01((now - a.Start) / a.Duration);
            if (t >= 1f) { a.Active = false; a.Object.SetActive(false); continue; }
            float expansion = 1f - Mathf.Pow(1f - t, 3f);
            a.Transform.localScale = Vector3.LerpUnclamped(a.EndScale * .28f, a.EndScale, expansion);
            Color c = a.Color;
            c.a *= (1f - t) * (1f - t);
            a.Block.SetColor("_Color", c);
            a.Renderer.SetPropertyBlock(a.Block);
        }
    }

    private static Mesh BuildFiligreeMesh()
    {
        const int segments = 48;
        var vertices = new List<Vector3>(segments * 4 + 48);
        var colors = new List<Color>(segments * 4 + 48);
        var triangles = new List<int>(segments * 6 + 72);

        // A soft ivory centre briefly washes over the note before the engraved
        // ring and rays become visible.
        AddSoftCore(vertices, colors, triangles);

        // Slender elliptical double-ring, like a brass engraved cartouche.
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            AddRingSegment(vertices, colors, triangles, a0, a1, .50f, .39f);
        }

        // Twelve tapered rays give the judgment a precise, musical attack.
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 side = new Vector3(-dir.y, dir.x, 0f) * .018f;
            Vector3 root = new Vector3(dir.x * .43f, dir.y * .24f, 0f);
            int v = vertices.Count;
            vertices.Add(root - side); vertices.Add(root + side); vertices.Add(root + dir * .28f);
            colors.Add(new Color(1f, 1f, 1f, .72f)); colors.Add(new Color(1f, 1f, 1f, .72f)); colors.Add(new Color(1f, 1f, 1f, 0f));
            triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
        }

        // Low, wide lens-like flare matching the piano key strike direction.
        AddFlareTriangle(vertices, colors, triangles,
            new Vector3(-.92f, 0f, 0f), new Vector3(0f, .075f, 0f), new Vector3(0f, 0f, 0f));
        AddFlareTriangle(vertices, colors, triangles,
            new Vector3(-.92f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0f, -.075f, 0f));
        AddFlareTriangle(vertices, colors, triangles,
            new Vector3(.92f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0f, .075f, 0f));
        AddFlareTriangle(vertices, colors, triangles,
            new Vector3(.92f, 0f, 0f), new Vector3(0f, -.075f, 0f), new Vector3(0f, 0f, 0f));

        var mesh = new Mesh { name = "Classical Judgment Filigree" };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddSoftCore(List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        const int segments = 24;
        int center = vertices.Count;
        vertices.Add(Vector3.zero);
        colors.Add(new Color(1f, .98f, .86f, 1f));

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices.Add(new Vector3(Mathf.Cos(angle) * .62f, Mathf.Sin(angle) * .48f, 0f));
            colors.Add(new Color(1f, .9f, .62f, 0f));
            if (i > 0)
            {
                triangles.Add(center);
                triangles.Add(center + i);
                triangles.Add(center + i + 1);
            }
        }
    }

    private static void AddFlareTriangle(List<Vector3> vertices, List<Color> colors,
        List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int v = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        colors.Add(new Color(1f, 1f, 1f, 0f));
        colors.Add(new Color(1f, 1f, 1f, .95f));
        colors.Add(new Color(1f, 1f, 1f, .95f));
        triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
    }

    private static void AddRingSegment(List<Vector3> vertices, List<Color> colors, List<int> triangles,
        float a0, float a1, float outer, float inner)
    {
        int v = vertices.Count;
        vertices.Add(new Vector3(Mathf.Cos(a0) * outer, Mathf.Sin(a0) * outer * .54f, 0f));
        vertices.Add(new Vector3(Mathf.Cos(a0) * inner, Mathf.Sin(a0) * inner * .54f, 0f));
        vertices.Add(new Vector3(Mathf.Cos(a1) * inner, Mathf.Sin(a1) * inner * .54f, 0f));
        vertices.Add(new Vector3(Mathf.Cos(a1) * outer, Mathf.Sin(a1) * outer * .54f, 0f));
        colors.Add(Color.white); colors.Add(new Color(1f, 1f, 1f, .18f)); colors.Add(new Color(1f, 1f, 1f, .18f)); colors.Add(Color.white);
        triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
        triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
    }
}
