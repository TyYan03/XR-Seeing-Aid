// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Monitors detected objects and provides audio warnings based on:
    /// - Distance to object
    /// - Direction relative to the user
    /// - Object priority (e.g., car > dog)
    ///
    /// Uses:
    /// - Meta Voice SDK (TTSSpeaker) if available
    /// - Android Text-to-Speech as fallback
    /// - Debug logs in Editor
    /// </summary>
    public class ObstacleAudioNotifier : MonoBehaviour
    {
        [Header("Distance Thresholds")]
        // Critical distance for danger warnings
        [SerializeField] private float DangerDistance = 1.0f;

        // Medium distance for warnings
        [SerializeField] private float WarningDistance = 2.0f;

        // Maximum distance to consider for alerts
        [SerializeField] private float MaxAlertDistance = 5.0f;

        [Tooltip("Objects within this horizontal range are considered directly in front")]
        [SerializeField] private float m_frontWidth = 0.35f;

        // Main camera reference
        private Camera m_cam;

        /// <summary>
        /// Priority map for object classes (lower = more dangerous)
        /// </summary>
        private readonly Dictionary<string, int> ClassPriority = new()
        {
            { "car", 1 },
            { "bus", 1 },
            { "truck", 1 },
            { "motorbike", 2 },
            { "bicycle", 2 },
            { "person", 2 },
            { "dog", 3 },
            { "cat", 4 },
            { "stop sign", 4 },
            { "traffic light", 5 }
        };

        [Header("Inference Source")]
        // Provides detected bounding boxes and world positions
        [SerializeField] private SentisInferenceUiManager m_uiInference;

        [Header("Filtering Settings")]
        [Tooltip("Minimum distance to trigger warnings")]
        [SerializeField] private float m_warningDistance = 1.0f;

        [Tooltip("Minimum dot product for object to be considered in front")]
        [SerializeField] private float m_frontDotThreshold = 0.5f;

        [Tooltip("Cooldown per object class (seconds)")]
        [SerializeField] private float m_cooldownSeconds = 5.0f;

        // Tracks last spoken time per object class
        private readonly Dictionary<string, float> m_lastSpokenPerClass = new();

        // Android TTS instance
        private AndroidJavaObject m_tts = null;
        private bool m_ttsInitialized = false;

        [Header("Voice SDK (optional)")]
        [SerializeField] private UnityEngine.Object m_ttsSpeakerObject;

        // Reflection-based TTS (Voice SDK)
        private object m_ttsSpeakerInstance = null;
        private MethodInfo m_ttsSpeakMethod = null;
        private bool m_voiceSdkAvailable = false;

        // Global cooldown to prevent rapid repeated speech
        private float m_last_time_spoken = 0f;

        /// <summary>
        /// Initialize references and TTS systems.
        /// </summary>
        private void Start()
        {
            if (m_uiInference == null)
            {
                m_uiInference = FindObjectOfType<SentisInferenceUiManager>();
            }

            // Initialize Android TTS if running on device
            if (Application.platform == RuntimePlatform.Android)
            {
                InitAndroidTts();
            }

            m_cam = Camera.main;

            // Attempt to initialize Voice SDK TTS
            TryInitVoiceSdkSpeaker();
        }

        /// <summary>
        /// Returns priority for a given class (default = low priority).
        /// </summary>
        private int GetPriority(string cls)
        {
            return ClassPriority.TryGetValue(cls, out var p) ? p : 99;
        }

        /// <summary>
        /// Computes a danger score combining:
        /// - Distance (closer = higher risk)
        /// - Priority (cars > people)
        /// - Alignment with forward direction
        /// </summary>
        float ComputeScore(int priority, float distance, float dot)
        {
            const float PriorityWeight = 10f;
            const float DotWeight = 2f;

            distance = Mathf.Max(distance, 0.1f);

            return (PriorityWeight / (distance * priority)) + (DotWeight * dot);
        }

        /// <summary>
        /// Builds a natural language warning sentence.
        /// </summary>
        private string BuildCombinedSentence(string cls, int count, float dist, bool isFront, bool isRight)
        {
            string dirPhrase = isFront ? "ahead" :
                               isRight ? "to your right" : "to your left";

            string noun = count > 1 ? $"{count} {cls}s" : $"A {cls}";

            if (dist < DangerDistance)
                return $"Danger. {noun} {dirPhrase} less than one meter away.";

            if (dist < WarningDistance)
                return $"Warning. {noun} {dirPhrase}, about {dist:F1} meters away.";

            return $"{noun} {dirPhrase}, approximately {dist:F1} meters away.";
        }

        /// <summary>
        /// Main loop:
        /// - Collect detections
        /// - Filter by distance & direction
        /// - Rank by danger score
        /// - Speak highest priority alert
        /// </summary>
        private void Update()
        {
            if (m_uiInference == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            var now = Time.time;

            // Collect valid alerts
            var alerts = new List<(string cls, float dist, float dot, Vector3 worldPos)>();

            foreach (var box in m_uiInference.BoxDrawn)
            {
                if (!box.WorldPos.HasValue) continue;

                var worldPos = box.WorldPos.Value;
                var dir = worldPos - cam.transform.position;
                var dist = dir.magnitude;

                if (dist > MaxAlertDistance) continue;

                var dot = Vector3.Dot(dir.normalized, cam.transform.forward);
                if (dot < m_frontDotThreshold) continue;

                var cls = string.IsNullOrEmpty(box.ClassName) ? "object" : box.ClassName;

                alerts.Add((cls, dist, dot, worldPos));
            }

            // Group detections by class
            var groups = alerts
                .GroupBy(a => a.cls)
                .Select(g =>
                {
                    var closest = g.OrderBy(a => a.dist).First();

                    return new
                    {
                        cls = g.Key,
                        count = g.Count(),
                        closestDist = closest.dist,
                        worldPos = closest.worldPos,
                        avgDot = g.Average(x => x.dot),
                        priority = GetPriority(g.Key)
                    };
                })
                .OrderBy(g => g.priority)
                .ToList();

            if (groups.Count == 0) return;

            // Score and pick most dangerous
            var scored = groups
                .Select(g => new
                {
                    g.cls,
                    g.count,
                    g.closestDist,
                    g.avgDot,
                    g.worldPos,
                    g.priority,
                    score = ComputeScore(g.priority, g.closestDist, g.avgDot)
                })
                .OrderByDescending(x => x.score)
                .ToList();

            var chosen = scored[0];

            // Cooldown checks
            m_lastSpokenPerClass.TryGetValue(chosen.cls, out var lastSpoken);

            if (now - lastSpoken < m_cooldownSeconds + 10f) return;
            if (now - m_last_time_spoken < m_cooldownSeconds) return;

            // Determine relative direction
            var local = m_cam.transform.InverseTransformPoint(chosen.worldPos);
            bool isFront = Mathf.Abs(local.x) <= m_frontWidth;
            bool isRight = local.x > 0f;

            // Build and speak message
            string message = BuildCombinedSentence(
                chosen.cls,
                chosen.count,
                chosen.closestDist,
                isFront,
                isRight
            );

            Speak(message);

            // Update cooldown trackers
            m_lastSpokenPerClass[chosen.cls] = now;
            m_last_time_spoken = now;
        }

        /// <summary>
        /// Handles speech output using:
        /// 1. Voice SDK (preferred)
        /// 2. Android TTS fallback
        /// 3. Debug logs (Editor)
        /// </summary>
        private void Speak(string text)
        {
            // Try Voice SDK first
            if (m_voiceSdkAvailable && m_ttsSpeakerInstance != null && m_ttsSpeakMethod != null)
            {
                try
                {
                    m_ttsSpeakMethod.Invoke(m_ttsSpeakerInstance, new object[] { text });
                    return;
                }
                catch { }
            }

            // Android TTS fallback
            if (Application.platform == RuntimePlatform.Android && m_tts != null && m_ttsInitialized)
            {
                try
                {
                    int sdkInt = 0;
                    using var ver = new AndroidJavaClass("android.os.Build$VERSION");
                    sdkInt = ver.GetStatic<int>("SDK_INT");

                    if (sdkInt >= 21)
                    {
                        var bundle = new AndroidJavaObject("android.os.Bundle");
                        var id = System.Guid.NewGuid().ToString();
                        m_tts.Call("speak", text, 0, bundle, id);
                    }
                    else
                    {
                        var hm = new AndroidJavaObject("java.util.HashMap");
                        m_tts.Call("speak", text, 0, hm);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"TTS failed: {e.Message}");
                }
            }
            else
            {
                Debug.Log(text);
            }
        }

        /// <summary>
        /// Attempts to initialize TTSSpeaker via inspector or reflection.
        /// </summary>
        private void TryInitVoiceSdkSpeaker()
        {
            if (m_ttsSpeakerObject != null)
            {
                if (m_ttsSpeakerObject is GameObject go)
                {
                    var comp = go.GetComponent("TTSSpeaker");
                    if (comp != null)
                    {
                        m_ttsSpeakerInstance = comp;
                        var t = comp.GetType();
                        m_ttsSpeakMethod = t.GetMethod("Speak", new System.Type[] { typeof(string) });
                        m_voiceSdkAvailable = m_ttsSpeakMethod != null;
                        return;
                    }
                }

                m_ttsSpeakerInstance = m_ttsSpeakerObject;
                var tt = m_ttsSpeakerInstance.GetType();
                m_ttsSpeakMethod = tt.GetMethod("Speak", new System.Type[] { typeof(string) });
                m_voiceSdkAvailable = m_ttsSpeakMethod != null;
                return;
            }

            m_voiceSdkAvailable = false;
        }

        /// <summary>
        /// Initializes Android TextToSpeech engine.
        /// </summary>
        private void InitAndroidTts()
        {
            try
            {
                using var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = up.GetStatic<AndroidJavaObject>("currentActivity");

                m_tts = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    activity,
                    new TtsInitListener(this)
                );
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to initialize Android TTS: {e.Message}");
                m_tts = null;
                m_ttsInitialized = false;
            }
        }

        /// <summary>
        /// Callback when Android TTS is initialized.
        /// </summary>
        public void OnTtsInit(int status)
        {
            const int SUCCESS = 0;

            if (status == SUCCESS && m_tts != null)
            {
                m_ttsInitialized = true;
            }
            else
            {
                m_ttsInitialized = false;
            }
        }

        /// <summary>
        /// Proxy class to receive Android TTS init callback.
        /// </summary>
        private class TtsInitListener : AndroidJavaProxy
        {
            private readonly ObstacleAudioNotifier m_parent;

            public TtsInitListener(ObstacleAudioNotifier parent)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                m_parent = parent;
            }

            void onInit(int status)
            {
                m_parent.OnTtsInit(status);
            }
        }
    }
}