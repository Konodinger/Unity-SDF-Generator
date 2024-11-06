using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UIElements;


namespace Zero.Editor
{
    public class SDFGenerator : EditorWindow
    {
        [Flags]
        public enum TextureModes
        {
            R = 0x01,
            G = 0x02,
            B = 0x04,
            A = 0x08,
        }

        public enum SDFSide
        {
            BothSides,
            InsideOnly,
            OutsideOnly,
        }

        public enum PostSDFEffects
        {
            None,
            Blur,
            ProgressiveBlur,
            MaxKernel,
            MinKernel,
        }

        // Left panel
        
        private ObjectField m_SourceField;
        private EnumFlagsField m_ModeField;
        private DropdownField m_SideField;
        private Toggle m_InvertField;
        private IntegerField m_GradientSizePXField;
        private Toggle m_ResizeTextureSDFField;
        private Toggle m_ClampBorderField;
        private ColorField m_BorderColorField;
        private Slider m_EdgeValueField;
        private Vector2Field m_ImageSizeRatioField;
        private DropdownField m_PostSDFEffectField;
        private SliderInt m_PostSDFRadiusField;
        private Toggle m_ResizeTexturePostSDFField;
        private Toggle m_AutoGenerateField;
        private Toggle m_OpenRenderDocGenerationField;

        private Label m_SourceSizeField;
        private List<ToolbarToggle> m_SourcePreviewChannelsField;
        private Image m_SourceTextureField;
        
        private Button m_GenerateButton;
        
        // Middle panel
        
        private Vector2Field m_PixelHoveredPositionField;
        private ColorField m_PixelHoveredColorField;
        private List<FloatField> m_PixelHoveredValuesField;

        private Label m_GeneratedSizeField;
        private List<ToolbarToggle> m_GeneratedPreviewChannelsField;
        private Image m_GeneratedTextureField;
        
        private Button m_SaveButton;
        private Button m_SaveAsButton;

        // Right panel

        private Toggle m_SuperKeepSourceField;
        private ObjectField m_SuperNewSourceField;
        private Toggle m_SuperSourceOnField;
        private Vector2Field m_SuperSourceScalingField;
        private Label m_SuperSourceScalingLabel;
        private Vector2Field m_SuperSourceOffsetField;
        private Label m_SuperSourceOffsetLabel;
        private Toggle m_SuperGeneratedOnField;
        private DropdownField m_SuperGeneratedChannelField;
        private DropdownField m_SuperBlendingModeField;
        private ColorField m_SuperGeneratedColorField;
        private FloatField m_SuperGeneratedIntensityField;
        private Toggle m_AutoSuperposeField;
        private Toggle m_OpenRenderDocSuperpositionField;
        
        private List<ToolbarToggle> m_SuperpositionPreviewChannelsField;
        private Image m_SuperpositionTextureField;

        private Button m_SuperposeButton;
        
        
        private Texture2D m_SourceTexture = null;
        private Texture2D m_SourceTextureReadable = null;
        private Vector2Int m_GeneratedTextureSize = Vector2Int.one;
        private Texture2D m_GeneratedTexture = null;
        private Texture2D m_SuperNewSourceTexture = null;
        private Texture2D m_SuperpositionTexture = null;

        private static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
        private static readonly int ChannelID = Shader.PropertyToID("_Channel");
        private static readonly int EdgeValueID = Shader.PropertyToID("_EdgeValue");
        private static readonly int BorderColorID = Shader.PropertyToID("_BorderColor");
        private static readonly int SpreadID = Shader.PropertyToID("_Spread");
        private static readonly int FeatherID = Shader.PropertyToID("_Feather");
        private static readonly int ScaleID = Shader.PropertyToID("_Scale");
        private static readonly int PostRadiusID = Shader.PropertyToID("_PostRadius");
        private static readonly int SuperTexFlagsID = Shader.PropertyToID("_SuperTexFlags");
        private static readonly int SuperColorID = Shader.PropertyToID("_SuperColor");
        private static readonly int SuperIntensityID = Shader.PropertyToID("_SuperIntensity");
        
        private bool IsTextureRescaled
        {
            get { return (m_ResizeTextureSDFField.value && ((SDFSide) m_SideField.index != SDFSide.InsideOnly))
                || (m_ResizeTexturePostSDFField.value && ((PostSDFEffects) m_PostSDFEffectField.index != PostSDFEffects.None)); }
        }

        [MenuItem("Tools/SDF Generator %g", false, 30001)]
        private static void ShowWindow()
        {
            SDFGenerator window = EditorWindow.GetWindow<SDFGenerator>();
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("SDF Generator");

            rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("SDFToolStyles"));
            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>("SDFToolTemplate");
            VisualElement root = tree.CloneTree();
            rootVisualElement.Add(root);
            
            // Left panel

            m_SourceField = root.Q<ObjectField>("source-field");
            m_SourceField.objectType = typeof(Texture2D);
            m_SourceField.RegisterValueChangedCallback(OnSourceValueChange);

            m_ModeField = root.Q<EnumFlagsField>("mode-field");
            m_ModeField.Init(TextureModes.A);
            m_ModeField.RegisterValueChangedCallback(OnModeValueChange);
            m_ModeField.SetEnabled(false);

            m_SideField = root.Q<DropdownField>("inside-outside-field");
            m_SideField.index = 0;
            m_SideField.RegisterValueChangedCallback(OnSideChange);
            m_SideField.SetEnabled(false);
            
            m_InvertField = root.Q<Toggle>("invert-field");
            m_InvertField.value = false;
            m_InvertField.RegisterCallback<ChangeEvent<bool>>(OnInvertChange);
            m_InvertField.SetEnabled(false);

            m_GradientSizePXField = root.Q<IntegerField>("gradient-size-px-field");
            m_GradientSizePXField.value = 20;
            m_GradientSizePXField.RegisterCallback<ChangeEvent<int>>(OnGradientSizePixelChange);
            m_GradientSizePXField.SetEnabled(false);

            m_ResizeTextureSDFField = root.Q<Toggle>("resize-texture-sdf-field");
            m_ResizeTextureSDFField.value = true;
            m_ResizeTextureSDFField.RegisterCallback<ChangeEvent<bool>>(OnResizeTextureChange);
            m_ResizeTextureSDFField.SetEnabled(false);

            m_ClampBorderField = root.Q<Toggle>("clamp-border-field");
            m_ClampBorderField.value = false;
            m_ClampBorderField.RegisterCallback<ChangeEvent<bool>>(OnClampBorderChange);
            m_ClampBorderField.SetEnabled(false);

            m_BorderColorField = root.Q<ColorField>("border-color-field");
            m_BorderColorField.value = new Color(0, 0, 0, 0);
            m_BorderColorField.RegisterValueChangedCallback(OnBorderColorChange);
            m_BorderColorField.SetEnabled(false);

