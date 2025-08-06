#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
#define XV_PLATFORM_SUPPORTED
#endif

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace Xvisio.Unity
{
    /// <summary>
    /// Provides functionality for managing SLAM mapping and localization using the XVisio SDK.
    /// This component should be attached to a GameObject in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class XvisioTrackedPoseDriver : MonoBehaviour
    {
        public enum UpdateType
        {
            Update,
            LateUpdate,
            FixedUpdate,
            Manual
        }

        [SerializeField] private Transform outputPose;
        [SerializeField] private UpdateType updateType = UpdateType.LateUpdate;
        [SerializeField] private bool enableLogs;
        
        [Header("Mapping")]
        [SerializeField] private string mapFileName = "map.bin";
        [SerializeField] private bool loadAutomatically;
        [SerializeField] private MapSaveEvents mapSaveEvents = new();
        [SerializeField] private MapLoadEvents mapLoadEvents = new();
        [SerializeField] private MapGeneralEvents mapGeneralEvents = new();

        [Header("Camera")]
        [SerializeField] private bool outputLeftEyeImage;
        [SerializeField] private XvisioImageTransform leftEyeImageTransform = XvisioImageTransform.InvertVertical;
        [SerializeField] private UnityEvent<Texture2D> onLeftEyeImage = new();
        [SerializeField] private bool outputRightEyeImage;
        [SerializeField] private XvisioImageTransform rightEyeImageTransform = XvisioImageTransform.InvertVertical;
        [SerializeField] private UnityEvent<Texture2D> onRightEyeImage = new();

        [Serializable]
        public class MapSaveEvents
        {
            public UnityEvent onMapSaved = new();
            public UnityEvent onMapSaveError = new();
            public UnityEvent onMapSaveStarted = new();
            public UnityEvent onMapSaveFinished = new();
        }
        
        [Serializable]
        public class MapLoadEvents
        {
            public UnityEvent onMapLoaded = new();
            public UnityEvent onMapLoadError = new();
            public UnityEvent onMapLoadStarted = new();
            public UnityEvent onMapLoadFinished = new();
        }
        
        [Serializable]
        public class MapGeneralEvents
        {
            public UnityEvent onReset = new();
            public UnityEvent onTracking = new();
            public UnityEvent onTrackingLost = new();
            public UnityEvent<float> onLocalized = new();
        }

        public float LastTrackingQuality { get; private set; } = 1;

        /// <summary>
        /// Indicates whether the SLAM map is currently loaded.
        /// </summary>
        public bool IsMapLoaded => API.IsMapLoaded;
        
        /// <summary>
        /// Gets a value indicating whether the <see cref="XvisioTrackedPoseDriver"/> is tracking.
        /// </summary>
        public bool IsTracking { get; private set; }
        
        /// <summary>
        /// Gets the most recent activate <see cref="XvisioTrackedPoseDriver"/>.
        /// </summary>
        public static XvisioTrackedPoseDriver Current { get; private set; }

        /// <summary>
        /// Gets or sets the current map file name. If the map file name changes, this will forcefully
        /// reset the SLAM.
        /// </summary>
        public string MapFileName
        {
            get => mapFileName;
            set
            {
                if (mapFileName == value)
                    return;
                API.ResetSlam();
                mapFileName = value;
                if (loadAutomatically)
                    LoadMap();
            }
        }

        /// <summary>
        /// Gets the current <see cref="XvisioUnityWrapper"/> to access the API.
        /// </summary>
        public XvisioUnityWrapper API { get; } = new();

        /// <summary>
        /// General events for mapping.
        /// </summary>
        public MapGeneralEvents MappingGeneralEvents => mapGeneralEvents;
        
        /// <summary>
        /// Events for saving the map.
        /// </summary>
        public MapSaveEvents MappingSaveEvents => mapSaveEvents;
        
        /// <summary>
        /// Events for loading the map.
        /// </summary>
        public MapLoadEvents MappingLoadEvents => mapLoadEvents;

        /// <summary>
        /// Invoked when the left eye image is rendered.
        /// </summary>
        public UnityEvent<Texture2D> OnLeftEyeImage => onLeftEyeImage;
        
        /// <summary>
        /// Invoked when the right eye image is rendered.
        /// </summary>
        public UnityEvent<Texture2D> OnRightEyeImage => onRightEyeImage;

        /// <summary>
        /// Gets the most recent left eye image.
        /// </summary>
        public Texture2D LeftEyeImage { get; private set; }
        
        /// <summary>
        /// Gets the most recent right eye image.
        /// </summary>
        public Texture2D RightEyeImage { get; private set; }
        
#if XV_PLATFORM_SUPPORTED

        private IEnumerator Start()
        {
            while (!API.Initialize())
                yield return new WaitForSeconds(1f);

            if (!IsMapLoaded)
            {
                if (loadAutomatically)
                    LoadMap();
            }
            else
            {
                try { mapLoadEvents.onMapLoaded?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void OnEnable()
        {
            API.Localized += OnLocalized;
            API.CslamSwitched += OnCslamSwitched;
            API.MapSavedStatusChanged += OnMapSavedStatusChanged;
            API.SlamReset += OnSlamReset;

            Current = this;
        }

        private void OnDisable()
        {
            API.Localized -= OnLocalized;
            API.CslamSwitched -= OnCslamSwitched;
            API.MapSavedStatusChanged -= OnMapSavedStatusChanged;
            API.SlamReset -= OnSlamReset;

            if (Current == this)
                Current = null;
        }

        private void OnDestroy()
        {
            API.Stop();
        }
        
        private void Update()
        {
            if (updateType is UpdateType.Update)
                ManualUpdate();
        }

        private void LateUpdate()
        {
            if (updateType is UpdateType.LateUpdate)
                ManualUpdate();
        }

        private void FixedUpdate()
        {
            if (updateType is UpdateType.FixedUpdate)
                ManualUpdate();
        }

        private void OnSlamReset()
        {
            try { mapGeneralEvents.onReset?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            if (IsTracking)
            {
                LastTrackingQuality = 1;
                TrackingLost();
                mapGeneralEvents.onTrackingLost?.Invoke();
            }
        }

        private void OnCslamSwitched(int mapQuality)
        {
            LastTrackingQuality = 0;
            try { mapLoadEvents.onMapLoaded?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            try { mapLoadEvents.onMapLoadFinished?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void OnMapSavedStatusChanged(MapSaveStatus status, int quality)
        {
            switch (status)
            {
                case MapSaveStatus.Saved:
                    LastTrackingQuality = 0;
                    try { mapSaveEvents.onMapSaved?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    break;
                case MapSaveStatus.Error:
                    try { mapSaveEvents.onMapSaveError?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    break;
                default:
                    return;
            }
            
            try { mapSaveEvents.onMapSaveFinished?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        private void OnLocalized(float quality)
        {
            LastTrackingQuality = quality;
            try { mapGeneralEvents.onLocalized?.Invoke(quality); } catch (Exception e) { Debug.LogException(e); }
        }
#else
        private void Start() { }
#endif
        
        /// <summary>
        /// Call this if the <see cref="UpdateType"/> is set to Manual.
        /// </summary>
        public void ManualUpdate()
        {
#if XV_PLATFORM_SUPPORTED
            if (!API.TryUpdate())
            {
                TrackingLost();
                return;
            }

            if (outputLeftEyeImage)
            {
                LeftEyeImage = API.GetLeftEyeStereoImage(leftEyeImageTransform);
                if (LeftEyeImage) try { onLeftEyeImage?.Invoke(LeftEyeImage); } catch (Exception e) { Debug.LogException(e); }
            }

            if (outputRightEyeImage)
            {
                RightEyeImage = API.GetRightEyeStereoImage(rightEyeImageTransform);
                if (RightEyeImage) try { onRightEyeImage?.Invoke(RightEyeImage); } catch (Exception e) { Debug.LogException(e); }
            }

            if (LastTrackingQuality > 0 && API.TryApplyTransform(!outputPose ? transform : outputPose))
                TrackingFound();
            else
                TrackingLost();
#endif
        }

        /// <summary>
        /// Resets the VSLAM device to begin a new map.
        /// </summary>
        public void ResetSlam()
        {
#if XV_PLATFORM_SUPPORTED
            API.ResetSlam();
#endif
        }

        /// <summary>
        /// Begins the vSLAM mapping system.
        /// </summary>
        public void StartSlam()
        {
            API.StartSlam();
        }

        /// <summary>
        /// Loads the SLAM map from the specified file.
        /// </summary>
        public void LoadMap()
        {
#if XV_PLATFORM_SUPPORTED
            try { mapLoadEvents.onMapLoadStarted?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            var map = GetMapPath();
            if (string.IsNullOrEmpty(map) || !File.Exists(map) || new FileInfo(map).Length == 0)
            {
                MapLoadFailed();
                return;
            }
            if (API.LoadMapAndSwitchToCslam(map))
                return;
            MapLoadFailed();
#endif
        }

        /// <summary>
        /// Saves the current SLAM map to the specified file.
        /// </summary>
        public void SaveMap()
        {
#if XV_PLATFORM_SUPPORTED
            try { mapSaveEvents.onMapSaveStarted?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            var path = GetMapPath();
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".bin"))
            {
                MapSaveFailed();
                return;
            }
            if (File.Exists(path))
                File.Delete(path);
            var directoryName = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
                Directory.CreateDirectory(directoryName!);
            if (API.SaveMapAndSwitchToCslam(path))
                return;
            MapSaveFailed();
#endif
        }

        /// <summary>
        /// Deletes the map with the specified file name.
        /// </summary>
        /// <param name="fileName">The filename/path to the map file.</param>
        /// <returns>True if the file was deleted.</returns>
        public void DeleteMap(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(".."))
                return;
            if (!fileName.EndsWith(".bin"))
                return;
            var file = Path.Combine(Application.persistentDataPath, fileName);
            if (!File.Exists(file))
                return;
            File.Delete(file);
        }

        /// <summary>
        /// Deletes the map with the filename of <see cref="MapFileName"/>.
        /// </summary>
        public void DeleteMap()
        {
            var path = GetMapPath();
            if (string.IsNullOrWhiteSpace(path))
                return;
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists)
                fileInfo.Delete();
        }

        private void MapLoadFailed()
        {
            try { mapLoadEvents.onMapLoadFinished?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            try { mapLoadEvents.onMapLoadError?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        private void MapSaveFailed()
        {
            try { mapSaveEvents.onMapSaveFinished?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            try { mapSaveEvents.onMapSaveError?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        private string GetMapPath()
        {
            return string.IsNullOrWhiteSpace(mapFileName) 
                ? string.Empty 
                : Path.Combine(Application.persistentDataPath, mapFileName.Trim());
        }
        
        private void TrackingFound()
        {

            if (IsTracking)
                return;
            mapGeneralEvents.onTracking?.Invoke();
            IsTracking = true;
        }

        private void TrackingLost()
        {

            if (!IsTracking)
                return;
            mapGeneralEvents.onTrackingLost?.Invoke();
            IsTracking = false;
        }
    }
}