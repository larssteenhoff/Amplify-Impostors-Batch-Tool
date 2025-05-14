#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using AmplifyImpostors;
using System.Linq;
using System;
using TheVisualEngine;

public class AmplifyImpostorBatchWindow : EditorWindow
{
    private List<GameObject> prefabList = new List<GameObject>();
    private Vector2 scrollPos;
    private AmplifyImpostorAsset globalSettings;
    private string status = "";
    // private enum BatchMode { ModifyPrefabs, BakeOnly }
    private enum BatchMode { ModifyPrefabs /*, BakeOnly*/ } // 'BakeOnly' commented out
    // private BatchMode batchMode = BatchMode.BakeOnly;
    private BatchMode batchMode = BatchMode.ModifyPrefabs; // Default to ModifyPrefabs
    private string lastException = null;
    private bool shouldRunBatch = false;
    private List<string> batchResults = new List<string>();
    private int batchProgressIndex = 0;
    private int batchTotal = 0;
    private bool isBatchRunning = false;
    private enum OutputFolderMode { NextToPrefab, NextToImpostorProfile, Custom }
    private OutputFolderMode outputFolderMode = OutputFolderMode.NextToPrefab;
    private string customOutputFolder = "";
    private bool addLodIfMissing = true;
    private bool animateCrossFading = true;
    private bool setStaticExceptBatching = false;
    private int lodFadeMode = 1; // 0=None, 1=CrossFade, 2=SpeedTree

    [MenuItem("Window/Amplify Impostors/Batch Converter", false, 2002)]
    public static void ShowWindow()
    {
        var window = GetWindow<AmplifyImpostorBatchWindow>("Amplify Impostors Batch Converter");
        window.minSize = new Vector2(500, 400);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += EditorUpdate;
    }
    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        EditorUtility.ClearProgressBar();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Batch Convert Prefabs to Impostors", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Mode selection
        EditorGUILayout.LabelField("Batch Mode:", EditorStyles.label);
        // batchMode = (BatchMode)EditorGUILayout.EnumPopup("Mode", batchMode);
        // string modeDesc = batchMode == BatchMode.ModifyPrefabs ?
        //     "Modify Prefabs: The original prefabs will be updated with the impostor component and settings." :
        //     "Bake Only: Impostors will be baked, but the original prefabs will not be modified.";
        // EditorGUILayout.HelpBox(modeDesc, MessageType.Info);
        // EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Modify Prefabs: The original prefabs will be updated with the impostor component and settings.", MessageType.Info);
        EditorGUILayout.Space();

        // Global impostor settings
        EditorGUILayout.LabelField("Global Impostor Settings", EditorStyles.boldLabel);
        globalSettings = (AmplifyImpostorAsset)EditorGUILayout.ObjectField(
            "Impostor Asset Preset",
            globalSettings,
            typeof(AmplifyImpostorAsset),
            false
        );
        if (globalSettings == null)
        {
            EditorGUILayout.HelpBox("Assign an AmplifyImpostorAsset to use as the global settings preset.", MessageType.Warning);
        }
        setStaticExceptBatching = EditorGUILayout.ToggleLeft("Set Static (except Static Batching)", setStaticExceptBatching);
        // LOD Fade Mode dropdown
        lodFadeMode = EditorGUILayout.Popup("LOD Fade Mode", lodFadeMode, new[] { "None", "CrossFade", "SpeedTree" });
        if (lodFadeMode == 1 || lodFadeMode == 2)
        {
            animateCrossFading = EditorGUILayout.ToggleLeft("Animate Cross Fading", animateCrossFading);
        }

