using UnityEngine;

namespace ImpactCFXDemo
{
    public class DemoInteractiveObject : MonoBehaviour
    {
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Rigidbody objectRigidbody;

        private void Awake()
        {
            objectRigidbody = GetComponent<Rigidbody>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        public void ResetObject()
        {
            transform.position = startPosition;
            transform.rotation = startRotation;

            objectRigidbody.linearVelocity = Vector3.zero;
            objectRigidbody.angularVelocity = Vector3.zero;
        }
    }
}

