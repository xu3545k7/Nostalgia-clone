using UnityEngine;

namespace Judgment
{
    public static class EffectManager
    {
        public static void PlayEffect(NoteController note, JudgmentResult result)
        {
            if (note == null) return;
            // 這裡可以根據 result 播放不同特效
            NoteJudgementMeshManager.EnsureCreated().ShowEffect(note, result, false);
            HitEffectRouter.Play(note, note.NoteData?.startLane ?? -1, result);
        }
    }
}
