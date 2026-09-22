using UnityEngine;

using System;
namespace Judgment
{
    // Shared payload used for transferring judgment data between modules
    public class JudgmentPayload
    {
        public NoteController note;
        public JudgmentResult result;
        public int buttonId;
        public float inputSongPosMs;
        public float pressSongPosMs;
        public float releaseSongPosMs;
        public float targetTimeMs;
        public float absoluteOffsetMs;
        public bool triggerPopup = true;
        public bool triggerMesh = false;
        public bool triggerHitSound = false;
        public bool isHoldHead = false;
        public bool isHoldTail = false;
        public bool isHoldExtra = false;
        /// <summary>
        /// This judgment is a length-driven continuation of a long note (a hold's
        /// per-beat tick, or a Trill slot after its head) rather than something the
        /// player aimed at. It still counts for combo and judgment tallies; only its
        /// SCORE share is reduced, so long notes stop being worth dozens of taps.
        /// Distinct from isHoldExtra, which also covers the Trill head and controls
        /// mesh/marking behaviour rather than scoring.
        /// </summary>
        public bool isSustainTick = false;
        public bool usePersistentMesh = false;
        public float headInputSongPosMs;
        public float headTargetTimeMs;
        public float headAbsoluteOffsetMs;
        public bool hasHeadTiming = false;

        /// <summary>Reset all fields to defaults so the object can be recycled from the pool.</summary>
        public void Reset()
        {
            note                 = null;
            result               = default;
            buttonId             = 0;
            inputSongPosMs       = 0f;
            pressSongPosMs       = 0f;
            releaseSongPosMs     = 0f;
            targetTimeMs         = 0f;
            absoluteOffsetMs     = 0f;
            triggerPopup         = true;   // matches class-level default
            triggerMesh          = false;
            triggerHitSound      = false;
            isHoldHead           = false;
            isHoldTail           = false;
            isHoldExtra          = false;
            isSustainTick        = false;
            usePersistentMesh    = false;
            headInputSongPosMs   = 0f;
            headTargetTimeMs     = 0f;
            headAbsoluteOffsetMs = 0f;
            hasHeadTiming        = false;
        }
    }
}
// Shared judgment-related types used across the project.
// This file provides a single canonical definition for JudgmentResult.



// Placing in the global namespace so existing non-namespaced call-sites
// (legacy code) can keep using `JudgmentResult` without adding `using`.

public enum JudgmentResult
{
    Perfect = 0,
    Great = 1,
    Good = 2,
    Miss = 3,
    Fail = 4
}
