using UnityEngine;
using System;
using System.Linq;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEditorInternal.VR;
using UnityEditor.SceneManagement;
using System.Linq.Expressions;
using System.Reflection;
using System.Collections.Generic;
using System.IO;

namespace UnityEditor.Rendering.HighDefinition
{
    enum InclusiveScope
    {
        HDRPAsset = 1 << 0,
        HDRP = HDRPAsset | 1 << 1, //HDRPAsset is inside HDRP and will be indented
        VR = 1 << 2,
        DXR = 1 << 3,
    }

    static class InclusiveScopeExtention
    {
        public static bool Contains(this InclusiveScope thisScope, InclusiveScope scope)
            => ((~thisScope) & scope) == 0;
    }

    partial class HDWizard
    {
        struct Entry
        {
            public delegate bool Checker();
            public delegate void Fixer(bool fromAsync);

            public readonly InclusiveScope scope;
            public readonly Style.ConfigStyle configStyle;
            public readonly Checker check;
            public readonly Fixer fix;
            public readonly int indent;
            public Entry(InclusiveScope scope, Style.ConfigStyle configStyle, Checker check, Fixer fix)
            {
                this.scope = scope;
                this.configStyle = configStyle;
                this.check = check;
                this.fix = fix;
                indent = scope == InclusiveScope.HDRPAsset ? 1 : 0;
            }
        }

        //In order to simplify how to had elements in the Wizard configuration checker,
        //now you only need to add your new checks in this array at the right position.
        //Both "Fix All" button and UI drawing will use it.
        //- Due to this automation, your fix method should have a bool in paramater even
        //if you dont need to behave differently regarding if you are sync or async.
        //for clarity, in those case, call it fromAsyncUnused.
        //- All the labels are now grouped in one structure for convenience. See Style.
        //- Indentation is computed in Entry if you use certain subscope.
        Entry[] m_Entries;
        Entry[] entries
        {
            get
            {
                // due to functor, cannot static link directly in an array and need lazy init
                if (m_Entries == null)
                    m_Entries = new[]
                    {
                        new Entry(InclusiveScope.HDRP, Style.hdrpColorSpace, IsColorSpaceCorrect, FixColorSpace),
                        new Entry(InclusiveScope.HDRP, Style.hdrpLightmapEncoding, IsLightmapCorrect, FixLightmap),
                        new Entry(InclusiveScope.HDRP, Style.hdrpShadowmask, IsShadowmaskCorrect, FixShadowmask),
                        new Entry(InclusiveScope.HDRP, Style.hdrpAsset, IsHdrpAssetCorrect, FixHdrpAsset),
                        new Entry(InclusiveScope.HDRPAsset, Style.hdrpAssetAssigned, IsHdrpAssetUsedCorrect, FixHdrpAssetUsed),
                        new Entry(InclusiveScope.HDRPAsset, Style.hdrpAssetRuntimeResources, IsHdrpAssetRuntimeResourcesCorrect, FixHdrpAssetRuntimeResources),
                        new Entry(InclusiveScope.HDRPAsset, Style.hdrpAssetEditorResources, IsHdrpAssetEditorResourcesCorrect, FixHdrpAssetEditorResources),
                        new Entry(InclusiveScope.HDRPAsset, Style.hdrpBatcher, IsSRPBatcherCorrect, FixSRPBatcher),
                        new Entry(InclusiveScope.HDRPAsset, Style.hdrpAssetDiffusionProfile, IsHdrpAssetDiffusionProfileCorrect, FixHdrpAssetDiffusionProfile),
                        new Entry(InclusiveScope.HDRP, Style.hdrpScene, IsDefaultSceneCorrect, FixDefaultScene),
                        new Entry(InclusiveScope.HDRP, Style.hdrpVolumeProfile, IsDefaultVolumeProfileAssigned, FixDefaultVolumeProfileAssigned),

                        new Entry(InclusiveScope.VR, Style.vrActivated, IsVRSupportedForCurrentBuildTargetGroupCorrect, FixVRSupportedForCurrentBuildTargetGroup),

                        new Entry(InclusiveScope.DXR, Style.dxrAutoGraphicsAPI, IsDXRAutoGraphicsAPICorrect, FixDXRAutoGraphicsAPI),
                        new Entry(InclusiveScope.DXR, Style.dxrD3D12, IsDXRDirect3D12Correct, FixDXRDirect3D12),
                        new Entry(InclusiveScope.DXR, Style.dxrStaticBatching, IsDXRStaticBatchingCorrect, FixDXRStaticBatching),
                        new Entry(InclusiveScope.DXR, Style.dxrScreenSpaceShadow, IsDXRScreenSpaceShadowCorrect, FixDXRScreenSpaceShadow),
                        new Entry(InclusiveScope.DXR, Style.dxrActivated, IsDXRActivationCorrect, FixDXRActivation),
                        new Entry(InclusiveScope.DXR, Style.dxrResources, IsDXRAssetCorrect, FixDXRAsset),
                        new Entry(InclusiveScope.DXR, Style.dxrShaderConfig, IsDXRShaderConfigCorrect, FixDXRShaderConfig),
                        new Entry(InclusiveScope.DXR, Style.dxrScene, IsDXRDefaultSceneCorrect, FixDXRDefaultScene),
                    };
                return m_Entries;
            }
        }
        
