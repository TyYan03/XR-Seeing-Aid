// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.UI;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Simple UI helper that makes a Text element blink by toggling its alpha.
    /// Useful for drawing user attention to important UI messages.
    /// </summary>
    [MetaCodeSample("XRSeeingAid-MultiObjectDetection")]
    public class DetectionUiBlinkText : MonoBehaviour
    {
        // Reference to the UI Text element that will blink
        [SerializeField] private Text m_labelInfo;

        // Time interval (seconds) between visibility toggles
        [SerializeField] private float m_blinkSpeed = 0.3f;

        // Tracks elapsed time since last blink toggle
        private float m_blinkTime = 0.0f;

        // Cached color of the text (used to modify alpha only)
        private Color m_color;

        /// <summary>
        /// Cache the initial text color at startup.
        /// </summary>
        private void Start()
        {
            m_color = m_labelInfo.color;
        }

        /// <summary>
        /// Runs after all Update calls.
        /// Handles blinking logic by toggling alpha at intervals.
        /// </summary>
        private void LateUpdate()
        {
            // Accumulate elapsed time
            m_blinkTime += Time.deltaTime;

            // When enough time has passed, toggle visibility
            if (m_blinkTime >= m_blinkSpeed)
            {
                // Toggle alpha between visible (1) and invisible (0)
                m_color.a = m_color.a > 0f ? 0f : 1f;

                // Apply updated color to text
                m_labelInfo.color = m_color;

                // Reset timer
                m_blinkTime = 0;
            }
        }
    }
}