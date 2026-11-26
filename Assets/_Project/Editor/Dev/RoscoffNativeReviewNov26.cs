using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

[InitializeOnLoad]
internal static class RoscoffNativeReviewNov26
{
    const string Folder = "Temp/RoscoffSceneChecksNov26";
    const string ScenePath = "Assets/_Project/Scenes/Development/RoscoffGreybox.unity";
    const string Request = Folder + "/capture.request";
    static double next;
    static RoscoffNativeReviewNov26() { EditorApplication.update += Check; }
    static void Check()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + 1;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        string run = File.ReadAllText(Request).Trim();
        if (run != "baseline" && run != "final") return;
        File.Delete(Request);
        string dir = Path.Combine(Folder, run);
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        Scene preview = default;
        bool overriding = false;
        var unsupported = typeof(Editor).Assembly.GetType("UnityEditor.Unsupported");
        var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
        var set = unsupported?.GetMethod("SetOverrideLightingSettings", flags, null, new[] {typeof(Scene)}, null);
        var restore = unsupported?.GetMethod("RestoreOverrideLightingSettings", flags, null, Type.EmptyTypes, null);
        try
        {
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            preview = EditorSceneManager.OpenPreviewScene(ScenePath);
            var roots = preview.GetRootGameObjects();
            var renderers = roots.SelectMany(obj => obj.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            var filters = roots.SelectMany(obj => obj.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            var solids = roots.SelectMany(obj => obj.GetComponentsInChildren<Collider>(true)).Where(c => c.enabled && !c.isTrigger).ToArray();
            var ladders = roots.SelectMany(obj => obj.GetComponentsInChildren<Ladder>(true)).ToArray();
            var spawns = roots.SelectMany(obj => obj.GetComponentsInChildren<PlayerSpawnPoint>(true)).ToArray();
            var missing = filters.Count(f => !f.sharedMesh) + renderers.Count(r => !r.sharedMaterial || !r.sharedMaterial.shader) + solids.OfType<MeshCollider>().Count(c => !c.sharedMesh);
            int missingScripts = roots.SelectMany(o => o.GetComponentsInChildren<Transform>(true)).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            log.AppendLine($"roots={roots.Length} renderers={renderers.Length} colliders={solids.Length} ladders={ladders.Length} spawns={spawns.Length} missingAssets={missing} missingScripts={missingScripts}");
            var view = EditorUtility.CreateGameObjectWithHideFlags("Roscoff temporary preview camera", HideFlags.HideAndDontSave, typeof(Camera));
            SceneManager.MoveGameObjectToScene(view,preview);
            var camera = view.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            if (set != null && restore != null)
            {
                set.Invoke(null,new object[]{preview});
                overriding = true;
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = overriding ? RenderSettings.fogColor : new Color(.57f,.61f,.64f);
            camera.nearClipPlane = .045f;
            camera.farClipPlane = 650;
            camera.fieldOfView = 69;
            camera.aspect = 16f/9;
            camera.useOcclusionCulling = false;
            log.AppendLine($"previewSceneLighting={overriding} fog={RenderSettings.fog} density={RenderSettings.fogDensity} gpu={SystemInfo.graphicsDeviceName}");
            var cameraLines = File.ReadAllLines(Folder + "/views.txt");
            foreach (var row in cameraLines)
            {
                var values = row.Split('|');
                camera.fieldOfView = float.Parse(values[7], CultureInfo.InvariantCulture);
                camera.transform.position = Point(values,1);
                camera.transform.LookAt(Point(values,4));
                Capture(camera, Path.Combine(dir,values[0]+".png"));
            }
            UnityEngine.Object.DestroyImmediate(view);
            if (overriding) { restore.Invoke(null,null); overriding = false; }
            Physics.SyncTransforms();
            var bounds = solids.Select(c => c.bounds).ToArray();
            var probe = EditorUtility.CreateGameObjectWithHideFlags("Roscoff temporary clearance check", HideFlags.HideAndDontSave, typeof(CapsuleCollider));
            SceneManager.MoveGameObjectToScene(probe,preview);
            var capsule=probe.GetComponent<CapsuleCollider>();
            capsule.radius=.295f; capsule.height=1.59f; capsule.enabled=false;
            var waypoints = File.ReadAllLines(Folder + "/waypoints.txt").Select(l => l.Split('|')).ToArray();
            int checkedCount=0, misses=0, blocks=0;
            foreach (var route in waypoints.GroupBy(p => p[0]))
            {
                Vector3[] points=route.Select(p => Point(p,1)).ToArray();
                int floorFailures=0, obstructions=0, samples=0;
                string example="";
                for (int k=1;k<points.Length;k++)
                {
                    int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(points[k-1],points[k])/.18f));
                    for (int j=0;j<=steps;j++)
                    {
                        Vector3 foot=Vector3.Lerp(points[k-1],points[k],(float)j/steps);
                        samples++;
                        if (!Floor(foot,solids,bounds,.27f,out float actualFloor)) {floorFailures++; if(example=="") example="floor "+foot; continue;}
                        foot.y=actualFloor;
                        var collision=Obstacle(foot,capsule,solids,bounds);
                        if(collision!=null) {obstructions++; if(example=="") example=collision.name+" "+foot;}
                    }
                }
                checkedCount+=samples;misses+=floorFailures;blocks+=obstructions;
                log.AppendLine($"{route.Key} | samples={samples} missingFloor={floorFailures} obstructed={obstructions} {example}");
            }
            int badExits=0, blockedClimbs=0;
            foreach (var l in ladders)
            {
                var foot=l.transform.position;var top=foot+Vector3.up*l.Height;
                var exit=top-l.Outward*.605f;
                bool landing=Floor(exit,solids,bounds,.18f,out _);
                var blocked=Obstacle(exit,capsule,solids,bounds);
                for (float h=.08f; h<l.Height && blocked==null;h+=.14f) blocked=Obstacle(foot+Vector3.up*h+l.Outward*.30f,capsule,solids,bounds);
                if(!landing) badExits++;
                if(blocked!=null)blockedClimbs++;
                log.AppendLine($"ladder={l.name} height={l.Height:F2} exitFloor={landing} obstruction={(blocked!=null?blocked.name:"clear")}");
            }
            UnityEngine.Object.DestroyImmediate(probe);
            log.AppendLine($"totalRouteSamples={checkedCount} floorMisses={misses} blockedSamples={blocks} missingLadderLandings={badExits} obstructedLadders={blockedClimbs}");
            log.AppendLine("completed");
        }
        catch (Exception e) {log.AppendLine(e.ToString());}
        finally
        {
            if(overriding)restore.Invoke(null,null);
            if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);
            File.WriteAllText(Path.Combine(dir,"report.txt"), log.ToString());
        }
    }
    static Vector3 Point(string[] p,int i)=>new Vector3(float.Parse(p[i],CultureInfo.InvariantCulture),float.Parse(p[i+1],CultureInfo.InvariantCulture),float.Parse(p[i+2],CultureInfo.InvariantCulture));
    static bool Floor(Vector3 p,Collider[] solids,Bounds[] boxes,float range,out float floor)
    {
        floor=float.NegativeInfinity;
        var ray=new Ray(p+Vector3.up*range,Vector3.down);
        for(int i=0;i<solids.Length;i++)
        {
            var b=boxes[i];
            if(p.x<b.min.x-.001f||p.x>b.max.x+.001f||p.z<b.min.z-.001f||p.z>b.max.z+.001f||p.y-range>b.max.y||p.y+range<b.min.y)continue;
            if(solids[i].Raycast(ray,out var hit,range*2) && hit.normal.y>.62f)floor=Mathf.Max(floor,hit.point.y);
        }
        return !float.IsNegativeInfinity(floor);
    }
    static Collider Obstacle(Vector3 foot,CapsuleCollider capsule,Collider[] solids,Bounds[] boxes)
    {
        var center=foot+Vector3.up*1.01f;
        var body=new Bounds(center,new Vector3(.59f,1.59f,.59f));
        for(int i=0;i<solids.Length;i++)
        {
            if(!body.Intersects(boxes[i]))continue;
            var other=solids[i];
            if(Physics.ComputePenetration(capsule,center,Quaternion.identity,other,other.transform.position,other.transform.rotation,out var direction,out float depth) && depth>.025f && direction.y<.68f)return other;
        }
        return null;
    }
    static void Capture(Camera c, string file)
    {
        var old=RenderTexture.active;
        var rt=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32);
        var texture=new Texture2D(960,540,TextureFormat.RGB24,false);
        try
        {
            c.targetTexture=rt;c.Render();c.Render();RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,960,540),0,0);texture.Apply();File.WriteAllBytes(file,texture.EncodeToPNG());
        }
        finally
        {
            c.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
