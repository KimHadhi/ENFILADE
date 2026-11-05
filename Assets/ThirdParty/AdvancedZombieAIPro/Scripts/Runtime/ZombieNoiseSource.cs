using UnityEngine;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    public sealed class ZombieNoiseSource : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float defaultNoiseIntensity = 10f;

        public float DefaultNoiseIntensity
        {
            get => defaultNoiseIntensity;
            set => defaultNoiseIntensity = Mathf.Max(0f, value);
        }

        public void MakeNoise()
        {
            MakeNoise(defaultNoiseIntensity);
        }

        public void MakeNoise(float noiseIntensity)
        {
            ZombieAI.NotifyNoise(transform.position, Mathf.Max(0f, noiseIntensity));
        }
    }
}
