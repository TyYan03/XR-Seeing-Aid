using System;
using System.Collections;
using Meta.XR;
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-Navigation-FastSCNN")]
    public class SentisFastSCNNRunManager : MonoBehaviour
    {
        [Header("Sentis Model config")]
        [SerializeField] private Vector2Int m_inputSize = new(512, 256); // Fast-SCNN input size
        [SerializeField] private BackendType m_backend = BackendType.GPUCompute;
        [SerializeField] private ModelAsset m_sentisModel;
        [SerializeField] private int m_layersPerFrame = 500;

        [Header("UI display references")]
        [SerializeField] private SentisInferenceSCNNUiManager m_uiInference;

        private Worker m_engine;
        private IEnumerator m_schedule;
        private bool m_started = false;
        private Tensor<float> m_input;
        private Tensor<float> m_output;
        private int m_downloadState = 0;
        private bool m_isWaiting = false;
        private Tensor<float> m_pullOutput;
        private Pose m_imageCameraPose;

        [Header("[Editor Only] Convert to Sentis")]
        public ModelAsset OnnxModel;
        [SerializeField, Range(0, 1)] private float m_iouThreshold = 0.6f;
        [SerializeField, Range(0, 1)] private float m_scoreThreshold = 0.23f;

        public bool IsModelLoaded { get; private set; } = false;

        #region Unity Functions
        private IEnumerator Start()
        {
            yield return new WaitForSeconds(0.05f); // small delay to let UI initialize
            LoadModel();
        }

        private void Update()
        {
            InferenceUpdate();
        }

        private void OnDestroy()
        {
            if (m_schedule != null) StopCoroutine(m_schedule);
            m_input?.Dispose();
            m_output?.Dispose();
            m_engine?.Dispose();
        }
        #endregion

        #region Public Functions
        public void RunInference(PassthroughCameraAccess cameraAccess)
        {
            if (!m_started)
            {
                m_imageCameraPose = cameraAccess.GetCameraPose();
                m_input?.Dispose();

                Texture targetTexture = cameraAccess.GetTexture();
                m_uiInference.SetDetectionCapture(targetTexture);

                // Convert texture to Tensor
                var textureTransform = new TextureTransform().SetDimensions(targetTexture.width, targetTexture.height, 3);
                m_input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.y, m_inputSize.x)); // C,H,W order
                TextureConverter.ToTensor(targetTexture, m_input, textureTransform);

                // Schedule the iterative execution
                m_schedule = m_engine.ScheduleIterable(m_input);
                m_downloadState = 0;
                m_started = true;
            }
        }

        public bool IsRunning()
        {
            return m_started;
        }
        #endregion

        #region Inference Functions
        private void LoadModel()
        {
            var model = ModelLoader.Load(m_sentisModel);
            m_engine = new Worker(model, m_backend);

            // Warm-up pass to load model to memory
            Texture loadingTexture = new Texture2D(m_inputSize.x, m_inputSize.y, TextureFormat.RGBA32, false);
            m_input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.y, m_inputSize.x));
            var textureTransform = new TextureTransform().SetDimensions(loadingTexture.width, loadingTexture.height, 3);
            TextureConverter.ToTensor(loadingTexture, m_input, textureTransform);
            m_engine.Schedule(m_input);

            IsModelLoaded = true;
            Debug.Log("Fast-SCNN Sentis model loaded successfully");
        }

        private void InferenceUpdate()
        {
            if (!m_started) return;

            try
            {
                switch (m_downloadState)
                {
                    case 0:
                        int it = 0;
                        while (m_schedule.MoveNext())
                        {
                            if (++it % m_layersPerFrame == 0) return; // yield after N layers
                        }
                        m_downloadState = 1;
                        break;

                    case 1:
                        if (!m_isWaiting)
                        {
                            PollRequestOutput();
                        }
                        else
                        {
                            if (m_pullOutput.IsReadbackRequestDone())
                            {
                                m_output = m_pullOutput.ReadbackAndClone();
                                m_isWaiting = false;
                                if (m_output.shape[0] > 0)
                                {
                                    Debug.Log("Fast-SCNN: output ready");
                                    m_downloadState = 2;
                                }
                                else
                                {
                                    Debug.LogError("Fast-SCNN: output empty");
                                    m_downloadState = 3;
                                }
                            }
                        }
                        break;

                    case 2:
                        // Deliver output to UI manager or AR overlay
                        // m_uiInference.DrawSegmentationMask(m_output, m_inputSize.x, m_inputSize.y, m_imageCameraPose);
                        m_downloadState = 3;
                        break;

                    case 3:
                        // Cleanup
                        m_started = false;
                        m_output?.Dispose();
                        m_downloadState = 4;
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Fast-SCNN Sentis error: {e.Message}");
                m_started = false;
            }
        }

        private void PollRequestOutput()
        {
            m_pullOutput = m_engine.PeekOutput(0) as Tensor<float>;
            if (m_pullOutput.dataOnBackend != null)
            {
                m_pullOutput.ReadbackRequest();
                m_isWaiting = true;
            }
            else
            {
                Debug.LogError("Fast-SCNN: No data output");
                m_downloadState = 3;
            }
        }
        #endregion
    }
}
