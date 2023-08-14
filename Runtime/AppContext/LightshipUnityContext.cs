using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Niantic.Lightship.AR.Loader;
using UnityEngine;
using Niantic.Lightship.AR.PAM;
using Niantic.Lightship.AR.Utilities.Profiling;
using Niantic.Lightship.AR.Settings.User;
using Niantic.Lightship.AR.Telemetry;


namespace Niantic.Lightship.AR
{
    internal class LightshipUnityContext
    {
        // Pointer to the unity context
        internal static IntPtr UnityContextHandle { get; private set; } = IntPtr.Zero;

        // Temporarily exposing this so loaders can inject the PlaybackDatasetReader into the PAM
        // To remove once all subsystems are implemented via playback.
        internal static PlatformAdapterManager PlatformAdapterManager { get; private set; }

        private static IntPtr s_propertyBagHandle = IntPtr.Zero;
        private static EnvironmentConfig s_environmentConfig;
        private static TelemetryService s_telemetryService;
        internal static bool s_isDeviceLidarSupported = false;

        // Event triggered right before the context is destroyed. Used by internal code its lifecycle is not managed
        // by native UnityContext
        internal static event Action OnDeinitialized;
        internal static event Action OnUnityContextHandleInitialized;

        internal static void Initialize(LightshipSettings settings, bool isDeviceLidarSupported, bool isTest = false)
        {
#if NIANTIC_LIGHTSHIP_AR_LOADER_ENABLED
            s_isDeviceLidarSupported = isDeviceLidarSupported;

            if (UnityContextHandle != IntPtr.Zero)
            {
                Debug.LogWarning($"Cannot initialize {nameof(LightshipUnityContext)} as it is already initialized");
                return;
            }

            Debug.Log($"Initializing {nameof(LightshipUnityContext)}");
            s_environmentConfig = new EnvironmentConfig
            {
                ApiKey = settings.ApiKey,
                ScanningEndpoint = settings.ScanningEndpoint,
                ScanningSqcEndpoint = settings.ScanningSqcEndpoint,
                SharedArEndpoint = settings.SharedArEndpoint,
                VpsEndpoint = settings.VpsEndpoint,
                VpsCoverageEndpoint = settings.VpsCoverageEndpoint,
                DefaultDepthSemanticsEndpoint = settings.DefaultDepthSemanticsEndpoint,
                FastDepthSemanticsEndpoint = settings.FastDepthSemanticsEndpoint,
                SmoothDepthSemanticsEndpoint = settings.SmoothDepthSemanticsEndpoint,
            };

            DeviceInfo deviceInfo = new DeviceInfo
            {
                AppId = Metadata.ApplicationId,
                Platform = Metadata.Platform,
                Manufacturer = Metadata.Manufacturer,
                ClientId = Metadata.ClientId,
                DeviceModel = Metadata.DeviceModel,
                Version = Metadata.Version,
                AppInstanceId = Metadata.AppInstanceId,
                DeviceLidarSupported = isDeviceLidarSupported,
            };
            UnityContextHandle = NativeApi.Lightship_ARDK_Unity_Context_Create(false, ref deviceInfo, ref s_environmentConfig);

            if (!isTest)
            {
                // Cannot use Application.persistentDataPath in testing
                AnalyticsTelemetryPublisher telemetryPublisher = new AnalyticsTelemetryPublisher(
                    endpoint: settings.TelemetryEndpoint,
                    directoryPath: Path.Combine(Application.persistentDataPath, "telemetry"),
                    key: settings.TelemetryApiKey,
                    registerLogger: false);

                s_telemetryService = new TelemetryService(UnityContextHandle, telemetryPublisher, settings.ApiKey);
            }
            OnUnityContextHandleInitialized?.Invoke();

            var modelPath = Path.Combine(Application.streamingAssetsPath, "full_model.bin");
            Debug.Log("Model path: " + modelPath);

            s_propertyBagHandle = NativeApi.Lightship_ARDK_Unity_Property_Bag_Create(UnityContextHandle);
            NativeApi.Lightship_ARDK_Unity_Property_Bag_Put
            (
                s_propertyBagHandle,
                "depth_semantics_model_path",
                modelPath
            );

            ProfilerUtility.RegisterProfiler(new UnityProfiler());
            ProfilerUtility.RegisterProfiler(new CTraceProfiler());

            CreatePam(settings);
#endif
        }