            m_EdgeValueField = root.Q<Slider>("edge-value-field");
            m_EdgeValueField.value = 0.5f;
            m_EdgeValueField.RegisterCallback<ChangeEvent<float>>(OnEdgeValueChange);
            m_EdgeValueField.SetEnabled(false);

            m_PostSDFEffectField = root.Q<DropdownField>("post-sdf-effect-field");
            m_PostSDFEffectField.index = 0;
            m_PostSDFEffectField.RegisterValueChangedCallback(OnPostSDFEffectChange);
            m_PostSDFEffectField.SetEnabled(false);

            m_PostSDFRadiusField = root.Q<SliderInt>("post-sdf-effect-radius-field");
            m_PostSDFRadiusField.value = 1;
            m_PostSDFRadiusField.RegisterCallback<ChangeEvent<int>>(OnPostSDFRadiusChange);
            m_PostSDFRadiusField.SetEnabled(false);

            m_ResizeTexturePostSDFField = root.Q<Toggle>("resize-texture-post-sdf-field");
            m_ResizeTexturePostSDFField.value = true;
            m_ResizeTexturePostSDFField.RegisterCallback<ChangeEvent<bool>>(OnResizeTextureChange);
            m_ResizeTexturePostSDFField.SetEnabled(false);

            m_AutoGenerateField = root.Q<Toggle>("auto-generate-field");
            m_AutoGenerateField.value = false;
            m_AutoGenerateField.RegisterCallback<ChangeEvent<bool>>(OnAutoGenerateToggle);
            m_AutoGenerateField.SetEnabled(false);
            
            m_ImageSizeRatioField = root.Q<Vector2Field>("image-size-ratio-field");
            UpdateImageSizeRatio();
            m_ImageSizeRatioField.SetEnabled(false);

            m_OpenRenderDocGenerationField = root.Q<Toggle>("open-renderdoc-generation-field");
            m_OpenRenderDocGenerationField.value = false;
            m_OpenRenderDocGenerationField.SetEnabled(false);

            m_SourceSizeField = root.Q<Label>("source-image-size-field");
            
            m_SourcePreviewChannelsField = new List<ToolbarToggle>();
            m_SourcePreviewChannelsField.Add(root.Q<ToolbarToggle>("source-preview-channel-r"));
            m_SourcePreviewChannelsField.Add(root.Q<ToolbarToggle>("source-preview-channel-g"));
            m_SourcePreviewChannelsField.Add(root.Q<ToolbarToggle>("source-preview-channel-b"));
            m_SourcePreviewChannelsField.Add(root.Q<ToolbarToggle>("source-preview-channel-a"));
            foreach (var previewChannel in m_SourcePreviewChannelsField)
            {
                previewChannel.value = true;
                previewChannel.RegisterCallback<ChangeEvent<bool>>(OnSourcePreviewChannelsChange);
                previewChannel.SetEnabled(false);
            }
            
            m_SourceTextureField = rootVisualElement.Q<Image>("source-view");
            m_SourceTextureField.RegisterCallback<MouseMoveEvent>((MouseMoveEvent evt) =>
            {
                OnTextureMouseMove(evt, m_SourceTextureField);
            });
            m_SourceTextureField.RegisterCallback<MouseLeaveEvent>(OnTextureMouseLeave);
            SetTextureSizeField(m_SourceSizeField, m_SourceTextureField);
            
            m_GenerateButton = root.Q<Button>("generate-button");
            m_GenerateButton.clicked += GenerateStatic;
            m_GenerateButton.SetEnabled(false);
            
            
            // Middle panel
            
            m_PixelHoveredPositionField = root.Q<Vector2Field>("pixel-position-field");
            m_PixelHoveredPositionField.SetEnabled(false);
            m_PixelHoveredPositionField.value = new Vector2Int(-1, -1);

            m_PixelHoveredColorField = root.Q<ColorField>("pixel-color-field");
            m_PixelHoveredColorField.SetEnabled(false);
            m_PixelHoveredColorField.value = new Color(0, 0, 0, 0);

            m_GeneratedSizeField = root.Q<Label>("generated-image-size-field");

            m_PixelHoveredValuesField = new List<FloatField>();
            m_PixelHoveredValuesField.Add(root.Q<FloatField>("current-color-value-field-r"));
            m_PixelHoveredValuesField.Add(root.Q<FloatField>("current-color-value-field-g"));
            m_PixelHoveredValuesField.Add(root.Q<FloatField>("current-color-value-field-b"));
            m_PixelHoveredValuesField.Add(root.Q<FloatField>("current-color-value-field-a"));
            foreach (var pixelHoveredValue in m_PixelHoveredValuesField)
            {
                pixelHoveredValue.value = 0;
            }
            
            m_GeneratedPreviewChannelsField = new List<ToolbarToggle>();
            m_GeneratedPreviewChannelsField.Add(root.Q<ToolbarToggle>("generated-preview-channel-r"));
            m_GeneratedPreviewChannelsField.Add(root.Q<ToolbarToggle>("generated-preview-channel-g"));
            m_GeneratedPreviewChannelsField.Add(root.Q<ToolbarToggle>("generated-preview-channel-b"));
            m_GeneratedPreviewChannelsField.Add(root.Q<ToolbarToggle>("generated-preview-channel-a"));
            foreach (var previewChannel in m_GeneratedPreviewChannelsField)
            {
                previewChannel.value = true;
                previewChannel.RegisterCallback<ChangeEvent<bool>>(OnGeneratedPreviewChannelsChange);
                previewChannel.SetEnabled(false);
            }
            
            m_GeneratedTextureField = rootVisualElement.Q<Image>("generated-view");
            m_GeneratedTextureField.RegisterCallback<MouseMoveEvent>((MouseMoveEvent evt) =>
            {
                OnTextureMouseMove(evt, m_GeneratedTextureField);
            });
            m_GeneratedTextureField.RegisterCallback<MouseLeaveEvent>(OnTextureMouseLeave);
            SetTextureSizeField(m_GeneratedSizeField, m_GeneratedTextureField);
            
            m_SaveButton = root.Q<Button>("save-button");
            m_SaveButton.clicked += SaveGeneratedTexture;
            m_SaveButton.SetEnabled(false);
        
            m_SaveAsButton = root.Q<Button>("save-as-button");
            m_SaveAsButton.clicked += SaveAsGeneratedTexture;
            m_SaveAsButton.SetEnabled(false);

            // Right panel

            m_SuperKeepSourceField = root.Q<Toggle>("super-keep-source-toggle");
            m_SuperKeepSourceField.value = true;
            m_SuperKeepSourceField.RegisterCallback<ChangeEvent<bool>>(OnSuperReplaceSourceChange);
            m_SuperKeepSourceField.SetEnabled(false);
            
            
            m_SuperNewSourceField = root.Q<ObjectField>("super-new-source-field");
            m_SuperNewSourceField.objectType = typeof(Texture2D);
            m_SuperNewSourceField.RegisterValueChangedCallback(OnSuperNewSourceValueChange);
            m_SuperNewSourceField.SetEnabled(false);
            
