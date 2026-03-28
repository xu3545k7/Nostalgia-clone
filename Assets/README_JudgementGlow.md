Judgement Glow effect (URP/HDRP compatible)

Files added:
- Assets/Shaders/UnlitAdditiveEmission.shader  (simple additive unlit shader)
- Assets/Scripts/Effects/JudgementGlowController.cs  (pooling + playback controller)

How to create the prefab in Unity:
1. Create a Quad: GameObject -> 3D Object -> Quad. Rotate/scale so it faces the camera and has desired size.
2. Create a Material using the shader `Custom/UnlitAdditiveEmission` and assign a radial gradient texture to _MainTex (center bright, edges soft). Enable HDR color for emission if needed.
3. Assign the material to the Quad's MeshRenderer.
4. As a child of the Quad, add a Point Light. Set Range small (e.g. 2) and Intensity to 0 initially.
5. Make the Quad prefab by dragging it into `Assets/Prefabs/JudgeGlow.prefab`.
6. Create an empty GameObject in the scene, attach `JudgementGlowController` component, assign the prefab to `glowPrefab`, set `judgementLineTransform` if needed.
7. Ensure Bloom/post-processing is enabled in your pipeline to get the glow bloom effect.

Integration into `JudgmentManager`:
- The code already injects calls into `ApplyJudgmentPayload` to trigger `JudgementGlowController.Instance.PlayAt(...)` when popups are shown. You only need to create the prefab and wire the glowPrefab in the controller component.

Tuning tips:
- Adjust `peakEmission` and Bloom settings for intensity.
- Use additive blending for bright stacking; alpha blend if you prefer soft fades.
- For HDRP volumetrics, you can consider adding volumetric lights or particles behind the quad for stronger depth.
