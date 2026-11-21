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
    [Tooltip("Unpauses all SoundEvents with the owner Transform locally")]
    public class SonityUnpauseAllAtOwner : SonitySoundManagerActionBase {

        protected override void DoSoundManagerAction() {
            m_SoundManager.UnpauseAllAtOwner(m_Transform);
        }
    }
}
#endif