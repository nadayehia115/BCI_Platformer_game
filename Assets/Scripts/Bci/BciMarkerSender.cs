using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// The UDP-to-LSL workaround for the event markers. Unity has no LSL plugin, so every event marker (a trial / block start, a block end,
// a discarded block) is broadcast as a tiny text payload on UDP port 5007 of this computer:
//
//     marker|<code>      e.g.  marker|1  (focus block starts)   marker|2  (rest block starts)   marker|99  (block ended)
//     finish             the calibration is over (file 2 saves its CSV and exits)
//
// File 2 (Python) listens on that port in a background thread and, the moment a payload arrives, stamps it with the LSL clock and
// pushes it into an LSL StreamOutlet ("UnityMarkers"), next to the EEG stream - so the precise LSL timestamping is done in Python.
// One socket is kept open for the whole run (a payload costs microseconds and never blocks a frame).
public static class BciMarkerSender
{
    static UdpClient client;
    static IPEndPoint target;
    static int targetPort;

    static void Open()
    {
        int port = BciSettings.Instance.MarkerPort;
        if (client != null && port == targetPort) return;
        Close();
        client = new UdpClient();
        target = new IPEndPoint(IPAddress.Loopback, port);
        targetPort = port;
    }

    public static void Close()
    {
        try { client?.Close(); } catch { }
        client = null;
    }

    // An event marker with its code.
    public static void Send(int code) => SendText("marker|" + code);

    // A control word for file 2 ("finish").
    public static void SendCommand(string word) => SendText(word);

    static void SendText(string text)
    {
        try
        {
            Open();
            byte[] data = Encoding.UTF8.GetBytes(text);
            client.Send(data, data.Length, target);
            Debug.Log($"[marker -> UDP {targetPort}] {text}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"BCI: could not send the marker '{text}': {e.Message}");
            Close();
        }
    }
}
