using UnityEngine;
using UnityEngine.Events;
using Oculus.Voice;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using UnityEngine.Android;

// Manages voice-controlled navigation using Wit.ai + Google Maps API + optional TTS
public class NavigationVoiceManager : MonoBehaviour
{
    [Header("Wit Configuration")]
    // Reference to Meta/Oculus Voice SDK (Wit.ai integration)
    [SerializeField] private AppVoiceExperience appVoiceExperience;

    [Header("Voice SDK (optional)")]
    [Tooltip("Optional: assign a TTSSpeaker GameObject (from Meta Voice SDK). If assigned or available, the notifier will use the Voice SDK to play TTS audio.")]
    [SerializeField]
    private UnityEngine.Object m_ttsSpeakerObject;

    [Header("Google Maps API")]
    // API key for Google Maps Directions API
    [SerializeField] private string googleMapsApiKey = "ADD_YOUR_OWN_GOOGLE_MAPS_API_KEY_HERE";

    // Default fallback origin if GPS is unavailable
    [SerializeField] private string originLocation = "37.7749,-122.4194";

    [Tooltip("Use device GPS location as origin. If disabled, uses the originLocation above.")]
    [SerializeField] private bool useDeviceLocation = true;

    // Reflection-based TTS handling (so project doesn't hard-depend on SDK class)
    private object m_ttsSpeakerInstance = null;
    private MethodInfo m_ttsSpeakMethod = null;
    private bool m_voiceSdkAvailable = false;

    // Tracks whether microphone is actively listening
    private bool isListening = false;

    // HTTP client (currently unused but initialized for potential API usage)
    private HttpClient httpClient;

    // Tracks whether GPS/location service successfully initialized
    private bool locationServiceInitialized = false;

    private void Awake()
    {
        // Ensure voice system is assigned
        if (appVoiceExperience == null)
        {
            Debug.LogError("NavigationVoiceManager: appVoiceExperience is not assigned!");
            return;
        }

        // Subscribe to voice events
        var events = appVoiceExperience.VoiceEvents;
        events.OnFullTranscription.AddListener(OnFullTranscription);
        events.OnError.AddListener(OnError);

        // Initialize HTTP client
        httpClient = new HttpClient();

        // Request Android location permission if needed
        if (Application.platform == RuntimePlatform.Android)
        {
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Permission.RequestUserPermission(Permission.FineLocation);
            }
        }

        // Start GPS initialization if enabled
        if (useDeviceLocation)
        {
            StartCoroutine(InitializeLocationServices());
        }

