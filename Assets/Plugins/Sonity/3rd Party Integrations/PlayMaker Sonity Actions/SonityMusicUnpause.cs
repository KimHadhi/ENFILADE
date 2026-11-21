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
    [Tooltip("Unpauses the SoundEvent at the Music Transform locally")]
    public class SonityMusicUnpause : SonityActionBase {

        public override bool HideGameObjectReference() => true;

        protected override void DoSoundEventAction() {
            m_SoundEvent.MusicUnpause();
        }
    }
}
#endif