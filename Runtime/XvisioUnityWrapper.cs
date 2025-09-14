#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
#define XV_PLATFORM_SUPPORTED
#endif

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Xvisio.Unity
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void XvCslamSwitchedCallback(int mapQuality);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void XvSavedCallback(XvisioMapSaveStatus statusOfSavedXvisioMap, int mapQuality);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void XvLocalizedCallback(float mapVisibility);

    public enum XvisioMapSaveStatus
    {
        Error = -1,
        NotLoaded = 0,
        Progress = 1,
        Saved = 2
    }

    /// <summary>
    /// Sets the type of SLAM to be used.
    /// </summary>
    public enum XvisioSlamType
    {
        /// <summary>
        /// SLAM using edge detection. Mapping is not supported with this type.
        /// </summary>
        Edge = 0,
        /// <summary>
        /// SLAM using a combination of edge detection and point cloud.
        /// </summary>
        Mixed = 1
    }

    public enum XvisioSlamMapEvent
    {
        MapSaved = 1,
        MapSaveFailed = 2,
        CSlamSwitched = 3,
        Localized = 4, 
        StereoPlanesUpdated = 5, 
        ToFPlanesUpdated = 6
    }

    [Flags]
    public enum XvisioOrientationFlipAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 4,
        X180 = 8,
        Y180 = 16,
        Z180 = 32,
    }
    
    public enum XvisioCamera
    {
        XR50 = 0,
        DS80 = 1
    }

    /// <summary>
    /// Exposes the XVisio SLAM API for Unity.
    /// </summary>
    public class XvisioUnityWrapper
    {
        private const string NativePackage = "xv-unity-wrapper.dll";

        private Texture2D _leftEyeStereoImage;
        private byte[] _leftEyeImageBuffer;
        private Texture2D _rightEyeStereoImage;
        private byte[] _rightEyeImageBuffer;

        private int? _leftEyeWidth;
        private int? _leftEyeHeight;
        private int? _rightEyeWidth;
        private int? _rightEyeHeight;

        private byte[] _stereoPlaneBuffer;
        private List<XvPlane> _stereoPlanes;
        private int _stereoPlaneCount;

        private static bool _initialized;

        private Dictionary<string, GameObject> _planes;

        /// <summary>
        /// Indicates whether the SLAM map is currently loaded.
        /// </summary>   
        public bool IsMapLoaded { get; private set; }

        /// <summary>
        /// The estimated device type based on the SLAM system's analysis.
        /// </summary>
        public XvisioCamera EstimatedCameraModel { get; private set; } = XvisioCamera.XR50;

        /// <summary>
        /// Invoked when a map is loaded successfully.
        /// </summary>
        public event XvCslamSwitchedCallback CslamSwitched;

        /// <summary>
        /// Invoked when a map is saved successfully.
        /// </summary>
        public event XvSavedCallback MapSavedStatusChanged;

        /// <summary>
        /// Invoked when the SLAM system is localized to a map.
        /// </summary>
        public event XvLocalizedCallback Localized;

        /// <summary>
        /// Invoked when the SLAM system is reset.
        /// </summary>
        public event Action SlamReset;
        
#if UNITY_EDITOR && XV_PLATFORM_SUPPORTED
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAssemblyReload()
        {
            if (!_initialized) return;
            xslam_uninit();
        }
#endif
#if XV_PLATFORM_SUPPORTED
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod]
        private static void InitOnLoad()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
                return;

            Application.quitting += () =>
            {
                if (_initialized)
                    xslam_uninit();
            };
        }
