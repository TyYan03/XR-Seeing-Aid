using System;
using System.Collections;
using Meta.XR;
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;

using System.IO;


public class TestScript : MonoBehaviour
{
    public static bool recorded = false;
    public Texture2D testPicture;

    [SerializeField] private PassthroughCameraAccess m_cameraAccess;

    [SerializeField]
    private ModelAsset modelAsset;
    public float[] results;

    private Worker worker;
    private bool isProcessing = false;
    private Tensor<float> m_input;
    private Pose m_imageCameraPose;

    [SerializeField] private RawImage displayImage;

    private Texture2D tex; 
    private RawImage rawImage;
    public Color32[] arr1D_colors;

    private Renderer quadRenderer;


    private IEnumerator Start()
    {
        Debug.Log("Loading model...");
        Model model = ModelLoader.Load(modelAsset);

        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(model);
        FunctionalTensor[] outputs = Functional.Forward(model, inputs);
        
        Model runtimeModel = graph.Compile(outputs);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);


        if (m_cameraAccess == null)
            {
                Debug.LogError($"PCA: {nameof(m_cameraAccess)} field is required "
                            + $"to operate properly");
                yield return null;
            }

            while (!m_cameraAccess.IsPlaying)
            {
                yield return null;
            }


        // runAI(testPicture);
        StartCoroutine(ProcessCameraStream());
    }

    private Shader FindSafeShader()
{
    Shader s;

    s = Shader.Find("Unlit/Texture");
    if (s != null) return s;

    s = Shader.Find("Sprites/Default");
    if (s != null) return s;

    // s = Shader.Find("Universal Render Pipeline/Unlit");
    // if (s != null) return s;

    // s = Shader.Find("Unlit/Color");
    // if (s != null) return s;

    // s = Shader.Find("Legacy Shaders/Diffuse");
    // if (s != null) return s;

    Debug.LogError("No valid shader found in this render pipeline!");
    return null;
}

    //Segmentation display helper class
    // public class SegmentationDisplay : MonoBehaviour
    // {
    //     public RawImage displayImage; // assign in inspector
    //     private Texture2D tex;
    //     private int H = 640;
    //     private int W = 640;
    //     private Color32[,] arr2D_colour;

    //     void Start()
    //     {
    //         tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
    //         displayImage.texture = tex;
    //     }

    //     public void UpdateSegmentationMap(Color32[,] arr2D_colour)
    //     {
    //         Color32[] pixels = new Color32[H * W];
    //         for (int y = 0; y < H; y++)
    //         {
    //             for (int x = 0; x < W; x++)
    //             {
    //                 pixels[y * W + x] = arr2D_colour[y, x];
    //             }
    //         }

    //         tex.SetPixels32(pixels);
    //         tex.Apply();
    //     }
    // }

    private IEnumerator ProcessCameraStream() { 
        while (true) { 
            if (!isProcessing) { 
                Debug.Log("Processing new frame...");
                isProcessing = true; 
                // Capture frame from Quest passthrough camera 
                // Texture2D frame = PassthroughCameraAccess.Instance.GetCameraFrame(); // Pseudo API 
                m_imageCameraPose = m_cameraAccess.GetCameraPose();
                // clean last input
                m_input?.Dispose();
                // Update Capture data
                Texture targetTexture = m_cameraAccess.GetTexture();
                // m_uiInference.SetDetectionCapture(targetTexture);
                // Convert the texture to a Tensor and schedule the inference
                var textureTransform = new TextureTransform().SetDimensions(targetTexture.width, targetTexture.height, 3);
               //////// m_input = new Tensor<float>(new TensorShape(1, 3, 640, 640));
                m_input = TextureConverter.ToTensor(testPicture, 640, 640, 3);
                //// TextureConverter.ToTensor(targetTexture, m_input, textureTransform);
                
                Debug.Log($"Running inference {m_input}");
                if (m_input != null) { 
                    yield return RunAI(); 
                } else { 
                    isProcessing = false; 
                    yield return null; 
                } 
            } else { 
                yield return null; // wait until current frame finishes 
            } 
        } 
    }

    private IEnumerator RunAI() { 
        worker.Schedule(m_input); // Wait until the worker finishes processing 
        
        Debug.Log("Waiting for inference results...");
        while (worker.PeekOutput(0) as Tensor<float> == null) { 
            yield return null; 
        } 

        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>; 
        Tensor<float> test = outputTensor.ReadbackAndClone();
        float[] results = outputTensor.DownloadToArray(); 
        Debug.Log($"Inference results received. {results[0]} values. {results.Length} ");

         int batch = test.shape[0];   // should be 1
        int C = test.shape[1];
        int H = test.shape[2];
        int W = test.shape[3];

        Debug.Log($"tensor is {test.shape[0]} x {test.shape[1]} x {test.shape[2]} x {test.shape[3]} ");

        // Result array: H*W, each pixel is a class index
        int[] pred = new int[H * W];

        for (int h = 0; h < H; h++)
        {
            for (int w = 0; w < W; w++)
            {
                float maxVal = float.NegativeInfinity;
                int maxIndex = 0;

                for (int c = 0; c < C; c++)
                {
                    float v = test[0, c, h, w];
                    if (v > maxVal)
                    {
                        maxVal = v;
                        maxIndex = c;
                    }
                }

                pred[h * W + w] = maxIndex;
            }
        }

        int[,] arr2D = new int[H, W];

        for (int h = 0; h < H; h++)
        {
            for (int w = 0; w < W; w++)
            {
                arr2D[h, w] = pred[h * W + w];
            }
        }

        Debug.Log("Segmentation output 2D array created.");


Color32[] pallete = new Color32[]
{
    new Color32(128, 64, 128, 255), //road
    new Color32(244, 35, 232, 255), //sidewalk
    new Color32(70, 70, 70, 255), //building
    new Color32(102, 102, 156, 255), //wall
    new Color32(190, 153, 153, 255), //fence
    new Color32(153, 153, 153, 255), //pole
    new Color32(250, 170, 30, 255), //traffic light
    new Color32(220, 220, 0, 255), //traffic sign
    new Color32(107, 142, 35, 255), //vegetation
    new Color32(152, 251, 152, 255), //terrain
    new Color32(0, 130, 180, 255), //sky(?) - sky in official dataset has 70, 130, 180
    new Color32(220, 20, 60, 255), //person
    new Color32(255, 0, 0, 255), //rider
    new Color32(0, 0, 142, 255), //car
    new Color32(0, 0, 70, 255), //truck
    new Color32(0, 60, 100, 255), //bus
    new Color32(0, 80, 100, 255), //train
    new Color32(0, 0, 230, 255), //motorcycle
    new Color32(119, 11, 32, 255), //bike
};
string[] city_class_names = new string[]
{
    "road", "sidewalk", "building", "wall", "fence", "pole",
    "traffic light", "traffic sign", "vegetation", "terrain",
    "sky", "person", "rider", "car", "truck", "bus",
    "train", "motorcycle", "bicycle"
};


/*---*/
Debug.Log("pre-Processing Colours...");
Color32[,] arr2D_colour = new Color32[H, W];
for (int h = 0; h < H; h++)
{
    for (int w = 0; w < W; w++)
    {
        arr2D_colour[h, w] = pallete[arr2D[h,w]];
    }
}
Debug.Log("Processing Colours...");
Debug.Log($"Top-left pixel class index: {arr2D[0,0]}, color: {arr2D_colour[0,0]}");
Debug.Log($"Bottom-right pixel class index: {arr2D[H-1,W-1]}, color: {arr2D_colour[H-1,W-1]}");

// for (int h = 0; h < H; h++)
// {
//     for (int w = 0; w < W; w++)
//     {
//         Debug.Log($"Color at ({h},{w}): {arr2D_colour[h, w]}");
//     }
// }
        // UpdateSegmentationMap(arr2D_colour);
        Debug.Log("Segmentation map updated.");

        // isProcessing = false;


        tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadRenderer = quad.GetComponent<Renderer>();
        Shader shader = FindSafeShader();
        if (shader == null) yield break;
        
        quadRenderer.material = new Material(shader);

        quadRenderer.material.mainTexture = tex;

        // Place quad in front of VR camera
        Transform cam = Camera.main.transform;
        quad.transform.SetParent(cam);
        quad.transform.localPosition = new Vector3(0, 0, 1.5f); // 1.5m in front
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(1.5f, 1.5f, 1f);

        // Flatten the 2D array into a 1D array (Texture2D.SetPixels32 uses 1D)
        Color32[] pixels = new Color32[H * W];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                // Texture2D has (0,0) at bottom-left
                pixels[y * W + x] = arr2D_colour[y, x];
            }
        }

        if (pixels.Length != tex.width * tex.height)
        {
            Debug.LogError($"Pixel array size mismatch! pixels={pixels.Length} tex={tex.width * tex.height} {tex.width}x{tex.height}");
            yield break;
        }
        Debug.Log($"Top-left pixel color: {pixels[0]}");
