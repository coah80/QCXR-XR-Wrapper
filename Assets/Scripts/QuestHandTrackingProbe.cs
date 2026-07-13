using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

public sealed class QuestHandTrackingProbe : MonoBehaviour
{
    private static readonly List<XRHandSubsystem> Subsystems = new();
    private string lastState;

    [RuntimeInitializeOnLoadMethod]
    private static void Initialize()
    {
        var probe = new GameObject(nameof(QuestHandTrackingProbe));
        DontDestroyOnLoad(probe);
        probe.AddComponent<QuestHandTrackingProbe>();
    }

    private IEnumerator Start()
    {
        while (true)
        {
            SubsystemManager.GetSubsystems(Subsystems);
            var subsystem = Subsystems.Count > 0 ? Subsystems[0] : null;
            var state = subsystem == null
                ? "missing"
                : $"running={subsystem.running} left={subsystem.leftHand.isTracked} right={subsystem.rightHand.isTracked}";

            if (state != lastState)
            {
                Debug.Log($"QuestHandTracking {state}");
                lastState = state;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