            m_SuperSourceOnField = root.Q<Toggle>("super-source-on-field");
            m_SuperSourceOnField.value = true;
            m_SuperSourceOnField.RegisterCallback<ChangeEvent<bool>>(OnSuperSourceOnChange);
            m_SuperSourceOnField.SetEnabled(false);

            m_SuperSourceScalingField = root.Q<Vector2Field>("super-source-scaling-field");
            m_SuperSourceScalingField.value = Vector2.one;
            m_SuperSourceScalingField.RegisterCallback<ChangeEvent<Vector2>>(OnSuperSourceScalingChange);
            m_SuperSourceScalingField.SetEnabled(false);

            m_SuperSourceScalingLabel = root.Q<Label>("super-source-scaling-label");
            m_SuperSourceScalingLabel.SetEnabled(false);

            m_SuperSourceOffsetField = root.Q<Vector2Field>("super-source-offset-field");
            m_SuperSourceOffsetField.value = Vector2.zero;
            m_SuperSourceOffsetField.RegisterCallback<ChangeEvent<Vector2>>(OnSuperSourceOffsetChange);
            m_SuperSourceOffsetField.SetEnabled(false);

            m_SuperSourceOffsetLabel = root.Q<Label>("super-source-offset-label");
            m_SuperSourceOffsetLabel.SetEnabled(false);

            m_SuperGeneratedOnField = root.Q<Toggle>("super-generated-on-field");
            m_SuperGeneratedOnField.value = true;
            m_SuperGeneratedOnField.RegisterCallback<ChangeEvent<bool>>(OnSuperGeneratedOnChange);
            m_SuperGeneratedOnField.SetEnabled(false);

            m_SuperGeneratedChannelField = root.Q<DropdownField>("super-generated-channel-field");
            m_SuperGeneratedChannelField.index = 3;
            m_SuperGeneratedChannelField.RegisterValueChangedCallback(OnSuperGeneratedChannelChange);
            m_SuperGeneratedChannelField.SetEnabled(false);

            m_SuperBlendingModeField = root.Q<DropdownField>("super-blending-mode-field");
            m_SuperBlendingModeField.index = 0;
            m_SuperBlendingModeField.RegisterValueChangedCallback(OnSuperBlendingModeChange);
            m_SuperBlendingModeField.SetEnabled(false);

            m_SuperGeneratedColorField = root.Q<ColorField>("super-sdf-color-field");
            m_SuperGeneratedColorField.value = Color.yellow;
            m_SuperGeneratedColorField.RegisterValueChangedCallback(OnSuperGeneratedColorChange);
            m_SuperGeneratedColorField.SetEnabled(false);
            
            m_SuperGeneratedIntensityField = root.Q<FloatField>("super-sdf-intensity-field");
            m_SuperGeneratedIntensityField.value = 1;
            m_SuperGeneratedIntensityField.RegisterCallback<ChangeEvent<float>>(OnSuperGeneratedIntensityChange);
            m_SuperGeneratedIntensityField.SetEnabled(false);

            m_AutoSuperposeField = root.Q<Toggle>("auto-superpose-field");
            m_AutoSuperposeField.value = false;
            m_AutoSuperposeField.RegisterCallback<ChangeEvent<bool>>(OnAutoSuperposeToggle);
            m_AutoSuperposeField.SetEnabled(false);

            m_OpenRenderDocSuperpositionField = root.Q<Toggle>("open-renderdoc-superposition-field");
            m_OpenRenderDocSuperpositionField.value = false;
            m_OpenRenderDocSuperpositionField.SetEnabled(false);
            
            
            m_SuperpositionPreviewChannelsField = new List<ToolbarToggle>();
            m_SuperpositionPreviewChannelsField.Add(root.Q<ToolbarToggle>("superposition-preview-channel-r"));
            m_SuperpositionPreviewChannelsField.Add(root.Q<ToolbarToggle>("superposition-preview-channel-g"));
            m_SuperpositionPreviewChannelsField.Add(root.Q<ToolbarToggle>("superposition-preview-channel-b"));
            m_SuperpositionPreviewChannelsField.Add(root.Q<ToolbarToggle>("superposition-preview-channel-a"));
            foreach (var previewChannel in m_SuperpositionPreviewChannelsField)
            {
                previewChannel.value = true;
                previewChannel.RegisterCallback<ChangeEvent<bool>>(OnSuperpositionPreviewChannelsChange);
                previewChannel.SetEnabled(false);
            }
            
            m_SuperpositionTextureField = rootVisualElement.Q<Image>("superposition-view");
            m_SuperpositionTextureField.RegisterCallback<MouseMoveEvent>((MouseMoveEvent evt) =>
            {
                OnTextureMouseMove(evt, m_SuperpositionTextureField);
            });
            m_SuperpositionTextureField.RegisterCallback<MouseLeaveEvent>(OnTextureMouseLeave);

            m_SuperposeButton = root.Q<Button>("superpose-button");
            m_SuperposeButton.clicked += SuperposeSourceAndSDFTextures;
            m_SuperposeButton.SetEnabled(false);
            
        }

        private void UpdateImageSizeRatio()
        {
            if (m_SourceTexture)
            {
                
            int radius = 0;
            if (m_ResizeTextureSDFField.value && ((SDFSide) m_SideField.index != SDFSide.InsideOnly))
                radius = m_GradientSizePXField.value;
            
            if ((SDFSide) m_SideField.index == SDFSide.OutsideOnly)
                radius *= 2;
            
            if (m_ResizeTexturePostSDFField.value && (PostSDFEffects) m_PostSDFEffectField.index != PostSDFEffects.None)
                radius += m_PostSDFRadiusField.value * 2;

            m_GeneratedTextureSize = new Vector2Int(m_SourceTexture.width + radius, m_SourceTexture.height + radius);
            m_ImageSizeRatioField.value = new Vector2((float) m_GeneratedTextureSize.x / m_SourceTexture.width, (float) m_GeneratedTextureSize.y / m_SourceTexture.height);
            }
            else
            {
                m_GeneratedTextureSize = Vector2Int.one;
                m_ImageSizeRatioField.value = Vector2.one;
            }
        }

