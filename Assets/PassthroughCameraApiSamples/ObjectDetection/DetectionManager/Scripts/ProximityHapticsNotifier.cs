// Copyright (c) Meta Platforms, Inc. and affiliates.
using System.Linq;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Vibrates the left or right controller with intensity based on the closest detected object's distance.
    /// Uses OVRInput.SetControllerVibration when available. If Meta Haptics SDK is present, this script can be extended
    /// to play .haptic clips instead (not required for simple proximity vibration).
    /// </summary>
    public class ProximityHapticsNotifier : MonoBehaviour
    {
        [SerializeField]
        private SentisInferenceUiManager m_uiInference;

        [Header("Proximity settings")]
        [Tooltip("Maximum distance (meters) at which haptics will trigger. Objects farther away produce no vibration.")]
        [SerializeField]
        private float m_maxDistance = 2.0f;

        [Tooltip("Minimum distance (meters) which maps to max vibration intensity.")]
        [SerializeField]
        private float m_minDistance = 0.1f;

        [Tooltip("How quickly vibration intensity updates (seconds). Lower = more responsive.")]
        [SerializeField]
        private float m_updateInterval = 0.05f;

        [Tooltip("If the detected object has an absolute X (camera local) <= this value, it is considered 'in front' and both controllers vibrate.")]
        [SerializeField]
        private float m_frontWidth = 0.15f;

        [Header("Haptics settings")]
        [Tooltip("Frequency parameter passed to OVRInput.SetControllerVibration (0..1).")]
        [SerializeField]
        private float m_frequency = 1.0f;

        // internal
        private float m_timer = 0f;
        private Camera m_cam;

        private void Start()
        {
            if (m_uiInference == null)
            {
                m_uiInference = FindObjectOfType<SentisInferenceUiManager>();
            }
            m_cam = Camera.main;
        }

        private void OnDisable()
        {
            StopAllHaptics();
        }

        private void Update()
        {
            m_timer -= Time.deltaTime;
            if (m_timer > 0f) return;
            m_timer = m_updateInterval;

            if (m_uiInference == null || m_cam == null)
            {
                StopAllHaptics();
                return;
            }

            // Find the closest detected box with a world position
            var boxes = m_uiInference.BoxDrawn;
            if (boxes == null || boxes.Count == 0)
            {
                StopAllHaptics();
                return;
            }

            float bestDist = float.MaxValue;
            Vector3 bestPos = Vector3.zero;
            foreach (var b in boxes)
            {
                if (!b.WorldPos.HasValue) continue;
                var d = Vector3.Distance(m_cam.transform.position, b.WorldPos.Value);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestPos = b.WorldPos.Value;
                }
            }

            if (bestDist == float.MaxValue || bestDist > m_maxDistance)
            {
                StopAllHaptics();
                return;
            }

            // Compute intensity: 1 at <= minDistance, 0 at >= maxDistance
            var t = Mathf.InverseLerp(m_maxDistance, m_minDistance, bestDist);
            var intensity = Mathf.Clamp01(t);

            // Decide side: if object is to the right in camera local space -> right controller, else left
            var local = m_cam.transform.InverseTransformPoint(bestPos);
            bool isFront = Mathf.Abs(local.x) <= m_frontWidth;
            bool isRight = local.x > 0f;

            if (isFront)
            {
                PlayHaptics(vibrateLeft: true, vibrateRight: true, intensity: intensity);
            }
            else if (isRight)
            {
                PlayHaptics(vibrateLeft: false, vibrateRight: true, intensity: intensity);
            }
            else
            {
                PlayHaptics(vibrateLeft: true, vibrateRight: false, intensity: intensity);
            }
        }

        private void PlayHaptics(bool vibrateLeft, bool vibrateRight, float intensity)
        {
            // player.Play(Controller.right);
            
            #if OVRPLUGIN_PRESENT || UNITY_ANDROID
            // Use OVRInput when available (works on Meta Quest)
            try
            {
                if (vibrateRight)
                {
                    OVRInput.SetControllerVibration(m_frequency, intensity, OVRInput.Controller.RTouch);
                }
                else
                {
                    OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                }

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
                // If OVRInput isn't available at runtime, do nothing
            }
            #else
            // No OVR available: no-op
            #endif
        }

        private void StopAllHaptics()
        {
            // player.Stop();
            #if OVRPLUGIN_PRESENT || UNITY_ANDROID
            try
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            }
            catch (System.Exception)
            {
            }
            #endif
        }
    }
}
