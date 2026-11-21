// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using System;
using System.Collections.Generic;

namespace Sonity.Internal {

    [Serializable]
    public class StatisticsInstanceInfo {
        public int instancesActive;
        public int instancesDisabled;
        public int voicesUsed;
        public float volumeAverageLinear;
        public SoundEventBase soundEvent;
    }

    [Serializable]
    public class SoundManagerInternalsStatistics {

        public List<StatisticsInstanceInfo> instancesInfo = new List<StatisticsInstanceInfo>();

        public bool statisticsExpandMain = false;
        public bool statisticsExpandGeneral = true;
        public bool statisticsExpandInstances = true;

        public SoundManagerStatisticsSorting statisticsSorting = SoundManagerStatisticsSorting.Name;

        public bool statisticsInfoActive = true;
        public bool statisticsInfoVoices = true;
        public bool statisticsInfoVolume = true;
        public bool statisticsInfoPlays = true;

        public bool statisticsFilterShowActive = true;
        public bool statisticsFilterShowInactive = false;

        [NonSerialized]
        public int statisticsVoicesPlayed;
        [NonSerialized]
        public int statisticsMaxSimultaneousVoices;
    }
}
#endif