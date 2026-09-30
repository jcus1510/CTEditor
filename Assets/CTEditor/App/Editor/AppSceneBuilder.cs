using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CTEditor.App.Editor
{
    /// <summary>
    /// Crea la escena de la APLICACIÓN (un objeto con AppRoot; todo lo demás lo crea AppRoot por código) y la abre en Play.
    /// La escena queda la primera en Build Settings para compilar la aplicación como programa de Windows.
    /// </summary>
    public static class AppSceneBuilder
    {
        public const string ScenePath = "Assets/CTEditor/App/Scenes/Aplicacion.unity";

        [MenuItem("CTEditor/Aplicación/Abrir la aplicación (Play)", false, 1)]
        public static void OpenAndPlay()
        {
            if (!File.Exists(ScenePath)) Build();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("CTEditor/Aplicación/Crear (o rehacer) la escena de la aplicación", false, 2)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Cámara");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
            cam.orthographic = true;
            camGo.tag = "MainCamera";

            new GameObject("CTEditor").AddComponent<AppRoot>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[CTEditor] Escena de la aplicación creada en {ScenePath} (primera en Build Settings). Pulsa Play para abrirla.");
        }

        [MenuItem("CTEditor/Aplicación/Abrir la carpeta del entorno de trabajo", false, 20)]
        public static void OpenWorkspaceFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
}
