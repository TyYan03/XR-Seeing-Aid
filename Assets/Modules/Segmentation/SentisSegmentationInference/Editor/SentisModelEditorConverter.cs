// Copyright (c) Meta Platforms, Inc. and affiliates.
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace XRSeeingAid.MultiObjectDetection.Editor
{
    [MetaCodeSample("XRSeeingAid-MultiObjectDetection")]
    [CustomEditor(typeof(SentisInferenceRunManager))]
    public class SentisSegmentationEditorConverter : UnityEditor.Editor
    {
        private const string FILEPATH = "Assets/Modules/Segmentation/SentisSegmentationInference/Model/segmentation_model.sentis";
        private SentisInferenceRunManager m_targetClass;

        public void OnEnable()
        {
            m_targetClass = (SentisInferenceRunManager)target;
        }

        public override void OnInspectorGUI()
        {
            _ = DrawDefaultInspector();

            if (GUILayout.Button("Generate Segmentation Sentis model"))
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

            // Compile the graph
            var modelFinal = graph.Compile(modelOutput);

            // Quantize weights for better performance on Quest
            ModelQuantizer.QuantizeWeights(QuantizationType.Uint8, ref modelFinal);

            // Save the Sentis model
            ModelWriter.Save(FILEPATH, modelFinal);

            // Refresh the Unity asset database
            AssetDatabase.Refresh();

            Debug.Log("Segmentation Sentis model generated at: " + FILEPATH);
        }
    }
}