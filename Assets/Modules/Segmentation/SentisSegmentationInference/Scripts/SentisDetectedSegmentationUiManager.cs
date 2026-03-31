// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using Meta.XR;
using Meta.XR.Samples;
using UnityEngine;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Manages the placement and scaling of the segmentation canvas so that it aligns
    /// with the passthrough camera's field of view.
    /// </summary>
    [MetaCodeSample("XRSeeingAid-MultiObjectDetection")]
    public class SentisDetectedSegmentationUiManager : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private GameObject m_detectionCanvas;

        [Tooltip("Distance in meters from the camera where the canvas will be placed.")]
        [SerializeField] private float m_canvasDistance = 0.5f;

        private Transform m_cameraTransform;

        private IEnumerator Start()
        {
            // Validate required reference
            if (m_cameraAccess == null)
            {
                Debug.LogError($"{nameof(m_cameraAccess)} is required for {nameof(SentisDetectedSegmentationUiManager)}.");
                enabled = false;
                yield break;
            }

            // Wait until passthrough camera is active
            while (!m_cameraAccess.IsPlaying)
                yield return null;

            // Try to find XR camera anchor
            GameObject centerEye = GameObject.Find("CenterEyeAnchor");

            if (centerEye == null)
            {
                // Fallback to main camera
                if (Camera.main != null)
                    centerEye = Camera.main.gameObject;
                else
                {
                    Debug.LogError("Could not find CenterEyeAnchor or Camera.main.");
                    yield break;
                }
            }

            m_cameraTransform = centerEye.transform;

            // Get canvas RectTransform
            var rect = m_detectionCanvas.GetComponent<RectTransform>()
                       ?? m_detectionCanvas.GetComponentInChildren<RectTransform>();

            // Compute vertical FOV using camera rays
            var bottomRay = m_cameraAccess.ViewportPointToRay(new Vector2(0.5f, 0f));
            var topRay = m_cameraAccess.ViewportPointToRay(new Vector2(0.5f, 1f));

            float verticalFovDeg = Vector3.Angle(bottomRay.direction, topRay.direction);
            float verticalFovRad = verticalFovDeg * Mathf.Deg2Rad;

            // Compute correct canvas scale
            double canvasHeight = 2.0 * m_canvasDistance * Math.Tan(verticalFovRad / 2.0);
            float scale = (float)(canvasHeight / rect.sizeDelta.y);

            rect.localScale = Vector3.one * scale;

            Debug.Log($"Segmentation canvas scaled: {scale}");
        }
    }
}