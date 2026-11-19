using UnityEditor;

namespace Pano2StereoVR.Study.Editor
{
    /// <summary>
    /// Script edits saved while the study is in Play mode must not reload the domain mid-session. On 11-19 a
    /// script added during a desktop walkthrough triggered "recompile and continue playing": the reload cleared
    /// the controller's non-serialised runtime state (settings, session coroutine), and Update threw
    /// NullReferenceException every frame. Assembly reloads are therefore locked for the duration of Play mode;
    /// pending script changes are compiled and loaded after Play mode ends.
    /// </summary>
    [InitializeOnLoad]
    internal static class StudyPlayModeReloadLock
    {
        private static bool _locked;

        static StudyPlayModeReloadLock()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && !_locked)
            {
                EditorApplication.LockReloadAssemblies();
                _locked = true;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && _locked)
            {
                EditorApplication.UnlockReloadAssemblies();
                _locked = false;
            }
        }
    }
}
