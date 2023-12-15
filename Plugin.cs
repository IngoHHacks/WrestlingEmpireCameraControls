using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using WrestlingEmpireCameraControls.Internal;

namespace WrestlingEmpireCameraControls
{
    [BepInPlugin(PluginGuid, PluginName, PluginVer)]
    [HarmonyPatch]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "IngoH.WrestlingEmpire.WrestlingEmpireCameraControls";
        public const string PluginName = "A Mod That Lets You Control The Camera In Wrestling Empire";
        public const string PluginVer = "1.0.0";

        internal static ManualLogSource Log;
        internal readonly static Harmony Harmony = new(PluginGuid);

        internal static string PluginPath;

        private static ConfigEntry<bool> _enableCameraMouseControl;
        private static ConfigEntry<bool> _enableCameraKeyboardControl;
        private static ConfigEntry<bool> _enableCameraScrollControl;
        private static ConfigEntry<bool> _enableCameraControllerControl;
        private static ConfigEntry<bool> _invertCameraHorizontal;
        private static ConfigEntry<bool> _invertCameraVertical;
        private static ConfigEntry<bool> _invertCameraScroll;

        private static ConfigEntry<float> _horizontalSensitivity;
        private static ConfigEntry<float> _verticalSensitivity;
        private static ConfigEntry<float> _scrollSensitivity;
        private static ConfigEntry<string> _controllerTauntKey;

