// Targeted adoption of the four user-approved banking PNGs; no full scene rebuild.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires batch Editor outside Play Mode.");
string manifestPath = "ArtRevamp/SFC-20260930/banking/adoption.json";
var manifest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(manifestPath));
int imported = 0;
foreach (var entry in manifest["assets"])
{
    string source = (string)entry["source"], target = (string)entry["asset"];
    var bytes = System.IO.File.ReadAllBytes(source);
    string digest;
    using (var hash = System.Security.Cryptography.SHA256.Create())
        digest = System.BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    if (digest != (string)entry["sha256"]) throw new System.InvalidOperationException("Approved source changed: " + source);
    if (!target.StartsWith("Assets/Art/Sprites/sfc_player_bank_", System.StringComparison.Ordinal))
        throw new System.InvalidOperationException("Unexpected adoption target.");
    if (System.IO.File.Exists(target) && !System.Linq.Enumerable.SequenceEqual(bytes, System.IO.File.ReadAllBytes(target)))
        throw new System.InvalidOperationException("Refusing to overwrite different existing art: " + target);
    System.IO.File.WriteAllBytes(target, bytes);
    UnityEditor.AssetDatabase.ImportAsset(target, UnityEditor.ImportAssetOptions.ForceUpdate);
    imported++;
}
if (imported != 4) throw new System.InvalidOperationException("Expected exactly four adopted PNGs.");
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
var animator = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.PlayerShipAnimator>();
var renderer = animator.GetComponent<UnityEngine.SpriteRenderer>();
var beforeSprite = renderer.sprite; var beforePosition = animator.transform.localPosition;
typeof(Shmup.EditorTools.BattleSceneBuilder).GetMethod("ConfigureShipAnimator",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { animator, renderer });
if (renderer.sprite != beforeSprite || animator.transform.localPosition != beforePosition)
    throw new System.InvalidOperationException("Starting sprite or geometry changed.");
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new System.InvalidOperationException("Cannot save Battle.");
UnityEditor.AssetDatabase.SaveAssets();
return new { imported, savedScene = scene.path, fullSceneRegeneration = false, pngPostprocessing = false };
