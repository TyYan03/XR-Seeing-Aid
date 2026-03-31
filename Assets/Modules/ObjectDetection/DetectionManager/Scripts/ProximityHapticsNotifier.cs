// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Linq;
using UnityEngine;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Provides haptic feedback (controller vibration) based on proximity of detected objects.
    /// 
    /// Behavior:
    /// - Finds the closest detected object
    /// - Maps distance → vibration intensity
    /// - Sends vibration to:
    ///     • Left controller (object on left)
    ///     • Right controller (object on right)
    ///     • Both controllers (object directly in front)
    /// 
    /// Uses OVRInput.SetControllerVibration (Meta Quest compatible).
    /// </summary>
    public class ProximityHapticsNotifier : MonoBehaviour
    {
        [Header("Inference Source")]
        // Reference to detection results (bounding boxes with world positions)
        [SerializeField]
        private SentisInferenceUiManager m_uiInference;

        [Header("Proximity Settings")]

        // Maximum distance at which vibration is applied
        [Tooltip("Maximum distance (meters) at which haptics will trigger. Objects farther away produce no vibration.")]
        [SerializeField]
        private float m_maxDistance = 2.0f;

        // Minimum distance that produces maximum vibration intensity
        [Tooltip("Minimum distance (meters) which maps to max vibration intensity.")]
        [SerializeField]
        private float m_minDistance = 0.1f;

        // How often vibration updates (lower = more responsive, higher = less CPU usage)
        [Tooltip("How quickly vibration intensity updates (seconds). Lower = more responsive.")]
        [SerializeField]
        private float m_updateInterval = 0.05f;

        // Horizontal threshold for "centered" objects (both controllers vibrate)
        [Tooltip("If object X offset is within this range, both controllers vibrate.")]
        [SerializeField]
        private float m_frontWidth = 0.15f;

        [Header("Haptics Settings")]

        // Frequency parameter for vibration (0–1)
        [Tooltip("Frequency parameter passed to OVRInput.SetControllerVibration (0..1).")]
        [SerializeField]
        private float m_frequency = 1.0f;

        // Timer controlling update frequency
        private float m_timer = 0f;

        // Cached main camera reference
        private Camera m_cam;

        /// <summary>
        /// Initialize references.
        /// </summary>
        private void Start()
        {
            // Auto-find inference manager if not assigned
            if (m_uiInference == null)
            {
                m_uiInference = FindObjectOfType<SentisInferenceUiManager>();
            }

            // Cache main camera
            m_cam = Camera.main;
        }

        /// <summary>
        /// Ensure haptics stop when object is disabled.
        /// </summary>
        private void OnDisable()
        {
            StopAllHaptics();
        }

        /// <summary>
        /// Main update loop:
        /// - Runs at a fixed interval
        /// - Finds closest object
        /// - Computes intensity and direction
        /// - Applies vibration
        /// </summary>
        private void Update()
        {
            // Throttle update rate
            m_timer -= Time.deltaTime;
            if (m_timer > 0f) return;
            m_timer = m_updateInterval;

            // Validate required references
            if (m_uiInference == null || m_cam == null)
            {
                StopAllHaptics();
                return;
            }

            // Get detected bounding boxes
            var boxes = m_uiInference.BoxDrawn;

            if (boxes == null || boxes.Count == 0)
            {
                StopAllHaptics();
                return;
            }

            // Find closest detected object with a valid world position
            float bestDist = float.MaxValue;
            Vector3 bestPos = Vector3.zero;

            foreach (var b in boxes)
            {
                if (!b.WorldPos.HasValue) continue;

                float d = Vector3.Distance(m_cam.transform.position, b.WorldPos.Value);

                if (d < bestDist)
                {
                    bestDist = d;
                    bestPos = b.WorldPos.Value;
                }
            }

            // If no valid object or too far away, stop haptics
            if (bestDist == float.MaxValue || bestDist > m_maxDistance)
            {
                StopAllHaptics();
                return;
            }

            // Convert distance → normalized intensity
            // 1 = very close, 0 = far
            float t = Mathf.InverseLerp(m_maxDistance, m_minDistance, bestDist);
            float intensity = Mathf.Clamp01(t);

            // Determine relative position (left / right / center)
            var local = m_cam.transform.InverseTransformPoint(bestPos);

            bool isFront = Mathf.Abs(local.x) <= m_frontWidth;
            bool isRight = local.x > 0f;

            // Apply vibration based on direction
            if (isFront)
            {
                // Object directly ahead → both controllers
                PlayHaptics(vibrateLeft: true, vibrateRight: true, intensity: intensity);
            }
            else if (isRight)
            {
                // Object on right → right controller
                PlayHaptics(vibrateLeft: false, vibrateRight: true, intensity: intensity);
            }
            else
            {
                // Object on left → left controller
                PlayHaptics(vibrateLeft: true, vibrateRight: false, intensity: intensity);
            }
        }

        /// <summary>
        /// Applies vibration to controllers using OVRInput.
        /// </summary>
        /// <param name="vibrateLeft">Whether to vibrate left controller</param>
        /// <param name="vibrateRight">Whether to vibrate right controller</param>
        /// <param name="intensity">Vibration strength (0–1)</param>
        private void PlayHaptics(bool vibrateLeft, bool vibrateRight, float intensity)
        {
            #if OVRPLUGIN_PRESENT || UNITY_ANDROID
            try
            {
                // Right controller
                if (vibrateRight)
                {
                    OVRInput.SetControllerVibration(m_frequency, intensity, OVRInput.Controller.RTouch);
                }
                else
                {
                    OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                }

                // Left controller
                if (vibrateLeft)
                {
                    OVRInput.SetControllerVibration(m_frequency, intensity, OVRInput.Controller.LTouch);
                }
                else
                {
                    OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                }
            }
            catch (System.Exception)
            {
                // Fail silently if OVRInput is unavailable
            }
            #else
            // No haptics support in this platform/configuration
            #endif
        }

        /// <summary>
        /// Stops all controller vibrations.
        /// </summary>
        private void StopAllHaptics()
        {
            #if OVRPLUGIN_PRESENT || UNITY_ANDROID
            try
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            }
            catch (System.Exception)
            {
                // Ignore errors
            }
            #endif
        }
    }
}