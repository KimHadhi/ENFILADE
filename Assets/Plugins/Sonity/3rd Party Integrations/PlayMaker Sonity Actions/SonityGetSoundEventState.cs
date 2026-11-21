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
    [Tooltip("Gets the current SoundEventState of the reference Sound Event.")]
    public class SonityGetSoundEventState : SonityActionBase {
        [RequiredField]
        [UIHint(UIHint.Variable)]
        [ObjectType(typeof(SoundEventState))]
        [Tooltip("Variable to store the state of the Sound Event. Can be Playing, Pause, Delay or NotPlaying.")]
        public FsmEnum storeStateIn;

        protected override void DoSoundEventAction() {
            storeStateIn.Value = m_SoundEvent.GetSoundEventState(m_Transform);
        }
    }
}
#endif