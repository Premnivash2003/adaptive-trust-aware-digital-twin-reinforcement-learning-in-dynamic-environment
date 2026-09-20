#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using ATADTRL.Scenarios;
using ATADTRL.Environment;
using ATADTRL.UI;

[InitializeOnLoad]
public static class ATADTRLModule1Verification
{
    private const string Key="ATADTRL.Validation.Running";
    private static ScenarioManager _manager;
    private static int _index, _failures;
    private static bool _started, _captured;
    private static double _startedAt;
    private static readonly int[] Scenarios={6,7,8,9,10,11,12,13,14,15};
    static ATADTRLModule1Verification()
    {
        if (SessionState.GetBool(Key,false)) { EditorApplication.update+=Tick; _startedAt=EditorApplication.timeSinceStartup; }
    }
    public static void Run()
    {
        string output=System.Environment.GetEnvironmentVariable("ATADTRL_VALIDATION_OUTPUT");
        if(string.IsNullOrEmpty(output))
            output=Path.Combine(Path.GetTempPath(), "ATADTRL-Verification-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        System.Environment.SetEnvironmentVariable("ATADTRL_VALIDATION_OUTPUT", output);
        _index=0; _failures=0; _started=false; _captured=false; _manager=null;
        _startedAt=EditorApplication.timeSinceStartup;
        EditorApplication.update-=Tick; EditorApplication.update+=Tick;
        SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/scene1.unity");
        EditorApplication.isPlaying=true;
    }
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup-_startedAt>1000) { Debug.LogError("VERIFICATION wall-clock watchdog expired"); Finish(2); return; }
            if (!EditorApplication.isPlaying) return;
            if (_manager==null) _manager=UnityEngine.Object.FindAnyObjectByType<ScenarioManager>();
            if (_manager==null || !_manager.IsInitialized) return;
            if (!_started)
            {
                _started=true; Time.timeScale=3f;
                if (_manager.ResearchScenarioCount!=15) throw new Exception("Expected 15 controlled scenarios");
                foreach(var s in _manager.Scenarios.Take(15))
                {
                    int h=s.dynamicObjects.Count(x=>x.type==ATADTRL.Core.DynamicObjectType.Human);
                    int f=s.dynamicObjects.Count(x=>x.type==ATADTRL.Core.DynamicObjectType.Forklift);
                    if(h!=5||f!=2||s.maxEpisodeDuration>120) throw new Exception("Actor count/time cap: "+s.scenarioCode);
                }
                _manager.OnScenarioCompleted+=Completed;
                _manager.StartScenario(Scenarios[_index]);
                Debug.Log("VERIFICATION started live E/T sweep; accelerated smoke test, not experimental evidence");
            }
            if (!_captured && _manager.EpisodeElapsedSeconds>2f)
            {
                _captured=true;
                Capture("operations.png",false);
                var ui=UnityEngine.Object.FindAnyObjectByType<WarehouseManagementUI>();
                if(ui!=null) { ui.ShowPage(); Capture("inventory.png",true); ui.HidePage(); }
            }
        }
        catch(Exception ex) { Debug.LogException(ex); Finish(3); }
    }
    private static void Completed(ScenarioDefinition s,ATADTRL.Core.PerformanceRecord result)
    {
        Debug.Log($"VERIFICATION RESULT {s.scenarioCode}: {result.completionStatus}, {result.episodeDuration:F1}s, path {result.pathLength:F1}m");
        if(result.completionStatus.Contains("REACH")||result.pathLength<0.1f) _failures++;
        _index++;
        if(_index>=Scenarios.Length) { Finish(_failures>0?1:0); return; }
        EditorApplication.delayCall+=()=> { if(_manager!=null) _manager.StartScenario(Scenarios[_index]); };
    }
    private static void Capture(string file,bool page)
    {
        string output=System.Environment.GetEnvironmentVariable("ATADTRL_VALIDATION_OUTPUT");
        if(string.IsNullOrEmpty(output)) return;
        Camera camera=Camera.main;
        if(camera==null) camera=UnityEngine.Object.FindAnyObjectByType<Camera>();
        if(camera==null) return;
        var texture=new RenderTexture(1600,900,24);
        var prior=camera.targetTexture;
        var canvases=UnityEngine.Object.FindObjectsByType<Canvas>();
        var modes=canvases.Select(x=>x.renderMode).ToArray();
        var cameras=canvases.Select(x=>x.worldCamera).ToArray();
        camera.targetTexture=texture;
        for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=RenderMode.ScreenSpaceCamera;canvases[i].worldCamera=camera;canvases[i].planeDistance=0.5f; }
        Canvas.ForceUpdateCanvases(); camera.Render();
        var old=RenderTexture.active; RenderTexture.active=texture;
        var png=new Texture2D(1600,900,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1600,900),0,0);png.Apply();
        File.WriteAllBytes(Path.Combine(output,file),png.EncodeToPNG());RenderTexture.active=old;
        camera.targetTexture=prior;
        for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i];canvases[i].worldCamera=cameras[i]; }
        UnityEngine.Object.DestroyImmediate(png);texture.Release();UnityEngine.Object.DestroyImmediate(texture);
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key,false);EditorApplication.update-=Tick;
        Debug.Log("VERIFICATION FINISHED code="+code);
        EditorApplication.Exit(code);
    }
}
#endif