        // Utility that grab all check within the scope or in sub scope included and check if everything is correct
        bool IsAllEntryCorrectInScope(InclusiveScope scope)
        {
            IEnumerable<Entry.Checker> checks = entries.Where(e => scope.Contains(e.scope)).Select(e => e.check);
            if (checks.Count() == 0)
                return true;

            IEnumerator<Entry.Checker> enumerator = checks.GetEnumerator();
            enumerator.MoveNext();
            bool result = enumerator.Current();
            if (enumerator.MoveNext())
                for (; result && enumerator.MoveNext();)
                    result &= enumerator.Current();
            return result;
        }

        // Utility that grab all check and fix within the scope or in sub scope included and performe fix if check return incorrect
        void FixAllEntryInScope(InclusiveScope scope)
        {
            IEnumerable<(Entry.Checker, Entry.Fixer)> pairs = entries.Where(e => scope.Contains(e.scope)).Select(e => (e.check, e.fix));
            if (pairs.Count() == 0)
                return;

            foreach ((Entry.Checker check, Entry.Fixer fix) in pairs)
                m_Fixer.Add(() =>
                {
                    if (!check())
                        fix(fromAsync: true);
                });
        }

        #region REFLECTION

        //reflect internal legacy enum
        enum LightmapEncodingQualityCopy
        {
            Low = 0,
            Normal = 1,
            High = 2
        }

        static Func<BuildTargetGroup, LightmapEncodingQualityCopy> GetLightmapEncodingQualityForPlatformGroup;
        static Action<BuildTargetGroup, LightmapEncodingQualityCopy> SetLightmapEncodingQualityForPlatformGroup;
        static Func<BuildTarget> CalculateSelectedBuildTarget;
        static Func<BuildTarget, GraphicsDeviceType[]> GetSupportedGraphicsAPIs;
        static Func<BuildTarget, bool> WillEditorUseFirstGraphicsAPI;
        static Action RequestCloseAndRelaunchWithCurrentArguments;
        static Func<BuildTarget, bool> GetStaticBatching;
        static Action<BuildTarget, bool> SetStaticBatching;

