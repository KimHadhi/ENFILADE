using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Temporary, collider-free glove meshes until authored first-person hands are available.</summary>
[DisallowMultipleComponent]
public sealed class FistsView : MonoBehaviour
{
    private PlayerMeleeCombat melee;
    private Transform root, left, right;
    private Material glove, cuff;
    private float punchTime;
    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        melee = GetComponent<PlayerMeleeCombat>();
        var camera = GetComponentInParent<PlayerMovement>()?.GetComponentInChildren<Camera>();
        if (melee == null || camera == null) return;
        melee.Punched -= Punch; melee.Punched += Punch;
        if (root != null) return;
        root = new GameObject("Fists (prototype)").transform;
        root.SetParent(camera.transform, false);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        glove = new Material(shader) { color = new Color(0.24f, 0.3f, 0.34f) };
        cuff = new Material(shader) { color = new Color(0.08f, 0.11f, 0.14f) };
        left = Hand("Left fist", -1); right = Hand("Right fist", 1);
    }
    private Transform Hand(string label, int side)
    {
        var hand = new GameObject(label).transform; hand.SetParent(root, false);
        Part(hand, "Palm", Vector3.zero, new Vector3(.12f, .12f, .16f), glove);
        Part(hand, "Thumb", new Vector3(-side * .065f, -.025f, .015f), new Vector3(.045f, .07f, .09f), glove);
        Part(hand, "Wrist", new Vector3(0, -.015f, -.105f), new Vector3(.10f, .10f, .09f), cuff);
        for (int i = 0; i < 4; i++) Part(hand, "Knuckle", new Vector3((i - 1.5f) * .03f, .025f, .085f), new Vector3(.028f, .065f, .035f), glove);
        return hand;
    }
    private void Part(Transform parent, string label, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = label;
        var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
        part.layer = root.parent.gameObject.layer;
        var renderer = part.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }
    private void Punch(float charge) => punchTime = .25f;
    private void LateUpdate()
    {
        if (root == null || melee == null) return;
        root.gameObject.SetActive(melee.IsActive);
        if (!melee.IsActive) { punchTime = 0f; return; }
        punchTime = Mathf.Max(0f, punchTime - Time.deltaTime);
        float punch = Mathf.Sin((1f - punchTime / .25f) * Mathf.PI);
        float guard = melee.IsGuarding || melee.IsGuardBreaking ? 1f : 0f;
        left.localPosition = Vector3.Lerp(new Vector3(-.22f, -.19f, .43f), new Vector3(-.12f, -.03f, .36f), guard);
        right.localPosition = Vector3.Lerp(new Vector3(.22f, -.19f, .43f - melee.ChargeRatio * .10f), new Vector3(.12f, -.03f, .36f), guard)
            + new Vector3(-punch * .1f, punch * .07f, punch * .25f);
        right.localRotation = Quaternion.Euler(-melee.ChargeRatio * 15f, -punch * 20f, -10f);
        left.localRotation = Quaternion.Euler(0f, 0f, 10f);
    }
    private void OnDestroy()
    {
        if (melee != null) melee.Punched -= Punch;
        if (root != null) Destroy(root.gameObject);
        if (glove != null) Destroy(glove);
        if (cuff != null) Destroy(cuff);
    }
    private void OnDisable()
    {
        if (melee != null) melee.Punched -= Punch;
        if (root != null) root.gameObject.SetActive(false);
    }
}
