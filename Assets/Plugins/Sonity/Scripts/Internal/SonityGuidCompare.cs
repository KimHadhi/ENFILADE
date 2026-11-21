// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

namespace Sonity.Internal {

    public static class SonityGuidCompare {

        public static bool IsSame(SoundContainerBase objectA, SoundContainerBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundContainerBase objectA, SoundContainerBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsNotSame(SoundContainerBase objectA, SoundContainerBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        public static bool IsSame(SoundEventBase objectA, SoundEventBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundEventBase objectA, SoundEventBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsNotSame(SoundEventBase objectA, SoundEventBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        public static bool IsSame(SoundPolyGroupBase objectA, SoundPolyGroupBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundPolyGroupBase objectA, SoundPolyGroupBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            } else {
                return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
            }
        }

        public static bool IsNotSame(SoundPolyGroupBase objectA, SoundPolyGroupBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        public static bool IsSame(SoundDataGroupBase objectA, SoundDataGroupBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundDataGroupBase objectA, SoundDataGroupBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsNotSame(SoundDataGroupBase objectA, SoundDataGroupBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        public static bool IsSame(SoundMixBase objectA, SoundMixBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundMixBase objectA, SoundMixBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsNotSame(SoundMixBase objectA, SoundMixBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        public static bool IsSame(SoundVolumeGroupBase objectA, SoundVolumeGroupBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundVolumeGroupBase objectA, SoundVolumeGroupBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsNotSame(SoundVolumeGroupBase objectA, SoundVolumeGroupBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        public static bool IsSame(SoundTagBase objectA, SoundTagBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundTagBase objectA, SoundTagBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsNotSame(SoundTagBase objectA, SoundTagBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        public static bool IsSame(SoundPhysicsConditionBase objectA, SoundPhysicsConditionBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return true; // If both are null
            }
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsSameNotNull(SoundPhysicsConditionBase objectA, SoundPhysicsConditionBase objectB) {
            if (objectA == null || objectB == null) {
                return false;
            }
            return objectA.internals.sonityGuid == objectB.internals.sonityGuid;
        }

        public static bool IsNotSame(SoundPhysicsConditionBase objectA, SoundPhysicsConditionBase objectB) {
            if (ReferenceEquals(objectA, objectB)) {
                return false; // If both are null
            }
            if (objectA is null || objectB is null) {
                return true;
            }
            return objectA.internals.sonityGuid != objectB.internals.sonityGuid;
        }

        // SoundPreset is Editor only, skip

        // SoundAreaBase
        // SoundAreaTagBase
        // SoundReverbBase

        // Music system also!
    }
}