        // Output folder selection for Bake Only mode (moved here, before prefab list)
        // if (batchMode == BatchMode.BakeOnly)
        // {
        //     EditorGUILayout.Space();
        //     EditorGUILayout.LabelField("Output Folder for Impostor Assets", EditorStyles.boldLabel);
        //     outputFolderMode = (OutputFolderMode)EditorGUILayout.EnumPopup("Save Location", outputFolderMode);
        //     if (outputFolderMode == OutputFolderMode.Custom)
        //     {
        //         EditorGUILayout.BeginHorizontal();
        //         EditorGUILayout.LabelField("Custom Folder:", GUILayout.Width(100));
        //         customOutputFolder = EditorGUILayout.TextField(customOutputFolder);
        //         if (GUILayout.Button("...", GUILayout.Width(30)))
        //         {
        //             string selected = EditorUtility.OpenFolderPanel("Select Output Folder", string.IsNullOrEmpty(customOutputFolder) ? Application.dataPath : customOutputFolder, "");
        //             if (!string.IsNullOrEmpty(selected))
        //             {
        //                 // Convert absolute path to relative project path if possible
        //                 if (selected.StartsWith(Application.dataPath))
        //                 {
        //                     customOutputFolder = "Assets" + selected.Substring(Application.dataPath.Length);
        //                 }
        //                 else
        //                 {
        //                     customOutputFolder = selected;
        //                 }
        //             }
        //         }
        //         EditorGUILayout.EndHorizontal();
        //         EditorGUILayout.HelpBox($"Impostor assets will be saved to: {customOutputFolder}", MessageType.Info);
        //     }
        //     else if (outputFolderMode == OutputFolderMode.NextToImpostorProfile && globalSettings != null)
        //     {
        //         string impostorProfilePath = AssetDatabase.GetAssetPath(globalSettings);
        //         string impostorProfileDir = !string.IsNullOrEmpty(impostorProfilePath) ? System.IO.Path.GetDirectoryName(impostorProfilePath) : "";
        //         EditorGUILayout.HelpBox($"Impostor assets will be saved next to the assigned impostor profile: {impostorProfileDir}", MessageType.Info);
        //     }
        //     else
        //     {
        //         EditorGUILayout.HelpBox("Impostor assets will be saved next to each prefab.", MessageType.Info);
        //     }
        // }

        EditorGUILayout.Space();
        EditorGUI.BeginDisabledGroup(isBatchRunning);
        if (GUILayout.Button("Bake Impostors", GUILayout.Height(32)))
        {
            if (globalSettings == null)
            {
                status = "Please assign a global impostor settings asset.";
            }
            else if (prefabList.Count == 0)
            {
                status = "Please add at least one prefab.";
            }
            else
            {
                shouldRunBatch = true;
            }
        }
        EditorGUI.EndDisabledGroup();

