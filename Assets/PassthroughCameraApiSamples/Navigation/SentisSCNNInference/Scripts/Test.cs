using System;
using System.Collections;
using Meta.XR;
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEngine;

public class Test : MonoBehaviour
{
    public Texture2D testPicture;

    [SerializeField]
    private ModelAsset modelAsset;
    public float[] results;

    private Worker worker;

    private void Start()
    {
        Model model = ModelLoader.Load(modelAsset);

        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(model);
        FunctionalTensor[] outputs = Functional.Forward(model, inputs);
        
        Model runtimeModel = graph.Compile(outputs);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        runAI(testPicture);
    }

    public void runAI(Texture2D picture)
    {
        using Tensor<float> inputTensor = TextureConverter.ToTensor(picture, 28, 28, 1);
        worker.Schedule(inputTensor);
        
        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        results = outputTensor.DownloadToArray();
    }

    private void OnDisable()
    {
        worker?.Dispose();
    }
}