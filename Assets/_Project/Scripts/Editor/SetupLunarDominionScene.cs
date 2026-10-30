#if UNITY_EDITOR
using LunarDominion.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace LunarDominion.Editor
{
    /// <summary>Editor helper only. Adds the bootstrap to the open scene so
    /// you do not have to create the GameObject by hand.</summary>
    public static class SetupLunarDominionScene
    {
        [MenuItem("Lunar Dominion/Add Bootstrap to Scene")]
        public static void AddBootstrap()
        {
            var existing = Object.FindFirstObjectByType<LunarDominionBootstrap>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Lunar Dominion] Bootstrap already exists in this scene.");
                return;
            }
            var go = new GameObject("LunarDominionBootstrap");
            go.AddComponent<LunarDominionBootstrap>();
            Selection.activeGameObject = go;
            Debug.Log("[Lunar Dominion] Bootstrap added. Press Play.");
        }
    }
}
#endif
