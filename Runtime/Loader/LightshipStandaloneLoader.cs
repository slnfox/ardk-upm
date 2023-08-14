using Niantic.Lightship.AR.Playback;
using Niantic.Lightship.AR.Subsystems;
using UnityEngine.XR;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace Niantic.Lightship.AR.Loader
{
    public class LightshipStandaloneLoader : XRLoaderHelper, ILightshipLoader
    {
        /// <summary>
        /// The `XROcclusionSubsystem` whose lifecycle is managed by this loader.
        /// </summary>
        public XROcclusionSubsystem LightshipOcclusionSubsystem => GetLoadedSubsystem<XROcclusionSubsystem>();

        /// <summary>
        /// The `XRPersistentAnchorSubsystem` whose lifecycle is managed by this loader.
        /// </summary>
        public XRPersistentAnchorSubsystem LightshipPersistentAnchorSubsystem =>
            GetLoadedSubsystem<XRPersistentAnchorSubsystem>();

        /// <summary>
        /// The `XRMeshingSubsystem` whose lifecycle is managed by this loader.
        /// </summary>
        public XRMeshSubsystem lightshipMeshSubsystem =>
            GetLoadedSubsystem<XRMeshSubsystem>();

        private PlaybackLoaderHelper _playbackHelper;
        private NativeLoaderHelper _nativeHelper;

        /// <summary>
        /// Initializes the loader.
        /// </summary>
        /// <returns>`True` if the session subsystems were successfully created, otherwise `false`.</returns>
        public override bool Initialize()
        {
            return ((ILightshipLoader)this).InitializeWithSettings(LightshipSettings.Instance);
        }

        bool ILightshipLoader.InitializeWithSettings(LightshipSettings settings, bool isTest)
        {
#if NIANTIC_LIGHTSHIP_AR_LOADER_ENABLED
            if (settings.EditorPlaybackSettings.UsePlayback)
            {
                _playbackHelper = new PlaybackLoaderHelper();
                if (!_playbackHelper.Initialize(this, settings))
                {
                    return false;
                }

                // When in playback mode, Lidar device support is dictated whether the Playback input
                // has LiDAR data or not
                bool isLidarSupported = _playbackHelper.DatasetReader.GetIsLidarAvailable();

                // Initialize native helper after playback helper, because playback helper creates the dataset reader,
                // then native helper injects it into the PAM
                _nativeHelper = new NativeLoaderHelper();
                return _nativeHelper.Initialize(this, settings, isLidarSupported, isTest);
            }
            else
            {
                // Initialize native helper with no subsystems except the API key.
                _nativeHelper = new NativeLoaderHelper();
                var emptySettings = LightshipSettings._CreateRuntimeInstance(apiKey: settings.ApiKey);
                return _nativeHelper.Initialize(this, emptySettings, false, isTest);
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Destroys each subsystem.
        /// </summary>
        /// <returns>Always returns `true`.</returns>
        public override bool Deinitialize()
        {
#if NIANTIC_LIGHTSHIP_AR_LOADER_ENABLED
            _playbackHelper?.Deinitialize(this);
            _nativeHelper?.Deinitialize(this);
#endif
            return true;
        }

        PlaybackDatasetReader ILightshipLoader.PlaybackDatasetReader => _playbackHelper?.DatasetReader;
    }
}