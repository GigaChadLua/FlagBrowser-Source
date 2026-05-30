using System.Text.Json.Serialization;
namespace FlagInjector;
public class FlagEntry
{
    public string  Name          { get; set; } = "";
    public string  Value         { get; set; } = "";
    public string  Type          { get; set; } = "string";
    public bool    Enabled       { get; set; } = true;
    public string  OriginalValue { get; set; } = "";
    public string? DefaultValue  { get; set; }
    public string  Hotkey        { get; set; } = "";
    public FlagEntry() { }
    public FlagEntry(string name, string value)
    {
        Name          = name;
        Value         = value;
        Type          = InferType(name, value);
        OriginalValue = value;
    }
    public void Update(string newValue)
    {
        Value = newValue;
        string fromName = InjectionEngine.InferTypeFromName(Name);
        if (fromName.Length == 0)
            Type = InferType(Name, newValue);
    }
    public static string InferType(string name, string value)
    {
        string fromName = InjectionEngine.InferTypeFromName(name);
        if (fromName.Length > 0) return fromName;
        return InferTypeFromValue(value);
    }
    public static string InferType(string v) => InferTypeFromValue(v);
    static string InferTypeFromValue(string v)
    {
        v = v.Trim().ToLowerInvariant();
        if (v is "true" or "false") return "bool";
        if (int.TryParse(v, out _))  return "int";
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _)) return "float";
        return "string";
    }
}
public class Profile
{
    public string          Name  { get; set; } = "Default";
    public List<FlagEntry> Flags { get; set; } = new();
}
public static class PresetFlags
{
    public static readonly IReadOnlyDictionary<string, string> All =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Rendering.ManualFullscreen",        "FFlagHandleAltEnterFullscreenManually" },
        { "Rendering.DisableScaling",          "DFFlagDisableDPIScale" },
        { "Rendering.MSAA",                    "FIntDebugForceMSAASamples" },
        { "Rendering.Mode.D3D11",              "FFlagDebugGraphicsPreferD3D11" },
        { "Rendering.Mode.D3D10",              "FFlagDebugGraphicsPreferD3D11FL10" },
        { "Rendering.Mode.Vulkan",             "FFlagDebugGraphicsPreferVulkan" },
        { "Rendering.Mode.OpenGL",             "FFlagDebugGraphicsPreferOpenGL" },
        { "Rendering.TextureQualityOverride",  "DFFlagTextureQualityOverrideEnabled" },
        { "Rendering.TextureQualityLevel",     "DFIntTextureQualityOverride" },
        { "Rendering.DisplayFPS",              "FFlagDebugDisplayFPS" },
        { "Rendering.GpuCulling",              "FFlagEnableGPULightCulling" },
        { "Rendering.CpuCulling",              "FFlagEnableCPULightCulling" },
        { "Rendering.GraySky",                 "FFlagDebugSkyGray" },
        { "Rendering.WhiteSky",                "FFlagDebugSkyWhite" },
        { "Rendering.CameraMaxZoom",           "FIntCameraMaxZoomDistance" },
        { "Rendering.ShadowIntensity",         "FIntRenderShadowIntensity" },
        { "Rendering.ShadowMapBias",           "DFIntShadowMapBias" },
        { "Rendering.TerrainTextureQuality",   "FIntTerrainArraySliceSize" },
        { "Rendering.PauseVoxelizer",          "FFlagPauseVoxelizer" },
        { "Rendering.OcclusionCulling",        "FFlagEnableOcclusionCulling" },
        { "Rendering.OcclusionCullingP2",      "FFlagEnableOcclusionCullingPhase2" },
        { "Rendering.OcclusionCullingP3",      "FFlagEnableOcclusionCullingPhase3" },
        { "Rendering.DisableTextures",         "FFlagDisableTextures" },
        { "Rendering.TextureCompositorJobs",   "DFIntTextureCompositorActiveJobs" },
        { "Rendering.GrayAvatar",              "DFIntAvatarQualityLevel" },
        { "Rendering.LowPolyMeshes1",          "DFIntCSGLevelOfDetailSwitchingDistance" },
        { "Rendering.LowPolyMeshes2",          "DFIntCSGLevelOfDetailSwitchingDistanceL12" },
        { "Rendering.LowPolyMeshes3",          "DFIntCSGLevelOfDetailSwitchingDistanceL23" },
        { "Rendering.LowPolyMeshes4",          "DFIntCSGLevelOfDetailSwitchingDistanceL34" },
        { "Rendering.Nograss.MinDist",         "FIntFRMMinGrassDistance" },
        { "Rendering.Nograss.MaxDist",         "FIntFRMMaxGrassDistance" },
        { "Rendering.Nograss.Strands",         "FIntRenderGrassDetailStrands" },
        { "Rendering.Particles.Deterministic", "FFlagDebugDeterministicParticles" },
        { "Rendering.Particles.Optimize",      "FFlagEnableParticleOptimizations" },
        { "Rendering.Particles.LOD",           "FFlagEnableParticleLOD" },
        { "Rendering.Particles.Culling",       "FFlagEnableParticleCulling" },
        { "Performance.DisablePostFX",         "FFlagDisablePostFx" },
        { "Performance.ReduceShadows",         "FIntRenderShadowIntensity" },
        { "Performance.DisableTerrainTextures","FIntTerrainArraySliceSize" },
        { "Performance.DisablePlayerShadows",  "DFIntCullFactorPixelThresholdShadowMapHighQuality" },
        { "Performance.LowGraphicsMode",       "DFFlagDebugRenderForceTechnologyVoxel" },
        { "Performance.HyperThreading",        "FFlagEnableHyperThreading" },
        { "Performance.OptCFrameUpdates",      "FFlagOptimizeCFrameUpdates" },
        { "Performance.OptCFrameUpdatesIC",    "FFlagOptimizeCFrameUpdatesIC" },
        { "Performance.TaskSchedulerFps",      "TaskSchedulerTargetFps" },
        { "Performance.UnlockFpsLimit",        "TaskSchedulerLimitTargetFpsTo2402" },
        { "Network.DefaultBps",                "DFIntDefaultNetworkBps" },
        { "Network.MaxWorkCatchupMs",          "DFIntMaxWorkCatchupMs" },
        { "Network.EnableLargeReplicator",     "FFlagEnableLargeReplicator" },
        { "Network.LargeReplicatorWrite",      "FFlagEnableLargeReplicatorWrite" },
        { "Network.LargeReplicatorRead",       "FFlagEnableLargeReplicatorRead" },
        { "Network.MTU",                       "DFIntConnectionMTUSize" },
        { "Network.MaxAssetPreload",           "DFIntMaxAssetPreloadCount" },
        { "Network.MeshPreloading",            "FFlagEnableMeshPreloading" },
        { "Network.SerializeRead",             "FFlagEnableSerializeRead" },
        { "Network.SerializeWrite",            "FFlagEnableSerializeWrite" },
        { "Network.ReplicatorMaxPacket",       "DFIntReplicatorMaxPacketSize" },
        { "Network.ReplicatorMaxBuffer",       "DFIntReplicatorMaxBufferSize" },
        { "Network.ReplicatorMinPacket",       "DFIntReplicatorMinPacketSize" },
        { "Network.ReplicatorTargetPacket",    "DFIntReplicatorTargetPacketSize" },
        { "Network.ReplicatorQueue",           "DFIntReplicatorMaxQueueSize" },
        { "Network.MaxPayload",                "DFIntMaxPayloadSize" },
        { "Network.EngineModuleReplication",   "FFlagEnableEngineModuleReplication" },
        { "Network.EngineModuleOptimization",  "FFlagEnableEngineModuleOptimization" },
        { "Preload.AllAssets",                 "FFlagEnablePreloadAllAssets" },
        { "Preload.Sound",                     "FFlagEnableSoundPreload" },
        { "Preload.Texture",                   "FFlagEnableTexturePreload" },
        { "Preload.Fonts",                     "FFlagEnableFontsPreload" },
        { "Preload.Teleport",                  "FFlagEnableTeleportPreload" },
        { "Preload.TeleportAsset",             "FFlagEnableTeleportAssetPreload" },
        { "Preload.Items",                     "FFlagEnableItemPreload" },
        { "Preload.Mesh",                      "FFlagEnableMeshPreloading" },
        { "Cache.LargeCache",                  "FFlagEnableLargeCache" },
        { "Cache.Eviction",                    "FFlagEnableCacheEviction" },
        { "Cache.CachePreloading",             "FFlagEnableCachePreloading" },
        { "Cache.EvictionThreshold",           "DFIntCacheEvictionThreshold" },
        { "Cache.MaxSize",                     "DFIntMaxCacheSize" },
        { "Cache.MaxTextureSize",              "DFIntMaxTextureCacheSize" },
        { "Cache.MaxMeshSize",                 "DFIntMaxMeshCacheSize" },
        { "Cache.MaxSoundSize",                "DFIntMaxSoundCacheSize" },
        { "Cache.Compression",                 "FFlagEnableCacheCompression" },
        { "Cache.CompressionLevel",            "DFIntCacheCompressionLevel" },
        { "Cache.MaxSizeBytes",                "DFIntMaxCacheSizeBytes" },
        { "Memory.Probing",                    "FFlagEnableMemoryProbing" },
        { "Memory.BasePercent",                "DFIntMemoryUtilityCurveBaseHundrethsPercent" },
        { "Memory.FinalDelta",                 "DFIntMemoryUtilityCurveFinalDeltaHundredths" },
        { "Memory.InitialDelta",               "DFIntMemoryUtilityCurveInitialDeltaHundredths" },
        { "Memory.Segments",                   "DFIntMemoryUtilityCurveNumSegments" },
        { "Memory.PenaltyBuffer",              "DFIntMemoryUtilityCurvePenaltyBuffer" },
        { "Telemetry.V2Url",                   "FStringTelemetryV2Url" },
        { "Telemetry.Protocol",                "FFlagEnableTelemetryProtocol" },
        { "Telemetry.GraphicsQuality",         "FFlagEnableGraphicsQualityUsageTelemetry" },
        { "Telemetry.GpuVsCpu",                "FFlagEnableGpuVsCpuBoundTelemetry" },
        { "Telemetry.RenderFidelity",          "FFlagEnableRenderFidelityTelemetry" },
        { "Telemetry.RenderDistance",          "FFlagEnableRenderDistanceTelemetry" },
        { "Telemetry.AudioPlugin",             "FFlagEnableAudioPluginTelemetry" },
        { "Telemetry.FmodErrors",              "FFlagEnableFmodErrorTelemetry" },
        { "Telemetry.DeviceRAM",               "FFlagEnableDeviceRAMTelemetry" },
        { "Telemetry.V2FrameRate",             "FFlagEnableV2FrameRateMetrics" },
        { "Telemetry.OpenTelemetry",           "FFlagEnableOpenTelemetry" },
        { "Telemetry.Service",                 "FFlagEnableTelemetryService" },
        { "Telemetry.VoiceChat",               "FFlagEnableVoiceChatTelemetry" },
        { "Telemetry.Webview",                 "FFlagEnableWebview2Telemetry" },
        { "UI.DisableAdsAPI",                  "FFlagEnableAdsAPI" },
        { "UI.DisableAdPortal",                "FFlagEnableAdPortal" },
        { "UI.DisableAdsService",              "FFlagEnableAdsService" },
        { "UI.DisableVideoAds",                "FFlagEnableVideoAds" },
        { "UI.NoGuiBlur",                      "FIntFullscreenTitleBarTriggerDelayMillis" },
        { "UI.DisableLayeredClothing",         "DFIntLayeredClothingMaxLayers" },
        { "UI.ChatBubbles",                    "FFlagEnableChatBubbles" },
        { "UI.DynamicTextSize",                "FFlagEnableDynamicTextSize" },
        { "UI.TextScaling",                    "FFlagEnableTextScaling" },
    };
    public static IReadOnlyList<string> AllFlagNames { get; } =
        All.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    public static IReadOnlyList<string> Categories { get; } =
        All.Keys.Select(k => k.Split('.')[0]).Distinct().OrderBy(x => x).ToList();
    public static IEnumerable<KeyValuePair<string, string>> GetCategory(string category) =>
        All.Where(kv => kv.Key.StartsWith(category + ".", StringComparison.OrdinalIgnoreCase));
}
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct MBI
{
    public IntPtr BaseAddress, AllocationBase;
    public uint   AllocationProtect, PartitionId;
    public nint   RegionSize;
    public uint   State, Protect, Type;
}
