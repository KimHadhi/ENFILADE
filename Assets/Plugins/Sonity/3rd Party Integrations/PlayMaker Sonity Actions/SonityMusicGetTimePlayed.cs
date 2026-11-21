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
    [Tooltip("Returns the time (in seconds) since the SoundEvent was played")]
    public class SonityMusicGetTimePlayed : SonityActionBase {
        [Tooltip("Variable to store returned results in")]
        [UIHint(UIHint.Variable)]
        [ObjectType(typeof(FsmFloat))]
        public FsmFloat storeTimeIn;

        public override bool HideGameObjectReference() => true;

        protected override void DoSoundEventAction() =>
            storeTimeIn.Value = m_SoundEvent.MusicGetTimePlayed();
    }
}
#endif