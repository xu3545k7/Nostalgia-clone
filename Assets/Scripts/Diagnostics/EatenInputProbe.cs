using System.Collections.Generic;

/// <summary>
/// 被防誤觸規則擋掉的按鍵，到底有沒有害到音符。
/// </summary>
/// <remarks>
/// <see cref="MistouchProbe"/> 只數「擋了幾下」，分不出擋掉的是擦碰還是真的要打的
/// 那一下。用真鋼琴彈、譜面比實際彈的音少的時候，擋掉的按鍵本來就很多，總數高不代表
/// 擋錯。
///
/// 這裡對每一下被擋的按鍵，記下它**本來會打到的那顆音符**，再看那顆音符的結局：
///
/// * 後來被別的按鍵打到 → 擋得對（或至少沒造成傷害）。
/// * 最後 Miss → **這一下很可能是被擋錯的**。
///
/// 每秒跟著 <c>[Mistouch]</c> 印一行，另外帶整首的累計：
///
/// <code>[Eaten] lock=3(hit 2 miss 1) taken=1(hit 1 miss 0) … | song: miss-after-eaten=4 of eaten-with-note=37</code>
/// </remarks>
public static class EatenInputProbe
{
    public enum Reason
    {
        /// <summary>鄰鍵鎖：剛有別的鍵判中，這顆音符比那顆晚太多。</summary>
        Lock,
        /// <summary>候選剛被同一幀的別的按鍵拿走，這一下配到更晚的音符。</summary>
        Taken,
        /// <summary>暫定判定被正主鎖到而作廢。</summary>
        ProvisionalDropped,
        /// <summary>輸入端：接點抖動過濾。</summary>
        InputChatter,
        /// <summary>輸入端：力度擦碰過濾（LooksLikeBrush）。</summary>
        InputBrush,
        /// <summary>輸入端：前瞻扣留被後來的重音揭穿。</summary>
        InputHeldBrush,
    }

    private const int ReasonCount = 6;
    private static readonly string[] Names = { "lock", "taken", "dropped", "in-chatter", "in-brush", "in-held" };

    private static readonly int[] eaten = new int[ReasonCount];
    private static readonly int[] noNote = new int[ReasonCount];
    private static readonly int[] hitLater = new int[ReasonCount];
    private static readonly int[] missed = new int[ReasonCount];

    private static int songEatenWithNote;
    private static int songMissAfterEaten;

    // 音符 → 第一次被擋時的原因。音符物件會被物件池重用，所以結局或回收時一定要移除。
    private static readonly Dictionary<NoteController, Reason> pending = new Dictionary<NoteController, Reason>();

    /// <summary>一下按鍵被擋掉了。<paramref name="candidate"/> 是它本來會打到的音符，沒有就傳 null。</summary>
    public static void Eaten(Reason reason, NoteController candidate)
    {
        int r = (int)reason;
        eaten[r]++;
        if (candidate == null)
        {
            noNote[r]++;
            return;
        }
        if (!pending.ContainsKey(candidate))
        {
            pending[candidate] = reason;
            songEatenWithNote++;
        }
    }

    /// <summary>音符有了結局。</summary>
    public static void Resolved(NoteController note, bool isMiss)
    {
        if (note == null || pending.Count == 0) return;
        if (!pending.TryGetValue(note, out Reason reason)) return;
        pending.Remove(note);
        if (isMiss)
        {
            missed[(int)reason]++;
            songMissAfterEaten++;
        }
        else
        {
            hitLater[(int)reason]++;
        }
    }

    /// <summary>音符被回收，沒等到結局。</summary>
    public static void Forget(NoteController note)
    {
        if (note != null) pending.Remove(note);
    }

    public static void ResetSong()
    {
        pending.Clear();
        songEatenWithNote = 0;
        songMissAfterEaten = 0;
        System.Array.Clear(eaten, 0, ReasonCount);
        System.Array.Clear(noNote, 0, ReasonCount);
        System.Array.Clear(hitLater, 0, ReasonCount);
        System.Array.Clear(missed, 0, ReasonCount);
    }

    /// <summary>讀出並清空這一秒的計數。這一秒什麼都沒擋時回傳 null。</summary>
    public static string TakeReport()
    {
        bool any = false;
        for (int i = 0; i < ReasonCount; i++)
            if (eaten[i] > 0 || hitLater[i] > 0 || missed[i] > 0) { any = true; break; }
        if (!any) return null;

        var b = new System.Text.StringBuilder(200);
        b.Append("[Eaten]");
        for (int i = 0; i < ReasonCount; i++)
        {
            if (eaten[i] == 0 && hitLater[i] == 0 && missed[i] == 0) continue;
            b.Append(' ').Append(Names[i]).Append('=').Append(eaten[i]);
            if (noNote[i] > 0) b.Append("(no-note ").Append(noNote[i]).Append(')');
            if (hitLater[i] > 0 || missed[i] > 0)
                b.Append("[hit ").Append(hitLater[i]).Append(" MISS ").Append(missed[i]).Append(']');
        }
        b.Append(" | song: miss-after-eaten=").Append(songMissAfterEaten)
         .Append(" of eaten-with-note=").Append(songEatenWithNote);

        System.Array.Clear(eaten, 0, ReasonCount);
        System.Array.Clear(noNote, 0, ReasonCount);
        System.Array.Clear(hitLater, 0, ReasonCount);
        System.Array.Clear(missed, 0, ReasonCount);
        return b.ToString();
    }
}
