using UnityEngine;
using UnityEngine.Events;

namespace ImpactCFXDemo
{
    public class DemoButton : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField]
        private UnityEvent onPressed;

        [Header("Animation")]
        [SerializeField]
        private Transform animationTransform;
        [SerializeField]
        private float pressAnimationTime = 0.5f;
        [SerializeField]
        private float pressAnimationHeight = 0.1f;
        [SerializeField]
        private AudioSource pressedAudio;

        private bool isPressed;
        private float pressAnimationTimer;

        private Vector3 defaultPosition;
        private Vector3 pressedPosition;

        private void Awake()
        {
            defaultPosition = animationTransform.localPosition;
            pressedPosition = defaultPosition - new Vector3(0, pressAnimationHeight, 0);

            pressAnimationTimer = -pressAnimationTime;
        }

        public void Press()
        {
            onPressed.Invoke();
            pressedAudio.Play();

            pressAnimationTimer = pressAnimationTime;
        }

        private void Update()
        {
            if (pressAnimationTimer > -pressAnimationTime)
            {
                float t = Mathf.Abs(pressAnimationTimer) / pressAnimationTime;
                animationTransform.localPosition = Vector3.Lerp(pressedPosition, defaultPosition, Mathf.SmoothStep(0, 1, t));

                pressAnimationTimer -= Time.deltaTime;
            }
        }
    }
}

