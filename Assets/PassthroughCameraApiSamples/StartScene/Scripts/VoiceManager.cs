using UnityEngine;
using UnityEngine.Events;
using Oculus.Voice;
using System.Collections.Generic;
using System.Reflection;

public class VoiceManager : MonoBehaviour
{
    [Header("Wit Configuration")]
    [SerializeField] private AppVoiceExperience appVoiceExperience;

    [Header("Voice SDK (optional)")]
    [Tooltip("Optional: assign a TTSSpeaker GameObject (from Meta Voice SDK). If assigned or available, the notifier will use the Voice SDK to play TTS audio.")]
    [SerializeField]
    private UnityEngine.Object m_ttsSpeakerObject;

    // Reflection handles for optional Voice SDK TTSSpeaker
    private object m_ttsSpeakerInstance = null;
    private MethodInfo m_ttsSpeakMethod = null;
    private bool m_voiceSdkAvailable = false;



    private bool isListening = false;

    private void Awake()
    {
        // if (appVoiceExperience == null)
        // {
        //     Debug.LogError("VoiceManager: appVoiceExperience is not assigned in the Inspector!");
        //     return;
        // }

        // // Subscribe to multiple useful events so we can see what's happening
        // var events = appVoiceExperience.VoiceEvents;
        // events.OnPartialTranscription.AddListener(OnPartial);
        // events.OnFullTranscription.AddListener(ActivateVoice);
        // events.OnError.AddListener(OnError);
        // events.OnRequestCompleted.AddListener(OnRequestCompleted);
        // // Additional diagnostics: listen for mic level and start/stop listening events
        // events.OnStartListening.AddListener(OnStartListening);
        // events.OnStoppedListening.AddListener(OnStoppedListening);
        // events.OnMicLevelChanged.AddListener(OnMicLevelChanged);

        // Debug.Log("VoiceManager: Activating AppVoiceExperience");
        // appVoiceExperience.Activate();
        if (appVoiceExperience == null)
        {
            Debug.LogError("VoiceManager: appVoiceExperience is not assigned!");
            return;
        }

        var events = appVoiceExperience.VoiceEvents;
        events.OnFullTranscription.AddListener(OnFullTranscription);
        events.OnError.AddListener(OnError);

        Debug.Log("VoiceManager Ready.");
    }

    void Update()
    {
        bool aPressed = OVRInput.Get(OVRInput.RawButton.A);

        // Begin listening while button is held
        if (aPressed && !isListening)
        {
            StartListening();
        }

        // Stop listening when button is released
        if (!aPressed && isListening)
        {
            StopListening();
        }
    }

    // Start recording
    private void StartListening()
    {
        isListening = true;
        Debug.Log("VoiceManager: Begin Listening");
        appVoiceExperience.Activate();   // Start microphone
    }

    // Stop recording
    private void StopListening()
    {
        isListening = false;
        Debug.Log("VoiceManager: Stop Listening");
        appVoiceExperience.Deactivate(); // Stop microphone and send request
    }

    private void OnFullTranscription(string text)
    {
        Debug.Log("Voice Command Received: " + text);

        // Normalize spacing + case
        string command = text.Trim().ToLower();
        LoadScene(command);
    }

    private void OnError(string error, string message)
    {
        Debug.LogError($"VoiceManager Error: {error} - {message}");
    }

    private void OnPartial(string partial)
    {
        Debug.Log("VoiceManager.OnPartialTranscription: " + partial);
    }

    private void OnStartListening()
    {
        Debug.Log("VoiceManager: OnStartListening");
    }

    private void OnStoppedListening()
    {
        Debug.Log("VoiceManager: OnStoppedListening");
    }

    private void OnMicLevelChanged(float level)
    {
        Debug.Log($"VoiceManager: Mic level {level}");
    }

    private void OnRequestCompleted()
    {
        Debug.Log("VoiceManager.OnRequestCompleted");
    }

    // private void OnError(string error, string message)
    // {
    //     Debug.LogError($"VoiceManager.OnError: {error} - {message}");
    // }

    private void ActivateVoice(string response)
    {
        Debug.Log("Voice Command Received (Full): " + response);
    }
    
    private void OnEnable()
    {
        Debug.Log("VoiceManager.OnEnable()");
    }

    private void Start()
    {
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
                    m_ttsSpeakMethod.Invoke(m_ttsSpeakerInstance, new object[] { "Speak and hold down the A button, release A when you are finished with your instruction. State which mode you want to enter: obstacle detection or navigation." });
                    Debug.Log($"Voice SDK TTSSpeaker invoked: Speak and hold down the A button, release A when you are finished with your instruction");
                }
                else
                {
                    Debug.Log("Assigned GameObject does not contain a TTSSpeaker component.");
                }
            }
        }
    }

    private void LoadScene(string sceneName)
    {
        // DebugUIBuilder.Instance.Hide();
        // Only choosing scenes for demo, in actuality everything will be one
        Debug.Log("Load scene: " + sceneName);
        int sceneId;
        if (sceneName == "obstacle detection") {
            sceneId = 1;
        } else if (sceneName == "navigation") {
            sceneId = 2;
        } else {
            Debug.Log("Voice command ignored.");
            return;
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneId);
    }
}
