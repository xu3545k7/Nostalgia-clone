using System;
using UnityEngine;

public class StaccatoNoteHandler : DefaultNoteHandler
{
    private static AudioClip staccatoClip;
    private static bool triedLoadClip = false;
    private bool headSoundAvailable = false;
    private bool headSoundConsumed = false;

    /// <summary>
    /// 斷奏的逐顆除錯紀錄。這些原本是無條件的 Debug.Log，每判定一顆斷奏就在判定路徑上
    /// 印兩三行（player build 每行都要抓呼叫堆疊、寫檔），密集段會直接吃掉幀時間。
    /// 要看的時候在 Player Settings 加上 NOSTALGIA_TRACE_STACCATO；沒加時整個呼叫連同
    /// 字串組裝都會被編譯器拿掉。
    /// </summary>
    [System.Diagnostics.Conditional("NOSTALGIA_TRACE_STACCATO")]
    private static void Trace(string message) => Debug.Log(message);

    public StaccatoNoteHandler() : base() { }
    public StaccatoNoteHandler(NoteController note) : base(note) { }
    public override JudgmentResult EvaluateHead(NoteController note, float songPos, float delta, int buttonId)
    {
        var res = base.EvaluateHead(note, songPos, delta, buttonId);
        headSoundAvailable = !headSoundConsumed && res != JudgmentResult.Miss;
        Trace($"[StaccatoNoteHandler] EvaluateHead note={DescribeNote(note)} songPos={songPos:F3} delta={delta:F3} buttonId={buttonId} result={res} headSoundAvailable={headSoundAvailable} headSoundConsumed={headSoundConsumed}");
        return res;
    }

    public override void PlayHeadSound(NoteController note, float songPos, JudgmentResult res)
    {
        Trace($"[StaccatoNoteHandler] PlayHeadSound note={DescribeNote(note)} songPos={songPos:F3} result={res} headSoundAvailable={headSoundAvailable} headSoundConsumed={headSoundConsumed}");
        if (!headSoundAvailable || res == JudgmentResult.Miss)
        {
            Trace($"[StaccatoNoteHandler] PlayHeadSound skipped for note={DescribeNote(note)} reason={(res == JudgmentResult.Miss ? "ResultMiss" : "FlagFalse")} consumed={headSoundConsumed}");
            return;
        }
        headSoundAvailable = false;
        if (!PlayStaccatoHeadSound())
        {
            Trace($"[StaccatoNoteHandler] PlayHeadSound fallback defaultHitSound note={DescribeNote(note)}");
            base.PlayHeadSound(note, songPos, res);
            headSoundConsumed = true;
            return;
        }
        headSoundConsumed = true;
    }

    public override JudgmentResult EvaluateAutoJudgeTap(NoteController note, float songPos)
    {
        var res = base.EvaluateAutoJudgeTap(note, songPos);
        headSoundAvailable = !headSoundConsumed && res != JudgmentResult.Miss;
        Trace($"[StaccatoNoteHandler] EvaluateAutoJudgeTap note={DescribeNote(note)} songPos={songPos:F3} result={res} headSoundAvailable={headSoundAvailable} headSoundConsumed={headSoundConsumed}");
        return res;
    }

    public override JudgmentResult EvaluateAutoStartHold(NoteController note, float pressSongPos)
    {
        var res = base.EvaluateAutoStartHold(note, pressSongPos);
        headSoundAvailable = !headSoundConsumed && res != JudgmentResult.Miss;
        Trace($"[StaccatoNoteHandler] EvaluateAutoStartHold note={DescribeNote(note)} pressSongPos={pressSongPos:F3} result={res} headSoundAvailable={headSoundAvailable} headSoundConsumed={headSoundConsumed}");
        return res;
    }

    public override JudgmentResult EvaluateHoldEnd(NoteController note, float releaseSongPos, float pressSongPos)
    {
        if (note == null || note.NoteData == null)
        {
            return base.EvaluateHoldEnd(note, releaseSongPos, pressSongPos);
        }

        float endTime = note.NoteData.endTime;
        float delta = releaseSongPos - endTime;

        JudgmentResult result;
        if (delta <= 50f)
        {
            result = JudgmentResult.Perfect;
        }
        else if (delta <= 100f)
        {
            result = JudgmentResult.Great;
        }
        else
        {
            result = JudgmentResult.Good;
        }

        Trace($"[StaccatoNoteHandler] EvaluateHoldEnd note={DescribeNote(note)} releaseSongPos={releaseSongPos:F3} targetEnd={endTime:F3} delta={delta:F3} result={result}");
        return result;
    }

    public override JudgmentResult EvaluateUnhandled(NoteController note, float songPos, bool anyKeyDown)
    {
        Trace($"[StaccatoNoteHandler] EvaluateUnhandled note={DescribeNote(note)} songPos={songPos:F3} anyKeyDown={anyKeyDown} (flag reset) consumedBefore={headSoundConsumed}");
        headSoundAvailable = false;
        headSoundConsumed = true;
        return base.EvaluateUnhandled(note, songPos, anyKeyDown);
    }

    private bool PlayStaccatoHeadSound()
    {
        var clip = GetStaccatoClip();
        var mgr = HitSoundManager.Instance ?? HitSoundManager.EnsureInstance();
        if (mgr == null)
        {
            Debug.LogWarning("[StaccatoNoteHandler] PlayStaccatoHeadSound aborted: HitSoundManager unavailable");
            return false;
        }

        if (clip != null)
        {
            float volume = 1f;
            try { volume = SettingsManager.Instance != null ? SettingsManager.Instance.HitSoundVolume : 1f; } catch { volume = 1f; }
            try
            {
                if (clip.loadState == AudioDataLoadState.Unloaded) { clip.LoadAudioData(); }
            }
            catch { }
            Trace($"[StaccatoNoteHandler] PlayStaccatoHeadSound playClip name={clip.name} loadState={clip.loadState} volume={volume}");
            mgr.PlayClip(clip, Mathf.Clamp01(volume), 1f);
            return true;
        }
        // Fallback to default hit sound if resource missing.
        Debug.LogWarning("[StaccatoNoteHandler] PlayStaccatoHeadSound missing clip, falling back to default hit sound");
        mgr.PlayHitSound();
        return true;
    }

    private static AudioClip GetStaccatoClip()
    {
        if (!triedLoadClip)
        {
            triedLoadClip = true;
            try
            {
                staccatoClip = Resources.Load<AudioClip>("Sound/Tap");
            }
            catch
            {
                staccatoClip = null;
            }
        }
        return staccatoClip;
    }

    private static string DescribeNote(NoteController note)
    {
        if (note == null) return "null";
        var nd = note.NoteData;
        if (nd == null) return $"{note.name}|data=null";
        try
        {
            string type = nd.type ?? "(null)";
            float start = nd.startTime;
            float end = nd.endTime;
            int laneStart = nd.startLane;
            int laneEnd = nd.endLane;
            return $"{note.name}|type={type}|start={start:F3}|end={end:F3}|lane={laneStart}-{laneEnd}";
        }
        catch (Exception ex)
        {
            return $"{note.name}|data=error:{ex.Message}";
        }
    }
}