        Debug.Log("NavigationVoiceManager Ready.");
    }

    // Initializes Unity location services (GPS)
    private IEnumerator InitializeLocationServices()
    {
        // If user has location services disabled
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("NavigationVoiceManager: Location services are not enabled by user. Using fallback location.");
            locationServiceInitialized = false;
            yield break;
        }

        // Start GPS with accuracy and update distance thresholds
        Input.location.Start(10f, 10f);

        // Wait up to 20 seconds for initialization
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        // Timeout case
        if (maxWait < 1)
        {
            Debug.LogError("NavigationVoiceManager: Timed out waiting for location services to initialize. Using fallback location.");
            locationServiceInitialized = false;
            yield break;
        }

        // Failure case
        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("NavigationVoiceManager: Unable to determine device location. Using fallback location.");
            locationServiceInitialized = false;
            yield break;
        }

        // Success
        locationServiceInitialized = true;
        Debug.Log("NavigationVoiceManager: Location services initialized successfully.");
    }

    // Returns current origin location string (lat,long)
    private string GetCurrentLocation()
    {
        // Use GPS if available
        if (useDeviceLocation && locationServiceInitialized && Input.location.status == LocationServiceStatus.Running)
        {
            LocationInfo locationInfo = Input.location.lastData;
            string locationString = $"{locationInfo.latitude},{locationInfo.longitude}";
            Debug.Log($"NavigationVoiceManager: Using device location: {locationString}");
            return locationString;
        }
        else
        {
            // Fallback location
            Debug.Log($"NavigationVoiceManager: Using fallback location: {originLocation}");
            return originLocation;
        }
    }

    void Update()
    {
        // Start voice capture when A button is pressed
        if (OVRInput.GetDown(OVRInput.RawButton.A) && !isListening)
        {
            StartListening();
        }

        // Stop voice capture when A button is released
        if (OVRInput.GetUp(OVRInput.RawButton.A) && isListening)
        {
            StopListening();
        }
    }

    // Begins microphone capture via Voice SDK
    private void StartListening()
    {
        if (appVoiceExperience.Active)
        {
            Debug.Log("NavigationVoiceManager: Already active, cannot start listening again.");
            return;
        }

        isListening = true;
        Debug.Log("NavigationVoiceManager: Begin Listening");

        // Activates microphone + sends audio to Wit.ai
        appVoiceExperience.Activate();
    }

    // Stops microphone capture
    private void StopListening()
    {
        isListening = false;
        StartCoroutine(StopListeningDelayed());
    }

    // Adds slight delay to ensure proper transcription capture
    private IEnumerator StopListeningDelayed()
    {
        yield return new WaitForSeconds(0.25f);
        Debug.Log("NavigationVoiceManager: Stop Listening");

        // Sends recorded audio for processing
        appVoiceExperience.Deactivate();
    }

    // Called when Wit.ai returns full transcription
    private void OnFullTranscription(string text)
    {
        Debug.Log("NavigationVoiceManager: Voice Command Received: " + text);

        // Handle empty input
        if (string.IsNullOrEmpty(text))
        {
            Debug.Log("NavigationVoiceManager: No voice command recognized.");
            SpeakText("I didn't hear you clearly. Please try again.");
            return;
        }

        // Normalize input
        string command = text.Trim().ToLower();

        // Detect navigation intent
        if (command.Contains("directions to") || command.Contains("navigate to") ||
            command.Contains("go to") || command.Contains("take me to") ||
            command.Contains("how do i get to"))
        {
            string destination = ExtractDestination(command);

            if (!string.IsNullOrEmpty(destination))
            {
                Debug.Log($"NavigationVoiceManager: Getting directions to: {destination}");
                SpeakText($"Getting directions to {destination}");

                // Fetch directions from Google Maps
                StartCoroutine(GetDirections(destination));
            }
            else
            {
                SpeakText("I couldn't understand the destination. Please say something like 'directions to Central Park'");
            }
        }
        // Handle "where am I"
        else if (command.Contains("where am i") || command.Contains("current location"))
        {
            if (useDeviceLocation && locationServiceInitialized && Input.location.status == LocationServiceStatus.Running)
            {
                LocationInfo locationInfo = Input.location.lastData;

                SpeakText($"You are currently at latitude {locationInfo.latitude:F4}, longitude {locationInfo.longitude:F4}. To get directions, say 'directions to' followed by your destination.");
            }
            else
            {
                SpeakText("You are currently at the origin location. To get directions, say 'directions to' followed by your destination.");
            }
        }
        // Fallback help response
        else
        {
            SpeakText("I can help you get directions. Try saying 'directions to' followed by your destination.");
        }
    }

    // Extracts destination string using regex patterns
    private string ExtractDestination(string command)
    {
        string[] patterns = {
            @"directions to (.+)",
            @"direction to (.+)",
            @"navigate to (.+)",
            @"go to (.+)",
            @"take me to (.+)",
            @"how do i get to (.+)"
        };

        foreach (string pattern in patterns)
        {
            var match = Regex.Match(command, pattern);

            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    // Calls Google Maps Directions API
    private IEnumerator GetDirections(string destination)
    {
        // Validate API key
        if (string.IsNullOrEmpty(googleMapsApiKey) || googleMapsApiKey == "YOUR_GOOGLE_MAPS_API_KEY_HERE")
        {
            SpeakText("Google Maps API key is not configured. Please add your API key in the inspector.");
            yield break;
        }

        // Build request URL
        string currentOrigin = GetCurrentLocation();
        string url = $"https://maps.googleapis.com/maps/api/directions/json?" +
                    $"origin={currentOrigin}&" +
                    $"destination={UnityWebRequest.EscapeURL(destination)}&" +
                    $"mode=walking&" +
                    $"key={googleMapsApiKey}";

        Debug.Log($"NavigationVoiceManager: Requesting directions from: {url}");

        // Send HTTP request
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            // Success
            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;

                DirectionsResponse directions = JsonUtility.FromJson<DirectionsResponse>(jsonResponse);

                // Validate response
                if (directions.status == "OK" && directions.routes.Length > 0)
                {
                    Route route = directions.routes[0];

                    // Speak step-by-step directions
                    StartCoroutine(FormatDirectionsForSpeech(route));
                }
                else
                {
                    SpeakText($"I couldn't find directions to {destination}. Please check the destination name and try again.");
                }
            }
            else
            {
                Debug.LogError($"NavigationVoiceManager: Google Maps API error: {request.error}");
                SpeakText("Sorry, I couldn't connect to Google Maps. Please check your internet connection.");
            }
        }
    }

    // Checks if TTS is currently speaking (via reflection)
    private bool IsSpeakerActive()
    {
        var type = m_ttsSpeakerInstance.GetType();
        var prop = type.GetProperty("IsSpeaking");

        if (prop != null)
            return (bool)prop.GetValue(m_ttsSpeakerInstance);

        return false;
    }

    // Coroutine to speak text and wait until finished
    private IEnumerator SpeakAsync(string text)
    {
        // Trigger TTS if available
        if (m_voiceSdkAvailable && m_ttsSpeakerInstance != null)
        {
            m_ttsSpeakMethod.Invoke(m_ttsSpeakerInstance, new object[] { text });
        }
        else
        {
            Debug.Log($"Would speak: {text}");
        }

        // Wait until speech starts
        yield return new WaitUntil(() => IsSpeakerActive());

        // Wait until speech ends
        yield return new WaitUntil(() => !IsSpeakerActive());
    }

    // Converts route data into spoken instructions
    private IEnumerator FormatDirectionsForSpeech(Route route)
    {
        Leg leg = route.legs[0];

        // Speak summary
        string distance = leg.distance.text;
        string duration = leg.duration.text;

        yield return StartCoroutine(
            SpeakAsync($"Directions to your destination. Total distance: {distance}. Estimated time: {duration}. ")
        );

        // Speak each step sequentially
        for (int i = 0; i < leg.steps.Length; i++)
        {
            Step step = leg.steps[i];

            string instruction = CleanHtmlTags(step.html_instructions);
            string cur_direction = ($"Step {i + 1}: {instruction}. ");

            yield return StartCoroutine(SpeakAsync(cur_direction));
        }
    }

    // Removes HTML tags from Google Maps instructions
    private string CleanHtmlTags(string htmlText)
    {
        string cleanText = Regex.Replace(htmlText, "<.*?>", string.Empty);

        // Decode common HTML entities
        cleanText = cleanText.Replace("&nbsp;", " ");
        cleanText = cleanText.Replace("&amp;", "&");
        cleanText = cleanText.Replace("&lt;", "<");
        cleanText = cleanText.Replace("&gt;", ">");
        cleanText = cleanText.Replace("&quot;", "\"");
        cleanText = cleanText.Replace("&#39;", "'");

        return cleanText;
    }

    // Async TTS call (non-blocking)
    private async Task SpeakText(string text)
    {
        if (m_voiceSdkAvailable && m_ttsSpeakerInstance != null && m_ttsSpeakMethod != null)
        {
            try
            {
                await Task.Run(() => m_ttsSpeakMethod.Invoke(m_ttsSpeakerInstance, new object[] { text }));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"NavigationVoiceManager: TTS Error: {e.Message}");
            }
        }
        else
        {
            Debug.Log($"NavigationVoiceManager: TTS not available. Would speak: {text}");
        }
    }

    // Handles voice recognition errors
    private void OnError(string error, string message)
    {
        Debug.LogError($"NavigationVoiceManager Error: {error} - {message}");
        SpeakText("Sorry, I encountered an error with voice recognition. Please try again.");
    }

    // Cleanup on app exit
    private void OnApplicationQuit()
    {
        if (useDeviceLocation && Input.location.status == LocationServiceStatus.Running)
        {
            Input.location.Stop();
            Debug.Log("NavigationVoiceManager: Location services stopped.");
        }
    }

    // Initializes optional TTS speaker via reflection
    private void Start()
    {
        Debug.Log("NavigationVoiceManager.Start()");

        if (m_ttsSpeakerObject != null)
        {
            if (m_ttsSpeakerObject is GameObject go)
            {
                var comp = go.GetComponent("TTSSpeaker");

                if (comp != null)
                {
                    m_ttsSpeakerInstance = comp;

                    var t = m_ttsSpeakerInstance.GetType();
                    m_ttsSpeakMethod = t.GetMethod("Speak", new System.Type[] { typeof(string) });

                    m_voiceSdkAvailable = m_ttsSpeakMethod != null;

                    string instructions = "Voice navigation is ready. Hold the A button and say 'directions to' followed by your destination.";

                    m_ttsSpeakMethod.Invoke(m_ttsSpeakerInstance, new object[] { instructions });
                }
            }
        }
        else
        {
            Debug.Log("NavigationVoiceManager: No TTS speaker assigned.");
        }
    }
}

// Data models for parsing Google Maps API JSON
[System.Serializable]
public class DirectionsResponse
{
    public string status;
    public Route[] routes;
}

[System.Serializable]
public class Route
{
    public Leg[] legs;
}

[System.Serializable]
public class Leg
{
    public Distance distance;
    public Duration duration;
    public Step[] steps;
}

[System.Serializable]
public class Distance
{
    public string text;
    public int value;
}

[System.Serializable]
public class Duration
{
    public string text;
    public int value;
}

[System.Serializable]
public class Step
{
    public string html_instructions;
    public Distance distance;
    public Duration duration;
}