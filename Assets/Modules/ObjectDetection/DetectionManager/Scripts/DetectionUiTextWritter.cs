// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Creates a "typewriter" effect for UI text:
    /// - Gradually reveals characters over time
    /// - Plays a sound per character
    /// - Adds slight pauses for punctuation
    /// </summary>
    [MetaCodeSample("XRSeeingAid-MultiObjectDetection")]
    public class DetectionUiTextWritter : MonoBehaviour
    {
        // Target UI Text element
        [SerializeField] private Text m_labelInfo;

        // Time delay between each character (smaller = faster typing)
        [SerializeField] private float m_writtingSpeed = 0.00015f;

        // Additional pause when encountering ':' character
        [SerializeField] private float m_writtingInfoPause = 0.005f;

        // Sound played per character typed
        [SerializeField] private AudioSource m_writtingSound;

        // Events triggered at start and end of typing
        public UnityEvent OnStartWritting;
        public UnityEvent OnFinishWritting;

        // Timer controlling character reveal timing
        private float m_writtingTime = 0;

        // Whether typing animation is currently active
        private bool m_isWritting = false;

        // Full text to be revealed
        private string m_currentInfo = "";

        // Current character index being revealed
        private int m_currentInfoIndex = 0;

        /// <summary>
        /// Initialize typing effect on startup.
        /// </summary>
        private void Start()
        {
            SetWrittingConfig();
        }

        /// <summary>
        /// Reinitialize typing when object is enabled.
        /// </summary>
        private void OnEnable()
        {
            SetWrittingConfig();
        }

        /// <summary>
        /// Reset typing state when object is disabled.
        /// </summary>
        private void OnDisable()
        {
            m_isWritting = false;
            m_writtingTime = 0;
            m_currentInfoIndex = 0;

            // Restore full text immediately
            m_labelInfo.text = m_currentInfo;
        }

        /// <summary>
        /// Handles the typewriter animation each frame.
        /// </summary>
        private void LateUpdate()
        {
            if (m_isWritting)
            {
                // Time to reveal next character
                if (m_writtingTime <= 0)
                {
                    // Reset timer
                    m_writtingTime = m_writtingSpeed;

                    // Play typing sound (if assigned)
                    m_writtingSound?.Play();

                    // Get next character
                    var nextChar = m_currentInfo.Substring(m_currentInfoIndex, 1);

                    // Append character to visible text
                    m_labelInfo.text += nextChar;

                    // Add extra pause for readability after ':'
                    if (nextChar == ":")
                    {
                        m_writtingTime += m_writtingInfoPause;
                    }

                    // Move to next character
                    m_currentInfoIndex++;

                    // If finished typing
                    if (m_currentInfoIndex >= m_currentInfo.Length)
                    {
                        m_isWritting = false;
                        OnFinishWritting?.Invoke();
                    }
                }
                else
                {
                    // Countdown timer
                    m_writtingTime -= Time.deltaTime;
                }
            }
        }

        /// <summary>
        /// Initializes typing animation using current label text.
        /// </summary>
        private void SetWrittingConfig()
        {
            if (!m_isWritting)
            {
                m_isWritting = true;

                // Cache full text and clear UI
                m_currentInfo = m_labelInfo.text;
                m_labelInfo.text = "";

                // Notify listeners that typing started
                OnStartWritting?.Invoke();
            }
        }
    }
}