Debug.Log($"Bottom-right pixel color: {pixels[pixels.Length-1]}");

        tex.SetPixels32(pixels); 
        tex.Apply();

        // Apply the pixels
        // tex.SetPixels32(pixels);
        // tex.Apply();
        // displayImage.texture = tex;
        // displayImage.SetNativeSize(); // opt

/*---*/
// def get_color_pallete(npimg, dataset='citys'):
//     """Visualize image.

//     Parameters
//     ----------
//     npimg : numpy.ndarray
//         Single channel image with shape `H, W, 1`.
//     dataset : str, default: 'pascal_voc'
//         The dataset that model pretrained on. ('pascal_voc', 'ade20k')
//     Returns
//     -------
//     out_img : PIL.Image
//         Image with color pallete
//     """
//     # recovery boundary
//     if dataset in ('pascal_voc', 'pascal_aug'):
//         npimg[npimg == -1] = 255
//     # put colormap
//     if dataset == 'ade20k':
//         npimg = npimg + 1
//         out_img = Image.fromarray(npimg.astype('uint8'))
//         out_img.putpalette(adepallete)
//         return out_img
//     elif dataset == 'citys':
//         out_img = Image.fromarray(npimg.astype('uint8'))
//         classes_to_blackout = citys_class_names[:]
//         for name in ["road", "sidewalk", "terrain"]:
//             if name in classes_to_blackout:
//                 classes_to_blackout.remove(name)
//         # new_palette = blackout_classes(cityspallete, classes_to_blackout)
//         new_palette = cityspallete[:]
//         # breakpoint()
//         # out_img.putpalette(cityspallete)
//         out_img.putpalette(new_palette)