        static void LoadReflectionMethods()
        {
            Type playerSettingsType = typeof(PlayerSettings);
            Type playerSettingsEditorType = playerSettingsType.Assembly.GetType("UnityEditor.PlayerSettingsEditor");
            Type lightEncodingQualityType = playerSettingsType.Assembly.GetType("UnityEditor.LightmapEncodingQuality");
            Type editorUserBuildSettingsUtilsType = playerSettingsType.Assembly.GetType("UnityEditor.EditorUserBuildSettingsUtils");
            var qualityVariable = Expression.Variable(lightEncodingQualityType, "quality_internal");
            var buildTargetVariable = Expression.Variable(typeof(BuildTarget), "platform");
            var staticBatchingVariable = Expression.Variable(typeof(int), "staticBatching");
            var dynamicBatchingVariable = Expression.Variable(typeof(int), "DynamicBatching");
            var staticBatchingParameter = Expression.Parameter(typeof(bool), "staticBatching");
            var buildTargetGroupParameter = Expression.Parameter(typeof(BuildTargetGroup), "platformGroup");
            var buildTargetParameter = Expression.Parameter(typeof(BuildTarget), "platform");
            var qualityParameter = Expression.Parameter(typeof(LightmapEncodingQualityCopy), "quality");
            var getLightmapEncodingQualityForPlatformGroupInfo = playerSettingsType.GetMethod("GetLightmapEncodingQualityForPlatformGroup", BindingFlags.Static | BindingFlags.NonPublic);
            var setLightmapEncodingQualityForPlatformGroupInfo = playerSettingsType.GetMethod("SetLightmapEncodingQualityForPlatformGroup", BindingFlags.Static | BindingFlags.NonPublic);
            var calculateSelectedBuildTargetInfo = editorUserBuildSettingsUtilsType.GetMethod("CalculateSelectedBuildTarget", BindingFlags.Static | BindingFlags.Public);
            var getSupportedGraphicsAPIsInfo = playerSettingsType.GetMethod("GetSupportedGraphicsAPIs", BindingFlags.Static | BindingFlags.NonPublic);
            var getStaticBatchingInfo = playerSettingsType.GetMethod("GetBatchingForPlatform", BindingFlags.Static | BindingFlags.NonPublic);
            var setStaticBatchingInfo = playerSettingsType.GetMethod("SetBatchingForPlatform", BindingFlags.Static | BindingFlags.NonPublic);
            var willEditorUseFirstGraphicsAPIInfo = playerSettingsEditorType.GetMethod("WillEditorUseFirstGraphicsAPI", BindingFlags.Static | BindingFlags.NonPublic);
            var requestCloseAndRelaunchWithCurrentArgumentsInfo = typeof(EditorApplication).GetMethod("RequestCloseAndRelaunchWithCurrentArguments", BindingFlags.Static | BindingFlags.NonPublic);
            var getLightmapEncodingQualityForPlatformGroupBlock = Expression.Block(
                new[] { qualityVariable },
                Expression.Assign(qualityVariable, Expression.Call(getLightmapEncodingQualityForPlatformGroupInfo, buildTargetGroupParameter)),
                Expression.Convert(qualityVariable, typeof(LightmapEncodingQualityCopy))
                );
            var setLightmapEncodingQualityForPlatformGroupBlock = Expression.Block(
                new[] { qualityVariable },
                Expression.Assign(qualityVariable, Expression.Convert(qualityParameter, lightEncodingQualityType)),
                Expression.Call(setLightmapEncodingQualityForPlatformGroupInfo, buildTargetGroupParameter, qualityVariable)
                );
            var getStaticBatchingBlock = Expression.Block(
                new[] { staticBatchingVariable, dynamicBatchingVariable },
                Expression.Call(getStaticBatchingInfo, buildTargetParameter, staticBatchingVariable, dynamicBatchingVariable),
                Expression.Equal(staticBatchingVariable, Expression.Constant(1))
                );
            var setStaticBatchingBlock = Expression.Block(
                new[] { staticBatchingVariable, dynamicBatchingVariable },
                Expression.Call(getStaticBatchingInfo, buildTargetParameter, staticBatchingVariable, dynamicBatchingVariable),
                Expression.Call(setStaticBatchingInfo, buildTargetParameter, Expression.Convert(staticBatchingParameter, typeof(int)), dynamicBatchingVariable)
                );
            var getLightmapEncodingQualityForPlatformGroupLambda = Expression.Lambda<Func<BuildTargetGroup, LightmapEncodingQualityCopy>>(getLightmapEncodingQualityForPlatformGroupBlock, buildTargetGroupParameter);
            var setLightmapEncodingQualityForPlatformGroupLambda = Expression.Lambda<Action<BuildTargetGroup, LightmapEncodingQualityCopy>>(setLightmapEncodingQualityForPlatformGroupBlock, buildTargetGroupParameter, qualityParameter);
            var calculateSelectedBuildTargetLambda = Expression.Lambda<Func<BuildTarget>>(Expression.Call(null, calculateSelectedBuildTargetInfo));
            var getSupportedGraphicsAPIsLambda = Expression.Lambda<Func<BuildTarget, GraphicsDeviceType[]>>(Expression.Call(null, getSupportedGraphicsAPIsInfo, buildTargetParameter), buildTargetParameter);
            var getStaticBatchingLambda = Expression.Lambda<Func<BuildTarget, bool>>(getStaticBatchingBlock, buildTargetParameter);
            var setStaticBatchingLambda = Expression.Lambda<Action<BuildTarget, bool>>(setStaticBatchingBlock, buildTargetParameter, staticBatchingParameter);
            var willEditorUseFirstGraphicsAPILambda = Expression.Lambda<Func<BuildTarget, bool>>(Expression.Call(null, willEditorUseFirstGraphicsAPIInfo, buildTargetParameter), buildTargetParameter);
            var requestCloseAndRelaunchWithCurrentArgumentsLambda = Expression.Lambda<Action>(Expression.Call(null, requestCloseAndRelaunchWithCurrentArgumentsInfo));
            GetLightmapEncodingQualityForPlatformGroup = getLightmapEncodingQualityForPlatformGroupLambda.Compile();
            SetLightmapEncodingQualityForPlatformGroup = setLightmapEncodingQualityForPlatformGroupLambda.Compile();
            CalculateSelectedBuildTarget = calculateSelectedBuildTargetLambda.Compile();
            GetSupportedGraphicsAPIs = getSupportedGraphicsAPIsLambda.Compile();
            GetStaticBatching = getStaticBatchingLambda.Compile();
            SetStaticBatching = setStaticBatchingLambda.Compile();
            WillEditorUseFirstGraphicsAPI = willEditorUseFirstGraphicsAPILambda.Compile();
            RequestCloseAndRelaunchWithCurrentArguments = requestCloseAndRelaunchWithCurrentArgumentsLambda.Compile();
        }

