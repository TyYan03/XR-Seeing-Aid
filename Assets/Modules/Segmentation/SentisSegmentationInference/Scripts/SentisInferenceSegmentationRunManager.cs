// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using Meta.XR;
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEngine;

namespace XRSeeingAid.MultiObjectDetection
{
    /// <summary>
    /// Handles running the segmentation model using Unity Sentis.
    /// Executes inference asynchronously and feeds results to the UI manager.
    /// </summary>
    [MetaCodeSample("XRSeeingAid-Navigation-Segmentation")]
    public class SentisSegmentationRunManager : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Model Configuration")]
        [SerializeField] private Vector2Int m_inputSize = new(512, 256);
        [SerializeField] private BackendType m_backend = BackendType.GPUCompute;
        [SerializeField] private ModelAsset m_sentisModel;
        [SerializeField] private int m_layersPerFrame = 500;

        [Header("References")]
        [SerializeField] private SentisInferenceSegmentationUiManager m_uiInference;
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private TextAsset m_labelsAsset;

        #endregion

        #region Runtime State

        private Worker m_engine;
        private IEnumerator m_schedule;

        private Tensor<float> m_input;
        private Tensor<float> m_output;
        private Tensor<float> m_pullOutput;

        private Tensor<int> m_labelIDs;
        private Tensor<int> m_pullLabelIDs;

        private Pose m_imageCameraPose;

        private bool m_started = false;
        private bool m_isWaiting = false;
        private int m_downloadState = 0;

        public bool IsModelLoaded { get; private set; } = false;

        #endregion

        #region Unity Lifecycle

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(0.05f);

            if (m_uiInference == null || m_cameraAccess == null || m_labelsAsset == null)
            {
                Debug.LogError("SegmentationRunManager missing required references.");
                yield break;
            }

            // Wait for camera
            while (!m_cameraAccess.IsPlaying)
                yield return null;

            // Load labels + model
            m_uiInference.SetLabels(m_labelsAsset);
            LoadModel();
        }

        private void Update()
        {
            InferenceUpdate();

            // Auto-run inference continuously
            if (IsModelLoaded && !m_started && m_cameraAccess.IsPlaying)
            {
                RunInference(m_cameraAccess);
            }
        }

        private void OnDestroy()
        {
            if (m_schedule != null) StopCoroutine(m_schedule);

            m_input?.Dispose();
            m_output?.Dispose();
            m_engine?.Dispose();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Starts a new inference pass.
        /// </summary>
        public void RunInference(PassthroughCameraAccess cameraAccess)
        {
            if (m_started) return;

            m_imageCameraPose = cameraAccess.GetCameraPose();

            m_input?.Dispose();

            Texture tex = cameraAccess.GetTexture();
            m_uiInference.SetDetectionCapture(tex);

            // Convert texture to tensor
            var transform = new TextureTransform().SetDimensions(tex.width, tex.height, 3);

            m_input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.y, m_inputSize.x));
            TextureConverter.ToTensor(tex, m_input, transform);

            // Schedule async execution
            m_schedule = m_engine.ScheduleIterable(m_input);

            m_downloadState = 0;
            m_started = true;
        }

        public bool IsRunning() => m_started;

        #endregion

        #region Model + Inference

        private void LoadModel()
        {
            if (m_sentisModel == null)
            {
                Debug.LogError("Sentis model not assigned.");
                return;
            }

            var model = ModelLoader.Load(m_sentisModel);
            m_engine = new Worker(model, m_backend);

            // Warm-up pass
            Texture dummy = new Texture2D(m_inputSize.x, m_inputSize.y);
            m_input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.y, m_inputSize.x));

            var transform = new TextureTransform().SetDimensions(dummy.width, dummy.height, 3);
            TextureConverter.ToTensor(dummy, m_input, transform);

            m_engine.Schedule(m_input);

            IsModelLoaded = true;

            Debug.Log("Segmentation model loaded.");
        }

        private void InferenceUpdate()
        {
            if (!m_started) return;

            try
            {
                if (m_downloadState == 0)
                {
                    int i = 0;

                    // Run partial layers per frame
                    while (m_schedule.MoveNext())
                    {
                        if (++i % m_layersPerFrame == 0)
                            return;
                    }

                    m_downloadState = 1;
                }
                else
                {
                    GetResults();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Segmentation error: {e.Message}");
            }
        }

        #endregion

        #region Output Handling

        private void GetResults()
        {
            switch (m_downloadState)
            {
                case 1:
                    RequestOutput();
                    break;

                case 3:
                    // Send results to UI
                    m_uiInference.DrawSegmentation(
                        m_output,
                        m_labelIDs,
                        m_inputSize.x,
                        m_inputSize.y,
                        m_imageCameraPose);

                    m_downloadState = 5;
                    break;

                case 4:
                    m_uiInference.OnObjectDetectionError();
                    m_downloadState = 5;
                    break;

                case 5:
                    // Cleanup
                    m_started = false;
                    m_output?.Dispose();
                    m_labelIDs?.Dispose();
                    break;
            }
        }

        private void RequestOutput()
        {
            if (!m_isWaiting)
            {
                m_pullOutput = m_engine.PeekOutput(0) as Tensor<float>;

                if (m_pullOutput?.dataOnBackend != null)
                {
                    m_pullOutput.ReadbackRequest();
                    m_isWaiting = true;
                }
                else
                {
                    m_downloadState = 4;
                }
            }
            else if (m_pullOutput.IsReadbackRequestDone())
            {
                m_output = m_pullOutput.ReadbackAndClone();
                m_isWaiting = false;

                m_downloadState = (m_output.shape[0] > 0) ? 3 : 4;
            }
        }

        #endregion
    }
}