        private void OnDisable()
        {
            // Left panel
            
            m_SourceField.UnregisterValueChangedCallback(OnSourceValueChange);
            m_ModeField.UnregisterValueChangedCallback(OnModeValueChange);
            m_SideField.UnregisterValueChangedCallback(OnSideChange);
            m_InvertField.UnregisterCallback<ChangeEvent<bool>>(OnInvertChange);
            m_GradientSizePXField.UnregisterCallback<ChangeEvent<int>>(OnGradientSizePixelChange);
            m_ResizeTextureSDFField.UnregisterCallback<ChangeEvent<bool>>(OnResizeTextureChange);
            m_ClampBorderField.UnregisterCallback<ChangeEvent<bool>>(OnClampBorderChange);
            m_BorderColorField.UnregisterValueChangedCallback(OnBorderColorChange);
            m_EdgeValueField.UnregisterCallback<ChangeEvent<float>>(OnEdgeValueChange);
            m_PostSDFEffectField.UnregisterValueChangedCallback(OnPostSDFEffectChange);
            m_PostSDFRadiusField.UnregisterCallback<ChangeEvent<int>>(OnPostSDFRadiusChange);
            m_ResizeTexturePostSDFField.UnregisterCallback<ChangeEvent<bool>>(OnResizeTextureChange);
            m_AutoGenerateField.UnregisterCallback<ChangeEvent<bool>>(OnAutoGenerateToggle);
            
            foreach (var previewChannelField in m_SourcePreviewChannelsField)
            {
                previewChannelField.UnregisterCallback<ChangeEvent<bool>>(OnSourcePreviewChannelsChange);
            }
            m_SourceTextureField.UnregisterCallback<MouseMoveEvent>((MouseMoveEvent evt) =>
            {
                OnTextureMouseMove(evt, m_SourceTextureField);
            });
            m_SourceTextureField.UnregisterCallback<MouseLeaveEvent>(OnTextureMouseLeave);
            
            m_GenerateButton.clicked -= GenerateStatic;
            
            // Middle panel
            
            foreach (var previewChannelField in m_GeneratedPreviewChannelsField)
            {
                previewChannelField.UnregisterCallback<ChangeEvent<bool>>(OnGeneratedPreviewChannelsChange);
            }
            m_GeneratedTextureField.UnregisterCallback<MouseMoveEvent>((MouseMoveEvent evt) =>
            {
                OnTextureMouseMove(evt, m_GeneratedTextureField);
            });
            m_GeneratedTextureField.UnregisterCallback<MouseLeaveEvent>(OnTextureMouseLeave);
            
            m_SaveButton.clicked -= SaveGeneratedTexture;
            m_SaveAsButton.clicked -= SaveAsGeneratedTexture;
            
            // Right panel

            m_SuperKeepSourceField.UnregisterCallback<ChangeEvent<bool>>(OnSuperReplaceSourceChange);
            m_SuperNewSourceField.UnregisterValueChangedCallback(OnSuperNewSourceValueChange);
            m_SuperSourceOnField.UnregisterCallback<ChangeEvent<bool>>(OnSuperSourceOnChange);
            m_SuperSourceScalingField.UnregisterCallback<ChangeEvent<Vector2>>(OnSuperSourceScalingChange);
            m_SuperSourceOffsetField.UnregisterCallback<ChangeEvent<Vector2>>(OnSuperSourceOffsetChange);
            m_SuperGeneratedOnField.UnregisterCallback<ChangeEvent<bool>>(OnSuperGeneratedOnChange);
            m_SuperGeneratedChannelField.UnregisterValueChangedCallback(OnSuperGeneratedChannelChange);
            m_SuperBlendingModeField.UnregisterValueChangedCallback(OnSuperBlendingModeChange);
            m_SuperGeneratedColorField.UnregisterValueChangedCallback(OnSuperGeneratedColorChange);
            m_SuperGeneratedIntensityField.UnregisterCallback<ChangeEvent<float>>(OnSuperGeneratedIntensityChange);
            m_AutoSuperposeField.UnregisterCallback<ChangeEvent<bool>>(OnAutoSuperposeToggle);
            
            
            foreach (var previewChannelField in m_SuperpositionPreviewChannelsField)
            {
                previewChannelField.UnregisterCallback<ChangeEvent<bool>>(OnSuperpositionPreviewChannelsChange);
            }
            m_SuperpositionTextureField.UnregisterCallback<MouseMoveEvent>((MouseMoveEvent evt) =>
            {
                OnTextureMouseMove(evt, m_SuperpositionTextureField);
            });
            m_SuperpositionTextureField.UnregisterCallback<MouseLeaveEvent>(OnTextureMouseLeave);

            m_SuperposeButton.clicked -= SuperposeSourceAndSDFTextures;
        }

        private void OnSourceValueChange(ChangeEvent<UnityEngine.Object> evt)
        {
            bool enable = UpdateSourceTexture((Texture2D) evt.newValue);
            
            
            m_ModeField.SetEnabled(enable);
            m_SideField.SetEnabled(enable);
            m_GenerateButton.SetEnabled(enable);
            m_InvertField.SetEnabled(enable);
            m_GradientSizePXField.SetEnabled(enable);
            m_ResizeTextureSDFField.SetEnabled(enable);
            m_ClampBorderField.SetEnabled(enable);
            m_BorderColorField.SetEnabled(enable);
            m_EdgeValueField.SetEnabled(enable);
            m_PostSDFEffectField.SetEnabled(enable);
            m_PostSDFRadiusField.SetEnabled(enable);
            m_ResizeTexturePostSDFField.SetEnabled(enable);
            m_AutoGenerateField.SetEnabled(enable);
            m_OpenRenderDocGenerationField.SetEnabled(enable);
            
            
            UpdateImageSizeRatio();
            OnGenerationParameterChange();
        }

        private void OnModeValueChange(ChangeEvent<Enum> evt)
        {
            UpdateSourcePreviewTexture();
            OnGenerationParameterChange();
        }

        private void OnSideChange(ChangeEvent<string> evt)
        {
            UpdateImageSizeRatio();
            if (evt.newValue == "Inside only") {
                m_ResizeTextureSDFField.style.display = DisplayStyle.None;
                m_ClampBorderField.style.display = DisplayStyle.None;
                m_BorderColorField.style.display = DisplayStyle.None;
            } else {
                m_ResizeTextureSDFField.style.display = DisplayStyle.Flex;
                if (m_ResizeTextureSDFField.value) {
                    m_ClampBorderField.style.display = DisplayStyle.Flex;
                    m_BorderColorField.style.display = m_ClampBorderField.value
                                                    ? DisplayStyle.None
                                                    : DisplayStyle.Flex;
                } else {
                    m_ClampBorderField.style.display = DisplayStyle.None;
                    m_BorderColorField.style.display = DisplayStyle.None;
                }
            }
                m_ResizeTextureSDFField.style.display = (evt.newValue == "Inside only")
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            
            AutoDisplayImageSizeRatio();
            
            OnGenerationParameterChange();
        }

        private void OnInvertChange(ChangeEvent<bool> evt)
        {
            OnGenerationParameterChange();
        }

        private void OnGradientSizePixelChange(ChangeEvent<int> evt)
        {
            if (evt.newValue < 0)
            {
                m_GradientSizePXField.value = 0;
            }
            UpdateImageSizeRatio();
            OnGenerationParameterChange();
        }

