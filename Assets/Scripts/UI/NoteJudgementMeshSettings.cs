using UnityEngine;

[CreateAssetMenu(fileName = "NoteJudgementMeshSettings", menuName = "Rhythm/Note Judgement Mesh Settings", order = 1000)]
public class NoteJudgementMeshSettings : ScriptableObject
{
    [Header("Effect Settings")]
    public float duration = 0.35f;
    public float meshHeight = 1.0f;
    public float surfaceOffset = 0.01f;
    public float fallbackWidth = 1.0f;
    public Vector3 rotationOffsetEuler = new Vector3(90f, 0f, 0f);
    public float widthScale = 0.8f;
    public float widthPadding = 0.0f;
    public Vector3 localPositionOffset = Vector3.zero;
    public bool snapToJudgmentPlane = true;

    [Header("Lane Width Derivation")]
    public int totalLaneCount = 28;
    public float trackWidthFallback = 105f;
    public float laneGapCompensation = 0f;
    public float laneWidthMultiplier = 1.0f;
    public float trackWidthSampleInterval = 0.5f;

    private const string ResourcePath = "NoteJudgementMeshSettings";

    public static NoteJudgementMeshSettings LoadFromResources()
    {
        return Resources.Load<NoteJudgementMeshSettings>(ResourcePath);
    }
}
