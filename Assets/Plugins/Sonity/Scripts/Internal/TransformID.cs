// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System;
using UnityEngine;

namespace Sonity.Internal {

    public readonly struct TransformID : IEquatable<TransformID> {

        // Unity 6000.5 removes Transform.InstanceID and replaces it with Transform.EntityId
#if UNITY_6000_4_OR_NEWER
        public readonly EntityId id;
        public TransformID(Transform transform){
            id = transform.GetEntityId();
        }
#else
        public readonly int id;
        public TransformID(Transform transform) {
            id = transform.GetInstanceID();
        }
#endif

        public bool Equals(TransformID other) {
            return id.Equals(other.id);
        }

        public override bool Equals(object obj) {
            return obj is TransformID other && Equals(other);
        }

        public override int GetHashCode() {
            return id.GetHashCode();
        }

        public static bool operator ==(TransformID left, TransformID right) {
            return left.Equals(right);
        }

        public static bool operator !=(TransformID left, TransformID right) {
            return !left.Equals(right);
        }
    }
}