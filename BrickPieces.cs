using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PieceManager;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace PieceManagerModTemplate
{
    // Each piece is assembled from copies of the vanilla "stone_wall_2x1" and "stone_wall_1x1" blocks,
    // merged into a single mesh. The prefab skeleton (collider, snap points, components) is made at plugin Awake;
    // the block meshes, material, effects and stats are taken from the vanilla blocks once ZNetScene loads.
    public static class BrickPieces
    {
        private const string Block2x1 = "stone_wall_2x1";
        private const string Block1x1 = "stone_wall_1x1";
        private static readonly string[] States = { "New", "Worn", "Broken" };

        // Icon lighting: flat grey ambient plus a soft key light. Lower both if icons look too bright, raise if too dark.
        private const float IconAmbient = 0.35f, IconLight = 0.9f;

        private static readonly Dictionary<GameObject, List<(Vector3 center, Vector3 size)>> Layouts = new();
        private static readonly Dictionary<GameObject, float> Health = new(); // pieces that don't use the vanilla 2x1 block's health
        private const float WallHealth = 2500f;
        private static GameObject _holder = null!;

        public static void Register()
        {
            if (LayerMask.NameToLayer("piece") < 0)
            {
                Log.Error("The game's \"piece\" layer was not found, no brick pieces were added.");
                return;
            }

            _holder = new GameObject("StoneBrickWalls_Prefabs");
            _holder.SetActive(false); // keeps prefab components from waking up
            UnityEngine.Object.DontDestroyOnLoad(_holder);

            Add("StoneBrick_Cube_A", "Stone brick cube (2x2x2)", "A solid cube of stacked stone blocks.", new Vector3(2, 2, 2), Cube(0));
            Add("StoneBrick_Cube_B", "Stone brick cube, alt (2x2x2)", "A solid cube of stacked stone blocks, alternate pattern.", new Vector3(2, 2, 2), Cube(1));
            Add("StoneBrick_Wall_4x2x1", "Stone brick wall (4x2x1)", "A stone block wall, 1m thick.", new Vector3(4, 2, 1), Wall(4, 2, 1), WallHealth);
            Add("StoneBrick_Wall_4x2x2", "Stone brick wall (4x2x2)", "A stone block wall, 2m thick.", new Vector3(4, 2, 2), Wall(4, 2, 2), WallHealth);
            Add("StoneBrick_Wall_8x2x1", "Stone brick wall (8x2x1)", "A long stone block wall, 1m thick.", new Vector3(8, 2, 1), Wall(8, 2, 1), WallHealth);
            Add("StoneBrick_Wall_8x4x1", "Stone brick wall (8x4x1)", "A long, tall stone block wall, 1m thick.", new Vector3(8, 4, 1), Wall(8, 4, 1), WallHealth);
            Add("StoneBrick_Wall_8x4x2", "Stone brick wall (8x4x2)", "A long, tall stone block wall, 2m thick.", new Vector3(8, 4, 2), Wall(8, 4, 2), WallHealth);
            Add("StoneBrick_Floor_4x4", "Stone brick floor, basket weave (4x4)", "A basket-weave stone block floor.", new Vector3(4, 1, 4), Floor(4, 4));
            Add("StoneBrick_Floor_4x4_Framed", "Stone brick floor, framed (4x4)", "A stone block floor with a framed square pattern.", new Vector3(4, 1, 4), FramedFloor());
        }

        private static void Add(string id, string name, string description, Vector3 size, List<(Vector3 center, Vector3 size)> bricks, float? health = null)
        {
            GameObject prefab;
            try
            {
                prefab = CreatePrefab(id, size);
            }
            catch (Exception e)
            {
                Log.Unknown($"creating the piece {id}", e);
                return;
            }

            try
            {
                BuildPiece piece = new(prefab);
                piece.Name.English(name);
                piece.Description.English(description);
                // shortcut: stone cost = 1.5 per cubic meter (vanilla 4x2 wall rate), tweak in the config if needed.
                piece.RequiredItems.Add("Stone", Mathf.Max(1, Mathf.RoundToInt(size.x * size.y * size.z * 1.5f)), true);
                piece.Category.Set(BuildPieceCategory.BuildingStonecutter);
                piece.Crafting.Set(CraftingTable.StoneCutter);
                Layouts[prefab] = bricks;
                if (health is { } hp) Health[prefab] = hp;
            }
            catch (Exception e)
            {
                Log.Unknown($"registering the piece {id}", e);
            }
        }

        // ---------- layouts: 1m grid, positions relative to the piece's bottom-center pivot ----------
        // Brick size (2,1,1) = 2x1 block along X, (1,1,2) = 2x1 block turned 90 degrees, (1,1,1) = 1x1 block.

        // 1m thick: running bond, every course shifted one meter, with 1x1 blocks filling the ends.
        // 2m thick: same running bond with both rows of a course shifted alike, but the 1m ends of the shifted
        // courses are filled by one 2x1 block turned across the wall, so only 2x1 blocks are used.
        private static List<(Vector3, Vector3)> Wall(int length, int height, int width)
        {
            List<(Vector3, Vector3)> bricks = new();
            if (width == 2)
            {
                for (int course = 0; course < height; ++course)
                {
                    float y = course + 0.5f;
                    int start = course % 2; // 1 = shifted course with crosswise end blocks
                    if (start == 1)
                        foreach (int x in new[] { 0, length - 1 })
                            bricks.Add((new Vector3(x + 0.5f - length / 2f, y, 0), new Vector3(1, 1, 2)));
                    for (int wythe = 0; wythe < 2; ++wythe)
                    for (int x0 = start; x0 + 2 <= length - start; x0 += 2)
                        bricks.Add((new Vector3(x0 + 1 - length / 2f, y, wythe - 0.5f), new Vector3(2, 1, 1)));
                }

                return bricks;
            }

            for (int course = 0; course < height; ++course)
            for (int wythe = 0; wythe < width; ++wythe)
            for (int x0 = -((course + wythe) % 2); x0 < length; x0 += 2)
            {
                int a = Math.Max(x0, 0), b = Math.Min(x0 + 2, length);
                bricks.Add((new Vector3((a + b) / 2f - length / 2f, course + 0.5f, wythe + 0.5f - width / 2f), new Vector3(b - a, 1, 1)));
            }

            return bricks;
        }

        // Two courses of two 2x1 blocks, the second course turned 90 degrees; phase picks which course faces front.
        private static List<(Vector3, Vector3)> Cube(int phase)
        {
            List<(Vector3, Vector3)> bricks = new();
            for (int course = 0; course < 2; ++course)
            for (int i = 0; i < 2; ++i)
            {
                float y = course + 0.5f, side = i - 0.5f;
                bricks.Add((course + phase) % 2 == 0
                    ? (new Vector3(0, y, side), new Vector3(2, 1, 1))
                    : (new Vector3(side, y, 0), new Vector3(1, 1, 2)));
            }

            return bricks;
        }

        // Basket weave: 2x2 cells of two 2x1 blocks, alternating direction like a checkerboard. 1m thick.
        private static List<(Vector3, Vector3)> Floor(int length, int width)
        {
            List<(Vector3, Vector3)> bricks = new();
            for (int i = 0; i < length / 2; ++i)
            for (int j = 0; j < width / 2; ++j)
            for (int k = 0; k < 2; ++k)
            {
                float cx = i * 2 + 1 - length / 2f, cz = j * 2 + 1 - width / 2f, half = k - 0.5f;
                bricks.Add((i + j) % 2 == 0
                    ? (new Vector3(cx, 0.5f, cz + half), new Vector3(2, 1, 1))
                    : (new Vector3(cx + half, 0.5f, cz), new Vector3(1, 1, 2)));
            }

            return bricks;
        }

        // Framed square: two blocks turned the same way in the middle, framed by blocks around the edges.
        private static List<(Vector3, Vector3)> FramedFloor()
        {
            // (x, z) of the block's corner cell on a 4x4 grid, and whether it runs along X (else along Z).
            (int x, int z, bool alongX)[] layout =
            {
                (0, 0, false), (0, 2, false), // left edge
                (3, 0, false), (3, 2, false), // right edge
                (1, 0, true), (1, 3, true), // top and bottom
                (1, 1, false), (2, 1, false), // middle pair
            };
            return layout.Select(b => b.alongX
                    ? (new Vector3(b.x + 1 - 2f, 0.5f, b.z + 0.5f - 2f), new Vector3(2, 1, 1))
                    : (new Vector3(b.x + 0.5f - 2f, 0.5f, b.z + 1 - 2f), new Vector3(1, 1, 2)))
                .ToList();
        }

        // ---------- prefab skeleton ----------

        private static GameObject CreatePrefab(string id, Vector3 size)
        {
            int layer = LayerMask.NameToLayer("piece");
            GameObject root = new(id) { layer = layer };
            root.transform.SetParent(_holder.transform, false);

            // Each wear state has a detailed (near) and a plain box (far) model, like the vanilla blocks.
            foreach (string state in States)
            {
                GameObject stateGo = new(state) { layer = layer };
                stateGo.transform.SetParent(root.transform, false);
                foreach (string lod in new[] { "high", "low" })
                {
                    GameObject go = new(lod) { layer = layer };
                    go.transform.SetParent(stateGo.transform, false);
                    go.AddComponent<MeshFilter>();
                    go.AddComponent<MeshRenderer>();
                }

                stateGo.AddComponent<LODGroup>();
                stateGo.SetActive(state == "New"); // WearNTear switches these at runtime
            }

            GameObject colliderGo = new("collider") { layer = layer };
            colliderGo.transform.SetParent(root.transform, false);
            BoxCollider col = colliderGo.AddComponent<BoxCollider>();
            col.size = size;
            col.center = new Vector3(0, size.y / 2, 0);

            foreach (Vector3 p in SnapPoints(size))
            {
                GameObject snap = new("_snappoint") { layer = layer, tag = "snappoint" };
                snap.transform.SetParent(root.transform, false);
                snap.transform.localPosition = p;
                snap.SetActive(false);
            }

            root.AddComponent<ZNetView>();
            Piece piece = root.AddComponent<Piece>();
            piece.m_name = id;
            piece.m_description = id;
            WearNTear wnt = root.AddComponent<WearNTear>();
            wnt.m_new = root.transform.Find("New").gameObject;
            wnt.m_worn = root.transform.Find("Worn").gameObject;
            wnt.m_broken = root.transform.Find("Broken").gameObject;
            return root;
        }

        // Corners plus every 2m along the edges, so pieces line up with each other and with the vanilla 2m grid.
        private static IEnumerable<Vector3> SnapPoints(Vector3 size)
        {
            float[] Steps(float len) => Enumerable.Range(0, Mathf.CeilToInt(len / 2f) + 1).Select(i => Mathf.Min(i * 2f, len)).Distinct().ToArray();
            bool Edge(float v, float len) => v == 0 || v == len;

            foreach (float x in Steps(size.x))
            foreach (float y in Steps(size.y))
            foreach (float z in Steps(size.z))
                if ((Edge(x, size.x) ? 1 : 0) + (Edge(y, size.y) ? 1 : 0) + (Edge(z, size.z) ? 1 : 0) >= 2)
                    yield return new Vector3(x - size.x / 2, y, z - size.z / 2);
        }

        // ---------- vanilla block data ----------

        private class Block
        {
            public Mesh High = null!, Low = null!;
            public Matrix4x4 HighMatrix, LowMatrix; // renderer position inside the vanilla prefab
        }

        // Reads the near/far meshes of a vanilla block from the LODGroup on its "new" model.
        private static Block? ReadBlock(GameObject prefab)
        {
            WearNTear? wear = prefab.GetComponent<WearNTear>();
            LODGroup? group = wear?.m_new?.GetComponent<LODGroup>();
            LOD[]? lods = group?.GetLODs();
            if (lods == null || lods.Length < 2 || lods[0].renderers.Length == 0 || lods[1].renderers.Length == 0) return null;

            Renderer high = lods[0].renderers[0], low = lods[1].renderers[0];
            Mesh? highMesh = high ? high.GetComponent<MeshFilter>()?.sharedMesh : null;
            Mesh? lowMesh = low ? low.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (highMesh == null || lowMesh == null || !highMesh.isReadable || !lowMesh.isReadable) return null;

            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            return new Block
            {
                High = highMesh, Low = lowMesh,
                HighMatrix = toRoot * high.transform.localToWorldMatrix,
                LowMatrix = toRoot * low.transform.localToWorldMatrix,
            };
        }

        private static Material? FirstMaterial(GameObject? model) => model ? model!.GetComponentInChildren<MeshRenderer>(true)?.sharedMaterial : null;

        // Merges one copy of the right vanilla block per brick. Each copy gets a random flip so neighbours don't look identical.
        private static (Mesh high, Mesh low) BuildMeshes(string id, List<(Vector3 center, Vector3 size)> bricks, Block long2x1, Block cube1x1)
        {
            Random rng = new(id.Sum(c => c)); // deterministic per piece
            List<CombineInstance> high = new(), low = new();
            foreach ((Vector3 center, Vector3 size) in bricks)
            {
                bool isLong = size.x > 1.5f || size.z > 1.5f;
                Block block = isLong ? long2x1 : cube1x1;
                float yaw = isLong
                    ? (size.z > 1.5f ? 90 : 0) + rng.Next(2) * 180 // 2x1 can only be flipped end-for-end
                    : rng.Next(4) * 90;
                Matrix4x4 place = Matrix4x4.TRS(center, Quaternion.Euler(0, yaw, 0), Vector3.one);
                high.Add(new CombineInstance { mesh = block.High, transform = place * block.HighMatrix });
                low.Add(new CombineInstance { mesh = block.Low, transform = place * block.LowMatrix });
            }

            return (Combine(id + "_high", high), Combine(id + "_low", low));
        }

        private static Mesh Combine(string name, List<CombineInstance> parts)
        {
            Mesh mesh = new() { name = name, indexFormat = IndexFormat.UInt32 }; // big walls can pass 65k vertices
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- finish prefabs once the game's prefabs exist ----------

        private static readonly HashSet<string> SkipPieceFields = new() { "m_name", "m_description", "m_icon", "m_category", "m_resources", "m_craftingStation", "m_enabled", "m_usage", "m_isUpgrade" };
        private static readonly HashSet<string> SkipWearFields = new() { "m_new", "m_worn", "m_broken", "m_wet", "m_fragmentRoots" };

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static class FinishPrefabs
        {
            private static void Prefix(ZNetScene __instance)
            {
                try
                {
                    Finish(__instance);
                }
                catch (Exception e)
                {
                    Log.Unknown("preparing the brick pieces", e);
                }
            }

            private static void Finish(ZNetScene scene)
            {
                GameObject? vanilla2x1 = scene.m_prefabs.Find(p => p && p.name == Block2x1);
                GameObject? vanilla1x1 = scene.m_prefabs.Find(p => p && p.name == Block1x1);
                if (vanilla2x1 == null || vanilla1x1 == null)
                {
                    Log.Error($"Vanilla pieces \"{Block2x1}\" or \"{Block1x1}\" not found (game update?), brick pieces will be invisible.");
                    return;
                }

                Block? long2x1 = ReadBlock(vanilla2x1), cube1x1 = ReadBlock(vanilla1x1);
                if (long2x1 == null || cube1x1 == null)
                {
                    Log.Error($"The models of \"{Block2x1}\" or \"{Block1x1}\" changed or can't be read (game update?), brick pieces will be invisible.");
                    return;
                }

                WearNTear vanillaWear = vanilla2x1.GetComponent<WearNTear>();
                Material? newMat = FirstMaterial(vanillaWear.m_new);
                Material? wornMat = FirstMaterial(vanillaWear.m_worn);
                if (newMat == null || wornMat == null)
                {
                    Log.Error($"Stone material not found on \"{Block2x1}\", brick pieces will be invisible.");
                    return;
                }

                LOD[] vanillaLods = vanillaWear.m_new.GetComponent<LODGroup>().GetLODs();

                foreach (KeyValuePair<GameObject, List<(Vector3 center, Vector3 size)>> entry in Layouts)
                {
                    GameObject prefab = entry.Key;
                    try
                    {
                        CopySerializedFields(vanilla2x1.GetComponent<ZNetView>(), prefab.GetComponent<ZNetView>(), new HashSet<string>());
                        CopySerializedFields(vanilla2x1.GetComponent<Piece>(), prefab.GetComponent<Piece>(), SkipPieceFields);
                        CopySerializedFields(vanillaWear, prefab.GetComponent<WearNTear>(), SkipWearFields);
                        if (Health.TryGetValue(prefab, out float hp)) prefab.GetComponent<WearNTear>().m_health = hp;
                    }
                    catch (Exception e) when (e is ArgumentException or FieldAccessException or TargetException)
                    {
                        Log.Error($"Could not copy stone stats to {prefab.name}: {e.Message}");
                    }
                    catch (Exception e)
                    {
                        Log.Unknown($"copying stone stats to {prefab.name}", e);
                    }

                    try
                    {
                        ApplyModels(prefab, entry.Value, long2x1, cube1x1, newMat, wornMat, vanillaLods);
                    }
                    catch (ArgumentException e) // mesh data rejected by Unity
                    {
                        Log.Error($"Could not assemble the blocks for {prefab.name}: {e.Message}");
                        continue;
                    }
                    catch (Exception e)
                    {
                        Log.Unknown($"assembling the blocks for {prefab.name}", e);
                        continue;
                    }

                    if (prefab.GetComponent<Piece>().m_icon != null || Application.isBatchMode) continue;
                    // Same trick the game uses for placement ghosts: stop the snapshot copy from registering on the network.
                    ZNetView.m_forceDisableInit = true;
                    // While the world loads the game's day/night lighting isn't set yet, and the default ambient light
                    // plus the icon light washes the stone out to white. Render icons under fixed lighting instead.
                    AmbientMode ambientMode = RenderSettings.ambientMode;
                    Color ambientLight = RenderSettings.ambientLight;
                    bool fog = RenderSettings.fog;
                    try
                    {
                        RenderSettings.ambientMode = AmbientMode.Flat;
                        RenderSettings.ambientLight = new Color(IconAmbient, IconAmbient, IconAmbient);
                        RenderSettings.fog = false;
                        BuildPiece.SnapshotPiece(prefab, IconLight);
                        if (prefab.GetComponent<Piece>().m_icon == null)
                            Log.Warning($"No build menu icon could be rendered for {prefab.name}.");
                    }
                    catch (Exception e)
                    {
                        Log.Unknown($"rendering the build menu icon for {prefab.name}", e);
                    }
                    finally
                    {
                        ZNetView.m_forceDisableInit = false;
                        RenderSettings.ambientMode = ambientMode;
                        RenderSettings.ambientLight = ambientLight;
                        RenderSettings.fog = fog;
                    }
                }
            }
        }

        private static void ApplyModels(GameObject prefab, List<(Vector3 center, Vector3 size)> bricks, Block long2x1, Block cube1x1, Material newMat, Material wornMat, LOD[] vanillaLods)
        {
            Transform newModel = prefab.transform.Find("New");
            if (newModel.Find("high").GetComponent<MeshFilter>().sharedMesh != null) return; // already built on an earlier world load

            (Mesh high, Mesh low) = BuildMeshes(prefab.name, bricks, long2x1, cube1x1);
            foreach (string state in States)
            {
                Transform model = prefab.transform.Find(state);
                Material mat = state == "New" ? newMat : wornMat;
                Renderer highRenderer = SetModel(model.Find("high"), high, mat);
                Renderer lowRenderer = SetModel(model.Find("low"), low, mat);
                model.GetComponent<LODGroup>().SetLODs(new[]
                {
                    new LOD(vanillaLods[0].screenRelativeTransitionHeight, new[] { highRenderer }),
                    new LOD(vanillaLods[1].screenRelativeTransitionHeight, new[] { lowRenderer }),
                });
                model.GetComponent<LODGroup>().RecalculateBounds();
            }
        }

        private static Renderer SetModel(Transform t, Mesh mesh, Material mat)
        {
            t.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = t.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            return renderer;
        }

        private static void CopySerializedFields<T>(T from, T to, HashSet<string> skip) where T : MonoBehaviour
        {
            for (Type? t = typeof(T); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (((f.IsPublic && !f.IsNotSerialized) || f.IsDefined(typeof(SerializeField))) && !skip.Contains(f.Name))
                    f.SetValue(to, f.GetValue(from));
        }
    }
}
