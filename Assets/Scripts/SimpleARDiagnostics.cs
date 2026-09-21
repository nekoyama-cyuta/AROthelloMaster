using System;
using UnityEngine;

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using Unity.XR.XREAL;
#endif

public class SimpleARDiagnostics : MonoBehaviour
{
    [Header("UI Text Display")]
    public TextMesh statusText;

    [Header("Rotation")]
    public Vector3 rotationSpeed = new Vector3(20f, 40f, 15f);

    private float updateTimer = 0f;
    private const float UPDATE_INTERVAL = 0.05f; // Faster update for smooth diagnostics
    private int frameCounter = 0;
    private float fps = 60f;

    private XREALCameraPoseTracker m_PoseTracker;

    void Start()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            m_PoseTracker = mainCam.GetComponent<XREALCameraPoseTracker>();
        }
    }

    void Update()
    {
        // Continuous rotation for unmistakable proof of active rendering
        transform.Rotate(rotationSpeed * Time.deltaTime, Space.Self);

        frameCounter++;
        updateTimer += Time.deltaTime;
        if (updateTimer >= UPDATE_INTERVAL)
        {
            fps = frameCounter / updateTimer;
            frameCounter = 0;
            updateTimer = 0f;
            UpdateStatusText();
        }
    }

    private void UpdateStatusText()
    {
        if (statusText == null) return;

        try
        {
            string deviceTypeStr = "Unknown";
            string deviceCatStr = "Unknown";
            string trackingTypeStr = "Unknown";

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            deviceTypeStr = XREALPlugin.GetDeviceType().ToString();
            deviceCatStr = XREALPlugin.GetDeviceCategory().ToString();
            trackingTypeStr = XREALPlugin.GetTrackingType().ToString();
#else
            deviceTypeStr = "EditorSimulation";
            deviceCatStr = "Editor";
            trackingTypeStr = "Editor/3DoF";
#endif

            Vector3 camPos = Vector3.zero;
            Vector3 camRot = Vector3.zero;
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                camPos = mainCam.transform.position;
                camRot = mainCam.transform.eulerAngles;
                if (m_PoseTracker == null)
                {
                    m_PoseTracker = mainCam.GetComponent<XREALCameraPoseTracker>();
                }
            }

            string trackerStatus = m_PoseTracker != null ? m_PoseTracker.TrackingStatusString : "PoseTracker N/A";

            // Blinking heartbeat indicator (dot toggles every ~0.5s)
            string heartbeat = ((int)(Time.time * 2f) % 2 == 0) ? "[●]" : "[○]";

            int displayCount = Display.displays != null ? Display.displays.Length : 1;

            statusText.text = $"{heartbeat} XREAL One Pro AR Online\n" +
                              $"Time: {Time.time:F1}s | FPS: {fps:F0} | Displays: {displayCount}\n" +
                              $"Device: {deviceTypeStr} ({deviceCatStr})\n" +
                              $"Mode: {trackingTypeStr}\n" +
                              $"Tracker: {trackerStatus}\n" +
                              $"Cam Pos: ({camPos.x:F2}, {camPos.y:F2}, {camPos.z:F2})\n" +
                              $"Cam Rot: ({camRot.x:F0}, {camRot.y:F0}, {camRot.z:F0})";
        }
        catch (Exception ex)
        {
            statusText.text = $"[XREAL AR Error]\n{ex.Message}";
        }
    }
}
