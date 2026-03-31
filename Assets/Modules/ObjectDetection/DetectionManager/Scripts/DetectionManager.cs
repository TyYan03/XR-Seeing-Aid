// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Voice;
using System.Reflection;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Manages object detection lifecycle, UI state, and optional voice feedback.
    /// Coordinates camera input, Sentis inference, and user interaction.
    /// </summary>
    [MetaCodeSample("XRSeeingAid-MultiObjectDetection")]
    public class DetectionManager : MonoBehaviour
    {
        [Header("Camera Access")]
        // Provides passthrough camera frames for inference
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [Header("Sentis Inference References")]
        // Handles running ML inference
        [SerializeField] private SentisInferenceRunManager m_runInference;

        [Space(10)]
        // Whether detection is currently paused
        private bool m_isPaused = true;

        // Whether the system has started (camera + Sentis ready)
        private bool m_isStarted = false;

        // Whether Sentis model has finished loading
        private bool m_isSentisReady = false;

        // Small delay to prevent immediate resume after pause
        private float m_delayPauseBackTime = 0;

        [Header("Voice SDK (optional)")]
        [Tooltip("Optional: assign a TTSSpeaker GameObject (from Meta Voice SDK). If assigned or available, the notifier will use the Voice SDK to play TTS audio.")]
        [SerializeField]
        private UnityEngine.Object m_ttsSpeakerObject;

        // Reflection-based references for optional TTS speaker (avoids hard dependency)
        private object m_ttsSpeakerInstance = null;
        private MethodInfo m_ttsSpeakMethod = null;
        private bool m_voiceSdkAvailable = false;

        #region Unity Functions

        /// <summary>
        /// Coroutine that initializes the system after Sentis model is loaded.
        /// Also sets up optional voice feedback instructions.
        /// </summary>
        private IEnumerator Start()
        {
            // Wait until Sentis model is fully loaded before continuing
            var sentisInference = FindAnyObjectByType<SentisInferenceRunManager>();
            while (!sentisInference.IsModelLoaded)
            {
                yield return null;
            }

            // Initialize optional TTS system
            Debug.Log("VoiceManager.Start()");

            if (m_ttsSpeakerObject != null)
            {
                // Ensure assigned object is a GameObject
                if (m_ttsSpeakerObject is GameObject go)
                {
                    // Attempt to retrieve TTSSpeaker component via reflection
                    var comp = go.GetComponent("TTSSpeaker");

                    if (comp != null)
                    {
                        m_ttsSpeakerInstance = comp;

                        // Cache Speak(string) method for runtime invocation
                        var t = m_ttsSpeakerInstance.GetType();
                        m_ttsSpeakMethod = t.GetMethod("Speak", new Type[] { typeof(string) });

                        m_voiceSdkAvailable = m_ttsSpeakMethod != null;

                        Debug.Log($"Voice SDK speaker found. Available={m_voiceSdkAvailable}");

                        // Provide startup instructions via TTS
                        m_ttsSpeakMethod.Invoke(
                            m_ttsSpeakerInstance,
                            new object[]
                            {
                                "ECE496. This program will detect obstacles and provide multi-modal feedback. Press the A button to start."
                            }
                        );

                        Debug.Log("Voice SDK TTSSpeaker invoked with startup instructions.");
                    }
                    else
                    {
                        Debug.Log("Assigned GameObject does not contain a TTSSpeaker component.");
                    }
                }
            }

            // Mark Sentis system as ready
            m_isSentisReady = true;
        }

        /// <summary>
        /// Main update loop:
        /// - Waits for system readiness
        /// - Handles pause state
        /// - Triggers inference when ready
        /// </summary>
        private void Update()
        {
            // Wait until camera is running and Sentis is ready
            if (!m_isStarted)
            {
                if (m_cameraAccess.IsPlaying && m_isSentisReady)
                {
                    m_isStarted = true;
                }
            }

            // Do not run inference if paused or camera is not active
            if (m_isPaused || !m_cameraAccess.IsPlaying)
            {
                if (m_isPaused)
                {
                    // Small delay buffer for returning from pause state
                    m_delayPauseBackTime = 0.1f;
                }
                return;
            }

            // Run inference only if no inference is currently running
            if (!m_runInference.IsRunning())
            {
                m_runInference.RunInference(m_cameraAccess);
            }
        }

        #endregion

        #region Public Functions

        /// <summary>
        /// Toggles pause state of detection system.
        /// Called by UI or external systems.
        /// </summary>
        /// <param name="pause">True to pause, false to resume</param>
        public void OnPause(bool pause)
        {
            m_isPaused = pause;
        }

        #endregion
    }
}