//         # draw class names on top of regions for visibility
//         try:
//             rgb = out_img.convert('RGB')
//             draw = ImageDraw.Draw(rgb)
//             # default font (will work without external TTF)
//             try:
//                 font = ImageFont.load_default()
//             except Exception:
//                 font = None

//             mask_arr = npimg.astype('int')
//             # breakpoint()
//             unique = np.unique(mask_arr)
//             # label each connected region separately; skip tiny regions to avoid clutter
//             for cls in unique:
//                 if cls < 0 or cls == 255:
//                     continue
//                 cls = int(cls)
//                 bin_mask = (mask_arr == cls).astype(np.uint8)
//                 # extract connected regions for this class; small regions filtered by get_regions_from_mask
//                 regions = get_regions_from_mask(bin_mask, min_area=200)
//                 regions = region_update_with_median_line(regions, bin_mask)
//                 if not regions:
//                     continue
//                 # palette color for this class
//                 base = 3 * cls
//                 if base + 2 < len(cityspallete):
//                     r, g, b = cityspallete[base:base+3]
//                 else:
//                     r, g, b = (0, 0, 0)
//                 lum = 0.299 * r + 0.587 * g + 0.114 * b
//                 text_color = (0, 0, 0) if lum > 128 else (255, 255, 255)
//                 outline_color = (255, 255, 255) if text_color == (0, 0, 0) else (0, 0, 0)
//                 name = citys_class_names[cls] if cls < len(citys_class_names) else f'class_{cls}'
//                 if name not in ["road", "sidewalk", "terrain"]:
//                     name = ""
//                 for rgn in regions:
//                     # breakpoint()
//                     x, y, w, h = rgn.get('bbox', (0, 0, 0, 0))
//                     cx = x + w // 2
//                     cy = y + h // 2
//                     if not name == "":
//                         print(f"Drawing class '{name}' at region bbox {(x,y,w,h)} with median horiz line {rgn.get('median_line', None)} and median vert line {rgn.get('median_line_y', None)}")
//                         draw.line(rgn["median_line"], fill=(0,0,0), width=3)
//                         draw.line(rgn["median_line_y"], fill=(255,255,255), width=3)