        private void OnResizeTextureChange(ChangeEvent<bool> evt)
        {
            m_ClampBorderField.style.display = (evt.newValue)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            m_BorderColorField.style.display = (evt.newValue && !m_ClampBorderField.value)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            UpdateImageSizeRatio();
            AutoDisplayImageSizeRatio();
            OnGenerationParameterChange();
        }

        private void OnClampBorderChange(ChangeEvent<bool> evt)
        {
            m_BorderColorField.style.display = (evt.newValue)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            OnGenerationParameterChange();
        }

        private void OnBorderColorChange(ChangeEvent<Color> evt)
        {
            OnGenerationParameterChange();
        }

        private void OnEdgeValueChange(ChangeEvent<float> evt)
        {
            OnGenerationParameterChange();
        }

        private void OnPostSDFEffectChange(ChangeEvent<string> evt)
        {
            UpdateImageSizeRatio();
            DisplayStyle resizePostSdfDisplay = (evt.newValue == "None")
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            m_PostSDFRadiusField.style.display = resizePostSdfDisplay;
            m_ResizeTexturePostSDFField.style.display = resizePostSdfDisplay;
            
            AutoDisplayImageSizeRatio();

            OnGenerationParameterChange();
        }

        private void OnPostSDFRadiusChange(ChangeEvent<int> evt)
        {
            UpdateImageSizeRatio();
            OnGenerationParameterChange();
        }

        private void AutoDisplayImageSizeRatio()
        {
            DisplayStyle display = IsTextureRescaled
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            m_ImageSizeRatioField.style.display = display;
        }
        
        private void OnAutoGenerateToggle(ChangeEvent<bool> evt)
        {
            DisplayStyle display = m_AutoGenerateField.value
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            m_OpenRenderDocGenerationField.style.display = display;
            m_GenerateButton.style.display = display;
            AutoDisplayRenderDocSuperposition();
            OnGenerationParameterChange();
        }

        private void AutoDisplayRenderDocSuperposition()
        {
            DisplayStyle display = m_AutoGenerateField.value || m_AutoSuperposeField.value
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            m_OpenRenderDocSuperpositionField.style.display = display;
        }

        private void OnSuperReplaceSourceChange(ChangeEvent<bool> evt)
        {
            DisplayStyle display = m_SuperKeepSourceField.value
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            m_SuperNewSourceField.style.display = display;
            OnSuperpositionParameterChange();
        }
        
        private void OnSuperNewSourceValueChange(ChangeEvent<UnityEngine.Object> evt)
        {
            m_SuperNewSourceTexture = (Texture2D) evt.newValue;
            OnSuperpositionParameterChange();
        }

        private void OnSuperSourceOnChange(ChangeEvent<bool> evt)
        {
            OnSuperpositionParameterChange();
        }

        private void OnSuperSourceScalingChange(ChangeEvent<Vector2> evt)
        {
            Vector2 newScaling = evt.newValue;
            if (newScaling.x < 0.00001f || newScaling.y < 0.00001f)
            {
                newScaling.x = Mathf.Max(0.00001f, newScaling.x);
                newScaling.y = Mathf.Max(0.00001f, newScaling.y);
                m_SuperSourceScalingField.value = newScaling;
            }
            OnSuperpositionParameterChange();
        }

        private void OnSuperSourceOffsetChange(ChangeEvent<Vector2> evt)
        {
            OnSuperpositionParameterChange();
        }
        
        private void OnSuperGeneratedOnChange(ChangeEvent<bool> evt)
        {
            OnSuperpositionParameterChange();
        }

        private void OnSuperGeneratedChannelChange(ChangeEvent<string> evt)
        {
            OnSuperpositionParameterChange();
        }

        private void OnSuperBlendingModeChange(ChangeEvent<string> evt)
        {
            OnSuperpositionParameterChange();
        }
        
        private void OnSuperGeneratedColorChange(ChangeEvent<Color> evt)
        {
            OnSuperpositionParameterChange();
        }
        
        private void OnSuperGeneratedIntensityChange(ChangeEvent<float> evt)
        {
            if (evt.newValue < 0)
            {
                m_SuperGeneratedIntensityField.value = 0;
            }
            OnSuperpositionParameterChange();
        }
        
        private void OnAutoSuperposeToggle(ChangeEvent<bool> evt)
        {
            m_SuperposeButton.style.display = m_AutoSuperposeField.value
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            AutoDisplayRenderDocSuperposition();
            OnSuperpositionParameterChange();
        }

        private void OnSourcePreviewChannelsChange(ChangeEvent<bool> evt)
        {
            UpdateSourcePreviewTexture();
        }

        private void OnGeneratedPreviewChannelsChange(ChangeEvent<bool> evt)
        {
            UpdateSDFGeneratedPreviewTexture();
        }

        private void OnSuperpositionPreviewChannelsChange(ChangeEvent<bool> evt)
        {
            UpdateSuperpositionPreviewTexture();
        }
        

        private void OnGenerationParameterChange()
        {
            if (m_AutoGenerateField.value && m_SourceTexture != null)
            {
                GenerateStatic();
            }
            else
            {
                UpdateSDFGeneratedTexture(null);
            }
        }

        private void OnSuperpositionParameterChange()
        {
            if (m_AutoSuperposeField.value && m_GeneratedTexture != null)
            {
                SuperposeSourceAndSDFTextures();
            }
            else
            {
                UpdateSuperpositionTexture(null);
            }
        }

        public void GenerateStatic()
        {
            if (m_SourceTexture == null)
                return;

            // Configure material
            Material material = CoreUtils.CreateEngineMaterial(Shader.Find("Internal/SDFGenerator"));
            if (material == null)
            {
                Debug.LogError("Error during SDF generation: no shader were found at Internal/SDFGenerator, or the material generated was corrupted.");
                return;
            }

            // Generate based on source textures
            material.SetFloat(FeatherID,
                m_GradientSizePXField.value / (float) Mathf.Max(m_SourceTexture.width, m_SourceTexture.height));
            material.SetFloat(EdgeValueID, m_EdgeValueField.value);
            GenerateAsset(material);

            // Cleanup
            DestroyImmediate(material);
        }

        public void GenerateAsset(Material material)
        {
            // Generate SDF data
            bool openRenderDoc = (m_OpenRenderDocGenerationField.value && !m_AutoGenerateField.value);


            UpdateSDFGeneratedTexture(
                Generate(m_SourceTexture, material, (TextureModes)m_ModeField.value,
                (SDFSide)m_SideField.index, m_InvertField.value, IsTextureRescaled,
                m_ClampBorderField.value, m_BorderColorField.value, (PostSDFEffects)m_PostSDFEffectField.index,
                m_PostSDFRadiusField.value, openRenderDoc,
                IsTextureRescaled ? m_GeneratedTextureSize.x : -1,
                IsTextureRescaled ? m_GeneratedTextureSize.y : -1)
                );
            
        }

