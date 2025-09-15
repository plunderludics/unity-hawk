// [This component is only used at build-time, but out of convenience
//  it gets included in the build so we put it in the Runtime assembly]
// [Probably some way to strip it out of build I guess?]
using UnityEngine;
using System.Collections.Generic;
using NaughtyAttributes;

using UnityEngine.SceneManagement;
using System.Linq;

namespace UnityHawk {

/// <summary>
/// Used to configure build settings for the UnityHawk package.
/// At most one BuildSettings component should be present in each scene.
/// </summary>
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

    [SerializeField]
    [Tooltip("The minimum log level for BuildProcessing logging")]
    Logger.LogLevel logLevel = Logger.LogLevel.Warning;

    // Thought - we could also have multiple ExtraAssets components in the scene,
    // it could be easier to manage in some ways than a single global list. Maybe multiple ExcludeFromBuild components too
    // But ideally still need a place for global config - seems overcomplicated maybe

#if UNITY_EDITOR

    void OnValidate() {
        Refresh();
    }

    void Refresh() {
        includedAssets = null; // Clear this so it doesn't get picked up by CollectBizhawkAssetDependencies
        var dependencies = BuildProcessing.CollectBizhawkAssetDependencies();
        includedAssets = dependencies.ToList();
    }
#endif // UNITY_EDITOR
}

}
