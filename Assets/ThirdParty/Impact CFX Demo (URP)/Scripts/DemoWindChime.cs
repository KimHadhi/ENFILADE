using UnityEngine;

namespace ImpactCFXDemo
{
    public class DemoWindChime : MonoBehaviour
    {
        public Rigidbody[] Rigidbodies;
        public float WindIntensity;
        public float WindSpeedNoiseSpeed;
        public float WindRotationNoiseSpeed;

        private Vector2 speedNoiseCoordinates;
        private Vector2 rotationNoiseCoordinates;

        private void Awake()
        {
            speedNoiseCoordinates = Random.insideUnitCircle * 100;
            rotationNoiseCoordinates = Random.insideUnitCircle * 100;
        }

        private void FixedUpdate()
        {
            float windNoise = Mathf.PerlinNoise(speedNoiseCoordinates.x, speedNoiseCoordinates.y) * 2 - 1;
            float windSpeed = Mathf.Clamp01(windNoise) * WindIntensity;
            float windRotation = Mathf.PerlinNoise(rotationNoiseCoordinates.x, rotationNoiseCoordinates.y) * 360f;

            Vector3 windVector = Quaternion.Euler(0, windRotation, 0) * Vector3.forward * windSpeed;
            Debug.DrawRay(transform.position, windVector, Color.white);

            if (Rigidbodies != null)
            {
                foreach (Rigidbody r in Rigidbodies)
                {
                    r.AddForce(windVector, ForceMode.Force);
                }
            }

            rotationNoiseCoordinates += Vector2.one * WindRotationNoiseSpeed * Time.deltaTime;
            speedNoiseCoordinates -= Vector2.one * WindSpeedNoiseSpeed * Time.deltaTime;
        }
    }
}

