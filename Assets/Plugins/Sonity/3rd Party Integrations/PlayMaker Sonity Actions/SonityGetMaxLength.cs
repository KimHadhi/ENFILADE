// PlayMaker integration by Simon Palmblad
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if SONITY_ENABLE_INTEGRATION_PLAYMAKER
using HutongGames.PlayMaker;
using UnityEngine;
using TooltipAttribute = HutongGames.PlayMaker.TooltipAttribute;
using Sonity.PlayMaker.Internal;

namespace Sonity.PlayMaker {

    [ActionCategory("Sonity")]
    [HelpURL("https://sonigon.com/sonity-documentation/")]
    [Tooltip("Returns the max length (in seconds) of the SoundEvent (calculated from the longest audioClip)")]
    public class SonityGetMaxLength : SonityActionBase {
        [RequiredField]
        [ObjectType(typeof(FsmFloat))]
        [UIHint(UIHint.Variable)]
        [Tooltip("Max length of the clip in seconds.")]
        public FsmFloat storeLengthIn;

        public override bool HideGameObjectReference() => true;

        protected override void DoSoundEventAction() {
            storeLengthIn.Value = m_SoundEvent.GetMaxLength();
        }
    }
}
#endif