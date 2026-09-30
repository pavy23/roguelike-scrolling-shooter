// One-time, targeted adoption of the user-selected native pilot. Uses the same
// loaders as BattleSceneBuilder; preserves all other scene fields and old assets.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires batch Editor outside Play Mode.");
var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
if (pipeline == null || !pipeline.GetType().FullName.Contains("Universal"))
    throw new System.InvalidOperationException("Expected URP.");
UnityEditor.AssetDatabase.Refresh();
var builder = typeof(Shmup.EditorTools.BattleSceneBuilder);
var staticFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
UnityEngine.Sprite Load(string name) => (UnityEngine.Sprite)builder.GetMethod("LoadAdoptedSprite", staticFlags).Invoke(null, new object[] { name });
UnityEngine.Sprite Enemy(string prefix) => (UnityEngine.Sprite)builder.GetMethod("LoadSfcPilotEnemySprite", staticFlags).Invoke(null, new object[] { prefix });
var shipFrames = (UnityEngine.Sprite[])builder.GetMethod("LoadShipAnimationFrames", staticFlags).Invoke(null, null);
if (shipFrames.Length != 2) throw new System.InvalidOperationException("Expected curated two-pose engine.");
var accepted = new System.Collections.Generic.Dictionary<string, UnityEngine.Sprite> {
    { "zako_straight", Enemy("zako_straight") }, { "zako_fast", Enemy("zako_fast") }, { "turret", Enemy("turret") }
};
void SetArray(UnityEditor.SerializedProperty property, System.Collections.Generic.List<UnityEngine.Sprite> values)
{
    property.arraySize = values.Count;
    for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
}
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
var serialized = new UnityEditor.SerializedObject(director);
var prefixes = serialized.FindProperty("_enemySpritePrefixes"); var sprites = serialized.FindProperty("_enemySprites");
int changedStatic = 0;
for (int i = 0; i < prefixes.arraySize; i++)
    if (accepted.TryGetValue(prefixes.GetArrayElementAtIndex(i).stringValue, out var sprite))
    { sprites.GetArrayElementAtIndex(i).objectReferenceValue = sprite; changedStatic++; }
if (changedStatic != 3) throw new System.InvalidOperationException("Missing enemy static binding.");
var clips = serialized.FindProperty("_animPrefixes"); var counts = serialized.FindProperty("_animFrameCounts");
var frames = serialized.FindProperty("_animFrames");
var newFrames = new System.Collections.Generic.List<UnityEngine.Sprite>();
int offset = 0, changedClips = 0;
for (int i = 0; i < clips.arraySize; i++)
{
    int count = counts.GetArrayElementAtIndex(i).intValue;
    if (accepted.TryGetValue(clips.GetArrayElementAtIndex(i).stringValue, out var sprite))
    { newFrames.Add(sprite); counts.GetArrayElementAtIndex(i).intValue = 1; changedClips++; }
    else for (int f = 0; f < count; f++) newFrames.Add((UnityEngine.Sprite)frames.GetArrayElementAtIndex(offset + f).objectReferenceValue);
    offset += count;
}
if (offset != frames.arraySize || changedClips != 3) throw new System.InvalidOperationException("Invalid existing clip table.");
SetArray(frames, newFrames);
var shipIds = serialized.FindProperty("_shipSpriteIds"); var shipSprites = serialized.FindProperty("_shipSprites");
int starterBindings = 0;
for (int i = 0; i < shipIds.arraySize; i++)
    if (shipIds.GetArrayElementAtIndex(i).stringValue == "starter")
    { shipSprites.GetArrayElementAtIndex(i).objectReferenceValue = shipFrames[0]; starterBindings++; }
if (starterBindings != 1) throw new System.InvalidOperationException("Starter binding is ambiguous.");
var player = (UnityEngine.Transform)serialized.FindProperty("_playerTransform").objectReferenceValue;
var muzzle = (UnityEngine.SpriteRenderer)serialized.FindProperty("_muzzleFlash").objectReferenceValue;
var muzzleBefore = muzzle.transform.localPosition;
serialized.ApplyModifiedPropertiesWithoutUndo();
player.GetComponent<UnityEngine.SpriteRenderer>().sprite = shipFrames[0];
var animator = player.GetComponent<Shmup.Presentation.Battle.PlayerShipAnimator>();
var animation = new UnityEditor.SerializedObject(animator);
SetArray(animation.FindProperty("_frames"), new System.Collections.Generic.List<UnityEngine.Sprite>(shipFrames));
animation.FindProperty("_framesPerSecond").floatValue = 10;
animation.ApplyModifiedPropertiesWithoutUndo();
if (muzzle.transform.localPosition != muzzleBefore) throw new System.InvalidOperationException("Muzzle anchor changed.");
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new System.InvalidOperationException("Could not save Battle.");

var title = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Title.unity");
var hangar = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.HangarScreen>();
var hangarData = new UnityEditor.SerializedObject(hangar);
var ids = hangarData.FindProperty("_shipIds"); var portraits = hangarData.FindProperty("_shipSprites");
int changedPortraits = 0;
for (int i = 0; i < ids.arraySize; i++)
    if (ids.GetArrayElementAtIndex(i).stringValue == "starter")
    { portraits.GetArrayElementAtIndex(i).objectReferenceValue = shipFrames[0]; changedPortraits++; }
if (changedPortraits != 1) throw new System.InvalidOperationException("Missing hangar starter portrait.");
hangarData.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(title);
if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(title)) throw new System.InvalidOperationException("Could not save Title.");
UnityEditor.AssetDatabase.SaveAssets();
return new { changedStatic, changedClips, engineFrames = shipFrames.Length, changedPortraits,
    muzzleLocalPosition = new[] { muzzleBefore.x, muzzleBefore.y, muzzleBefore.z },
    oldAssetGuidsPreserved = true, fullSceneRegeneration = false };
