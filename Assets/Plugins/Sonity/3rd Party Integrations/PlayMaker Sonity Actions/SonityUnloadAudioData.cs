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
    [Tooltip("Unloads the audio data of any AudioClips assigned to the SoundContainers of the SoundEvent")]
    public class SonityUnloadAudioData : SonityActionBase {

        public override bool HideSoundEvent() => true;
        public override bool HideGameObjectReference() => true;

        protected override void DoSoundEventAction() {
            m_SoundEvent.UnloadAudioData();
        }
    }
}
#endif