// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using Meta.XR;
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class SentisInferenceUiManager : MonoBehaviour
    {
        [Header("Placement configureation")]
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [Header("UI display references")]
        [SerializeField] private SentisObjectDetectedUiManager m_detectionCanvas;
        [SerializeField] private RawImage m_displayImage;
        [SerializeField] private Sprite m_boxTexture;
        [SerializeField] private Color m_boxColor;
        [SerializeField]
        [Tooltip("Translucent fill color for the rounded box.")]
        private Color m_boxFillColor;
        [SerializeField]
        [Tooltip("Border thickness in pixels for the rounded box (inset for the fill).")]
        private float m_boxBorderThickness = 6f;
        [SerializeField] private Font m_font;
        [SerializeField] private Color m_fontColor;
        [SerializeField] private int m_fontSize = 80;
        [Space(10)]
        public UnityEvent<int> OnObjectsDetected;
        
        [Header("Smoothing")]
        [SerializeField]
        [Tooltip("Higher values make boxes follow targets more tightly; lower values make motion smoother/slower.")]
        private float m_panelLerpSmoothing = 12f;

        public List<BoundingBox> BoxDrawn = new();

        private string[] m_labels;
        private List<GameObject> m_boxPool = new();
        private Transform m_displayLocation;

        //bounding box data
        public struct BoundingBox
        {
            public float CenterX;
            public float CenterY;
            public float Width;
            public float Height;
            public float Distance;
            public string Label;
            public Vector3? WorldPos;
            public string ClassName;
        }

        #region Unity Functions
        private void Start()
        {
            m_displayLocation = m_displayImage.transform;
        }
        #endregion

        #region Detection Functions
        public void OnObjectDetectionError()
        {
            // Clear current boxes
            ClearAnnotations();

            // Set obejct found to 0
            OnObjectsDetected?.Invoke(0);
        }
        #endregion

        #region BoundingBoxes functions
        public void SetLabels(TextAsset labelsAsset)
        {
            //Parse neural net m_labels
            m_labels = labelsAsset.text.Split('\n');
        }

        public void SetDetectionCapture(Texture image)
        {
            m_displayImage.texture = image;
            m_detectionCanvas.CapturePosition();
        }

        public void DrawUIBoxes(Tensor<float> output, Tensor<int> labelIDs, float imageWidth, float imageHeight, Pose cameraPose)
        {
            // Update canvas position
            m_detectionCanvas.UpdatePosition();

            float displayWidth = m_displayImage.rectTransform.rect.width;
            var displayHeight = m_displayImage.rectTransform.rect.height;

            var boxesFound = output.shape[0];
            if (boxesFound <= 0)
            {
                // No detections - clear existing annotations
                ClearAnnotations();
                OnObjectsDetected?.Invoke(0);
                return;
            }
            var maxBoxes = Mathf.Min(boxesFound, 200);

            OnObjectsDetected?.Invoke(maxBoxes);

            // Keep track of placed centers to avoid drawing multiple boxes for the same object
            var placedCenters = new List<Vector2>(maxBoxes);
            var placedClasses = new List<string>(maxBoxes);

            // Track which pooled panels have been matched to a detection this frame
            var matched = new List<bool>(new bool[m_boxPool.Count]);

            // Clear current logical boxes (UI panels will be reused when possible)
            BoxDrawn.Clear();

            // Draw (or update) the bounding boxes
            for (var n = 0; n < maxBoxes; n++)
            {
                // Get bounding box center coordinates
                var normalizedCenterX = output[n, 0] / imageWidth;
                var normalizedCenterY = output[n, 1] / imageHeight;
                var centerX = displayWidth * (normalizedCenterX - 0.5f);
                var centerY = displayHeight * (normalizedCenterY - 0.5f);

                // Get object class name
                var classname = m_labels[labelIDs[n]];

                // Calculate width/height in display space (used for dedupe threshold)
                var width = output[n, 2] * (displayWidth / imageWidth);
                var height = output[n, 3] * (displayHeight / imageHeight);

                // Simple de-duplication: skip boxes that are the same class and very near an already-placed center
                var center2D = new Vector2(centerX, centerY);
                var isDuplicate = false;
                for (int pi = 0; pi < placedCenters.Count; ++pi)
                {
                    if (placedClasses[pi] == classname)
                    {
                        // threshold based on box size; fallback to 30px
                        var threshold = Mathf.Max(Mathf.Min(width, height) * 0.5f, 30f);
                        if (Vector2.Distance(center2D, placedCenters[pi]) < threshold)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                }
                if (isDuplicate)
                {
                    continue;
                }

                // Mark this center/class as placed so subsequent detections in this frame won't duplicate
                placedCenters.Add(center2D);
                placedClasses.Add(classname);

                // Get the 3D marker world position using Depth Raycast
                var ray = m_cameraAccess.ViewportPointToRay(new Vector2(normalizedCenterX, 1.0f - normalizedCenterY), cameraPose);
                var worldPos = m_environmentRaycast.Raycast(ray);

                // Compute distance to camera (if we have a world position) and create a new bounding box data object
                float? distanceMeters = null;
                if (worldPos.HasValue)
                {
                    // cameraPose is the pose at image capture; Unity units are meters
                    distanceMeters = Vector3.Distance(cameraPose.position, worldPos.Value);
                }

                var box = new BoundingBox
                {
                    CenterX = centerX,
                    CenterY = centerY,
                    ClassName = classname,
                    Width = width,
                    Height = height,
                    Distance = distanceMeters.HasValue ? distanceMeters.Value : 99f,
                    Label = distanceMeters.HasValue ? $"{classname.ToUpper()} {distanceMeters.Value:0.00}m" : $"{classname.ToUpper()}",
                    WorldPos = worldPos,
                };

                // Try to find an existing pooled panel to reuse: same class, near center, similar size
                GameObject panel = null;
                int panelIndex = -1;
                float bestDist = float.MaxValue;
                for (int pi = 0; pi < m_boxPool.Count; ++pi)
                {
                    var p = m_boxPool[pi];
                    if (p == null) continue;
                    if (matched.Count <= pi) matched.Add(false);
                    if (matched[pi]) continue; // already assigned this frame

                    var label = p.GetComponentInChildren<Text>();
                    if (label == null) continue;
                    if (label.text != classname) continue;

                    // compute panel center in local display coords
                    var pLocalPos = p.transform.localPosition;
                    var pCenter = new Vector2(pLocalPos.x, -pLocalPos.y);
                    var dist = Vector2.Distance(pCenter, center2D);

                    // size similarity (use rect transform sizeDelta)
                    var prt = p.GetComponent<RectTransform>();
                    var pSize = prt != null ? prt.sizeDelta : Vector2.zero;
                    var sizeDiff = Mathf.Abs(pSize.x - width) + Mathf.Abs(pSize.y - height);

                    // threshold based on box size; fallback to 30px
                    var threshold = Mathf.Max(Mathf.Min(width, height) * 0.5f, 300f);
                    if (dist < threshold && sizeDiff < Mathf.Max(width, height))
                    {
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            panel = p;
                            panelIndex = pi;
                        }
                    }
                }

                // If no matching active panel found, try to get an inactive panel from the pool
                if (panel == null)
                {
                    for (int pi = 0; pi < m_boxPool.Count; ++pi)
                    {
                        var p = m_boxPool[pi];
                        if (p == null) continue;
                        if (!p.activeSelf)
                        {
                            panel = p;
                            panelIndex = pi;
                            break;
                        }
                    }
                }

                // If still no panel found, create a new one
                if (panel == null)
                {
                    Color boxColorInit = GetDistanceColor(box.Distance);
                    // panel = CreateNewBox(m_boxColor);
                    panel = CreateNewBox(boxColorInit, box.Label);
                    panelIndex = m_boxPool.Count - 1;
                    // ensure matched list covers new index
                    if (matched.Count <= panelIndex) matched.Add(false);
                }

                // Mark panel as used
                if (panelIndex >= 0 && panelIndex < matched.Count) matched[panelIndex] = true;

                // Immediately place/update the panel to the new detection
                var targetLocalPos = new Vector3(box.CenterX, -box.CenterY, box.WorldPos.HasValue ? box.WorldPos.Value.z : 0.0f);
                var camPos = cameraPose.position;
                var worldTarget = m_displayLocation.TransformPoint(targetLocalPos);
                var targetRot = Quaternion.LookRotation(worldTarget - camPos);

                var rt = panel.GetComponent<RectTransform>();
                var targetSize = new Vector2(box.Width, box.Height);

                panel.SetActive(true);
                panel.transform.localPosition = targetLocalPos;
                panel.transform.rotation = targetRot;
                if (rt != null) rt.sizeDelta = targetSize;
                Color boxColor = GetDistanceColor(box.Distance);

                // Update panel background color
                var img = panel.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.color = boxColor;
                }

                // Update the outline or border if your prefab uses it
                var fillTransform = panel.transform.Find("BoxFill");
                if (fillTransform != null)
                {
                    Color fillColor = new Color(boxColor.r, boxColor.g, boxColor.b, 35f / 255f);
                    var fillImg = fillTransform.GetComponent<Image>();
                    if (fillImg != null)
                        fillImg.color = fillColor;
                }

                //Set label text
                var lbl = panel.GetComponentInChildren<Text>();
                if (lbl != null)
                {
                    lbl.text = box.Label;
                    // lbl.fontSize = m_fontSize;
                    RectTransform rtP = img.GetComponent<RectTransform>();
                    float widthP = rtP.rect.width;
                    int fontSize = (int)(widthP / (box.Label.Length * 0.7f));
                    lbl.fontSize = fontSize;
                    // lbl.resizeTextForBestFit = true;
                    // lbl.resizeTextMinSize = 10;
                    // lbl.resizeTextMaxSize = m_fontSize; 
                }

                // Add to the list of boxes
                BoxDrawn.Add(box);
            }

            // Deactivate any panels that weren't matched this frame
            for (int i = 0; i < m_boxPool.Count; ++i)
            {
                var p = m_boxPool[i];
                if (p == null) continue;
                if (i < matched.Count && matched[i]) continue;
                if (p.activeSelf) p.SetActive(false);
            }
        }

        private void ClearAnnotations()
        {
            foreach (var box in m_boxPool)
            {
                box?.SetActive(false);
            }
            BoxDrawn.Clear();
        }

        private void DrawBox(BoundingBox box, int id, Pose cameraPose)
        {
            //Create the bounding box graphic or get from pool
            GameObject panel;
            if (id < m_boxPool.Count)
            {
                panel = m_boxPool[id];
                if (panel == null)
                {
                    Color boxColor = GetDistanceColor(box.Distance);
                    // panel = CreateNewBox(m_boxColor);
                    panel = CreateNewBox(boxColor, box.Label);
                }
                else
                {
                    panel.SetActive(true);
                }
            }
            else
            {
                Color boxColor = GetDistanceColor(box.Distance);
                // panel = CreateNewBox(m_boxColor);
                panel = CreateNewBox(boxColor, box.Label);
            }
            // Place immediately (no smoothing)
            var targetLocalPos = new Vector3(box.CenterX, -box.CenterY, box.WorldPos.HasValue ? box.WorldPos.Value.z : 0.0f);
            // Use the cached camera pose passed from the inference run manager so the ray
            // used to compute worldPos corresponds to the same capture time.
            var camPos = cameraPose.position;
            // rotation faces toward the captured camera position. Compute world position from the display parent
            var worldTarget = m_displayLocation.TransformPoint(targetLocalPos);
            var targetRot = Quaternion.LookRotation(worldTarget - camPos);

            var rt = panel.GetComponent<RectTransform>();
            var targetSize = new Vector2(box.Width, box.Height);

            // Immediately set final transform and size
            panel.transform.localPosition = targetLocalPos;
            panel.transform.rotation = targetRot;
            if (rt != null) rt.sizeDelta = targetSize;
            //Set label text
            var label = panel.GetComponentInChildren<Text>();
            label.text = box.Label;
            label.fontSize = m_fontSize;
        }

        public Color GetDistanceColor(float distance)
        {
            // Clamp distance between 1 and 4
            float d = Mathf.Clamp(distance, 0.5f, 3.5f);
        
            // Normalize to 0–1
            float t = (d - 0.5f) / 3f;
        
            // Split the gradient into 2 halves:
            // 0.0 → 0.5  = Orange → Red
            // 0.5 → 1.0  = Green → Orange
        
            if (t > 0.5f)
            {
                // Green (1.0) → Orange (0.5)
                float lerp = (t - 0.5f) / 0.5f;
                return Color.Lerp(Color.green, new Color(1f, 0.5f, 0f), 1f - lerp);
            }
            else
            {
                // Orange (0.5) → Red (0.0)
                float lerp = t / 0.5f;
                return Color.Lerp(new Color(1f, 0.5f, 0f), Color.red, 1f - lerp);
            }
        }

        private GameObject CreateNewBox(Color color, string label)
        {
            // Create the parent panel which will act as the border container
            var panel = new GameObject("ObjectBox");
            _ = panel.AddComponent<CanvasRenderer>();
            var borderImg = panel.AddComponent<Image>();
            borderImg.raycastTarget = false;
            borderImg.sprite = m_boxTexture;
            borderImg.type = Image.Type.Sliced;
            // Do not fill the center of the border image so the child fill (translucent) shows through
            borderImg.fillCenter = false;
            borderImg.color = color; // border color
            panel.transform.SetParent(m_displayLocation, false);

            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);

            // Create the fill image as a child and inset it by border thickness to create a border effect
            var fill = new GameObject("BoxFill");
            _ = fill.AddComponent<CanvasRenderer>();
            fill.transform.SetParent(panel.transform, false);
            var fillImg = fill.AddComponent<Image>();
            fillImg.raycastTarget = false;
            fillImg.sprite = m_boxTexture;
            fillImg.type = Image.Type.Sliced;
            Color translucentColor = new Color(
                color.r,
                color.g,
                color.b,
                35f / 255f
            );
            fillImg.color = translucentColor;
            // fillImg.color = m_boxFillColor; // translucent fill

            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0, 0);
            fillRt.anchorMax = new Vector2(1, 1);
            // inset by border thickness
            var inset = Mathf.Max(0f, m_boxBorderThickness);
            fillRt.offsetMin = new Vector2(inset, inset);
            fillRt.offsetMax = new Vector2(-inset, -inset);

            //Create the label on top of the fill
            var text = new GameObject("ObjectLabel");
            _ = text.AddComponent<CanvasRenderer>();
            text.transform.SetParent(panel.transform, false);
            var txt = text.AddComponent<Text>();
            txt.font = m_font;
            txt.color = m_fontColor;
            float width = panelRt.rect.width;
            int fontSize = (int)(width / (label.Length * 0.7f));
            txt.fontSize = fontSize;
            // txt.resizeTextForBestFit = true;
            // txt.resizeTextMinSize = 1;
            // txt.resizeTextMaxSize = 300;

            // txt.fontSize = m_fontSize;
            
            
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            
            // txt.raycastTarget = false;

            var rt2 = text.GetComponent<RectTransform>();
            rt2.anchorMin = new Vector2(0, 0);
            rt2.anchorMax = new Vector2(1, 1);
            // Keep label inset so it doesn't overlap the border
            // rt2.offsetMin = new Vector2(20 + inset, 0);
            // rt2.offsetMax = new Vector2(-inset, 30);
            float yPos = panelRt.rect.height / 2f;
            rt2.anchoredPosition = new Vector2(8, yPos);
            // float targetWidth = rt2.rect.width;
            // float textWidth = txt.preferredWidth;
            // if (textWidth > targetWidth)
            // {
            //     txt.fontSize = Mathf.FloorToInt(txt.fontSize * (targetWidth / textWidth));
            // }

            m_boxPool.Add(panel);
            return panel;
        }
        #endregion
    }

    // Smoothing helper removed - boxes are placed immediately when drawn.
}
