// Copyright (c) Meta Platforms, Inc. and affiliates.
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Polls detections produced by SentisInferenceUiManager and provides audio warnings
    /// when an object is closer than a configured distance and roughly in front of the user.
    /// On Android (Quest) this uses the platform TextToSpeech (via AndroidJavaObject).
    /// In the Editor it falls back to Debug.Log.
    /// </summary>
    public class ObstacleAudioNotifier : MonoBehaviour
    {
        [SerializeField] private float DangerDistance = 1.0f;
        [SerializeField] private float WarningDistance = 2.0f;
        [SerializeField] private float MaxAlertDistance = 5.0f;


        [Tooltip("If the detected object has an absolute X (camera local) <= this value, it is considered 'in front' and both controllers vibrate.")]
        [SerializeField]
        private float m_frontWidth = 0.35f;

        private Camera m_cam;

        private readonly Dictionary<string, int> ClassPriority = new()
        {
            // lower coef = higher danger
            { "car", 1 },
            { "bus", 1 },
            { "truck", 1 },
            { "motorbike", 2 },
            { "bicycle", 2 },
            { "person", 2 },
            { "dog", 3 },
            { "cat", 4 },
            { "stop sign", 4 },
            { "traffic light", 5 } //everything else?
        };

        [SerializeField]
        private SentisInferenceUiManager m_uiInference;

        [SerializeField]
        [Tooltip("Distance (meters) under which a warning is triggered")]
        private float m_warningDistance = 1.0f;

        [SerializeField]
        [Tooltip("Minimum dot product between camera forward and direction to object for it to be considered " +
                 "'in front' (1 = directly ahead, 0 = perpendicular).")]
        private float m_frontDotThreshold = 0.5f;

        [SerializeField]
        [Tooltip("Minimum seconds between speaking warnings for the same class")]
        private float m_cooldownSeconds = 5.0f;

        // track last spoken time per class to avoid spamming the same warning repeatedly
        private readonly Dictionary<string, float> m_lastSpokenPerClass = new();

        // Android TTS objects
        private AndroidJavaObject m_tts = null;
        private bool m_ttsInitialized = false;

        [Header("Voice SDK (optional)")]
        [Tooltip("Optional: assign a TTSSpeaker GameObject (from Meta Voice SDK). If assigned or available, the notifier will use the Voice SDK to play TTS audio.")]
        [SerializeField]
        private UnityEngine.Object m_ttsSpeakerObject;

        // Reflection handles for optional Voice SDK TTSSpeaker
        private object m_ttsSpeakerInstance = null;
        private MethodInfo m_ttsSpeakMethod = null;
        private bool m_voiceSdkAvailable = false;

        private float m_last_time_spoken = 0f;

        private void Start()
        {
            if (m_uiInference == null)
            {
                m_uiInference = FindObjectOfType<SentisInferenceUiManager>();
            }

            if (Application.platform == RuntimePlatform.Android)
            {
                InitAndroidTts();
            }
            Debug.Log($"ObstacleAudioNotifier started. Platform={Application.platform}, uiInference={(m_uiInference!=null)}");
            m_cam = Camera.main;
            // Try to resolve an optional TTSSpeaker from the Voice SDK via the assigned object or reflection.
            TryInitVoiceSdkSpeaker(); 
        }

        // private void Update()
        // {
        //     if (m_uiInference == null) return;

        //     var cam = Camera.main;
        //     if (cam == null) return;

        //     var now = Time.time;

        //     foreach (var box in m_uiInference.BoxDrawn)
        //     {
        //         if (!box.WorldPos.HasValue) continue;
        //         var worldPos = box.WorldPos.Value;
        //         var dir = worldPos - cam.transform.position;
        //         var dist = dir.magnitude;
        //         if (dist > m_warningDistance) continue;

        //         var dot = Vector3.Dot(dir.normalized, cam.transform.forward);
        //         if (dot < m_frontDotThreshold) continue; // not sufficiently in front

        //         var cls = string.IsNullOrEmpty(box.ClassName) ? "object" : box.ClassName;
        //         m_lastSpokenPerClass.TryGetValue(cls, out var lastTime);
        //         if (now - lastTime < m_cooldownSeconds) continue; // still cooling down

        //         var message = $"Warning, there is a {cls} in front of you";
        //         Speak(message);
        //         m_lastSpokenPerClass[cls] = now;
        //         // We break after speaking one warning this frame to avoid overlapping TTS
        //         break;
        //     }
        // }

         // START CHANGE

        private int GetPriority(string cls)
        {
            return ClassPriority.TryGetValue(cls, out var p) ? p : 99;
        }

        private string DirectionPhrase(float angle, float dot)
        {
            // If very forward-facing, ignore small left/right variations
            if (Mathf.Abs(angle) < 10f && dot > 0.6f)
                return "ahead";

            if (angle < -30f)
                return "to your left";

            if (angle > 30f)
                return "to your right";

            // Between 10 and 30 degrees
            if (angle < 0)
                return "slightly to your left";

            return "slightly to your right";
        }


        float ComputeScore(int priority, float distance, float dot)
        {
            Debug.Log($"ComputeScore: priority={priority}, distance={distance}, dot={dot}");
            const float PriorityWeight = 10f; 
            const float DotWeight = 2f;        

            distance = Mathf.Max(distance, 0.1f);

            float score = (PriorityWeight / (distance * priority)) + (DotWeight * dot);
            return score;
        }



        private string BuildCombinedSentence(string cls, int count, float dist, bool isFront, bool isRight)
        {
            // string dirPhrase = DirectionPhrase(dot);
            string dirPhrase = "TEMP";
            if (isFront)
                dirPhrase = "ahead";
            else if (isRight)
                dirPhrase = "to your right";
            else
                dirPhrase = "to your left";

            string noun = count > 1 ? $"{count} {cls}s" : $"A {cls}";

            if (dist < DangerDistance)
                return $"Danger. {noun} {dirPhrase} less than one meter away.";

            if (dist < WarningDistance)
                return $"Warning. {noun} {dirPhrase}, about {dist:F1} meters away.";

            return $"{noun} {dirPhrase}, approximately {dist:F1} meters away.";
        }


        private void Update()
        {
            if (m_uiInference == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            var now = Time.time;

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

                alerts.Add((cls, dist, dot, dir));
            }

            //group by class (in case of multiple of same obstacle)
            var groups = alerts
                .GroupBy(a => a.cls)
                .Select(g => 
                {
                    var closestAlert = g.OrderBy(a => a.dist).First();
                    return new
                    {
                        cls = g.Key,
                        count = g.Count(),
                        closestDist = closestAlert.dist,
                        worldPos = closestAlert.worldPos,
                        avgDot = g.Average(x => x.dot),
                        priority = GetPriority(g.Key)
                    };
                })
                .OrderBy(g => g.priority) // lower = more important
                .ToList();

            //only alert highest priority
            if (groups.Count == 0) return;

            foreach (var g in groups)
            {
                Debug.Log($"Group: class={g.cls}, world_pos={g.worldPos}, count={g.count}, closestDist={g.closestDist}, avgDot={g.avgDot}, priority={g.priority}");
            }
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

            var chosen = scored[0];  // highest danger score wins

            foreach (var g in scored)
            {
                Debug.Log($"Scored: class={g.cls}, score={g.score}");
            }

            m_lastSpokenPerClass.TryGetValue(chosen.cls, out var lastSpoken);

            if (now - lastSpoken < m_cooldownSeconds + 10f)
                return;
            if (now - m_last_time_spoken < m_cooldownSeconds)
                return;

            var local = m_cam.transform.InverseTransformPoint(chosen.worldPos);
            bool isFront = Mathf.Abs(local.x) <= m_frontWidth;
            bool isRight = local.x > 0f;
            string message = BuildCombinedSentence(chosen.cls, chosen.count, chosen.closestDist, isFront, isRight);

            Speak(message);
            m_lastSpokenPerClass[chosen.cls] = now;
            m_last_time_spoken = now;
        }

        // END CHANGE

        private void Speak(string text)
        {
            Debug.Log($"Speak called with text: {text}");
            // Prefer Voice SDK TTSSpeaker if available
            if (m_voiceSdkAvailable && m_ttsSpeakerInstance != null && m_ttsSpeakMethod != null)
            {
                try
                {
                    // DONT FORGET TO UNCOMMENT BELOW WHEN USING VOICE SDK
                    m_ttsSpeakMethod.Invoke(m_ttsSpeakerInstance, new object[] { text });
                    Debug.Log($"Voice SDK TTSSpeaker invoked: {text}");
                    return;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Voice SDK TTSSpeaker invoke failed: {e.Message}. Falling back to Android TTS.");
                    // Fall back to Android TTS if available
                    try
                    {
                        if (Application.platform == RuntimePlatform.Android)
                        {
                            // call the existing Android TTS path directly
                            if (m_tts != null && m_ttsInitialized)
                            {
                                // reuse Speak() path by letting it proceed (we just return from voice SDK path)
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Fallback Android TTS attempt also failed: {ex.Message}");
                    }
                    // allow method to continue to Android TTS fallback below
                }
            }

            if (Application.platform == RuntimePlatform.Android && m_tts != null && m_ttsInitialized)
            {
                try
                {
                    // Use appropriate speak signature depending on Android API level.
                    int sdkInt = 0;
                    try
                    {
                        using var ver = new AndroidJavaClass("android.os.Build$VERSION");
                        sdkInt = ver.GetStatic<int>("SDK_INT");
                    }
                    catch { sdkInt = 0; }

                    Debug.Log($"TTS speak requested. sdkInt={sdkInt}, ttsInitialized={m_ttsInitialized}, ttsIsNull={(m_tts==null)}, text={text}");

                    if (sdkInt >= 21)
                    {
                        // API 21+ speak(CharSequence text, int queueMode, Bundle params, String utteranceId)
                        var bundle = new AndroidJavaObject("android.os.Bundle");
                        var utteranceId = System.Guid.NewGuid().ToString();
                        m_tts.Call("speak", text, 0, bundle, utteranceId);
                        Debug.Log($"TTS speak called (API21+) utteranceId={utteranceId}");
                    }
                    else
                    {
                        // Older APIs: speak(String text, int queueMode, HashMap params)
                        var hm = new AndroidJavaObject("java.util.HashMap");
                        m_tts.Call("speak", text, 0, hm);
                        Debug.Log("TTS speak called (legacy)");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"TTS speak failed: {e.Message}");
                }
            }
            else
            {
                Debug.Log(text);
            }
        }

        private void TryInitVoiceSdkSpeaker()
        {
            // If an object was assigned in the Inspector, try to use it first
            if (m_ttsSpeakerObject != null)
            {
                // The user may assign either a TTSSpeaker component or the GameObject containing it.
                // Handle both cases: if a GameObject was assigned, get the component; if a component was assigned, use it.
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
                        return;
                    }
                    else
                    {
                        Debug.Log("Assigned GameObject does not contain a TTSSpeaker component.");
                    }
                }

                // If the assigned object is a Component (or other object), try to use it directly
                m_ttsSpeakerInstance = m_ttsSpeakerObject;
                var tt = m_ttsSpeakerInstance.GetType();
                m_ttsSpeakMethod = tt.GetMethod("Speak", new System.Type[] { typeof(string) });
                m_voiceSdkAvailable = m_ttsSpeakMethod != null;
                Debug.Log($"Voice SDK speaker assigned via inspector. Available={m_voiceSdkAvailable}");
                return;
            }

            // Otherwise, try to locate a TTSSpeaker type via reflection in loaded assemblies
            System.Type speakerType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var ty in asm.GetTypes())
                    {
                        if (ty.Name == "TTSSpeaker")
                        {
                            speakerType = ty;
                            break;
                        }
                    }
                }
                catch { }
                if (speakerType != null) break;
            }

            if (speakerType != null)
            {
                // Find an instance in the scene
                var obj = UnityEngine.Object.FindObjectOfType(speakerType);
                if (obj != null)
                {
                    m_ttsSpeakerInstance = obj;
                    m_ttsSpeakMethod = speakerType.GetMethod("Speak", new System.Type[] { typeof(string) });
                    m_voiceSdkAvailable = m_ttsSpeakMethod != null;
                    // Additional validation: ensure the TTSSpeaker has VoiceSettings assigned to avoid runtime NRE inside the SDK
                    try
                    {
                        var prop = speakerType.GetProperty("VoiceSettings");
                        if (prop != null)
                        {
                            var val = prop.GetValue(m_ttsSpeakerInstance);
                            if (val == null)
                            {
                                Debug.LogWarning("TTSSpeaker found but VoiceSettings is null. Please set a Voice Preset or custom VoiceSettings on the TTSSpeaker.");
                                m_voiceSdkAvailable = false;
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        // ignore reflection errors and leave availability as-is
                    }
                    Debug.Log($"Voice SDK TTSSpeaker found in scene. Available={m_voiceSdkAvailable}");
                    return;
                }
            }

            m_voiceSdkAvailable = false;
            Debug.Log("Voice SDK TTSSpeaker not found; will use Android TTS fallback if available.");
        }

        private void InitAndroidTts()
        {
            try
            {
                using var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = up.GetStatic<AndroidJavaObject>("currentActivity");
                // Create a TextToSpeech instance with an initialization listener
                m_tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new TtsInitListener(this));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to initialize Android TTS: {e.Message}");
                m_tts = null;
                m_ttsInitialized = false;
            }
        }

        // Called by the Android init listener when TTS is ready
        public void OnTtsInit(int status)
        {
            const int SUCCESS = 0; // TextToSpeech.SUCCESS
            if (status == SUCCESS && m_tts != null)
            {
                // Try set language and speech rate; ignore failures.
                try
                {
                    using var localeClass = new AndroidJavaClass("java.util.Locale");
                    AndroidJavaObject locale = null;
                    try { locale = localeClass.GetStatic<AndroidJavaObject>("US"); } catch { locale = localeClass.CallStatic<AndroidJavaObject>("getDefault"); }
                    if (locale != null)
                    {
                        m_tts.Call<int>("setLanguage", locale);
                    }
                    m_tts.Call("setSpeechRate", 1.0f);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"TTS init: failed to set language or rate: {e.Message}");
                }
                m_ttsInitialized = true;
                Debug.Log("Android TTS initialized");
            }
            else
            {
                m_ttsInitialized = false;
                Debug.LogError($"Android TTS init failed: status={status}");
            }
        }

        // AndroidJavaProxy class for TTS init callback
        private class TtsInitListener : AndroidJavaProxy
        {
            private readonly ObstacleAudioNotifier m_parent;
            public TtsInitListener(ObstacleAudioNotifier parent) : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                m_parent = parent;
            }

            void onInit(int status)
            {
                // Called on Java thread; marshal back to Unity main thread using Unity's message queue
                // We'll call back into the parent directly; Unity's AndroidJavaProxy will marshal to C# thread.
                m_parent.OnTtsInit(status);
            }
        }
    }
}
