// Copyright (c) Meta Platforms, Inc. and affiliates.
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection.Editor
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    [CustomEditor(typeof(SentisInferenceRunManager))]
    public class SentisFastSCNNEditorConverter : UnityEditor.Editor
    {
        private const string FILEPATH = "Assets/PassthroughCameraApiSamples/Navigation/SentisSCNNInference/Model/fast_scnn_citys.sentis";
        private SentisInferenceRunManager m_targetClass;

        public void OnEnable()
        {
            m_targetClass = (SentisInferenceRunManager)target;
        }

        public override void OnInspectorGUI()
        {
            _ = DrawDefaultInspector();

            if (GUILayout.Button("Generate Fast-SCNN Sentis model"))
            {
                OnEnable(); // Get the latest values from the serialized object
                ConvertModel(); // convert the ONNX model to Sentis
            }
        }

        private void ConvertModel()
        {
            // Load the ONNX model
            var model = ModelLoader.Load(m_targetClass.OnnxModel);

            // Create a functional graph
            var graph = new FunctionalGraph();
            var input = graph.AddInput(model, 0);

            // For segmentation, we can just forward the model output directly
            var modelOutput = Functional.Forward(model, input)[0];  // Shape: (1, C, H, W)

            // Optional: You can normalize output or apply argmax if multiclass
            // For example, if multiclass segmentation:
            // var mask = Functional.ArgMax(modelOutput, axis: 1);

            // Compile the graph
            var modelFinal = graph.Compile(modelOutput);

            // Quantize weights for better performance on Quest
            ModelQuantizer.QuantizeWeights(QuantizationType.Uint8, ref modelFinal);

            // Save the Sentis model
            ModelWriter.Save(FILEPATH, modelFinal);

            // Refresh the Unity asset database
            AssetDatabase.Refresh();

            Debug.Log("Fast-SCNN Sentis model generated at: " + FILEPATH);
        }
    }
}