using UnityEngine;
#if RIP_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace RealisticImpacts
{
    // Fly camera for the demo scenes.
    // Hold right mouse to look around, WASD + Q/E to move, Shift for speed boost,
    // mouse wheel to change the base speed. Works with both input backends.
    public class DemoFlyCamera : MonoBehaviour
    {
        public float moveSpeed = 6f;
        public float boostMultiplier = 3f;
        public float lookSensitivity = 2.5f;

        private float yaw;
        private float pitch;

        private void Start()
        {
            Vector3 angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = angles.x > 180f ? angles.x - 360f : angles.x;

#if !RIP_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Debug.LogWarning("[Realistic Impacts] DemoFlyCamera: camera controls are disabled because " +
                "the Input System package is not installed. To move around the demo scene, install " +
                "\"Input System\" from Window > Package Manager > Unity Registry.", this);
#endif
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            bool looking = RightMouseHeld();

            Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !looking;

            if (looking)
            {
                Vector2 look = LookDelta() * lookSensitivity;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            float scroll = ScrollDelta();
            if (scroll != 0f)
                moveSpeed = Mathf.Clamp(moveSpeed * (1f + scroll * 0.15f), 0.5f, 100f);

            Vector3 dir = MoveDirection();
            if (dir.sqrMagnitude > 0f)
            {
                float speed = moveSpeed * (BoostHeld() ? boostMultiplier : 1f);
                transform.position += transform.TransformDirection(dir.normalized) * (speed * Time.deltaTime);
            }
        }

#if RIP_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static bool RightMouseHeld()
            => Mouse.current != null && Mouse.current.rightButton.isPressed;

        private static bool BoostHeld()
            => Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        private static Vector2 LookDelta()
            => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.05f : Vector2.zero;

        private static float ScrollDelta()
            => Mouse.current != null ? Mathf.Clamp(Mouse.current.scroll.ReadValue().y, -1f, 1f) : 0f;

        private static Vector3 MoveDirection()
        {
            Keyboard k = Keyboard.current;
            if (k == null)
                return Vector3.zero;

            Vector3 dir = Vector3.zero;
            if (k.wKey.isPressed) dir.z += 1f;
            if (k.sKey.isPressed) dir.z -= 1f;
            if (k.dKey.isPressed) dir.x += 1f;
            if (k.aKey.isPressed) dir.x -= 1f;
            if (k.eKey.isPressed) dir.y += 1f;
            if (k.qKey.isPressed) dir.y -= 1f;
            return dir;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
        private static bool RightMouseHeld() => Input.GetMouseButton(1);

        private static bool BoostHeld() => Input.GetKey(KeyCode.LeftShift);

        private static Vector2 LookDelta()
            => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

        private static float ScrollDelta()
            => Mathf.Clamp(Input.mouseScrollDelta.y, -1f, 1f);

        private static Vector3 MoveDirection()
        {
            Vector3 dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir.z += 1f;
            if (Input.GetKey(KeyCode.S)) dir.z -= 1f;
            if (Input.GetKey(KeyCode.D)) dir.x += 1f;
            if (Input.GetKey(KeyCode.A)) dir.x -= 1f;
            if (Input.GetKey(KeyCode.E)) dir.y += 1f;
            if (Input.GetKey(KeyCode.Q)) dir.y -= 1f;
            return dir;
        }
#else
        // Project set to the new Input System without the package installed:
        // no input backend available, stay idle instead of throwing.
        private static bool RightMouseHeld() => false;
        private static bool BoostHeld() => false;
        private static Vector2 LookDelta() => Vector2.zero;
        private static float ScrollDelta() => 0f;
        private static Vector3 MoveDirection() => Vector3.zero;
#endif
    }
}