        #endregion

        #region Queue

        class QueuedLauncher
        {
            Queue<Action> m_Queue = new Queue<Action>();
            bool m_Running = false;
            bool m_StopRequested = false;

            public void Stop() => m_StopRequested = true;

            void Start()
            {
                m_Running = true;
                EditorApplication.update += Run;
            }

            void End()
            {
                EditorApplication.update -= Run;
                m_Running = false;
            }

            void Run()
            {
                if (m_StopRequested)
                {
                    m_Queue.Clear();
                    m_StopRequested = false;
                }
                if (m_Queue.Count > 0)
                    m_Queue.Dequeue()?.Invoke();
                else
                    End();
            }

            public void Add(Action function)
            {
                m_Queue.Enqueue(function);
                if (!m_Running)
                    Start();
            }

            public void Add(params Action[] functions)
            {
                foreach (Action function in functions)
                    Add(function);
            }
        }
        QueuedLauncher m_Fixer = new QueuedLauncher();

        #endregion
        
        #region HDRP_FIXES

        bool IsHDRPAllCorrect()
            => IsAllEntryCorrectInScope(InclusiveScope.HDRP);
        void FixHDRPAll()
            => FixAllEntryInScope(InclusiveScope.HDRP);

        bool IsHdrpAssetCorrect()
            => IsAllEntryCorrectInScope(InclusiveScope.HDRPAsset);
        void FixHdrpAsset(bool fromAsyncUnused)
            => FixAllEntryInScope(InclusiveScope.HDRPAsset);