#endif

        /// <summary>
        /// Initializes the XVisio SLAM system.
        /// </summary>
        /// <returns>True if initialization was successful, otherwise false.</returns>
        public async Task<bool> InitializeAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_initialized || xslam_init())
                    {
                        _initialized = true;
                        var started = xslam_start_slam();
                        if (started)
                        {
                            if (xv_device_model(out var model))
                                EstimatedCameraModel = (XvisioCamera)model;
                            return true;
                        }
                    }
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Checks if the XVisio SLAM system is ready to use.
        /// </summary>
        /// <returns>True if the system is ready, otherwise false.</returns>
        public bool TryUpdate()
        {
            if (!_initialized)
                return false;
            if (!xslam_ready())
                return false;
            DequeueEvents();
            return true;
        }

        public bool IsReady()
        {
            return _initialized && xslam_ready();
        }

        public Texture2D GetLeftEyeStereoImage()
        {
            var width = _leftEyeWidth ??= xslam_get_stereo_width();
            var height = _leftEyeHeight ??= xslam_get_stereo_height();
            if (width <= 1 || height <= 1)
            {
                if (_leftEyeStereoImage) UnityEngine.Object.Destroy(_rightEyeStereoImage);
                _leftEyeImageBuffer = null;
                _leftEyeWidth = null;
                _rightEyeWidth = null;
                return Texture2D.blackTexture;
            }

            _leftEyeStereoImage ??= new Texture2D(width, height, TextureFormat.BGRA32, mipChain: false);
            _leftEyeImageBuffer ??= new byte[width * height * 4];
            var handle = GCHandle.Alloc(_leftEyeImageBuffer, GCHandleType.Pinned);
            try
            {
                var ok = xslam_get_left_image(handle.AddrOfPinnedObject(), width, height, 0, out _);
                if (ok) { _leftEyeStereoImage.LoadRawTextureData(_leftEyeImageBuffer); _leftEyeStereoImage.Apply(false); }
            }
            finally { handle.Free(); }
            return _leftEyeStereoImage;
        }

        public Texture2D GetRightEyeStereoImage()
        {
            var width = _rightEyeWidth ??= xslam_get_stereo_width();
            var height = _rightEyeHeight ??= xslam_get_stereo_height();
            if (width <= 1 || height <= 1)
            {
                if (_rightEyeStereoImage) UnityEngine.Object.Destroy(_rightEyeStereoImage);
                _rightEyeImageBuffer = null;
                _rightEyeWidth = null;
                _rightEyeHeight = null;
                return Texture2D.blackTexture;
            }
            
            _rightEyeStereoImage ??= new Texture2D(width, height, TextureFormat.BGRA32, mipChain: false);
            _rightEyeImageBuffer = new byte[width * height * 4];
            var handle = GCHandle.Alloc(_rightEyeImageBuffer, GCHandleType.Pinned);
            try
            {
                var ok = xslam_get_right_image(handle.AddrOfPinnedObject(), width, height, 0, out _);
                if (ok) { _rightEyeStereoImage.LoadRawTextureData(_rightEyeImageBuffer); _rightEyeStereoImage.Apply(false); }
            }
            finally { handle.Free(); }
            return _rightEyeStereoImage;
        }

        private void DequeueEvents()
        {
            while (xslam_dequeue(out var status))
            {
                switch (status)
                {
                    case XvisioSlamMapEvent.MapSaved:
                    case XvisioSlamMapEvent.MapSaveFailed:
                        MapSavedStatusChanged?.Invoke(
                            xslam_get_most_recent_save_status(), 
                            xslam_get_current_map_quality());
                        break;
                    case XvisioSlamMapEvent.CSlamSwitched:
                        CslamSwitched?.Invoke(xslam_get_current_map_quality());
                        break;
                    case XvisioSlamMapEvent.Localized:
                        Localized?.Invoke(xslam_get_current_map_visibility());
                        break;
                    case XvisioSlamMapEvent.StereoPlanesUpdated:
                        break;
                    case XvisioSlamMapEvent.ToFPlanesUpdated:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        /// <summary>
        /// Uninitializes the XVisio SLAM system, releasing any resources it holds.
        /// </summary>
        /// <returns>True if uninitialization was successful, otherwise false.</returns>
        public bool Uninitialize()
        {
            if (!_initialized)
                return true;
            _initialized = false;
            var uninit = xslam_uninit();
            return uninit;
        }

        /// <summary>
        /// Sets the type of SLAM to be used.
        /// </summary>
        /// <param name="type">The type of SLAM to set.</param>
        public void SetSlamType(XvisioSlamType type) => xslam_slam_type(type);

        /// <summary>
        /// Gets the current transform from the SLAM system.
        /// </summary>
        /// <param name="transform">The transform to apply the SLAM data to.</param>
        /// <param name="flipAxes">What axes we need to flip to match Unity orientation.</param>
        /// <returns>True if the transform was successfully applied, otherwise false.</returns>
        public bool TryApplyTransform(Transform transform)
        {
            if (!xslam_get_6dof(out var localPosition, out var localRotation, out var confidence, out var timestamp))
                return false;
            var flipAxes = EstimatedCameraModel switch
            {
                XvisioCamera.XR50 => XvisioOrientationFlipAxis.Z | XvisioOrientationFlipAxis.X180,
                XvisioCamera.DS80 => XvisioOrientationFlipAxis.X | XvisioOrientationFlipAxis.Z,
            };
            transform.SetLocalPositionAndRotation(
                ConvertXvPositionToUnity(localPosition),
                ConvertXvRotationToUnity(localRotation, flipAxes));
            return true;
        }

        private static Vector3 ConvertXvPositionToUnity(Vector3 xvPosition)
        {
            return -xvPosition;
        }

        private static Quaternion ConvertXvRotationToUnity(XvVector4d xvRotation, XvisioOrientationFlipAxis flipAxes)
        {
            var localEuler = new Quaternion((float)xvRotation.x, (float)xvRotation.y, (float)xvRotation.z, (float)xvRotation.w).eulerAngles;
            if (flipAxes.HasFlag(XvisioOrientationFlipAxis.X))
                localEuler.x = -localEuler.x;
            if (flipAxes.HasFlag(XvisioOrientationFlipAxis.Y))
                localEuler.y = -localEuler.y;
            if (flipAxes.HasFlag(XvisioOrientationFlipAxis.Z))
                localEuler.z = -localEuler.z;
            if (flipAxes.HasFlag(XvisioOrientationFlipAxis.X180))
                localEuler.x += 180;
            if (flipAxes.HasFlag(XvisioOrientationFlipAxis.Y180))
                localEuler.y += 180;
            if (flipAxes.HasFlag(XvisioOrientationFlipAxis.Z180))
                localEuler.z += 180;
            var localRotation = Quaternion.Euler(localEuler);
            return localRotation;
        }

        /// <summary>
        /// Loads the map at the specified path and switches the current SLAM to C-SLAM.
        /// </summary>
        /// <param name="path">The path to the saved map.</param>
        /// <returns>Whether the load operation has started.</returns>
        public bool LoadMapAndSwitchToCslam(string path)
        {
            if (!_initialized)
                return false;
            
            if (!xslam_ready())
                return false;

            if (!xslam_load_map_and_switch_to_cslam(path))
                return false;

            if (IsMapLoaded)
                ResetSlam();

            IsMapLoaded = true;
            return true;
        }

        /// <summary>
        /// Saves the map to the specified path and switches the current SLAM to C-SLAM.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool SaveMapAndSwitchToCslam(string path)
        {
            if (!_initialized) return false;
            return xslam_ready() && xslam_save_map_and_switch_to_cslam(path);
        }

        public bool StartSlam()
        {
            if (!_initialized)
                return false;
            return xslam_start_slam();
        }

        public bool ResetSlam()
        {
            if (!_initialized)
                return false;
            if (!xslam_reset_slam())
                return false;
            IsMapLoaded = false;
            SlamReset?.Invoke();
            return true;
        }

        public void Stop()
        {
            if (!xslam_uninit())
                return;
            IsMapLoaded = false;
            if (_leftEyeStereoImage) UnityEngine.Object.Destroy(_leftEyeStereoImage);
            if (_rightEyeStereoImage) UnityEngine.Object.Destroy(_rightEyeStereoImage);
            _leftEyeImageBuffer = null;
            _rightEyeImageBuffer = null;
        }
        
        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xv_device_model(out int model);

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_init();

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_ready();

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_uninit();

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_reset_slam();

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void xslam_slam_type(XvisioSlamType t);

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, CharSet = CharSet.Ansi)]
        private static extern bool xslam_load_map_and_switch_to_cslam(string path);

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, CharSet = CharSet.Ansi)]
        private static extern bool xslam_save_map_and_switch_to_cslam(string path);

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_dequeue(out XvisioSlamMapEvent outEvent);

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int xslam_get_current_map_quality();

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern XvisioMapSaveStatus xslam_get_most_recent_save_status();

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern float xslam_get_current_map_visibility();

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_get_left_image(IntPtr data, int width, int height, int flip, out double timestamp);

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_get_right_image(IntPtr data, int width, int height, int flip, out double timestamp);

        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int xslam_get_stereo_width();
        
        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int xslam_get_stereo_height();
        
        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_start_slam();

        private struct XvVector4d { public double x, y, z, w; }
        [DllImport(NativePackage, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern bool xslam_get_6dof(out Vector3 position, out XvVector4d orientation, out double confidence, out long timestamp);
    }
}