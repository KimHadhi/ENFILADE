// Guid by Tobias Johansson
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Sonity.Internal {

    [StructLayout(LayoutKind.Explicit), Serializable]
    public struct SonityGuid : IComparable<SonityGuid>, IEquatable<SonityGuid>, IFormattable {

        [NonSerialized]
        [FieldOffset(0)]
        public Guid Guid;
        [FieldOffset(0), SerializeField]
        public int A;
        [FieldOffset(4), SerializeField]
        public int B;
        [FieldOffset(8), SerializeField]
        public int C;
        [FieldOffset(12), SerializeField]
        public int D;

        // AssetDatabase is only available in the editor 
        public SonityGuid(UnityEngine.Object unityObject) {
#if UNITY_EDITOR
            if (unityObject == null) {
                this = Empty;
            } else {
                this = Parse(UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(unityObject)));
            }
#else
            this = Empty;
#endif
        }

        public static SonityGuid Empty = new SonityGuid(0, 0, 0, 0);

        public bool IsEmpty() {
            return Equals(Empty);
        }

        public SonityGuid(SonityGuid source) {
            Guid = default;
            A = source.A;
            B = source.B;
            C = source.C;
            D = source.D;
        }

        public SonityGuid(int a, int b, int c, int d) {
            Guid = default;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public SonityGuid(byte[] bytes) {
            Guid = default;
            A = BitConverter.ToInt32(bytes, 0);
            B = BitConverter.ToInt32(bytes, 4);
            C = BitConverter.ToInt32(bytes, 8);
            D = BitConverter.ToInt32(bytes, 12);
        }

        public SonityGuid(string input) {
            this = Parse(input);
        }

        public SonityGuid(Guid guid) {
            this = new SonityGuid(guid.ToByteArray());
        }

        public Guid ToGuid() {
            return Guid;
        }

        public static SonityGuid NewGuid() {
            return new SonityGuid(Guid.NewGuid());
        }

        public int CompareTo(SonityGuid other) {
            int result = A.CompareTo(other.A);
            if (result == 0) result = B.CompareTo(other.B);
            if (result == 0) result = C.CompareTo(other.C);
            if (result == 0) result = D.CompareTo(other.D);
            return result;
        }

        public static implicit operator Guid(SonityGuid guid) {
            return guid.ToGuid();
        }

        public static implicit operator SonityGuid(Guid guid) {
            return new SonityGuid(guid);
        }

        public bool Equals(SonityGuid other) {
            return A == other.A && B == other.B && C == other.C && D == other.D;
        }

        public static SonityGuid Parse(string input) {
            return Guid.Parse(input);
        }

        public override string ToString() {
            return ToGuid().ToString();
        }

        public string ToString(string format, IFormatProvider formatProvider) {
            return ToGuid().ToString(format, formatProvider);
        }

        public static bool operator ==(SonityGuid a, SonityGuid b) {
            return a.Equals(b);
        }

        public static bool operator !=(SonityGuid a, SonityGuid b) {
            return a.Equals(b) == false;
        }

        public static bool operator ==(SonityGuid a, Guid b) {
            return a.Equals(b);
        }

        public static bool operator !=(SonityGuid a, Guid b) {
            return a.Equals(b) == false;
        }

        public override bool Equals(object obj) {
            if ((obj is SonityGuid) == false) {
                return false;
            }
            SonityGuid guid = (SonityGuid)obj;
            return A == guid.A && B == guid.B && C == guid.C && D == guid.D;
        }

        public Int32 CompareTo(object obj) {
            if (obj == null) {
                return -1;
            }
            if (obj is Guid) {
                return ((Guid)obj).CompareTo(Guid);
            }
            return -1;
        }

        public Int32 CompareTo(Guid other) {
            return Guid.CompareTo(other);
        }

        public bool Equals(Guid other) {
            return Guid == other;
        }

        public override int GetHashCode() {
            return A.GetHashCode() ^ B.GetHashCode() ^ C.GetHashCode() ^ D.GetHashCode();
        }
    }
}