        bool IsColorSpaceCorrect()
            => PlayerSettings.colorSpace == ColorSpace.Linear;
        void FixColorSpace(bool fromAsyncUnused)
            => PlayerSettings.colorSpace = ColorSpace.Linear;

        bool IsLightmapCorrect()
        {
            // Shame alert: plateform supporting Encodement are partly hardcoded
            // in editor (Standalone) and for the other part, it is all in internal code.
            return GetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.Standalone) == LightmapEncodingQualityCopy.High
                && GetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.Android) == LightmapEncodingQualityCopy.High
                && GetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.Lumin) == LightmapEncodingQualityCopy.High
                && GetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.WSA) == LightmapEncodingQualityCopy.High;
        }
        void FixLightmap(bool fromAsyncUnused)
        {
            SetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.Standalone, LightmapEncodingQualityCopy.High);
            SetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.Android, LightmapEncodingQualityCopy.High);
            SetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.Lumin, LightmapEncodingQualityCopy.High);
            SetLightmapEncodingQualityForPlatformGroup(BuildTargetGroup.WSA, LightmapEncodingQualityCopy.High);
        }

        bool IsShadowmaskCorrect()
            //QualitySettings.SetQualityLevel.set quality is too costy to be use at frame
            => QualitySettings.shadowmaskMode == ShadowmaskMode.DistanceShadowmask;
        void FixShadowmask(bool fromAsyncUnused)
        {
            int currentQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; ++i)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
            }
            QualitySettings.SetQualityLevel(currentQuality, applyExpensiveChanges: false);
        }

        bool IsHdrpAssetUsedCorrect()
            => GraphicsSettings.renderPipelineAsset != null && GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset;
        void FixHdrpAssetUsed(bool fromAsync)
        {
            if (ObjectSelector.opened)
                return;
            CreateOrLoad<HDRenderPipelineAsset>(fromAsync
                ? () => m_Fixer.Stop()
            : (Action)null,
                asset => GraphicsSettings.renderPipelineAsset = asset);
        }

        bool IsHdrpAssetRuntimeResourcesCorrect()
            => IsHdrpAssetUsedCorrect()
            && HDRenderPipeline.defaultAsset.renderPipelineResources != null;
        void FixHdrpAssetRuntimeResources(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);
            HDRenderPipeline.defaultAsset.renderPipelineResources
                = AssetDatabase.LoadAssetAtPath<RenderPipelineResources>(HDUtils.GetHDRenderPipelinePath() + "Runtime/RenderPipelineResources/HDRenderPipelineResources.asset");
            ResourceReloader.ReloadAllNullIn(HDRenderPipeline.defaultAsset.renderPipelineResources, HDUtils.GetHDRenderPipelinePath());
        }

        bool IsHdrpAssetEditorResourcesCorrect()
            => IsHdrpAssetUsedCorrect()
            && HDRenderPipeline.defaultAsset.renderPipelineEditorResources != null;
        void FixHdrpAssetEditorResources(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);
            HDRenderPipeline.defaultAsset.renderPipelineEditorResources
                = AssetDatabase.LoadAssetAtPath<HDRenderPipelineEditorResources>(HDUtils.GetHDRenderPipelinePath() + "Editor/RenderPipelineResources/HDRenderPipelineEditorResources.asset");
            ResourceReloader.ReloadAllNullIn(HDRenderPipeline.defaultAsset.renderPipelineEditorResources, HDUtils.GetHDRenderPipelinePath());
        }

        bool IsSRPBatcherCorrect()
            => IsHdrpAssetUsedCorrect() && HDRenderPipeline.currentAsset.enableSRPBatcher;
        void FixSRPBatcher(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);

            var hdAsset = HDRenderPipeline.currentAsset;
            hdAsset.enableSRPBatcher = true;
            EditorUtility.SetDirty(hdAsset);
        }

        bool IsHdrpAssetDiffusionProfileCorrect()
        {
            var profileList = HDRenderPipeline.defaultAsset?.diffusionProfileSettingsList;
            return IsHdrpAssetUsedCorrect() && profileList.Length != 0 && profileList.Any(p => p != null);
        }
        void FixHdrpAssetDiffusionProfile(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);

            var hdAsset = HDRenderPipeline.currentAsset;
            hdAsset.diffusionProfileSettingsList = hdAsset.renderPipelineEditorResources.defaultDiffusionProfileSettingsList;
            EditorUtility.SetDirty(hdAsset);
        }

        bool IsDefaultSceneCorrect()
            => HDProjectSettings.defaultScenePrefab != null;
        void FixDefaultScene(bool fromAsync)
        {
            if (ObjectSelector.opened)
                return;
            CreateOrLoadDefaultScene(fromAsync ? () => m_Fixer.Stop() : (Action)null, scene => HDProjectSettings.defaultScenePrefab = scene, forDXR: false);
            m_DefaultScene.SetValueWithoutNotify(HDProjectSettings.defaultScenePrefab);
        }

        bool IsDefaultVolumeProfileAssigned()
        {
            if (!IsHdrpAssetUsedCorrect())
                return false;

            var hdAsset = HDRenderPipeline.currentAsset;
            return hdAsset.defaultVolumeProfile != null && !hdAsset.defaultVolumeProfile.Equals(null);
        }
        void FixDefaultVolumeProfileAssigned(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);

            var hdAsset = HDRenderPipeline.currentAsset;
            EditorDefaultSettings.GetOrAssignDefaultVolumeProfile(hdAsset);
            EditorUtility.SetDirty(hdAsset);
        }

        #endregion

        #region HDRP_VR_FIXES

        bool IsVRAllCorrect()
            => IsAllEntryCorrectInScope(InclusiveScope.VR);
        void FixVRAll()
            => FixAllEntryInScope(InclusiveScope.VR);

        bool IsVRSupportedForCurrentBuildTargetGroupCorrect()
            => VREditor.GetVREnabledOnTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        void FixVRSupportedForCurrentBuildTargetGroup(bool fromAsyncUnused)
            => VREditor.SetVREnabledOnTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup, true);

        #endregion

        #region HDRP_DXR_FIXES

        bool IsDXRAllCorrect()
            => IsAllEntryCorrectInScope(InclusiveScope.DXR);

        void FixDXRAll()
            => FixAllEntryInScope(InclusiveScope.DXR);

        bool IsDXRAutoGraphicsAPICorrect()
            => !PlayerSettings.GetUseDefaultGraphicsAPIs(CalculateSelectedBuildTarget());
        void FixDXRAutoGraphicsAPI(bool fromAsyncUnused)
            => PlayerSettings.SetUseDefaultGraphicsAPIs(CalculateSelectedBuildTarget(), false);

        bool IsDXRDirect3D12Correct()
            => PlayerSettings.GetGraphicsAPIs(CalculateSelectedBuildTarget()).FirstOrDefault() == GraphicsDeviceType.Direct3D12;
        void FixDXRDirect3D12(bool fromAsync)
        {
            if (GetSupportedGraphicsAPIs(CalculateSelectedBuildTarget()).Contains(GraphicsDeviceType.Direct3D12))
            {
                var buidTarget = CalculateSelectedBuildTarget();
                if (PlayerSettings.GetGraphicsAPIs(buidTarget).Contains(GraphicsDeviceType.Direct3D12))
                {
                    PlayerSettings.SetGraphicsAPIs(
                        buidTarget,
                        new[] { GraphicsDeviceType.Direct3D12 }
                            .Concat(
                                PlayerSettings.GetGraphicsAPIs(buidTarget)
                                    .Where(x => x != GraphicsDeviceType.Direct3D12))
                            .ToArray());
                }
                else
                {
                    PlayerSettings.SetGraphicsAPIs(
                        buidTarget,
                        new[] { GraphicsDeviceType.Direct3D12 }
                            .Concat(PlayerSettings.GetGraphicsAPIs(buidTarget))
                            .ToArray());
                }
                if (fromAsync)
                    m_Fixer.Stop();
                ChangedFirstGraphicAPI(buidTarget);
            }
        }

        void ChangedFirstGraphicAPI(BuildTarget target)
        {
            //It seams that the 64 version is not check for restart for a strange reason
            if (target == BuildTarget.StandaloneWindows64)
                target = BuildTarget.StandaloneWindows;

            // If we're changing the first API for relevant editor, this will cause editor to switch: ask for scene save & confirmation
            if (WillEditorUseFirstGraphicsAPI(target))
            {
                if (EditorUtility.DisplayDialog("Changing editor graphics device",
                    "You've changed the active graphics API. This requires a restart of the Editor. After restarting finish fixing DXR configuration by launching the wizard again.",
                    "Restart Editor", "Not now"))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        RequestCloseAndRelaunchWithCurrentArguments();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        bool IsDXRAssetCorrect()
            => HDRenderPipeline.defaultAsset != null
            && HDRenderPipeline.defaultAsset.renderPipelineRayTracingResources != null;
        void FixDXRAsset(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);
            HDRenderPipeline.defaultAsset.renderPipelineRayTracingResources
                = AssetDatabase.LoadAssetAtPath<HDRenderPipelineRayTracingResources>(HDUtils.GetHDRenderPipelinePath() + "Runtime/RenderPipelineResources/HDRenderPipelineRayTracingResources.asset");
            ResourceReloader.ReloadAllNullIn(HDRenderPipeline.defaultAsset.renderPipelineRayTracingResources, HDUtils.GetHDRenderPipelinePath());
        }
        
        static void CopyFolder(string sourceFolder, string destFolder)
        {
            if (!Directory.Exists(destFolder))
                Directory.CreateDirectory(destFolder);
            string[] files = Directory.GetFiles(sourceFolder);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string dest = Path.Combine(destFolder, name);
                File.Copy(file, dest);
            }
            string[] folders = Directory.GetDirectories(sourceFolder);
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                string dest = Path.Combine(destFolder, name);
                CopyFolder(folder, dest);
            }
        }

        static PackageManager.Requests.AddRequest s_AddRequest = null;
        bool IsDXRShaderConfigCorrect()
        {
            // To be accurate, to check that the config package is doing the right thing is to make sure we are referencing in the manifest a specific folder (defined by us)
            // and to check if the ray tracing shader config value is set to 1 in the .cs.hlsl file. Because doing that is a bit of an over check, let's supposed that if
            // we are pointing to our custom location, it means it has been previously set up.
            StreamReader streamReader = new StreamReader("Packages/manifest.json");
            while (!streamReader.EndOfStream)
            {
                string line = streamReader.ReadLine();
                if (line == "    \"com.unity.render-pipelines.high-definition-config\": \"file:../LocalPackages/com.unity.render-pipelines.high-definition-config\",")
                {
                    streamReader.Close();
                    return true;
                }
            }
            streamReader.Close();
            return false;
        }
        void FixDXRShaderConfig(bool fromAsyncUnused)
        {
            // Make sure to delete the previous local package (if any)
            if (Directory.Exists("LocalPackages/com.unity.render-pipelines.high-definition-config"))
            {
                Directory.Delete("LocalPackages/com.unity.render-pipelines.high-definition-config", true);
            }

            // First let's try to grab the cached version of pack-man
            bool found = false;
            string packageCache = Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%");
            var directories = Directory.GetDirectories(packageCache + "/Unity/cache/packages/packages.unity.com");
            for (int dirIdx = 0; dirIdx < directories.Length; ++dirIdx)
            {
                if (directories[dirIdx].Contains("com.unity.render-pipelines.high-definition-config"))
                {
                    CopyFolder(directories[dirIdx], "LocalPackages/com.unity.render-pipelines.high-definition-config");
                    found = true;
                    break;
                }
            }

            // If we were not able to find it, we can't solve it
            if (!found)
                return;

            // Then we want to make sure that the shader config value is set to 1
            string[] lines = System.IO.File.ReadAllLines("LocalPackages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl");
            for (int lineIdx = 0; lineIdx < lines.Length; ++lineIdx)
            {
                if (lines[lineIdx].Contains("SHADEROPTIONS_RAYTRACING"))
                {
                    lines[lineIdx] = "#define SHADEROPTIONS_RAYTRACING (1)";
                    break;
                }
            }
            File.WriteAllLines("LocalPackages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl", lines);

            // Replace the path of this package using the packman API
            s_AddRequest = PackageManager.Client.Add("file:../LocalPackages/com.unity.render-pipelines.high-definition-config");
            EditorApplication.update += RequestUpdate;
        }

        void RequestUpdate()
        {
            if (s_AddRequest != null)
            {
                if (s_AddRequest.Status == PackageManager.StatusCode.Success || s_AddRequest.Status == PackageManager.StatusCode.Failure)
                {
                    if (s_AddRequest.Status == PackageManager.StatusCode.Failure)
                    {
                        Debug.LogError("Failed to update HDRP Config Package");
                        Debug.LogError(s_AddRequest.Error.message);
                    }
                    s_AddRequest = null;
                    EditorApplication.update -= RequestUpdate;
                }
            }
        }

        bool IsDXRScreenSpaceShadowCorrect()
            => HDRenderPipeline.currentAsset != null
            && HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams.supportScreenSpaceShadows;
        void FixDXRScreenSpaceShadow(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);
            //as property returning struct make copy, use serializedproperty to modify it
            var serializedObject = new SerializedObject(HDRenderPipeline.currentAsset);
            var propertySupportScreenSpaceShadow = serializedObject.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.supportScreenSpaceShadows");
            propertySupportScreenSpaceShadow.boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        bool IsDXRStaticBatchingCorrect()
            => !GetStaticBatching(CalculateSelectedBuildTarget());
        void FixDXRStaticBatching(bool fromAsyncUnused)
            => SetStaticBatching(CalculateSelectedBuildTarget(), false);

        bool IsDXRActivationCorrect()
            => HDRenderPipeline.currentAsset != null
            && HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.supportRayTracing;
        void FixDXRActivation(bool fromAsyncUnused)
        {
            if (!IsHdrpAssetUsedCorrect())
                FixHdrpAssetUsed(fromAsync: false);
            //as property returning struct make copy, use serializedproperty to modify it
            var serializedObject = new SerializedObject(HDRenderPipeline.currentAsset);
            var propertySupportRayTracing = serializedObject.FindProperty("m_RenderPipelineSettings.supportRayTracing");
            propertySupportRayTracing.boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        bool IsDXRDefaultSceneCorrect()
            => HDProjectSettings.defaultDXRScenePrefab != null;
        void FixDXRDefaultScene(bool fromAsync)
        {
            if (ObjectSelector.opened)
                return;
            CreateOrLoadDefaultScene(fromAsync ? () => m_Fixer.Stop() : (Action)null, scene => HDProjectSettings.defaultDXRScenePrefab = scene, forDXR: true);
            m_DefaultDXRScene.SetValueWithoutNotify(HDProjectSettings.defaultDXRScenePrefab);
        }

        #endregion
    }
}