        private void Awake()
        {
            Plugin.Log = base.Logger;
            PluginPath = Path.GetDirectoryName(Info.Location);

            _enableCameraMouseControl =
                Config.Bind("Camera", "EnableMouseControl", true, "Enable mouse control of the camera");
            _enableCameraKeyboardControl = Config.Bind("Camera", "EnableKeyboardControl", true,
                "Enable keyboard control of the camera");
            _enableCameraScrollControl =
                Config.Bind("Camera", "EnableScrollControl", true, "Enable scroll control of the camera");
            _enableCameraControllerControl = Config.Bind("Camera", "EnableControllerControl", true,
                "Enable controller control of the camera");
            _invertCameraHorizontal =
                Config.Bind("Camera", "InvertHorizontal", false, "Invert horizontal camera control");
            _invertCameraVertical = Config.Bind("Camera", "InvertVertical", false, "Invert vertical camera control");
            _invertCameraScroll = Config.Bind("Camera", "InvertScroll", false, "Invert scroll camera control");

            _horizontalSensitivity = Config.Bind("Camera", "HorizontalSensitivity", 1f,
                new ConfigDescription("Horizontal camera control sensitivity",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            _verticalSensitivity = Config.Bind("Camera", "VerticalSensitivity", 1f,
                new ConfigDescription("Vertical camera control sensitivity",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            _scrollSensitivity = Config.Bind("Camera", "ScrollSensitivity", 1f,
                new ConfigDescription("Scroll camera control sensitivity", new AcceptableValueRange<float>(0.1f, 10f)));
            _controllerTauntKey = Config.Bind("Camera", "ControllerTauntKey", "RightTrigger",
                new ConfigDescription("Controller key to use for taunting if camera control is enabled",
                    new AcceptableValueList<string>("LeftTrigger", "RightTrigger", "LeftShoulder", "RightShoulder",
                        "LeftStickButton", "RightStickButton")));
        }

        private void OnEnable()
        {
            Harmony.PatchAll();
            Logger.LogInfo($"Loaded {PluginName}!");
        }

        private void OnDisable()
        {
            Harmony.UnpatchSelf();
            Logger.LogInfo($"Unloaded {PluginName}!");
        }

        private static int _camMoved;
        private static float _targetMagnitude;

        public static float TargetMagnitude
        {
            get => _targetMagnitude;
            set
            {
                if (_lockMagnitude)
                {
                    return;
                }

                var t = value;
                if (t < 5f)
                {
                    t = 5f;
                }
                else if (t > 1000f)
                {
                    t = 1000f;
                }

                var camPos = new Vector3(MappedCam.x, MappedCam.y, MappedCam.z);
                var camFoc = new Vector3(MappedCam.focX, MappedCam.focY, MappedCam.focZ);
                var camDir = camPos - camFoc;
                camDir = camDir.normalized * t;
                var camPosRot = camFoc + camDir;
                var origT = t;
                while (t > 6f && !ValidMovement(camPos.x, camPos.y, camPos.z, camPosRot.x, camPosRot.y, camPosRot.z))
                {
                    t -= 1f;
                    camDir = camDir.normalized * t;
                }
                if (t <= 6f && origT > 20f)
                {
                    t = origT - 5f;
                }

                _targetMagnitude = t;
            }
        }

        private static void ChangeTargetMagnitude(float change)
        {
            bool temp = _lockMagnitude;
            _lockMagnitude = false;
            TargetMagnitude += change * _scrollSensitivity.Value;
            _lockMagnitude = temp;
        }

#pragma warning disable Harmony003
        private static bool CamRotateHorizontal(int direction, float magnitude = 1f)
        {
            magnitude *= _horizontalSensitivity.Value;
            direction *= _invertCameraHorizontal.Value ? -1 : 1;
            var camPos = new Vector3(MappedCam.x, MappedCam.y, MappedCam.z);
            var camFoc = new Vector3(MappedCam.focX, MappedCam.focY, MappedCam.focZ);
            var camDir = camPos - camFoc;
            if (_camMoved <= 0)
            {
                TargetMagnitude = camDir.magnitude;
            }
            else
            {
                camDir = camDir.normalized * TargetMagnitude;
            }

            var camDirRot = Quaternion.AngleAxis(magnitude * direction, Vector3.up) * camDir;
            var camPosRot = camFoc + camDirRot;
            while (magnitude > 0.001f &&
                   !ValidMovement(camPos.x, camPos.y, camPos.z, camPosRot.x, camPosRot.y, camPosRot.z))
            {
                magnitude /= 2f;
                camDirRot = Quaternion.AngleAxis(magnitude * direction, Vector3.up) * camDir;
                camPosRot = camFoc + camDirRot;
            }

            _camMoved = 1 + (Application.targetFrameRate / 10);
            if (magnitude > 0.001f)
            {
                MappedCam.x = camPosRot.x;
                MappedCam.y = camPosRot.y;
                MappedCam.z = camPosRot.z;
                return true;
            }

            return false;
        }

        private static bool CamRotateVertical(int direction, float magnitude = 1f)
        {
            magnitude *= _verticalSensitivity.Value;
            direction *= _invertCameraVertical.Value ? -1 : 1;
            var camPos = new Vector3(MappedCam.x, MappedCam.y, MappedCam.z);
            var camFoc = new Vector3(MappedCam.focX, MappedCam.focY, MappedCam.focZ);
            var camDir = camPos - camFoc;
            if (_camMoved <= 0)
            {
                TargetMagnitude = camDir.magnitude;
            }
            else
            {
                camDir = camDir.normalized * TargetMagnitude;
            }

            var camDirRot = Quaternion.AngleAxis(magnitude * direction, Vector3.Cross(camDir, Vector3.up)) * camDir;
            var camPosRot = camFoc + camDirRot;
            var yRot = Quaternion.LookRotation(camDir, Vector3.up).eulerAngles.y;
            var yRotRot = Quaternion.LookRotation(camDirRot, Vector3.up).eulerAngles.y;
            bool a = true;
            while (magnitude > 0.001f && (Math.Abs(yRot - yRotRot) > 90f ||
                                          !ValidMovement(camPos.x, camPos.y, camPos.z, camPosRot.x, camPosRot.y,
                                              camPosRot.z)))
            {
                magnitude /= 2f;
                camDirRot = Quaternion.AngleAxis(magnitude * direction, Vector3.Cross(camDir, Vector3.up)) * camDir;
                camPosRot = camFoc + camDirRot;
            }

            _camMoved = 1 + (Application.targetFrameRate / 10);
            if (magnitude > 0.001f)
            {
                MappedCam.x = camPosRot.x;
                MappedCam.y = camPosRot.y;
                MappedCam.z = camPosRot.z;
                return true;
            }

            a = Math.Abs(yRot - yRotRot) > 90f;
            return a;
        }

        private static void UpdateMagnitude()
        {
            var camPos = new Vector3(MappedCam.x, MappedCam.y, MappedCam.z);
            var camFoc = new Vector3(MappedCam.focX, MappedCam.focY, MappedCam.focZ);
            var camDir = camPos - camFoc;
            camDir = camDir.normalized * TargetMagnitude;
            var camPosRot = camFoc + camDir;
            MappedCam.x = camPosRot.x;
            MappedCam.y = camPosRot.y;
            MappedCam.z = camPosRot.z;
        }
#pragma warning restore Harmony003

        private static Vector3 _prevCamPos;
        private static bool _lockMagnitude;
        private static bool _lockCamera;

        [HarmonyPatch(typeof(BLNKDHIGFAN), nameof(BLNKDHIGFAN.DIJBHIAAIOF))]
        [HarmonyPrefix]
        private static void BLNKDHIGFAN_DIJBHIAAIOF()
        {
            if (_enableCameraKeyboardControl.Value && SceneManager.GetActiveScene().name == "Game")
            {
                float mul = 60f;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    mul *= 4f;
                }

                if (Input.GetKey(KeyCode.I))
                {
                    var origT = TargetMagnitude;
                    while (!CamRotateVertical(1, mul / Application.targetFrameRate) && TargetMagnitude > 5f &&
                        !_lockMagnitude)
                    {
                        TargetMagnitude -= 1f;
                    }
                    if (TargetMagnitude <= 5f && origT > 20f)
                    {
                        TargetMagnitude = origT - 5f;
                    }
                }

                if (Input.GetKey(KeyCode.K))
                {
                    var origT = TargetMagnitude;
                    while (!CamRotateVertical(-1, mul / Application.targetFrameRate) && TargetMagnitude > 5f &&
                        !_lockMagnitude)
                    {
                        TargetMagnitude -= 1f;
                    }
                    if (TargetMagnitude <= 5f && origT > 20f)
                    {
                        TargetMagnitude = origT - 5f;
                    }
                }

                if (Input.GetKey(KeyCode.J))
                {
                    var origT = TargetMagnitude;
                    while (!CamRotateHorizontal(1, mul / Application.targetFrameRate) && TargetMagnitude > 5f &&
                        !_lockMagnitude)
                    {
                        TargetMagnitude -= 1f;
                    }
                    if (TargetMagnitude <= 5f && origT > 20f)
                    {
                        TargetMagnitude = origT - 5f;
                    }
                }

                if (Input.GetKey(KeyCode.L))
                {
                    var origT = TargetMagnitude;
                    while (!CamRotateHorizontal(-1, mul / Application.targetFrameRate) && TargetMagnitude > 5f &&
                        !_lockMagnitude)
                    {
                        TargetMagnitude -= 1f;
                    }
                    if (TargetMagnitude <= 5f && origT > 20f)
                    {
                        TargetMagnitude = origT - 5f;
                    }
                }

                if (Input.GetKey(KeyCode.Equals))
                {
                    ChangeTargetMagnitude(-5f);
                    _camMoved = 1 + (Application.targetFrameRate / 10);
                    UpdateMagnitude();
                }

                if (Input.GetKey(KeyCode.Minus))
                {
                    ChangeTargetMagnitude(5f);
                    _camMoved = 1 + (Application.targetFrameRate / 10);
                    UpdateMagnitude();
                }

                if (Input.GetKeyDown(KeyCode.M))
                {
                    var camPos = new Vector3(MappedCam.x, MappedCam.y, MappedCam.z);
                    var camFoc = new Vector3(MappedCam.focX, MappedCam.focY, MappedCam.focZ);
                    var camDir = camPos - camFoc;
                    TargetMagnitude = camDir.magnitude;
                    _lockMagnitude = !_lockMagnitude;
                    MappedMatch.PostComment("Distance Locked: " + (_lockMagnitude ? "Yes" : "No"));
                }
                
                if (Input.GetKeyDown(KeyCode.N))
                {
                    _lockCamera = !_lockCamera;
                    MappedMatch.PostComment("Camera Locked: " + (_lockCamera ? "Yes" : "No"));
                }
            }

            if (_enableCameraMouseControl.Value)
            {
                if (Input.GetMouseButtonDown((int)MouseButton.MiddleMouse))
                {
                    _lockMagnitude = !_lockMagnitude;
                }

                if (Input.GetMouseButton((int)MouseButton.RightMouse))
                {
                    if (Math.Abs(Input.GetAxis("Mouse X")) > 0.001f)
                    {
                        var origT = TargetMagnitude;
                        while (!CamRotateHorizontal(Input.GetAxis("Mouse X") > 0 ? 1 : -1,
                                10f * Math.Abs(Input.GetAxis("Mouse X")) / Application.targetFrameRate) &&
                            TargetMagnitude > 5f && !_lockMagnitude)
                        {
                            TargetMagnitude -= 1f;
                        }
                        if (TargetMagnitude <= 5f && origT > 20f)
                        {
                            TargetMagnitude = origT - 5f;
                        }
                    }

                    if (Math.Abs(Input.GetAxis("Mouse Y")) > 0.001f)
                    {
                        var origT = TargetMagnitude;
                        while (!CamRotateVertical(-Input.GetAxis("Mouse Y") > 0 ? 1 : -1,
                                   10f * Math.Abs(Input.GetAxis("Mouse Y")) / Application.targetFrameRate) &&
                               TargetMagnitude > 5f && !_lockMagnitude)
                        {
                            TargetMagnitude -= 1f;
                        }
                        if (TargetMagnitude <= 5f && origT > 20f)
                        {
                            TargetMagnitude = origT - 5f;
                        }
                    }
                }
            }

            if (_enableCameraScrollControl.Value)
            {
                if (Input.mouseScrollDelta.y != 0)
                {
                    var camPos = new Vector3(MappedCam.x, MappedCam.y, MappedCam.z);
                    var camFoc = new Vector3(MappedCam.focX, MappedCam.focY, MappedCam.focZ);
                    var camDir = camPos - camFoc;
                    if (_camMoved <= 0)
                    {
                        TargetMagnitude = camDir.magnitude;
                    }

                    ChangeTargetMagnitude(Input.mouseScrollDelta.y * -5f * (_invertCameraScroll.Value ? -1 : 1));
                    _camMoved = 1 + (Application.targetFrameRate / 10);
                    UpdateMagnitude();
                }
            }

            if (_camMoved > 0 && _camMoved < 1 + (Application.targetFrameRate / 10))
            {
                if (MappedWorld.Inside(MappedCam.x, MappedCam.y, MappedCam.z) == 0)
                {
                    TargetMagnitude -= 2f;
                    UpdateMagnitude();
                }
                UpdateMagnitude();
            }
            else if (_lockMagnitude)
            {
                UpdateMagnitude();
            }

            _prevCamPos = new Vector3(MappedCam.x, MappedCam.y, MappedCam.z);
        }

        [HarmonyPatch(typeof(BLNKDHIGFAN), nameof(BLNKDHIGFAN.DIJBHIAAIOF))]
        [HarmonyPostfix]
        private static void BLNKDHIGFAN_DIJBHIAAIOF_Post()
        {
            if (_camMoved-- > 0 || _lockCamera)
            {
                MappedCam.handle.transform.position = _prevCamPos;
                MappedCam.x = _prevCamPos.x;
                MappedCam.y = _prevCamPos.y;
                MappedCam.z = _prevCamPos.z;
                if (MappedCam.type != 5)
                {
                    MappedCam.handle.transform.LookAt(new Vector3(MappedCam.focX, MappedCam.focY, MappedCam.focZ));
                }
            }
        }

        private enum ControllerType
        {
            CPU = -1,
            Virtual = 0,
            Keyboard = 1,
            SteamDeck = 2,
            Console_Xbox = 3,
            Console_Gamepad = 4,
            Xbox = 5,
            Gamepad = 6,
            CustomSlot = 7,
        }

        [HarmonyPatch(typeof(BJMGCKGNCHO), nameof(BJMGCKGNCHO.NCOEPCFFBJA))]
        [HarmonyPostfix]
        private static void BJMGCKGNCHO_NCOEPCFFBJA(BJMGCKGNCHO __instance)
        {
            MappedController _controller = __instance;

            if (!_enableCameraControllerControl.Value || _controller.type == (int)ControllerType.CPU ||
                _controller.type == (int)ControllerType.Virtual ||
                _controller.type == (int)ControllerType.Keyboard)
            {
                return;
            }

            float rightStickX = _controller.rightStickX;
            float rightStickY = _controller.rightStickY;
            if (rightStickX != 0)
            {
                var origT = TargetMagnitude;
                while (!CamRotateHorizontal(-rightStickX > 0 ? 1 : -1,
                       60f / Application.targetFrameRate * Math.Abs(rightStickX)) && TargetMagnitude > 5f &&
                   !_lockMagnitude)
                {
                    TargetMagnitude -= 1f;
                }
                if (TargetMagnitude <= 5f && origT > 20f)
                {
                    TargetMagnitude = origT - 5f;
                }
            }
            if (rightStickY != 0)
            {
                var origT = TargetMagnitude;
                while (!CamRotateVertical(rightStickY > 0 ? 1 : -1,
                           60f / Application.targetFrameRate * Math.Abs(rightStickY)) && TargetMagnitude > 5f &&
                       !_lockMagnitude)
                {
                    TargetMagnitude -= 1f;
                }
                if (TargetMagnitude <= 5f && origT > 20f)
                {
                    TargetMagnitude = origT - 5f;
                }
            }

            switch (_controllerTauntKey.Value)
            {
                case "LeftTrigger":
                    if (_controller.leftTrigger != 0 && _controller.rightTrigger == 0)
                    {
                        _controller.button[5] = 1;
                        _controller.leftTrigger = 0;
                    }
                    else
                    {
                        _controller.button[4] = 0;
                    }

                    break;
                case "LeftShoulder":
                    if (_controller.leftShoulder != 0 && _controller.rightShoulder == 0)
                    {
                        _controller.button[5] = 1;
                        _controller.leftShoulder = 0;
                    }
                    else
                    {
                        _controller.button[5] = 0;
                    }

                    break;
                case "RightShoulder":
                    if (_controller.rightShoulder != 0 && _controller.leftShoulder == 0)
                    {
                        _controller.button[5] = 1;
                        _controller.rightShoulder = 0;
                    }
                    else
                    {
                        _controller.button[5] = 0;
                    }

                    break;
                case "LeftStickButton":
                    if (_controller.leftStickButton != 0 && _controller.rightStickButton == 0)
                    {
                        _controller.button[5] = 1;
                        _controller.leftStickButton = 0;
                    }
                    else
                    {
                        _controller.button[5] = 0;
                    }

                    break;
                case "RightStickButton":
                    if (_controller.rightStickButton != 0 && _controller.leftStickButton == 0)
                    {
                        _controller.button[5] = 1;
                        _controller.rightStickButton = 0;
                    }
                    else
                    {
                        _controller.button[5] = 0;
                    }

                    break;
                default:
                    if (_controller.rightTrigger != 0 && _controller.leftTrigger == 0)
                    {
                        _controller.button[5] = 1;
                        _controller.rightTrigger = 0;
                    }
                    else
                    {
                        _controller.button[5] = 0;
                    }

                    break;
            }
        }

        private static bool ValidMovement(float srcX, float srcY, float srcZ, float dstX, float dstY, float dstZ)
        {
            if (MappedWorld.Inside(srcX, srcY, srcZ) == 0)
            {
                return true;
            }
            if (MappedWorld.Inside(dstX, dstY, dstZ) == 0)
            {
                return false;
            }
            float x = srcX;
            float y = srcY;
            float z = srcZ;
            float dx = (dstX - srcX) / 10f;
            float dy = (dstY - srcY) / 10f;
            float dz = (dstZ - srcZ) / 10f;
            for (int i = 0; i < 10; i++)
            {
                x += dx;
                y += dy;
                z += dz;
                if (MappedWorld.Inside(x, y, z) == 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}