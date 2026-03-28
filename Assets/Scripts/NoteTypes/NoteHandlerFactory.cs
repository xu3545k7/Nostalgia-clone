using System;
using UnityEngine;

public static class NoteHandlerFactory
{
    public static INoteHandler Create(NoteController note)
    {
        if (note == null) return new DefaultNoteHandler();

        // prefer NoteController cached hints (set once on Init — zero allocation)
        if (note.IsSoft) return new SoftNoteHandler(note);
        if (note.IsStaccato) return new StaccatoNoteHandler(note);

        // Fall back to NoteData.type direct string compare — no reflection, no boxing
        var nd = note.NoteData;
        if (nd != null && !string.IsNullOrEmpty(nd.type))
        {
            if (string.Equals(nd.type, "hold",     StringComparison.OrdinalIgnoreCase)) return new HoldNoteHandler(note);
            if (string.Equals(nd.type, "stac",     StringComparison.OrdinalIgnoreCase)) return new StaccatoNoteHandler(note);
            if (string.Equals(nd.type, "staccato", StringComparison.OrdinalIgnoreCase)) return new StaccatoNoteHandler(note);
            if (string.Equals(nd.type, "soft",     StringComparison.OrdinalIgnoreCase)) return new SoftNoteHandler(note);
            if (string.Equals(nd.type, "tap",      StringComparison.OrdinalIgnoreCase)) return new TapNoteHandler(note);
        }

        // default to tap handler
        return new TapNoteHandler(note);
    }
}
