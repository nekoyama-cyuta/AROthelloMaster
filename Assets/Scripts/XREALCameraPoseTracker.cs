using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using Unity.XR.XREAL;
#endif

/// <summary>
/// Directly tracks the XR Head / CenterEye node from Unity's XR Input system
/// and applies rotation (and position if 6DoF) directly to the Camera transform.
/// This completely eliminates dependency on unassigned Input System action maps.
/// </summary>
public class XREALCameraPoseTracker : MonoBehaviour
{
    [Header("Tracking Settings")]
    [Tooltip("Apply rotation tracking to this transform.")]
    public bool trackRotation = true;

    [Tooltip("Apply position tracking to this transform (6DoF).")]
    public bool trackPosition = true;

    [Tooltip("Fallback to 3DoF if position tracking is not available.")]
    public bool fallbackTo3DoF = true;

    // Diagnostic information exposed for HUD
    public bool IsHeadDeviceValid { get; private set; } = false;
    public string HeadDeviceName { get; private set; } = "None";
    public Vector3 LastRawPosition { get; private set; } = Vector3.zero;
    public Quaternion LastRawRotation { get; private set; } = Quaternion.identity;
    public string TrackingStatusString { get; private set; } = "Searching for Head Device...";

    private InputDevice m_HeadDevice;
    private float m_DeviceScanTimer = 0f;
    private const float SCAN_INTERVAL = 1.0f;

    void OnEnable()
    {
        InputDevices.deviceConnected += OnDeviceConnected;
        InputDevices.deviceDisconnected += OnDeviceDisconnected;
        AcquireHeadDevice();
    }

    void OnDisable()
    {
        InputDevices.deviceConnected -= OnDeviceConnected;
        InputDevices.deviceDisconnected -= OnDeviceDisconnected;
    }

    void Start()
    {
        AcquireHeadDevice();
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        if (trackPosition)
        {
            try
            {
                _ = XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_6DOF);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XREALCameraPoseTracker] Failed to switch 6DoF: {ex.Message}");
            }
        }
#endif
    }

    void Update()
    {
        if (!m_HeadDevice.isValid)
        {
            m_DeviceScanTimer += Time.deltaTime;
            if (m_DeviceScanTimer >= SCAN_INTERVAL)
            {
                m_DeviceScanTimer = 0f;
                AcquireHeadDevice();
            }
        }

        if (m_HeadDevice.isValid)
        {
            IsHeadDeviceValid = true;
            HeadDeviceName = m_HeadDevice.name;

            bool gotRot = false;
            bool gotPos = false;

            if (trackRotation && m_HeadDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            {
                transform.localRotation = rot;
                LastRawRotation = rot;
                gotRot = true;
            }

            if (trackPosition && m_HeadDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
            {
                // In 6DoF mode, apply positional offset
                transform.localPosition = pos;
                LastRawPosition = pos;
                gotPos = true;
            }

            if (gotRot && gotPos)
            {
                TrackingStatusString = $"6DoF Active ({HeadDeviceName})";
            }
            else if (gotRot)
            {
                TrackingStatusString = $"3DoF Active (Rot only) ({HeadDeviceName})";
            }
            else
            {
                TrackingStatusString = $"Device Valid, waiting for pose ({HeadDeviceName})";
            }
        }
        else
        {
            IsHeadDeviceValid = false;
            TrackingStatusString = "Head Device not found / 0DoF";
        }
    }

    private void AcquireHeadDevice()
    {
        m_HeadDevice = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!m_HeadDevice.isValid)
        {
            m_HeadDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        }

        if (!m_HeadDevice.isValid)
        {
            // Scan all HMD devices
            List<InputDevice> hmds = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmds);
            if (hmds.Count > 0)
            {
                m_HeadDevice = hmds[0];
            }
        }

        if (m_HeadDevice.isValid)
        {
            IsHeadDeviceValid = true;
            HeadDeviceName = m_HeadDevice.name;
            Debug.Log($"[XREALCameraPoseTracker] Head device acquired: {m_HeadDevice.name} (Role: {m_HeadDevice.characteristics})");
        }
    }

    private void OnDeviceConnected(InputDevice device)
    {
        Debug.Log($"[XREALCameraPoseTracker] Device connected: {device.name}, characteristics: {device.characteristics}");
        if (!m_HeadDevice.isValid)
        {
            AcquireHeadDevice();
        }
    }

    private void OnDeviceDisconnected(InputDevice device)
    {
        Debug.Log($"[XREALCameraPoseTracker] Device disconnected: {device.name}");
        if (m_HeadDevice == device)
        {
            m_HeadDevice = default;
            IsHeadDeviceValid = false;
            TrackingStatusString = "Device disconnected";
        }
    }
}
