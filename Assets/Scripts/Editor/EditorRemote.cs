using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Lets the Python script press Play in the Unity editor (its R key). The game itself (BciListener) only exists while
// Play mode is running, so something outside of Play mode has to listen for "play"; this is that: a tiny UDP listener
// on port 5006 that starts when the editor opens and survives script reloads. Messages: play | stop.
// Only the machine itself can reach it (127.0.0.1).
[InitializeOnLoad]
public static class EditorRemote
{
    const int Port = 5006;
    const string GameScenePath = "Assets/Scenes/GameScene.unity";

    static UdpClient client;
    static readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();

    static EditorRemote()
    {
        // Unity also loads editor scripts in hidden helper processes (the asset import workers). One of those grabbing the
        // port would swallow "play" messages, because it has no editor to press Play in, so only the real editor listens.
        if (AssetDatabase.IsAssetImportWorkerProcess() || UnityEngine.Application.isBatchMode) return;

        try
        {
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Loopback, Port));
        }
        catch (SocketException)
        {
            client = null;                  // port taken (a second editor instance, perhaps): the Python R key just won't work
            return;
        }
        new Thread(Listen) { IsBackground = true }.Start();
        EditorApplication.update += Process;
        AssemblyReloadEvents.beforeAssemblyReload += Close;      // the socket must be released before scripts reload
        EditorApplication.quitting += Close;
    }

    static void Listen()
    {
        var local = client;
        while (true)
        {
            try
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                queue.Enqueue(Encoding.UTF8.GetString(local.Receive(ref from)).Trim());
            }
            catch (Exception)
            {
                return;                     // the socket was closed
            }
        }
    }

    static void Process()
    {
        while (queue.TryDequeue(out string command))
        {
            if (command == "play" && !EditorApplication.isPlaying)
            {
                // play GameScene, not whatever happens to be open (unless the open scene has unsaved changes)
                var open = SceneManager.GetActiveScene();
                if (open.path != GameScenePath && !open.isDirty) EditorSceneManager.OpenScene(GameScenePath);
                EditorApplication.isPlaying = true;
            }
            else if (command == "stop" && EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }
    }

    static void Close()
    {
        try { client?.Close(); } catch (Exception) { }
        client = null;
    }
}
