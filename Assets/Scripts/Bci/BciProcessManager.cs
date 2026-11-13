using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Starts the Python scripts silently (no console window), keeps hold of every process, and makes sure NO script is left running
// when Unity stops (OnApplicationQuit / OnDestroy kill and dispose them all).
//
// It also owns the listener for the electrode statuses that file 1 sends (UDP, background thread): the headset check screen reads
// Statuses from here. It lives through scene changes (DontDestroyOnLoad), so the Python scripts started on one screen keep running
// on the next ones.
//
// The path of the Python program is PythonInterpreterPath of the BciSettings asset (Assets/Resources/BciSettings.asset), which
// can be edited in the Inspector.
public class BciProcessManager : MonoBehaviour
{
    class Tracked
    {
        public Process process;
        public string tag;
        public int quitPort;        // a UDP port on which the script understands "quit" (0 = none: it is simply killed)
    }

    static BciProcessManager instance;
    readonly List<Tracked> processes = new List<Tracked>();

    BciUdpListener statusListener;

    // ---- what file 1 reports ----
    public readonly Dictionary<string, string> Statuses = new Dictionary<string, string>();   // electrode -> GOOD / NOISY / FLATLINE / RAILED / NO DATA
    public bool HeadsetReady { get; private set; }           // file 1 said "ready|"
    public string HeadsetError { get; private set; }         // file 1 said "error|..."
    public float LastStatusTime { get; private set; } = -100f;
    public bool ClassifierReady { get; private set; }        // bci_game.py said "classifier_ready|": the model is loaded and the EEG stream found
    public bool RecorderReady { get; private set; }          // file 2 said "recorder_ready|": it has the EEG stream and listens for markers

    public static BciProcessManager Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("BCI Process Manager");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<BciProcessManager>();
            }
            return instance;
        }
    }

    public static string ScriptsDir
    {
        get
        {
            string custom = BciSettings.Instance.ScriptsFolder;
            return string.IsNullOrWhiteSpace(custom) ? Path.Combine(Application.streamingAssetsPath, "Python") : custom;
        }
    }

    // ---- the status listener ----

    public void StartStatusListener()
    {
        if (statusListener != null) return;
        HeadsetReady = false;
        HeadsetError = null;
        Statuses.Clear();
        try
        {
            statusListener = new BciUdpListener(BciSettings.Instance.StatusPort, "status");
        }
        catch (Exception e)
        {
            Debug.LogError($"BCI: could not listen for the electrode status on UDP port {BciSettings.Instance.StatusPort}: {e.Message}");
        }
    }

    void Update()
    {
        if (statusListener == null) return;
        while (statusListener.TryDequeue(out string text))
        {
            if (text.StartsWith("status|", StringComparison.Ordinal))
            {
                foreach (string pair in text.Substring(7).Split(','))
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0) Statuses[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim().ToUpperInvariant();
                }
                HeadsetReady = true;
                HeadsetError = null;
                LastStatusTime = Time.unscaledTime;
            }
            else if (text.StartsWith("ready|", StringComparison.Ordinal)) HeadsetReady = true;
            else if (text.StartsWith("recorder_ready|", StringComparison.Ordinal)) RecorderReady = true;
            else if (text.StartsWith("classifier_ready|", StringComparison.Ordinal)) ClassifierReady = true;
            else if (text.StartsWith("error|", StringComparison.Ordinal)) HeadsetError = text.Substring(6);
        }
    }

    // ---- starting and stopping scripts ----

    // Starts `script` (a file name in the Python folder) with its command-line arguments. Returns the process, or null when Python
    // could not be started (the reason is in the Console).
    public Process Launch(string script, string arguments, string tag, int quitPort = 0)
    {
        Stop(tag);
        if (tag == "recorder") RecorderReady = false;
        if (tag == "classifier") ClassifierReady = false;
        string scriptPath = Path.Combine(ScriptsDir, script);
        if (!File.Exists(scriptPath))
        {
            Debug.LogError($"BCI: the Python script is missing: {scriptPath}");
            return null;
        }

        string python = BciSettings.Instance.PythonInterpreterPath;
        var info = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(python) ? "python" : python,
            Arguments = $"-u \"{scriptPath}\" {arguments}",
            WorkingDirectory = ScriptsDir,
            UseShellExecute = false,
            CreateNoWindow = true,                                // silent: no console window
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        info.EnvironmentVariables["PYTHONUTF8"] = "1";

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[py:{tag}] {e.Data}"); };
        process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[py:{tag}] {e.Data}"); };
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            Debug.LogError($"BCI: could not start Python ('{info.FileName}'): {e.Message}\n" +
                           "Set PythonInterpreterPath in Assets/Resources/BciSettings.asset (Inspector).");
            process.Dispose();
            return null;
        }
        lock (processes) processes.Add(new Tracked { process = process, tag = tag, quitPort = quitPort });
        Debug.Log($"BCI: started {script} (pid {process.Id}) as '{tag}'");
        return process;
    }

    public Process Get(string tag)
    {
        lock (processes)
            foreach (var t in processes)
                if (t.tag == tag) return t.process;
        return null;
    }

    public bool IsRunning(string tag)
    {
        var p = Get(tag);
        try { return p != null && !p.HasExited; } catch { return false; }
    }

    // Stops one script: a polite "quit" first (a script that holds the headset needs to release it), then the whole process tree is
    // killed. Disposes the process.
    public void Stop(string tag, bool graceful = true)
    {
        Tracked found = null;
        lock (processes)
        {
            foreach (var t in processes) if (t.tag == tag) found = t;
            if (found != null) processes.Remove(found);
        }
        if (found != null) Terminate(found, graceful ? 1.5f : 0f);
    }

    public void KillAll()
    {
        List<Tracked> all;
        lock (processes)
        {
            all = new List<Tracked>(processes);
            processes.Clear();
        }
        foreach (var t in all)
            if (t.quitPort > 0) BciUdp.Send("quit", t.quitPort);          // ask every script to leave politely at the same time ...
        float wait = all.Exists(t => t.quitPort > 0) ? 1.5f : 0f;
        foreach (var t in all) Terminate(t, wait, alreadyAsked: true);       // ... then make sure none is left
        if (statusListener != null)
        {
            statusListener.Dispose();
            statusListener = null;
        }
    }

    static void Terminate(Tracked t, float politeSeconds, bool alreadyAsked = false)
    {
        var p = t.process;
        try
        {
            if (!p.HasExited && politeSeconds > 0f)
            {
                if (!alreadyAsked && t.quitPort > 0) BciUdp.Send("quit", t.quitPort);
                p.WaitForExit((int)(politeSeconds * 1000f));
            }
            if (!p.HasExited)
            {
                try
                {
                    // Python started helper processes (the headset stream): take the whole tree down. Windows: taskkill.
                    var kill = Process.Start(new ProcessStartInfo("taskkill", $"/PID {p.Id} /T /F")
                    { CreateNoWindow = true, UseShellExecute = false });
                    kill?.WaitForExit(2000);
                }
                catch { }
                if (!p.HasExited) p.Kill();
                Debug.Log($"BCI: killed '{t.tag}' (pid {p.Id})");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"BCI: stopping '{t.tag}': {e.Message}");
        }
        finally
        {
            try { p.Dispose(); } catch { }
        }
    }

    // No Python script may outlive Unity.
    void OnApplicationQuit() => KillAll();

    void OnDestroy()
    {
        KillAll();
        if (instance == this) instance = null;
    }
}