        private static void CreatePam(LightshipSettings settings)
        {
            if (PlatformAdapterManager != null)
            {
                Debug.LogWarning("Cannot create PAM as it is already created");
                return;
            }

            // Create the PAM, which will create the SAH
            Debug.Log("Creating PAM");
            if (settings.EditorPlaybackEnabled || settings.DevicePlaybackEnabled)
            {
                PlatformAdapterManager =
                    AR.PAM.PlatformAdapterManager.Create<PAM.NativeApi, PlaybackSubsystemsDataAcquirer>
                    (
                        UnityContextHandle,
                        AR.PAM.PlatformAdapterManager.ImageProcessingMode.GPU
                    );
            }
            else
            {
                PlatformAdapterManager =
                    AR.PAM.PlatformAdapterManager.Create<PAM.NativeApi, SubsystemsDataAcquirer>
                    (
                        UnityContextHandle,
                        AR.PAM.PlatformAdapterManager.ImageProcessingMode.CPU
                    );
            }
        }

        private static void DisposePam()
        {
            Debug.Log("Disposing PAM");

            PlatformAdapterManager?.Dispose();
            PlatformAdapterManager = null;
        }

        internal static void Deinitialize()
        {
            OnDeinitialized?.Invoke();
#if NIANTIC_LIGHTSHIP_AR_LOADER_ENABLED
            if (UnityContextHandle != IntPtr.Zero)
            {
                Debug.Log($"Shutting down {nameof(LightshipUnityContext)}");

                DisposePam();

                if (s_propertyBagHandle != IntPtr.Zero)
                {
                    NativeApi.Lightship_ARDK_Unity_Property_Bag_Release(s_propertyBagHandle);
                    s_propertyBagHandle = IntPtr.Zero;
                }

                s_telemetryService?.Dispose();
                s_telemetryService = null;

                ProfilerUtility.ShutdownAll();

                NativeApi.Lightship_ARDK_Unity_Context_Shutdown(UnityContextHandle);
                UnityContextHandle = IntPtr.Zero;
            }
#endif
        }

        /// <summary>
        /// Container to wrap the native Lightship C APIs
        /// </summary>
        private static class NativeApi
        {
            [DllImport(LightshipPlugin.Name)]
            public static extern IntPtr Lightship_ARDK_Unity_Context_Create(
                bool disableCtrace, ref DeviceInfo deviceInfo, ref EnvironmentConfig environmentConfig);

            [DllImport(LightshipPlugin.Name)]
            public static extern void Lightship_ARDK_Unity_Context_Shutdown(IntPtr unityContext);

            [DllImport(LightshipPlugin.Name)]
            public static extern IntPtr Lightship_ARDK_Unity_Property_Bag_Create(IntPtr unityContext);

            [DllImport(LightshipPlugin.Name)]
            public static extern void Lightship_ARDK_Unity_Property_Bag_Release(IntPtr bagHandle);

            [DllImport(LightshipPlugin.Name)]
            public static extern bool Lightship_ARDK_Unity_Property_Bag_Put(IntPtr bagHandle, string key, string value);
        }

        // PLEASE NOTE: Do NOT add feature flags in this struct.
        [StructLayout(LayoutKind.Sequential)]
        private struct EnvironmentConfig
        {
            public string ApiKey;
            public string VpsEndpoint;
            public string VpsCoverageEndpoint;
            public string SharedArEndpoint;
            public string FastDepthSemanticsEndpoint;
            public string DefaultDepthSemanticsEndpoint;
            public string SmoothDepthSemanticsEndpoint;
            public string ScanningEndpoint;
            public string ScanningSqcEndpoint;
            public string TelemetryEndpoint;
            public string TelemetryKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfo
        {
            public string AppId;
            public string Platform;
            public string Manufacturer;
            public string DeviceModel;
            public string ClientId;
            public string Version;
            public string AppInstanceId;
            public bool DeviceLidarSupported;
        }
    }
}