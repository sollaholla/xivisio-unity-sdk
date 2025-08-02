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
        [SerializeField] private string mapFileName = "xvmap.map";
        [SerializeField] private bool loadMapOnStart;
        [SerializeField] private MapSaveEvents mapSaveEvents = new();
        [SerializeField] private MapLoadEvents mapLoadEvents = new();
        [SerializeField] private MapGeneralEvents mapGeneralEvents = new();

        [Header("Camera")]
        [SerializeField] private bool outputLeftEyeImage;
        [SerializeField] private UnityEvent<Texture2D> onLeftEyeImage = new();
        [SerializeField] private bool outputRightEyeImage;
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
            public UnityEvent<float> onLocalized = new();
        }
        
        private readonly XvisioUnityWrapper _api = new();
        
        /// <summary>
        /// Indicates whether the SLAM map is currently loaded.
        /// </summary>
        public bool IsMapLoaded => _api.IsMapLoaded;

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
                _api.ResetSlam();
                mapFileName = value;
            }
        }

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

        private IEnumerator Start()
        {
            while (!_api.Initialize())
                yield return new WaitForSeconds(1f);

            if (!IsMapLoaded)
            {
                if (loadMapOnStart)
                    LoadMap();
            }
            else
                mapLoadEvents.onMapLoaded?.Invoke();
        }

        private void OnEnable()
        {
            _api.Localized += OnLocalized;
            _api.CslamSwitched += OnCslamSwitched;
            _api.MapSavedStatusChanged += OnMapSavedStatusChanged;
            _api.SlamReset += OnSlamReset;
        }

        private void OnDisable()
        {
            _api.Localized -= OnLocalized;
            _api.CslamSwitched -= OnCslamSwitched;
            _api.MapSavedStatusChanged -= OnMapSavedStatusChanged;
            _api.SlamReset -= OnSlamReset;
        }

        private void OnDestroy()
        {
            _api.Stop();
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
        
        public void ManualUpdate()
        {
            if (!_api.TryUpdate()) 
                return;

            if (outputLeftEyeImage)
            {
                var t = _api.GetLeftEyeStereoImage();
                if (t) onLeftEyeImage?.Invoke(t);
            }

            if (outputRightEyeImage)
            {
                var t = _api.GetRightEyeStereoImage();
                if (t) onRightEyeImage?.Invoke(t);
            }

            _api.TryApplyTransform(!outputPose ? transform : outputPose);
        }

        /// <summary>
        /// Resets the VSLAM device.
        /// </summary>
        public void ResetSlam()
        {
            _api.ResetSlam();
        }

        /// <summary>
        /// Loads the SLAM map from the specified file.
        /// </summary>
        public void LoadMap()
        {
            var map = Path.Combine(Application.persistentDataPath, mapFileName);
            if (!File.Exists(map) || new FileInfo(map).Length == 0)
                return;
            mapLoadEvents.onMapLoadStarted?.Invoke();
            if (_api.LoadMapAndSwitchToCslam(map))
                return;
            mapLoadEvents.onMapLoadFinished?.Invoke();
            mapLoadEvents.onMapLoadError?.Invoke();
        }

        /// <summary>
        /// Saves the current SLAM map to the specified file.
        /// </summary>
        public void SaveMap()
        {
            mapSaveEvents.onMapSaveStarted?.Invoke();
            if (_api.SaveMapAndSwitchToCslam(Path.Combine(Application.persistentDataPath, mapFileName)))
                return;
            mapSaveEvents.onMapSaveFinished?.Invoke();
            mapSaveEvents.onMapSaveError?.Invoke();
        }

        private void OnSlamReset()
        {
            mapGeneralEvents.onReset?.Invoke();
        }

        private void OnCslamSwitched(int status)
        {
            mapLoadEvents.onMapLoaded?.Invoke();
            mapLoadEvents.onMapLoadFinished?.Invoke();
        }

        private void OnMapSavedStatusChanged(MapSaveStatus status, int quality)
        {
            switch (status)
            {
                case MapSaveStatus.Saved:
                    mapSaveEvents.onMapSaved?.Invoke();
                    break;
                case MapSaveStatus.Error:
                    mapSaveEvents.onMapSaveError?.Invoke();
                    break;
                default:
                    return;
            }
            mapSaveEvents.onMapSaveFinished?.Invoke();
        }

        private void OnLocalized(float pct)
        {
            mapGeneralEvents.onLocalized?.Invoke(pct);
        }
    }
}