using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class VisualEffectsWiringTests
{
    [Test]
    public void StaccatoUsesBodyAndSeparateArrowIndicator()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Note.prefab");
        Assert.NotNull(prefab);
        var note = prefab.GetComponent<NoteController>();
        Assert.NotNull(note);
        var serialized = new SerializedObject(note);
        Assert.NotNull(serialized.FindProperty("staccatoRightSprite").objectReferenceValue);
        Assert.NotNull(serialized.FindProperty("staccatoLeftSprite").objectReferenceValue);
        Assert.NotNull(serialized.FindProperty("rightStaccatoIndicatorSprite").objectReferenceValue);
        Assert.NotNull(serialized.FindProperty("leftStaccatoIndicatorSprite").objectReferenceValue);
    }

    [Test]
    public void JudgmentLinePrefabHasGlowAndHollowMaterial()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/JudgmentLine.prefab");
        Assert.NotNull(prefab);
        Assert.NotNull(prefab.GetComponent<JudgmentLineGlow>());
        var renderer = prefab.GetComponent<Renderer>();
        Assert.NotNull(renderer);
        Assert.NotNull(renderer.sharedMaterial);
        Assert.AreEqual("Custom/HollowJudgmentLine", renderer.sharedMaterial.shader.name);
        Assert.IsTrue(renderer.sharedMaterial.HasProperty("_EmissionColor"));
    }

    [Test]
    public void JudgmentGlowWritesVisibleEmissionToRenderer()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/JudgmentLine.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var glow = instance.GetComponent<JudgmentLineGlow>();
            var renderer = instance.GetComponent<Renderer>();
            var method = typeof(JudgmentLineGlow).GetMethod("ApplyColor", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(glow, new object[] { Color.white, 0.5f });
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Color emission = block.GetColor(Shader.PropertyToID("_EmissionColor"));
            Assert.Greater(emission.r, 0.45f);
            Assert.Greater(emission.a, 0.45f);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