        public static Texture2D Generate(Texture2D texture, Material material, TextureModes mode,
            SDFSide side, bool invertValues, bool rescaleTexture, bool clampBorder, Color borderColor,
            PostSDFEffects effect, int effectRadius, bool openRenderDoc, int width = -1, int height = -1)
        {
            if (openRenderDoc)
            {
                RenderDoc.BeginCaptureRenderDoc(focusedWindow);
            }
            
            if (width == -1) width = texture.width;
            if (height == -1) height = texture.height;
            TextureFormat format = GetTextureFormat(mode);
            
            Texture2D result = new Texture2D(width, height, format, false);
            result.filterMode = FilterMode.Point;
            Color[] pixels = result.GetPixels();
            for (int c = 3; c >= 0; c--)
            {
                if (((int) mode & (1 << c)) == 0) continue;
                material.SetInteger(ChannelID, c);
                var resultC = GenerateSDF(texture, material, side, invertValues, rescaleTexture, clampBorder, borderColor, effect, effectRadius, width, height);
                var resPx = resultC.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i][c] = resPx[i][0];
                    }
                DestroyImmediate(resultC);
            }

            result.SetPixels(pixels);
            result.Apply();
            
            if (openRenderDoc)
            {
                RenderDoc.EndCaptureRenderDoc(focusedWindow);
            }
            
            return result;
        }

        // Generate a distance field
        // The "material" must be a SDF generating material (ie. the one at UnitySDF/SDFGenerator.mat)
        // Optionally push the results to the specified texture (must be a compatible format)
        public static Texture2D GenerateSDF(Texture2D texture, Material material, SDFSide side,
            bool invertValues, bool rescaleTexture, bool clampBorder, Color borderColor,
            PostSDFEffects effect, int effectRadius, int width = -1, int height = -1)
        {
            if (width == -1) width = texture.width;
            if (height == -1) height = texture.height;

            // Allocate some temporary buffers
            RenderTextureDescriptor stepFormat =
                new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_UNorm, 0, 0);
            stepFormat.sRGB = false;
            RenderTexture target0 = RenderTexture.GetTemporary(stepFormat);
            RenderTexture target1 = RenderTexture.GetTemporary(stepFormat);
            RenderTexture target2 = RenderTexture.GetTemporary(stepFormat);
            target0.filterMode = FilterMode.Point;
            target1.filterMode = FilterMode.Point;
            target2.filterMode = FilterMode.Point;
            target0.wrapMode = TextureWrapMode.Clamp;
            target1.wrapMode = TextureWrapMode.Clamp;
            target2.wrapMode = TextureWrapMode.Clamp;

            int firstPass = material.FindPass("SDFPass");
            int finalPass = material.FindPass("FinalSDFPass");

            if (rescaleTexture)
            {
                int blitCenterPass = material.FindPass("BlitInCenter");
                if (clampBorder)
                {
                    material.EnableKeyword("CLAMP_BORDER");
                }
                else
                {
                    material.DisableKeyword("CLAMP_BORDER");
                    material.SetColor(BorderColorID, borderColor);
                }
                material.SetVector(ScaleID,
                    new Vector2((float) width / texture.width, (float) height / texture.height));
                Graphics.Blit(texture, target0, material, blitCenterPass);
            }
            else
            {
                material.SetVector(ScaleID, new Vector2(2, 2));
            }


            // Detect edges of image
            material.EnableKeyword("FIRSTPASS");
            material.SetFloat(SpreadID, 1);
            Graphics.Blit(rescaleTexture ? target0 : texture, target1, material, firstPass);
            material.DisableKeyword("FIRSTPASS");
            Swap(ref target1, ref target2);

            // Gather nearest edges with varying spread values
            for (int i = 11; i >= 0; i--)
            {
                material.SetFloat(SpreadID, Mathf.Pow(2, i));
                Graphics.Blit(target2, target1, material, firstPass);
                Swap(ref target1, ref target2);
            }

            var resultFormat =
                new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, 0, 0);
            //resultFormat.sRGB = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
            var resultTarget = RenderTexture.GetTemporary(resultFormat);
            resultTarget.wrapMode = TextureWrapMode.Clamp;

            // Compute the final distance from nearest edge value
            switch (side)
            {
                case SDFSide.BothSides:
                    material.EnableKeyword("BOTH_SIDES");
                    break;
                case SDFSide.InsideOnly:
                    material.EnableKeyword("INSIDE_ONLY");
                    break;
                case SDFSide.OutsideOnly:
                    material.EnableKeyword("OUTSIDE_ONLY");
                    break;
                    
            }

            if (invertValues)
            {
                material.EnableKeyword("INVERT_SDF");
            }
            else
            {
                material.DisableKeyword("INVERT_SDF");
            }
            Graphics.Blit(target2, resultTarget, material, finalPass);
            
            // If a post-SDF effect was selected, apply it here
            if (effect != PostSDFEffects.None)
            {
                var resultTarget2 =
                    RenderTexture.GetTemporary(width, height, 0, GraphicsFormat.R8G8B8A8_UNorm);
                resultTarget2.wrapMode = TextureWrapMode.Clamp;
                material.SetFloat(PostRadiusID, effectRadius);
                switch (effect)
                {
                    case PostSDFEffects.Blur:
                        material.EnableKeyword("BLUR");
                        break;
                    case PostSDFEffects.ProgressiveBlur:
                        material.EnableKeyword("PROGRESSIVE_BLUR");
                        break;
                    case PostSDFEffects.MaxKernel:
                        material.EnableKeyword("MAX_KERNEL");
                        break;
                    case PostSDFEffects.MinKernel:
                        material.EnableKeyword("MIN_KERNEL");
                        break;
                }
                Graphics.Blit(resultTarget, resultTarget2, material, material.FindPass("PostSDFEffect"));
                Swap(ref resultTarget, ref resultTarget2);
                RenderTexture.ReleaseTemporary(resultTarget2);
            }
            
            var result = new Texture2D(width, height, TextureFormat.R8, false);

            // Copy to CPU
            result.ReadPixels(new Rect(0, 0, result.width, result.height), 0, 0);

            // Clean up
            RenderTexture.ReleaseTemporary(resultTarget);
            RenderTexture.ReleaseTemporary(target2);
            RenderTexture.ReleaseTemporary(target1);

            return result;
        }

        private void SuperposeSourceAndSDFTextures()
        {
            
            if (m_GeneratedTexture == null)
            {
                return;
            }
            
            bool openRenderDoc = (m_OpenRenderDocSuperpositionField.value && !m_AutoSuperposeField.value);
            
            Texture2D sourceTexture = m_SuperKeepSourceField.value ? m_SourceTexture : m_SuperNewSourceTexture;
            
            Material material = CoreUtils.CreateEngineMaterial(Shader.Find("Internal/SDFGenerator"));
            if (material == null)
            {
                Debug.LogError("Error during sdf superposition: no shader were found at Internal/SDFGenerator, or the material generated was corrupted.");
                return;
            }
            
            material.SetFloat(FeatherID,
                m_GradientSizePXField.value / (float) Mathf.Max(m_SourceTexture.width, m_SourceTexture.height));
            material.SetFloat(EdgeValueID, m_EdgeValueField.value);
            UpdateSuperpositionTexture(SuperposeSourceAndSDFTextures(sourceTexture,
                m_GeneratedTexture, material,
                m_SuperSourceOnField.value, m_SuperSourceScalingField.value, m_SuperSourceOffsetField.value,
                m_SuperGeneratedOnField.value, m_SuperGeneratedChannelField.index, m_SuperBlendingModeField.index,
                m_SuperGeneratedColorField.value, m_SuperGeneratedIntensityField.value, openRenderDoc));
            DestroyImmediate(material);
        }

        private static Texture2D SuperposeSourceAndSDFTextures(Texture2D sourceTexture, Texture2D sdfTexture,
            Material material, bool sourceOn, Vector2 sourceScaling, Vector2 sourceOffset, bool sdfOn, int sdfChannel, int blendingMode, Color sdfColor,
            float sdfIntensity, bool openRenderDoc)
        {
            if (openRenderDoc)
            {
                RenderDoc.BeginCaptureRenderDoc(focusedWindow);
            }
            RenderTextureDescriptor stepFormat =
                new RenderTextureDescriptor(sdfTexture.width, sdfTexture.height, GraphicsFormat.R8G8B8A8_UNorm, 0, 0);
            stepFormat.sRGB = true;
            RenderTexture target = RenderTexture.GetTemporary(stepFormat);
            target.filterMode = FilterMode.Point;
            target.wrapMode = TextureWrapMode.Clamp;

            int superpositionPass = material.FindPass("SuperposeSDF");

            switch (blendingMode)
            {
                case 0:
                    material.EnableKeyword("SOURCE_OVER_SDF");
                    break;
                case 1:
                    material.EnableKeyword("SDF_OVER_SOURCE");
                    break;
                case 2:
                    material.EnableKeyword("MEAN_BLENDING");
                    break;
            }
            
            material.SetTexture(SourceTexID, sourceTexture);
            material.SetTextureScale(SourceTexID, sourceScaling);
            material.SetTextureOffset(SourceTexID, sourceOffset);
            material.SetInteger(ChannelID, sdfChannel);
            material.SetInteger(SuperTexFlagsID, Convert.ToInt32(sdfOn) + 2 * Convert.ToInt32(sourceOn));
            material.SetColor(SuperColorID, sdfColor);
            material.SetFloat(SuperIntensityID, sdfIntensity);

            Graphics.Blit(sdfTexture, target, material, superpositionPass);
            
            
            Texture2D superposedTexture = new Texture2D(sdfTexture.width, sdfTexture.height,
                TextureFormat.RGBA32, false);
            superposedTexture.ReadPixels(new Rect(0, 0, sdfTexture.width, sdfTexture.height), 0, 0);
            RenderTexture.ReleaseTemporary(target);
            
            if (openRenderDoc)
            {
                RenderDoc.EndCaptureRenderDoc(focusedWindow);
            }

            return superposedTexture;
        }

        private static void Swap<T>(ref T v1, ref T v2)
        {
            (v1, v2) = (v2, v1);
        }

        private static TextureFormat GetTextureFormat(TextureModes mode)
        {
            TextureFormat format;
            switch (mode)
            {
                case TextureModes.A:
                    format = TextureFormat.Alpha8;
                    break;
                case TextureModes.R:
                    format = TextureFormat.R8;
                    break;
                default:
                    format = ((mode & TextureModes.A) == 0)
                        ? TextureFormat.RGB24
                        : TextureFormat.RGBA32;
                    break;
            }

            return format;
        }

        private static void SetNewTextureField(Image textureField, Texture2D newTexture)
        {
            Texture2D previousTexture = (Texture2D)textureField.image;
            textureField.image = newTexture;
            if (previousTexture != null)
            {
                DestroyImmediate(previousTexture);
            }
        }

        private bool UpdateSourceTexture(Texture2D newSourceTexture)
        {
            m_SourceTexture = newSourceTexture;
            bool textureNonNull = m_SourceTexture != null;
            
            // Get a readable copy of the source texture.
            if (m_SourceTextureReadable != null)
            {
                DestroyImmediate(m_SourceTextureReadable);
            }

            if (textureNonNull)
            {
                RenderTexture renderTex = RenderTexture.GetTemporary(
                    m_SourceTexture.width,
                    m_SourceTexture.height);
 
                Graphics.Blit(m_SourceTexture, renderTex);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTex;
                m_SourceTextureReadable = new Texture2D(m_SourceTexture.width, m_SourceTexture.height);
                m_SourceTextureReadable.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
                m_SourceTextureReadable.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTex);
            }

            foreach (var previewChannel in m_SourcePreviewChannelsField)
            {
                previewChannel.SetEnabled(textureNonNull);
            }
            
            UpdateSourcePreviewTexture();
            
            return textureNonNull;
        }

        private void UpdateSourcePreviewTexture()
        {
            UpdatePreviewTexture(m_SourceTextureField, m_SourceTextureReadable, m_SourcePreviewChannelsField, TextureFormat.RGBA32);
            SetTextureSizeField(m_SourceSizeField, m_SourceTextureField);
        }

        private bool UpdateSDFGeneratedTexture(Texture2D newSDFGeneratedTexture)
        {
            if (m_GeneratedTexture != null)
                DestroyImmediate(m_GeneratedTexture);
            m_GeneratedTexture = newSDFGeneratedTexture;

            bool textureNonNull = m_GeneratedTexture != null;
            foreach (var previewChannel in m_GeneratedPreviewChannelsField)
            {
                previewChannel.SetEnabled(textureNonNull);
            }
            
            m_SaveButton.SetEnabled(textureNonNull);
            m_SaveAsButton.SetEnabled(textureNonNull);
            
            m_SuperKeepSourceField.SetEnabled(textureNonNull);
            m_SuperNewSourceField.SetEnabled(textureNonNull);
            m_SuperSourceOnField.SetEnabled(textureNonNull);
            m_SuperSourceScalingField.SetEnabled(textureNonNull);
            m_SuperSourceScalingLabel.SetEnabled(textureNonNull);
            m_SuperSourceOffsetField.SetEnabled(textureNonNull);
            m_SuperSourceOffsetLabel.SetEnabled(textureNonNull);
            m_SuperGeneratedOnField.SetEnabled(textureNonNull);
            m_SuperGeneratedChannelField.SetEnabled(textureNonNull);
            m_SuperBlendingModeField.SetEnabled(textureNonNull);
            m_SuperGeneratedColorField.SetEnabled(textureNonNull);
            m_SuperGeneratedIntensityField.SetEnabled(textureNonNull);
            m_AutoSuperposeField.SetEnabled(textureNonNull);
            m_OpenRenderDocSuperpositionField.SetEnabled(textureNonNull);
            m_SuperposeButton.SetEnabled(textureNonNull);
            
            UpdateSDFGeneratedPreviewTexture();
            OnSuperpositionParameterChange();
            return textureNonNull;
        }

        private void UpdateSDFGeneratedPreviewTexture()
        {
            UpdatePreviewTexture(m_GeneratedTextureField, m_GeneratedTexture, m_GeneratedPreviewChannelsField, GetTextureFormat((TextureModes)m_ModeField.value));
            SetTextureSizeField(m_GeneratedSizeField, m_GeneratedTextureField);
        }

        private bool UpdateSuperpositionTexture(Texture2D texture)
        {
            if (m_SuperpositionTexture != null)
            {
                DestroyImmediate(m_SuperpositionTexture);
            }
            m_SuperpositionTexture = texture;
            
            bool textureNonNull = m_SuperpositionTexture != null;
            foreach (var previewChannel in m_SuperpositionPreviewChannelsField)
            {
                previewChannel.SetEnabled(textureNonNull);
            }
            UpdateSuperpositionPreviewTexture();
            return textureNonNull;
        }

        private void UpdateSuperpositionPreviewTexture()
        {
            UpdatePreviewTexture(m_SuperpositionTextureField, m_SuperpositionTexture, m_SuperpositionPreviewChannelsField, m_SuperpositionTexture ? m_SuperpositionTexture.format : TextureFormat.RGBA32);
        }

        private static void UpdatePreviewTexture(Image textureField, Texture2D newTexture, List<ToolbarToggle> previewChannels,
            TextureFormat format)
        {
            if (newTexture == null)
            {
                SetNewTextureField(textureField, null);
                return;
            }
            
            Texture2D previewTexture = new Texture2D(newTexture.width, newTexture.height, format, false);
            previewTexture.filterMode = FilterMode.Point;
            Color previewMask = new Color();
            for (int i = 0; i < 4; ++i)
            {
                previewMask[i] = previewChannels[i].value ? 1 : 0;
            }
            
            Color[] texturePixels = newTexture.GetPixels();
            for (int i = 0; i < texturePixels.Length; ++i)
            {
                texturePixels[i] *= previewMask;
            }
            if (previewMask.a == 0)
            {
                for (int i = 0; i < texturePixels.Length; ++i)
                {
                    texturePixels[i].a = 1;
                }
            }
            previewTexture.SetPixels(texturePixels);
            previewTexture.Apply();
            SetNewTextureField(textureField, previewTexture);
        }

        void SaveAsGeneratedTexture()
        {
            try
            {
                string path = AssetDatabase.GetAssetPath(m_SourceTexture);
                SaveGeneratedTexture(EditorUtility.SaveFilePanelInProject("Save as",
                    Path.GetFileNameWithoutExtension(path) + "_sdf",
                    "png",
                    "Please enter a file name to save the sdf to",
                    Path.GetDirectoryName(path)));
            }
            catch(Exception e)
            {
                Debug.LogException(e);
            }
        }

        void SaveGeneratedTexture()
        {
            string path = AssetDatabase.GetAssetPath(m_SourceTexture);
            path = Path.GetDirectoryName(path) + "\\" + Path.GetFileNameWithoutExtension(path) + "_sdf.png";
            SaveGeneratedTexture(path);
        }

        void SaveGeneratedTexture(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            
            // Generate the new asset
            File.WriteAllBytes(path, m_GeneratedTexture.EncodeToPNG());
            AssetDatabase.Refresh();
            
            // Recreate a new generated texture, as the generated texture needs to be distinct from the new texture file.
            GenerateStatic();
            
            Texture2D sdfTexture = (Texture2D) AssetDatabase.LoadMainAssetAtPath(path);
            Selection.activeObject = sdfTexture;

            // Disable compression and use simple format
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                string sourcePath = AssetDatabase.GetAssetPath(m_SourceTexture);
                TextureImporter sourceImporter = string.IsNullOrEmpty(sourcePath)
                    ? default
                    : AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                if (sourceImporter != null)
                {
                    CopyTextureImporterSettings(importer, sourceImporter);
                    importer.sRGBTexture = false;
                    importer.npotScale = TextureImporterNPOTScale.None;

                    TextureImporterPlatformSettings sourceSettings = sourceImporter.GetDefaultPlatformTextureSettings();
                    sourceSettings.format = (TextureImporterFormat) GetTextureFormat((TextureModes)m_ModeField.value);
                    
                    importer.SetPlatformTextureSettings(sourceSettings);
                    importer.SaveAndReimport();
                }
            }
        }

        public static void CopyTextureImporterSettings(TextureImporter source, TextureImporter target)
        {
            SerializedObject sourceObj = new SerializedObject(source);
            SerializedObject targetObj = new SerializedObject(target);

            SerializedProperty prop = sourceObj.GetIterator();
            while (prop.Next(true))
            {
                targetObj.CopyFromSerializedProperty(prop);
            }

            targetObj.ApplyModifiedProperties();
        }

        private void SetTextureSizeField(Label sizeField, Image textureField)
        {
            sizeField.text = (textureField.image)
                ? $"{textureField.image.width}*{textureField.image.height}px"
                : "No image";
        }
        
        
        private void OnTextureMouseMove(MouseMoveEvent evt, Image textureField)
        {
            
            if (textureField.image != null)
            {
                float textureImageRatio = Math.Max(textureField.image.width / textureField.layout.width,
                    textureField.image.height / textureField.layout.height);
                Vector2 centerPos = (evt.localMousePosition - textureField.layout.size / 2) *
                                    textureImageRatio;
                centerPos.x += textureField.image.width / 2.0f;
                centerPos.y = textureField.image.height / 2.0f - centerPos.y;
                Vector2Int texturePos = Vector2Int.FloorToInt(centerPos);

                if (texturePos.x < 0 || texturePos.y < 0 || texturePos.x >= textureField.image.width ||
                    texturePos.y >= textureField.image.height)
                {
                    UpdatePixelHovered(new Vector2Int(-1, -1), new Color(0, 0, 0, 0));
                }
                else
                {
                    Color currentPixelColor = ((Texture2D) textureField.image).GetPixel(texturePos.x, texturePos.y);
                    UpdatePixelHovered(texturePos, currentPixelColor);
                }
            }
        }

        private void OnTextureMouseLeave(MouseLeaveEvent evt)
        {
            
            UpdatePixelHovered(new Vector2Int(-1, -1), new Color(0, 0, 0, 0));
        }

        private void UpdatePixelHovered(Vector2Int position, Color color)
        {
            m_PixelHoveredPositionField.value = position;
            m_PixelHoveredColorField.value = color;
            for (int i = 0; i < 4; ++i)
            {
                m_PixelHoveredValuesField[i].value = color[i];
            }
        }
    }
}