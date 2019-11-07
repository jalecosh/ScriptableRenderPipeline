using UnityEngine.Rendering.HighDefinition;
using System;

using Object = UnityEngine.Object;

namespace UnityEditor.Rendering.HighDefinition
{
    class SerializedFrameSettings
    {
        SerializedProperty root;
        SerializedProperty bitDatas;
        SerializedProperty overrides;
        public SerializedProperty lodBias;
        public SerializedProperty lodBiasMode;
        public SerializedProperty lodBiasQualityLevel;
        public SerializedProperty maximumLODLevel;
        public SerializedProperty maximumLODLevelMode;
        public SerializedProperty maximumLODLevelQualityLevel;
        public SerializedProperty materialQuality;

        public SerializedObject serializedObject => bitDatas.serializedObject;

        public LitShaderMode? litShaderMode
        {
            get
            {
                bool? val = IsEnabled(FrameSettingsField.LitShaderMode);
                return val == null
                    ? (LitShaderMode?)null
                    : val.Value == true
                        ? LitShaderMode.Deferred
                        : LitShaderMode.Forward;
            }
            set => SetEnabled(FrameSettingsField.LitShaderMode, value == LitShaderMode.Deferred);
        }

        public bool? IsEnabled(FrameSettingsField field)
            => HaveMultipleValue(field) ? (bool?)null : bitDatas.GetBitArrayAt((uint)field);
        public void SetEnabled(FrameSettingsField field, bool value)
            => bitDatas.SetBitArrayAt((uint)field, value);
        public bool HaveMultipleValue(FrameSettingsField field)
            => bitDatas.HasBitArrayMultipleDifferentValue((uint)field);

        public bool GetOverrides(FrameSettingsField field)
            => overrides?.GetBitArrayAt((uint)field) ?? false; //rootOverride can be null in case of hdrpAsset defaults
        public void SetOverrides(FrameSettingsField field, bool value)
            => overrides?.SetBitArrayAt((uint)field, value); //rootOverride can be null in case of hdrpAsset defaults
        public bool HaveMultipleOverride(FrameSettingsField field)
            => overrides?.HasBitArrayMultipleDifferentValue((uint)field) ?? false;

        ref FrameSettings GetData(Object obj)
        {
            if (obj is HDAdditionalCameraData)
                return ref (obj as HDAdditionalCameraData).renderingPathCustomFrameSettings;
            if (obj is HDProbe)
                return ref (obj as HDProbe).frameSettings;
            if (obj is HDRenderPipelineAsset)
                switch (HDRenderPipelineUI.selectedFrameSettings)
                {
                    case HDRenderPipelineUI.SelectedFrameSettings.Camera:
                        return ref (obj as HDRenderPipelineAsset).GetDefaultFrameSettings(FrameSettingsRenderType.Camera);
                    case HDRenderPipelineUI.SelectedFrameSettings.BakedOrCustomReflection:
                        return ref (obj as HDRenderPipelineAsset).GetDefaultFrameSettings(FrameSettingsRenderType.CustomOrBakedReflection);
                    case HDRenderPipelineUI.SelectedFrameSettings.RealtimeReflection:
                        return ref (obj as HDRenderPipelineAsset).GetDefaultFrameSettings(FrameSettingsRenderType.RealtimeReflection);
                    default:
                        throw new System.ArgumentException("Unknown kind of HDRenderPipelineUI.SelectedFrameSettings");
                }
            throw new System.ArgumentException("Unknown kind of object");
        }

        FrameSettingsOverrideMask? GetMask(Object obj)
        {
            if (obj is HDAdditionalCameraData)
                return (obj as HDAdditionalCameraData).renderingPathCustomFrameSettingsOverrideMask;
            if (obj is HDProbe)
                return (obj as HDProbe).frameSettingsOverrideMask;
            if (obj is HDRenderPipelineAsset)
                return null;
            throw new System.ArgumentException("Unknown kind of object");
        }

        public SerializedFrameSettings(SerializedProperty rootData, SerializedProperty rootOverride)
        {
            root = rootData;
            bitDatas = rootData.FindPropertyRelative("bitDatas");
            overrides = rootOverride?.FindPropertyRelative("mask");  //rootOverride can be null in case of hdrpAsset defaults
            lodBias = rootData.FindPropertyRelative("lodBias");
            lodBiasMode = rootData.FindPropertyRelative("lodBiasMode");
            lodBiasQualityLevel = rootData.FindPropertyRelative("lodBiasQualityLevel");
            maximumLODLevel = rootData.FindPropertyRelative("maximumLODLevel");
            maximumLODLevelMode = rootData.FindPropertyRelative("maximumLODLevelMode");
            maximumLODLevelQualityLevel = rootData.FindPropertyRelative("maximumLODLevelQualityLevel");
            materialQuality = rootData.Find((FrameSettings s) => s.materialQuality);
        }

        public struct TitleDrawingScope : IDisposable
        {
            bool hasOverride;

            public TitleDrawingScope(UnityEngine.Rect rect, UnityEngine.GUIContent label, SerializedFrameSettings serialized)
            {
                EditorGUI.BeginProperty(rect, label, serialized.root);

                hasOverride = serialized.overrides != null;
                if (hasOverride)
                    EditorGUI.BeginProperty(rect, label, serialized.overrides);
            }

            void IDisposable.Dispose()
            {
                EditorGUI.EndProperty();
                if (hasOverride)
                    EditorGUI.EndProperty();
            }
        }
    }
}
