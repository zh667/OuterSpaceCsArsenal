using UnityEngine;

// The game looks this type up by its exact global name ("ModPluginLauncher") and calls launcher().
public class ModPluginLauncher : BaseModLauncher
{
    public override void launcher()
    {
        base.launcher();
        setDefLoader(new CsArsenal.CsDefLoader(config));
        CsArsenal.CsPatches.Apply();   // Harmony is loaded first: manifest dependency com.harmony
        if (CsArsenal.ModBehaviour.Instance != null) return;
        var go = new GameObject("CsArsenal");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<CsArsenal.ModBehaviour>();
        Debug.Log("[CsArsenal] loaded " + CsArsenal.ModBehaviour.Version);
    }
}
