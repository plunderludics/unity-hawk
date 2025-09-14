#if UNITY_EDITOR // This component only used in editor

using UnityEngine;
using System.Collections.Generic;
using NaughtyAttributes;

using UnityEngine.SceneManagement;
using System.Linq;

namespace UnityHawk {

[ExecuteInEditMode]
public class BuildSettings : MonoBehaviour {
    [Tooltip("Whether to include inactive objects and disabled components when searching for asset dependencies")]
    public bool includeInactive = true;

    // TODO: option for excluding transforms from dependency collection ?

    [Tooltip("Extra bizhawk assets to include in the build")]
    public List<BizhawkAsset> extraAssets;
    
    [ReadOnly, SerializeField]
    [Tooltip("These are all the bizhawk assets that will be included in the build")]
    List<BizhawkAsset> includedAssets;

    // Thought - we could also have multiple ExtraAssets components in the scene,
    // it could be easier to manage in some ways than a single global list. Maybe multiple ExcludeFromBuild components too
    // But ideally still need a place for global config - seems overcomplicated maybe

    void OnValidate() {
        Refresh();
    }

    void Refresh() {
        includedAssets = null; // Clear this so it doesn't get picked up by CollectBizhawkAssetDependencies
        var dependencies = BuildProcessing.CollectBizhawkAssetDependencies();
        Debug.Log($"[unity-hawk] BuildSettings Refresh: {dependencies.Count} dependencies");
        includedAssets = dependencies.ToList();
    }
}

}

#endif // UNITY_EDITOR