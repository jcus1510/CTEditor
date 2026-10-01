using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Workspace;
#if CTEDITOR_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CTEditor.App
{
    /// <summary>
    /// Punto de entrada de la APLICACIÓN CTEditor. Va en un objeto de la escena de la aplicación (CTEditor → Aplicación →
    /// Crear escena). Crea todo por código: la ventana a pantalla completa con la resolución del monitor, el panel de UI
    /// Toolkit con la escala del usuario (y la de Windows), el sistema de eventos de entrada y la interfaz (AppShell).
    /// </summary>
    public sealed class AppRoot : MonoBehaviour
    {
        [Tooltip("Arrancar a pantalla completa (F11 cambia a ventana).")]
        [SerializeField] private bool startFullscreen = true;

        private PanelSettings _panel;
        private AppShell _shell;
        private UIDocument _document;
        /// <summary>Our own root inside the document: it survives the document recreating its tree.</summary>
        private VisualElement _host;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            if (!Application.isEditor && startFullscreen)
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            EnsureCamera();
            EnsureEventSystem();
        }

        private void Start()
        {
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "CTEditor (interfaz)";
            _panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("CTEditorApp/Tema");
            if (_panel.themeStyleSheet == null)
                Debug.LogWarning("[CTEditor] No se encontró Resources/CTEditorApp/Tema.tss: los controles se verán sin estilo base.");
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.clearColor = true;

            var uiObject = new GameObject("Interfaz");
            uiObject.transform.SetParent(transform, false);
            uiObject.SetActive(false);
            _document = uiObject.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
            uiObject.SetActive(true);

            _host = new VisualElement { name = "CTEditor" };
            _host.style.position = Position.Absolute;
            _host.style.left = 0; _host.style.top = 0; _host.style.right = 0; _host.style.bottom = 0;
            AttachHost();
            var workspacePath = Path.Combine(Application.persistentDataPath, WorkspaceSettings.FileName);
            _shell = new AppShell(_host, workspacePath)
            {
                ScaleChanged = ApplyScale,
                ToggleFullscreen = ToggleFullscreen,
                IsFullscreen = () => Screen.fullScreenMode != FullScreenMode.Windowed,
                Quit = QuitApp,
            };
            ApplyScale(_shell.Workspace.UiScale);
            _shell.Rebuild(); // icons are drawn for the real scale, known only now
            ApplyClearColor();
            // The main camera only clears the screen: maps are drawn by their own cameras into textures.
            if (Camera.main != null) Camera.main.cullingMask = 0;
        }

        /// <summary>
        /// The UIDocument rebuilds its tree when it is inspected or validated in the Unity editor (clicking «Interfaz» in
        /// the Hierarchy): the interface then vanished. We keep everything in our own element and put it back.
        /// </summary>
        private void AttachHost()
        {
            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null || _host.parent == root) return;
            _host.RemoveFromHierarchy();
            root.style.flexGrow = 1;
            root.Add(_host);
        }

        private void ApplyScale(float userScale)
        {
            float dpi = Screen.dpi > 0 ? Screen.dpi / 96f : 1f;
            _panel.scale = Mathf.Clamp(userScale * dpi, 0.5f, 4f);
            Ui.PixelsPerPoint = _panel.scale;
            if (_shell != null) _shell.PixelsPerPoint = _panel.scale;
            ApplyClearColor();
        }

        private void ApplyClearColor()
        {
            if (_panel != null) _panel.colorClearValue = Ui.C("fondo");
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = Ui.C("fondo");
        }

        private void ToggleFullscreen()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            else
                Screen.SetResolution(Mathf.RoundToInt(Display.main.systemWidth * 0.8f), Mathf.RoundToInt(Display.main.systemHeight * 0.8f),
                    FullScreenMode.Windowed);
        }

        private void QuitApp()
        {
            _shell?.Shutdown();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Update()
        {
            if (_host != null) AttachHost();
            _shell?.Tick(Time.unscaledDeltaTime);
#if CTEDITOR_INPUT_SYSTEM
            // Function keys work even when no field has the keyboard focus (UI Toolkit only sends keys to a focused element).
            var kb = Keyboard.current;
            if (kb == null || _shell == null) return;
            if (_shell.Root.panel?.focusController?.focusedElement != null) return; // AppShell already gets the keys
            if (kb.f11Key.wasPressedThisFrame) _shell.RunAction("pantalla_completa");
            if (kb.f5Key.wasPressedThisFrame) _shell.RunAction(kb.ctrlKey.isPressed ? "probar_aqui" : "jugar");
            if (kb.ctrlKey.isPressed && kb.sKey.wasPressedThisFrame) _shell.RunAction("guardar");
#endif
        }

        private void OnApplicationQuit() => _shell?.Shutdown();

        private void OnDestroy()
        {
            if (_panel != null) Destroy(_panel);
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Cámara");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
            cam.orthographic = true;
            go.tag = "MainCamera";
        }

        /// <summary>
        /// UI Toolkit needs an EventSystem with the Input System module when only the new Input System is active (this
        /// project). Created by reflection so the app does not depend on uGUI at compile time.
        /// </summary>
        private static void EnsureEventSystem()
        {
            var esType = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
            if (esType == null || FindFirstObjectByType(esType) != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent(esType);
            var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem")
                         ?? Type.GetType("UnityEngine.EventSystems.StandaloneInputModule, UnityEngine.UI");
            if (module != null) go.AddComponent(module);
        }
    }
}
