using UnityEditor;
using UnityEngine;

// GunClipRetarget: KINEMATION's camera. One part of the class; the rest is in GunClipRetarget.cs and the files beside it.
internal static partial class GunClipRetarget
{
    // KINEMATION's camera point on its arms: the CameraSocket its CAS integration puts on the arms skeleton's head
    // (SK_Arms_Mono_Skeleton), copied onto the source arms here with the same parent bone and local pose. The arms model
    // itself has no camera bone, and the clips' camera_bone (under root) only carries the camera's motion, not where it
    // sits: the guns were placed with no camera at all until 2026-11-07, and sat at the eyes' height.
    private const string CameraSocketName = "CameraSocket", ArmsSkeletonName = "SK_Arms_Mono_Skeleton";

    private static Transform AddCameraSocket(Transform source)
    {
        var existing = FindLike(source, CameraSocketName);
        if (existing != null) return existing;
        Transform socket = null;
        foreach (string guid in AssetDatabase.FindAssets($"{ArmsSkeletonName} t:Prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            socket = prefab != null ? FindLike(prefab.transform, CameraSocketName) : null;
            if (socket != null) break;
        }
        var parent = socket != null && socket.parent != null ? FindLike(source, socket.parent.name) : null;
        if (parent == null)
        {
            Debug.LogWarning($"GunClipRetarget: KINEMATION's {CameraSocketName} ({ArmsSkeletonName}) was not found; the guns are placed where Retarget Pro puts them.");
            return null;
        }
        var made = new GameObject(CameraSocketName) { hideFlags = HideFlags.DontSave }.transform;
        made.SetParent(parent, false);
        made.SetLocalPositionAndRotation(socket.localPosition, socket.localRotation);
        return made;
    }

    // Another pack's camera point: the Camera on its rig (the Tactical Shooter Pack's FPCamera on its character's head),
    // copied onto the source skeleton under the bone of the same name, with the same local pose, as AddCameraSocket does.
    private static Transform AddCameraFrom(Transform source, GameObject rig)
    {
        var camera = rig.GetComponentInChildren<Camera>(true);
        var parent = camera != null && camera.transform.parent != null ? FindLike(source, camera.transform.parent.name) : null;
        if (parent == null)
        {
            Debug.LogWarning($"GunClipRetarget: {rig.name} has no camera on a bone of {source.name}; its guns are placed where Retarget Pro puts them.", rig);
            return null;
        }
        var made = new GameObject(CameraSocketName) { hideFlags = HideFlags.DontSave }.transform;
        made.SetParent(parent, false);
        made.SetLocalPositionAndRotation(camera.transform.localPosition, camera.transform.localRotation);
        return made;
    }
}
