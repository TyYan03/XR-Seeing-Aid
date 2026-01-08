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

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionManager : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [Header("Controls configuration")]
        [SerializeField] private OVRInput.RawButton m_actionButton = OVRInput.RawButton.A;

        [Header("Ui references")]
        [SerializeField] private DetectionUiMenuManager m_uiMenuManager;

        [Header("Placement configureation")]
        [SerializeField] private GameObject m_spwanMarker;
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
        [SerializeField] private float m_spawnDistance = 0.25f;
        [SerializeField] private AudioSource m_placeSound;

        [Header("Sentis inference ref")]
        [SerializeField] private SentisInferenceRunManager m_runInference;
        [SerializeField] private SentisInferenceUiManager m_uiInference;
        [Space(10)]
        public UnityEvent<int> OnObjectsIdentified;

        private bool m_isPaused = true;
        private List<GameObject> m_spwanedEntities = new();
        private bool m_isStarted = false;
        private bool m_isSentisReady = false;
        private float m_delayPauseBackTime = 0;

        [Header("Voice SDK (optional)")]
        [Tooltip("Optional: assign a TTSSpeaker GameObject (from Meta Voice SDK). If assigned or available, the notifier will use the Voice SDK to play TTS audio.")]
        [SerializeField]
        private UnityEngine.Object m_ttsSpeakerObject;

        // Reflection handles for optional Voice SDK TTSSpeaker
        private object m_ttsSpeakerInstance = null;
        private MethodInfo m_ttsSpeakMethod = null;
        private bool m_voiceSdkAvailable = false;

        #region Unity Functions
        private void Awake() => OVRManager.display.RecenteredPose += CleanMarkersCallBack;

        private void OnDestroy() => OVRManager.display.RecenteredPose -= CleanMarkersCallBack;

        private IEnumerator Start()
        {
            // Wait until Sentis model is loaded
            var sentisInference = FindAnyObjectByType<SentisInferenceRunManager>();
            while (!sentisInference.IsModelLoaded)
            {
                yield return null;
            }
            // Add speak instructions here
            Debug.Log("VoiceManager.Start()");
            if (m_ttsSpeakerObject != null)
            {
                // Alert user how to start program
                if (m_ttsSpeakerObject is GameObject go)
                {
                    // try to get a component named TTSSpeaker
                    var comp = go.GetComponent("TTSSpeaker");
                    if (comp != null)
                    {
                        m_ttsSpeakerInstance = comp;
                        var t = m_ttsSpeakerInstance.GetType();
                        m_ttsSpeakMethod = t.GetMethod("Speak", new System.Type[] { typeof(string) });
                        m_voiceSdkAvailable = m_ttsSpeakMethod != null;
                        Debug.Log($"Voice SDK speaker found on assigned GameObject. Available={m_voiceSdkAvailable}");
                        m_ttsSpeakMethod.Invoke(m_ttsSpeakerInstance, new object[] { "ECE496. This program will detect obstacles and provide multi-modal feedback. Press the Ae button to start." });
                        Debug.Log($"Voice SDK TTSSpeaker invoked: Speak and hold down the A button, release A when you are finished with your instruction");
                    }
                    else
                    {
                        Debug.Log("Assigned GameObject does not contain a TTSSpeaker component.");
                    }
                }
            }
            m_isSentisReady = true;
        }

        private void Update()
        {
            if (!m_isStarted)
            {
                // Manage the Initial Ui Menu
                if (m_cameraAccess.IsPlaying && m_isSentisReady)
                {
                    m_isStarted = true;
                }
            }
            else
            {
                // Press A button to spawn 3d markers
                // if (OVRInput.GetUp(m_actionButton) && m_delayPauseBackTime <= 0)
                // {
                //     SpwanCurrentDetectedObjects();
                // }
                // // Cooldown for the A button after return from the pause menu
                // m_delayPauseBackTime -= Time.deltaTime;
                // if (m_delayPauseBackTime <= 0)
                // {
                //     m_delayPauseBackTime = 0;
                // }
            }

            // Don't start Sentis inference if the app is paused or we don't have a camera image yet
            if (m_isPaused || !m_cameraAccess.IsPlaying)
            {
                if (m_isPaused)
                {
                    // Set the delay time for the A button to return from the pause menu
                    m_delayPauseBackTime = 0.1f;
                }
                return;
            }

            // Run a new inference when the current inference finishes
            if (!m_runInference.IsRunning())
            {
                m_runInference.RunInference(m_cameraAccess);
            }
        }
        #endregion

        #region Marker Functions
        /// <summary>
        /// Clean 3d markers when the tracking space is re-centered.
        /// </summary>
        private void CleanMarkersCallBack()
        {
            foreach (var e in m_spwanedEntities)
            {
                Destroy(e, 0.1f);
            }
            m_spwanedEntities.Clear();
            OnObjectsIdentified?.Invoke(-1);
        }
        /// <summary>
        /// Spwan 3d markers for the detected objects
        /// </summary>
        private void SpwanCurrentDetectedObjects()
        {
            var count = 0;
            foreach (var box in m_uiInference.BoxDrawn)
            {
                if (PlaceMarkerUsingEnvironmentRaycast(box.WorldPos, box.ClassName))
                {
                    count++;
                }
            }
            if (count > 0)
            {
                // Play sound if a new marker is placed.
                m_placeSound.Play();
            }
            OnObjectsIdentified?.Invoke(count);
        }

        /// <summary>
        /// Place a marker using the environment raycast
        /// </summary>
        private bool PlaceMarkerUsingEnvironmentRaycast(Vector3? position, string className)
        {
            // Check if the position is valid
            if (!position.HasValue)
            {
                return false;
            }

            // Check if you spanwed the same object before
            var existMarker = false;
            foreach (var e in m_spwanedEntities)
            {
                var markerClass = e.GetComponent<DetectionSpawnMarkerAnim>();
                if (markerClass)
                {
                    var dist = Vector3.Distance(e.transform.position, position.Value);
                    if (dist < m_spawnDistance && markerClass.GetYoloClassName() == className)
                    {
                        existMarker = true;
                        break;
                    }
                }
            }

            if (!existMarker)
            {
                // spawn a visual marker
                var eMarker = Instantiate(m_spwanMarker);
                m_spwanedEntities.Add(eMarker);

                // Update marker transform with the real world transform
                eMarker.transform.SetPositionAndRotation(position.Value, Quaternion.identity);
                eMarker.GetComponent<DetectionSpawnMarkerAnim>().SetYoloClassName(className);
            }

            return !existMarker;
        }
        #endregion

        #region Public Functions
        /// <summary>
        /// Pause the detection logic when the pause menu is active
        /// </summary>
        public void OnPause(bool pause)
        {
            m_isPaused = pause;
        }
        #endregion
    }
}