//                         # draw.ellipse()
//                         # draw.line(rgn["median_line"]["y"], fill=(255,255,255), width=10)
//                         #draw point for median line
//                         # draw.circle((400,400), radius=5, fill=(0,0,0))
//                         # draw.ellipse(xy=[(0-5,0-5),(0+5,0+5)], fill=(0,0,0))
//                         # draw.point(rgn.get('median_line', []), fill=(0,0,0))
//                     if name == "":
//                         outline_color = (0,0,0)
//                         text_color = (0,0,0)
//                     if font is not None:
//                         for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
//                             draw.text((cx + dx, cy + dy), name, font=font, fill=outline_color)
//                         draw.text((cx, cy), name, font=font, fill=text_color)
//                         # draw.ellipse(xy=[(cx-30,cy-30),(cx+30,cy+30)], fill=(0,0,0))
//                         # draw.circle(xy=(cx,cy), radius=30, fill=(0,0,0))
//                     else:
//                         draw.text((cx, cy), name, fill=text_color)
//                         # draw.ellipse(xy=[(cx-30,cy-30),(cx+30,cy+30)], fill=(0,0,0))
//                         # draw.circle(xy=(cx,cy), radius=30, fill=(0,0,0))

//             return rgb
//         except Exception:
//             # if any drawing step fails, fall back to paletted image
//             return out_img
//     out_img = Image.fromarray(npimg.astype('uint8'))
//     out_img.putpalette(vocpallete)
//     return out_img
/*---*/

        // for (int y = 0; y < H; y++)
        // {
        //     for (int x = 0; x < W; x++)
        //     {
        //         Debug.Log($"Result at ({x},{y}): {arr2D[y, x]}");
        //     }
        //     // Debug.Log($"Result at ({i}): {arr2D[0, i]}");
        // }


        // string path1 = Path.Combine(Application.persistentDataPath, "segmentation_output.txt");
        // Debug.Log("Saving CSV to: (check)" + "segmentation_output.txt");
        // Debug.Log(Application.persistentDataPath);
        // string path1 = Path.Combine(Application.persistentDataPath, "segmentation_output.txt");
        // Debug.Log($"Saving CSV to: (check) {path1}");

        // if (recorded == false)
        // {
        //     recorded = true;
        //     string path = Path.Combine(Application.persistentDataPath, "segmentation_output.txt");
        //     Debug.Log("Saving CSV to: " + path);
        //     int h_arr = arr2D.GetLength(0);
        //     int w_arr = arr2D.GetLength(1);
        //     Debug.Log($"Array dimensions: {h_arr} x {w_arr}");
        //     // int H = arr2D.GetLength(0);
        //     // int W = arr2D.GetLength(1);

        //     using (StreamWriter writer = new StreamWriter(path))
        //     {
        //         for (int h = 0; h < h_arr; h++)
        //         {
        //             for (int w = 0; w < w_arr; w++)
        //             {
        //                 writer.Write(arr2D[h, w]);

        //                 if (w < w_arr - 1)
        //                     writer.Write(",");
        //             }
        //             writer.WriteLine();
        //         }
        //     }

        //     Debug.Log("CSV saved to: " + path);
        //     // using (StreamWriter writer = new Stream
        // }
        // // return arr2D;
    // }



        // foreach (var p in pred)
        // {
        //     Debug.Log($"Predicted class index: {p}");
        // }

        // for (int i = 0; i < 640; i++)
        // {
        //     Debug.Log($"Result at ({i}): {results[i]}");
        // }
        // TODO: do something with results here 
        isProcessing = false; 
    }

    // public void runAI(Texture2D picture)
    // {
    //     using Tensor<float> inputTensor = TextureConverter.ToTensor(picture, 640, 640, 3);
    //     worker.Schedule(inputTensor);
        
    //     Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
    //     results = outputTensor.DownloadToArray();
    // }

    private void OnDisable()
    {
        worker?.Dispose();
    }
}