        // Remove per-prefab results from the window, just show a summary message
        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.HelpBox(status, MessageType.Info);
        }
        if (!string.IsNullOrEmpty(lastException))
        {
            EditorGUILayout.HelpBox(lastException, MessageType.Error);
        }
        // Only show a summary message after batch
        if (isBatchRunning == false && batchResults.Count > 0)
        {
            EditorGUILayout.HelpBox("Batch complete. Check the Console for details.", MessageType.Info);
        }
        if ((isBatchRunning == false && batchResults.Count > 0) || !string.IsNullOrEmpty(lastException) || !string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Clear Results"))
            {
                batchResults.Clear();
                lastException = null;
                status = "";
                Repaint();
            }
        }

        EditorGUILayout.Space();
        // Drag and drop area
        EditorGUILayout.LabelField("Drag Prefabs Here:", EditorStyles.label);
        Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop Prefabs Here", EditorStyles.helpBox);
        HandleDragAndDrop(dropArea);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefabs to Convert:", EditorStyles.label);
        // Prefab list controls
        if (prefabList.Count > 0)
        {
            if (GUILayout.Button("Clear Prefab List"))
            {
                prefabList.Clear();
                Repaint();
            }
        }
        // Make the list fill the rest of the window
        float listHeight = Mathf.Max(120, position.height - 320);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true), GUILayout.Height(listHeight));
        for (int i = 0; i < prefabList.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            prefabList[i] = (GameObject)EditorGUILayout.ObjectField(prefabList[i], typeof(GameObject), false);
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                prefabList.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        // Add LODGroup option
        addLodIfMissing = EditorGUILayout.ToggleLeft("Add LODGroup if missing and set up LOD0/LOD1", addLodIfMissing);
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition))
            return;

        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is GameObject go && PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab)
                    {
                        if (!prefabList.Contains(go))
                            prefabList.Add(go);
                    }
                }
            }
            Event.current.Use();
        }
    }

    private void EditorUpdate()
    {
        if (shouldRunBatch && !isBatchRunning)
        {
            shouldRunBatch = false;
            isBatchRunning = true;
            batchProgressIndex = 0;
            batchTotal = prefabList.Count;
            batchResults.Clear();
            status = "Starting batch conversion...";
            lastException = null;
            EditorApplication.delayCall += RunBatchConvert;
        }
    }

    private void RunBatchConvert()
    {
        int success = 0, fail = 0;
        for (int idx = 0; idx < prefabList.Count; idx++)
        {
            batchProgressIndex = idx + 1;
            float progress = (float)batchProgressIndex / Mathf.Max(1, batchTotal);
            string progressInfo = $"Processing {idx + 1}/{batchTotal}: {prefabList[idx]?.name ?? "<null>"}";
            EditorUtility.DisplayProgressBar("Amplify Impostors Batch", progressInfo, progress);

            var prefab = prefabList[idx];
            if (prefab == null)
            {
                batchResults.Add($"[{idx}] Null prefab in list.");
                Debug.LogError($"[{idx}] Null prefab in list.");
                fail++;
                continue;
            }
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                batchResults.Add($"[{idx}] Prefab '{prefab.name}' has no valid asset path.");
                Debug.LogError($"[{idx}] Prefab '{prefab.name}' has no valid asset path.");
                fail++;
                continue;
            }
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                LODGroup lodGroup = instance.GetComponent<LODGroup>();
                if (instance == null)
                {
                    batchResults.Add($"[{idx}] Could not instantiate prefab: {prefab.name}");
                    Debug.LogError($"[{idx}] Could not instantiate prefab: {prefab.name}");
                    fail++;
                    continue;
                }
                // --- FIX: Define and populate sourceRenderers and lod0Child here ---
                Renderer[] sourceRenderers = null;
                Transform lod0Child = null;
                // Enhanced: Prefer child named 'LOD 0' for source, else all valid mesh/skinned renderers
                var allRenderers = instance.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r != null && r.enabled)
                    .Where(r => {
                        var mf = r.GetComponent<MeshFilter>();
                        return mf != null && mf.sharedMesh != null;
                    })
                    .Cast<Renderer>()
                    .ToList();
                // Optionally, include SkinnedMeshRenderers with a valid mesh
                allRenderers.AddRange(instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r != null && r.enabled && r.sharedMesh != null)
                    .Cast<Renderer>());
                sourceRenderers = allRenderers.ToArray();

                // If you want to prefer a child named 'LOD 0', do this:
                lod0Child = instance.transform.Find("LOD0");
                if (lod0Child != null)
                {
                    sourceRenderers = lod0Child.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(r => r != null && r.enabled)
                        .Where(r => {
                            var mf = r.GetComponent<MeshFilter>();
                            return mf != null && mf.sharedMesh != null;
                        })
                        .Cast<Renderer>()
                        .ToArray();
                }

                // If LODGroup exists, use LOD0's renderers as source (overrides above)
                if (lodGroup != null)
                {
                    var lods = lodGroup.GetLODs();
                    if (lods.Length > 0 && lods[0].renderers != null && lods[0].renderers.Length > 0)
                        sourceRenderers = lods[0].renderers;
                }
                else if (addLodIfMissing)
                {
                    // Add LODGroup and set up LOD0/LOD1
                    lodGroup = instance.AddComponent<LODGroup>();
                    LOD[] lods = new LOD[2];
                    lods[0] = new LOD(0.5f, sourceRenderers); // LOD0: source mesh
                    lods[1] = new LOD(0.01f, new Renderer[0]); // LOD1: impostor will be assigned after bake
                    lodGroup.SetLODs(lods);
                }

                // Defensive: skip if no valid renderers
                if (sourceRenderers == null || sourceRenderers.Length == 0)
                {
                    Debug.LogError($"No valid mesh renderers with meshes found for prefab: {prefab.name}. Skipping impostor bake.");
                    continue;
                }

                // Ensure the source mesh is set for each renderer
                foreach (var renderer in sourceRenderers)
                {
                    var meshRendererTmp = renderer as MeshRenderer;
                    if (meshRendererTmp != null)
                    {
                        var meshFilter = meshRendererTmp.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh == null)
                        {
                            // Try to assign the mesh from the prefab if possible
                            var prefabMeshFilter = PrefabUtility.GetCorrespondingObjectFromSource(meshFilter) as MeshFilter;
                            if (prefabMeshFilter != null && prefabMeshFilter.sharedMesh != null)
                            {
                                meshFilter.sharedMesh = prefabMeshFilter.sharedMesh;
                            }
                        }
                    }
                }

                AmplifyImpostor ai = instance.GetComponent<AmplifyImpostor>();
                if (ai == null)
                    ai = instance.AddComponent<AmplifyImpostor>();
                ai.RootTransform = instance.transform;
                if (globalSettings == null)
                {
                    batchResults.Add($"[{idx}] Global impostor settings asset is null.");
                    Debug.LogError($"[{idx}] Global impostor settings asset is null.");
                    fail++;
                    continue;
                }
                // Create unique AmplifyImpostorAsset
                string prefabDir = System.IO.Path.GetDirectoryName(path);
                string impostorAssetPath = System.IO.Path.Combine(prefabDir, prefab.name + "_Impostor.asset").Replace("\\", "/");
                string presetAssetPath = System.IO.Path.Combine(prefabDir, prefab.name + "_ImpostorPreset.asset").Replace("\\", "/");

                // Create and copy AmplifyImpostorAsset
                AmplifyImpostors.AmplifyImpostorAsset newAsset = ScriptableObject.CreateInstance<AmplifyImpostors.AmplifyImpostorAsset>();
                AmplifyImpostors.AmplifyImpostorAsset refAsset = globalSettings;
                // Do NOT copy Material and Mesh from the globalSettings; let the bake process generate and store them
                // newAsset.Material = refAsset.Material;
                // newAsset.Mesh = refAsset.Mesh;
                newAsset.Version = refAsset.Version;
                newAsset.ImpostorType = refAsset.ImpostorType;
                newAsset.LockedSizes = refAsset.LockedSizes;
                newAsset.SelectedSize = refAsset.SelectedSize;
                newAsset.TexSize = refAsset.TexSize;
                newAsset.DecoupleAxisFrames = refAsset.DecoupleAxisFrames;
                newAsset.HorizontalFrames = refAsset.HorizontalFrames;
                newAsset.VerticalFrames = refAsset.VerticalFrames;
                newAsset.PixelPadding = refAsset.PixelPadding;
                newAsset.MaxVertices = refAsset.MaxVertices;
                newAsset.Tolerance = refAsset.Tolerance;
                newAsset.NormalScale = refAsset.NormalScale;
                newAsset.ShapePoints = (Vector2[])refAsset.ShapePoints.Clone();
                // Reuse the global preset
                newAsset.Preset = refAsset.Preset;
                newAsset.OverrideOutput = new System.Collections.Generic.List<AmplifyImpostors.TextureOutput>();
                foreach (var output in refAsset.OverrideOutput)
                {
                    newAsset.OverrideOutput.Add(output != null ? output.Clone() : null);
                }
                AssetDatabase.CreateAsset(newAsset, impostorAssetPath);

                ai.Data = newAsset;
                AssetDatabase.SaveAssets(); // Ensure the asset is registered before baking

                ai.Renderers = sourceRenderers;
                ai.m_impostorName = prefab.name + "_Impostor";
                ai.RenderAllDeferredGroups(ai.Data);
                AssetDatabase.SaveAssets(); // Ensure sub-assets are saved

                // Ensure impostor GameObject uses the correct mesh and material
                if (ai.m_lastImpostor != null && ai.Data != null)
                {
                    var mf = ai.m_lastImpostor.GetComponent<MeshFilter>();
                    if (mf != null) mf.sharedMesh = ai.Data.Mesh;
                    var mr = ai.m_lastImpostor.GetComponent<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = ai.Data.Material;
                }

                // After baking, ensure mesh and material are sub-assets of the impostor asset
                if (ai.Data != null)
                {
                    if (ai.Data.Mesh != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ai.Data.Mesh)))
                        AssetDatabase.AddObjectToAsset(ai.Data.Mesh, ai.Data);
                    if (ai.Data.Material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ai.Data.Material)))
                        AssetDatabase.AddObjectToAsset(ai.Data.Material, ai.Data);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                // Copy material properties from the source mesh to the baked impostor material using TVE inspector logic
                MeshRenderer meshRenderer = null;
                foreach (var r in sourceRenderers)
                {
                    meshRenderer = r as MeshRenderer;
                    if (meshRenderer != null && meshRenderer.sharedMaterial != null)
                        break;
                }

                if (meshRenderer != null && ai.Data.Material != null)
                {
                    var srcMat = meshRenderer.sharedMaterial;
                    var dstMat = ai.Data.Material;
                    TVEUtils.CopyMaterialProperties(srcMat, dstMat);
                    TVEUtils.SetImpostorFeatures(srcMat, dstMat);
                    dstMat.SetFloat("_IsInitialized", 1);
                    TVEUtils.SetMaterialSettings(dstMat);
                    EditorUtility.SetDirty(dstMat);
                }
                else
                {
                    Debug.LogWarning($"[BatchTool] No valid MeshRenderer with material found for prefab: {prefab.name}");
                }

                // After baking, assign impostor to LOD1 (or last LOD) using only the renderers from the 'Impostor' child GameObject
                GameObject impostorGO = ai.m_lastImpostor;
                
                // --- TVE: Copy material properties from source mesh to impostor mesh ---
                if (impostorGO != null && sourceRenderers != null && sourceRenderers.Length > 0)
                {
                    var impostorRenderers = impostorGO.GetComponentsInChildren<MeshRenderer>(true);
                    int rendererCount = Mathf.Min(sourceRenderers.Length, impostorRenderers.Length);
                    for (int r = 0; r < rendererCount; r++)
                    {
                        var meshRenderer2 = sourceRenderers[r] as MeshRenderer;
                        var impostorMaterial = impostorRenderers[r].sharedMaterial;
                        if (meshRenderer2 != null && impostorMaterial != null)
                        {
                            var srcMat2 = meshRenderer2.sharedMaterial;
                            TVEUtils.CopyMaterialProperties(srcMat2, impostorMaterial);
                            TVEUtils.SetImpostorFeatures(srcMat2, impostorMaterial);
                            impostorMaterial.SetFloat("_IsInitialized", 1);
                            TVEUtils.SetMaterialSettings(impostorMaterial);
                            EditorUtility.SetDirty(impostorMaterial);
                        }
                    }
                }

                if (impostorGO != null)
                {
                    if (lodGroup != null)
                        impostorGO.transform.SetParent(lodGroup.transform, false);
                    else
                        impostorGO.transform.SetParent(instance.transform, false);

                    impostorGO.transform.localPosition = Vector3.zero;
                    impostorGO.transform.localRotation = Quaternion.identity;
                    impostorGO.transform.localScale = Vector3.one;
                    impostorGO.transform.position = impostorGO.transform.parent.position;
                    impostorGO.transform.rotation = impostorGO.transform.parent.rotation;
                    Debug.Log($"[BatchTool] Impostor '{impostorGO.name}' parent: {impostorGO.transform.parent?.name}, localPosition: {impostorGO.transform.localPosition}, localRotation: {impostorGO.transform.localRotation.eulerAngles}, localScale: {impostorGO.transform.localScale}");
                }

                if (lodGroup != null && impostorGO != null)
                {
                    var lods = lodGroup.GetLODs();
                    var impostorRenderers = impostorGO.GetComponentsInChildren<Renderer>();
                    if (lods.Length >= 2)
                    {
                        lods[lods.Length - 1].renderers = impostorRenderers;
                    }
                    else if (lods.Length == 1)
                    {
                        // Add a second LOD for the impostor
                        Array.Resize(ref lods, 2);
                        lods[1] = new LOD(0.01f, impostorRenderers);
                    }
                    lodGroup.SetLODs(lods);
                    Debug.Log($"Assigned impostor to last LOD for prefab: {prefab.name}");
                }
                else if (lodGroup == null && impostorGO != null && addLodIfMissing)
                {
                    // --- Create LOD0 child and move renderers if no LODGroup exists ---
                    var origRenderers = sourceRenderers;
                    if (lod0Child == null)
                    {
                        var lod0GO = new GameObject("LOD0");
                        lod0GO.transform.SetParent(instance.transform, false);
                        lod0GO.transform.localPosition = Vector3.zero;
                        lod0GO.transform.localRotation = Quaternion.identity;
                        lod0GO.transform.localScale = Vector3.one;
                        lod0Child = lod0GO.transform;
                        foreach (var renderer in origRenderers)
                        {
                            if (renderer != null)
                            {
                                renderer.transform.SetParent(lod0Child, false);
                                renderer.transform.localPosition = Vector3.zero;
                                renderer.transform.localRotation = Quaternion.identity;
                                renderer.transform.localScale = Vector3.one;
                            }
                        }
                    }
                    else
                    {
                        origRenderers = lod0Child.GetComponentsInChildren<Renderer>(true)
                            .Where(r => r != null && r.enabled && r.GetComponent<MeshFilter>()?.sharedMesh != null)
                            .ToArray();
                        lod0Child.localPosition = Vector3.zero;
                        lod0Child.localRotation = Quaternion.identity;
                        lod0Child.localScale = Vector3.one;
                        foreach (Transform child in lod0Child)
                        {
                            child.localPosition = Vector3.zero;
                            child.localRotation = Quaternion.identity;
                            child.localScale = Vector3.one;
                        }
                    }
                    // Add LODGroup and set up LOD0 (original), LOD1 (impostor)
                    lodGroup = instance.AddComponent<LODGroup>();
                    var impostorRenderers = impostorGO.GetComponentsInChildren<Renderer>();
                    LOD[] lods = new LOD[2];
                    lods[0] = new LOD(0.5f, origRenderers);
                    lods[1] = new LOD(0.01f, impostorRenderers);
                    lodGroup.SetLODs(lods);
                    Debug.Log($"Created LODGroup and assigned impostor to LOD1 for prefab: {prefab.name}");
                }
                else if (lodGroup == null)
                {
                    Debug.Log($"No LODGroup found on prefab: {prefab.name}, impostor not assigned to LOD.");
                }
                else if (impostorGO == null)
                {
                    Debug.LogWarning($"Impostor GameObject was not created for prefab: {prefab.name}.");
                }
                if (setStaticExceptBatching && instance != null)
                {
                    var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                    // Set on root
                    GameObjectUtility.SetStaticEditorFlags(instance, flags);
                    // Set on all children recursively
                    foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == instance.transform) continue; // already set root
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                    }
                }
                if (lodGroup != null)
                {
                    lodGroup.fadeMode = (UnityEngine.LODFadeMode)lodFadeMode;
                    lodGroup.animateCrossFading = animateCrossFading;
                    EditorUtility.SetDirty(lodGroup);
                }
                if (batchMode == BatchMode.ModifyPrefabs && impostorGO != null)
                    PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                batchResults.Add($"[{idx}] Success: {prefab.name}");
                success++;
            }
            catch (System.Exception ex)
            {
                batchResults.Add($"[{idx}] Error processing {prefab?.name ?? "<null>"}: {ex}");
                Debug.LogError($"[{idx}] Error processing {prefab?.name ?? "<null>"}: {ex}");
                lastException = ex.ToString();
                fail++;
            }
            finally
            {
                if (instance != null)
                    DestroyImmediate(instance);
            }
        }
        status = $"Batch complete. Success: {success}, Failed: {fail}";
        isBatchRunning = false;
        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Repaint();
    }
}
#endif 
