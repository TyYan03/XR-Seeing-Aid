// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Manages UI menus and state transitions:
    /// - Loading screen
    /// - Permission screen
    /// - Initial/start menu
    /// - Detection info display
    /// Also controls pause/resume behavior for detection.
    /// </summary>
    [MetaCodeSample("XRSeeingAid-MultiObjectDetection")]
    public class DetectionUiMenuManager : MonoBehaviour
    {
        [Header("UI Buttons")]
        // Controller button used to interact with menus
        [SerializeField] private OVRInput.RawButton m_actionButton = OVRInput.RawButton.A;

        [Header("UI Elements References")]
        // Panel shown while model is loading
        [SerializeField] private GameObject m_loadingPanel;

        // Initial menu panel (start screen)
        [SerializeField] private GameObject m_initialPanel;

        // Panel shown when permissions are missing
        [SerializeField] private GameObject m_noPermissionPanel;

        // Text element displaying detection stats
        [SerializeField] private Text m_labelInformation;

        // Audio feedback when pressing UI buttons
        [SerializeField] private AudioSource m_buttonSound;

        /// <summary>
        /// Enables/disables input handling externally.
        /// </summary>
        public bool IsInputActive { get; set; } = false;

        /// <summary>
        /// Event fired when pause state changes (true = paused).
        /// </summary>
        public UnityEvent<bool> OnPause;

        // Tracks whether we are currently in the initial menu
        private bool m_initialMenu;

        // Detection stats (for UI display)
        private int m_objectsDetected = 0;
        private int m_objectsIdentified = 0;

        // Current pause state (default = paused)
        public bool IsPaused { get; private set; } = true;

        #region Unity Functions

        /// <summary>
        /// Initializes UI flow:
        /// 1. Wait for Sentis model
        /// 2. Wait for permissions
        /// 3. Show initial menu
        /// </summary>
        private IEnumerator Start()
        {
            // Hide menus initially
            m_initialPanel.SetActive(false);
            m_noPermissionPanel.SetActive(false);

            // Show loading screen while model loads
            m_loadingPanel.SetActive(true);

            var sentisInference = FindFirstObjectByType<SentisInferenceRunManager>();

            while (!sentisInference.IsModelLoaded)
            {
                yield return null;
            }

            // Hide loading screen once ready
            m_loadingPanel.SetActive(false);

            // Show permission screen
            OnNoPermissionMenu();

            // Wait until required permissions are granted
            while (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene) ||
                   !OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
            {
                yield return null;
            }

            // Switch to initial/start menu
            OnInitialMenu();
        }

        /// <summary>
        /// Handles menu input updates.
        /// </summary>
        private void Update()
        {
            // Ignore input if disabled externally
            if (!IsInputActive)
                return;

            // Handle initial menu interactions
            if (m_initialMenu)
            {
                InitialMenuUpdate();
            }
        }

        #endregion

        #region UI State: No Permissions Menu

        /// <summary>
        /// Activates the "no permissions" UI state.
        /// </summary>
        private void OnNoPermissionMenu()
        {
            m_initialMenu = false;
            IsPaused = true;

            m_initialPanel.SetActive(false);
            m_noPermissionPanel.SetActive(true);
        }

        #endregion

        #region UI State: Initial Menu

        /// <summary>
        /// Activates the initial/start menu.
        /// </summary>
        private void OnInitialMenu()
        {
            m_initialMenu = true;
            IsPaused = true;

            m_initialPanel.SetActive(true);
            m_noPermissionPanel.SetActive(false);
        }

        /// <summary>
        /// Handles input while in the initial menu.
        /// </summary>
        private void InitialMenuUpdate()
        {
            // When user releases the action button, start detection
            if (OVRInput.GetUp(m_actionButton))
            {
                m_buttonSound?.Play();
                OnPauseMenu(false); // Unpause system
            }
        }

        /// <summary>
        /// Toggles pause menu state and notifies listeners.
        /// </summary>
        /// <param name="visible">True = paused, False = running</param>
        private void OnPauseMenu(bool visible)
        {
            m_initialMenu = false;
            IsPaused = visible;

            // Hide all menus
            m_initialPanel.SetActive(false);
            m_noPermissionPanel.SetActive(false);

            // Notify other systems (e.g., DetectionManager)
            OnPause?.Invoke(visible);
        }

        #endregion

        #region UI State: Detection Information

        /// <summary>
        /// Updates the UI text with current detection statistics.
        /// </summary>
        private void UpdateLabelInformation()
        {
            m_labelInformation.text =
                $"Unity Sentis version: 2.1.3\n" +
                $"AI model: Yolo\n" +
                $"Detecting objects: {m_objectsDetected}\n" +
                $"Objects identified: {m_objectsIdentified}";
        }

        /// <summary>
        /// Updates number of currently detected objects.
        /// </summary>
        public void OnObjectsDetected(int objects)
        {
            m_objectsDetected = objects;
            UpdateLabelInformation();
        }

        /// <summary>
        /// Updates total identified objects count.
        /// Pass a negative value to reset the counter.
        /// </summary>
        public void OnObjectsIndentified(int objects)
        {
            if (objects < 0)
            {
                // Reset counter
                m_objectsIdentified = 0;
            }
            else
            {
                // Accumulate count
                m_objectsIdentified += objects;
            }

            UpdateLabelInformation();
        }

        #endregion
    }
}