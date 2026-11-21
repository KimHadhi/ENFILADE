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
    [Tooltip("Pauses the SoundEvent with the owner Transform locally")]
    public class SonityPause : SonityActionBase {
        [Tooltip("If the SoundEvent should be paused even if it is set to \"Ignore Local Pause\"")]
        public FsmBool forcePause = false;

        protected override void DoSoundEventAction() {
            m_SoundEvent.Pause(m_Transform, forcePause.Value);
        }
    }
}
#endif