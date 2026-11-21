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
    [Tooltip("Stops the SoundEvent playing at the UI Transform")]
    public class SonityUIStop : SonityActionBase {

        [Tooltip("Enable fadeout for sound when stopping playback")]
        public FsmBool allowFadeout = true;

        protected override void DoSoundEventAction() {
            m_SoundEvent.UIStop(allowFadeout.Value);
        }

        public override bool HideGameObjectReference() => true;
    }
}
#endif