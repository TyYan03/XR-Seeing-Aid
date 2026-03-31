// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using Meta.XR;
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Responsible for rendering segmentation masks and (optionally) bounding boxes.
    /// Converts model output tensors into textures displayed in-world.
    /// </summary>
    [MetaCodeSample("XRSeeingAid-MultiObjectDetection")]
    public class SentisInferenceSegmentationUiManager : MonoBehaviour
    {
        #region Inspector

        [Header("References")]
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private SentisDetectedSegmentationUiManager m_detectionCanvas;
        [SerializeField] private RawImage m_displayImage;

        [Header("Segmentation Overlay")]
        [SerializeField] private RawImage m_segmentationOverlayImage;
        [SerializeField] private float m_maskOpacity = 0.5f;
        [SerializeField] private bool m_enableSegmentationOverlay = true;

        public UnityEvent<int> OnObjectsDetected;

        #endregion

        private Texture2D m_segmentationTexture;
        private string[] m_labels;

        #region Unity

        private void Start()
        {
            if (m_segmentationOverlayImage != null)
            {
                var rt = m_segmentationOverlayImage.rectTransform;

                // Stretch to full canvas
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                m_segmentationOverlayImage.color = Color.white;
                m_segmentationOverlayImage.raycastTarget = false;
            }
        }

        private void OnDestroy()
        {
            if (m_segmentationTexture != null)
                Destroy(m_segmentationTexture);
        }

        #endregion

        #region Public API

        public void SetLabels(TextAsset labelsAsset)
        {
            m_labels = labelsAsset.text.Split('\n');
        }

        public void SetDetectionCapture(Texture image)
        {
            m_displayImage.gameObject.SetActive(false);
            m_detectionCanvas.CapturePosition();
        }

        /// <summary>
        /// Converts model output tensor into a segmentation mask texture.
        /// </summary>
        public void DrawSegmentation(
            Tensor<float> output,
            Tensor<int> labelIDs,
            float imageWidth,
            float imageHeight,
            Pose cameraPose)
        {
            if (!m_enableSegmentationOverlay || m_segmentationOverlayImage == null)
                return;

            if (output.shape.rank != 4)
            {
                Debug.LogError("Invalid tensor shape for segmentation.");
                return;
            }

            int C = output.shape[1];
            int H = output.shape[2];
            int W = output.shape[3];

            // Argmax per pixel
            int[] pred = new int[H * W];

            for (int h = 0; h < H; h++)
            {
                for (int w = 0; w < W; w++)
                {
                    float maxVal = float.MinValue;
                    int maxIdx = 0;

                    for (int c = 0; c < C; c++)
                    {
                        float val = output[0, c, h, w];
                        if (val > maxVal)
                        {
                            maxVal = val;
                            maxIdx = c;
                        }
                    }

                    pred[h * W + w] = maxIdx;
                }
            }

            var palette = GetPalette();
            var pixels = new Color32[H * W];

            // Build texture (flip vertically for Unity)
            for (int h = 0; h < H; h++)
            {
                for (int w = 0; w < W; w++)
                {
                    int src = h * W + w;
                    int dst = (H - 1 - h) * W + w;

                    int cls = Mathf.Clamp(pred[src], 0, palette.Length - 1);

                    if (cls == 0)
                    {
                        pixels[dst] = new Color32(0, 0, 0, 0);
                    }
                    else
                    {
                        var col = palette[cls];
                        col.a = (byte)(255 * m_maskOpacity);
                        pixels[dst] = col;
                    }
                }
            }

            // Create or reuse texture
            if (m_segmentationTexture == null ||
                m_segmentationTexture.width != W ||
                m_segmentationTexture.height != H)
            {
                if (m_segmentationTexture != null)
                    Destroy(m_segmentationTexture);

                m_segmentationTexture = new Texture2D(W, H, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear
                };
            }

            m_segmentationTexture.SetPixels32(pixels);
            m_segmentationTexture.Apply();

            m_segmentationOverlayImage.texture = m_segmentationTexture;
        }

        #endregion

        #region Helpers

        private Color32[] GetPalette()
        {
            // Simplified palette
            return new Color32[]
            {
                new Color32(0,0,0,255),       // background
                new Color32(219,101,83,255),  // road
                new Color32(50,117,35,255),   // sidewalk
                new Color32(129,143,40,255),  // crosswalk
                new Color32(20,59,2,255),     // terrain
            };
        }

        public void SetSegmentationOverlayEnabled(bool enabled)
        {
            m_enableSegmentationOverlay = enabled;
            if (m_segmentationOverlayImage != null)
                m_segmentationOverlayImage.gameObject.SetActive(enabled);
        }

        public void SetSegmentationMaskOpacity(float opacity)
        {
            m_maskOpacity = Mathf.Clamp01(opacity);
        }

        #endregion
    }
}