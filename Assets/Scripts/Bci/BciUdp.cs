using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// A UDP listener that runs on its OWN background thread, so the screens and the mini-game never wait for the network.
// Every message is echoed to the Unity Console (so the data stream can be watched) and queued; the owner takes the messages out on
// the main thread with TryDequeue (Unity's API can only be used there).
public sealed class BciUdpListener : IDisposable
{
    readonly UdpClient client;
    readonly Thread thread;
    readonly string label;
    readonly int port;
    volatile bool running;

    public readonly ConcurrentQueue<string> Messages = new ConcurrentQueue<string>();

    public BciUdpListener(int port, string label)
    {
        this.port = port;
        this.label = label;
        client = new UdpClient();
        // ReuseAddress: a port left open by a previous Play session (or by a crashed script) must not block this one.
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
        running = true;
        thread = new Thread(Listen) { IsBackground = true, Name = "BciUdp-" + label };
        thread.Start();
        Debug.Log($"BCI UDP listener '{label}' on port {port}");
    }

    void Listen()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = client.Receive(ref any);                      // blocks this thread only
                string text = Encoding.UTF8.GetString(data).Trim();
                Debug.Log($"[UDP {label}:{port}] {text}");                  // Debug.Log may be called from any thread
                Messages.Enqueue(text);
            }
            catch (SocketException)
            {
                if (!running) break;                                         // closed on purpose
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public bool TryDequeue(out string text) => Messages.TryDequeue(out text);

    public void Dispose()
    {
        running = false;
        try { client.Close(); } catch { }
        try { thread.Join(300); } catch { }
    }
}

public static class BciUdp
{
    // One text message to a port on this computer.
    public static void Send(string text, int port)
    {
        try
        {
            using (var udp = new UdpClient())
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                udp.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"BCI UDP send to port {port} failed: {e.Message}");
        }
    }
}
