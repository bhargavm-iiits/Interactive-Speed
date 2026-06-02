using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor script that runs automatically inside the Unity Editor.
/// Finds the VRCar GameObject and adds a kinematic Rigidbody to it if missing.
/// This silences the "WheelCollider requires an attached Rigidbody to function" warning on scene load.
/// </summary>
[InitializeOnLoad]
public static class VRCarRigidbodyFixer
{
    static VRCarRigidbodyFixer()
    {
        EditorApplication.hierarchyChanged += FixVRCarRigidbody;
    }

    private static void FixVRCarRigidbody()
    {
        // Don't run this check/modification during Play Mode to prevent InvalidOperationException
        if (EditorApplication.isPlaying || EditorApplication.isPaused) return;

        var carGo = GameObject.Find("VRCar");
        if (carGo != null)
        {
            var rb = carGo.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = carGo.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                Debug.Log("[VRCarRigidbodyFixer] Automatically added kinematic Rigidbody to VRCar in Editor to silence WheelCollider warning.");
                EditorSceneManager.MarkSceneDirty(carGo.scene);
            }
        }
    }
}
