using MSCLoader;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MSCMultiplayer
{
    public class RemotePlayer
    {
        public byte ID;
        public string Nick;
        public GameObject CarObject;
        public GameObject BodyObject;
        public Vector3 TargetCarPos;
        public Quaternion TargetCarRot;
        public Vector3 TargetBodyPos;
        public Quaternion TargetBodyRot;
    }

    public class MSCMultiplayer : Mod
    {
        public override string ID => "MSCMultiplayer";
        public override string Name => "MSC Multiplayer";
        public override string Author => "Pyt_o";
        public override string Version => "0.1.0";
        public override string Description => "Multiplayer mod dla My Summer Car";

        // Siec
        private UdpClient udpClient;
        private Thread receiveThread;
        private byte myID = 0;
        private bool isHost = false;
        private bool inGame = false;
        private bool connected = false;

        // Gracze
        private Dictionary<byte, RemotePlayer> remotePlayers = new Dictionary<byte, RemotePlayer>();
        private Dictionary<byte, string> playerList = new Dictionary<byte, string>();

        // Lokalne obiekty
        private Transform playerCar;
        private Transform playerBody;
        private float sendTimer = 0f;
        private float pingTimer = 0f;
        private const float SEND_INTERVAL = 0.05f;
        private const float PING_INTERVAL = 5f;

        // Chat
        private List<string> chatMessages = new List<string>();
        private string chatInput = "";
        private bool showChat = false;
        private float chatFadeTimer = 0f;

        // GUI
        private bool showMenu = false;
        private bool showPrivate = false;
        private string serverIP = "192.168.10.159";
        private string serverPort = "7777";
        private string playerNick = "Gracz";
        private Rect menuRect = new Rect(Screen.width / 2 - 300, Screen.height / 2 - 225, 600, 500);
        private string statusMsg = "Niepodlaczony";

        public override void ModSetup()
        {
            SetupFunction(Mod.Setup.OnMenuLoad, OnMenuLoad);
            SetupFunction(Mod.Setup.OnLoad, OnModLoad);
            SetupFunction(Mod.Setup.Update, OnUpdate);
            SetupFunction(Mod.Setup.OnGUI, OnGUIDraw);
        }

        private void OnMenuLoad()
        {
            try { playerNick = System.Environment.UserName; } catch { }
            ModConsole.Log("[MSCMultiplayer] Gotowy.");
        }

        private void OnModLoad()
        {
            inGame = true;

            GameObject satsuma = GameObject.Find("SATSUMA(557kg, 248)");
            if (satsuma != null)
            {
                playerCar = satsuma.transform;
                ModConsole.Log("[MSCMultiplayer] Znaleziono Satsume.");
            }

            GameObject player = GameObject.Find("PLAYER");
            if (player != null)
            {
                playerBody = player.transform;
                ModConsole.Log("[MSCMultiplayer] Znaleziono gracza.");
            }
        }

        // ===================== UPDATE =====================

        private void OnUpdate()
        {
            if (!inGame || !connected || myID == 0) return;

            sendTimer += Time.deltaTime;
            pingTimer += Time.deltaTime;
            chatFadeTimer += Time.deltaTime;

            if (sendTimer >= SEND_INTERVAL)
            {
                sendTimer = 0f;
                SendCarPosition();
                SendBodyPosition();
            }

            if (pingTimer >= PING_INTERVAL)
            {
                pingTimer = 0f;
                try { udpClient?.Send(new byte[] { 0x09 }, 1); } catch { }
            }

            // Interpolacja pozycji
            foreach (var rp in remotePlayers.Values)
            {
                if (rp.CarObject != null)
                {
                    rp.CarObject.transform.position = Vector3.Lerp(
                        rp.CarObject.transform.position, rp.TargetCarPos, Time.deltaTime * 15f);
                    rp.CarObject.transform.rotation = Quaternion.Lerp(
                        rp.CarObject.transform.rotation, rp.TargetCarRot, Time.deltaTime * 15f);
                }
                if (rp.BodyObject != null)
                {
                    rp.BodyObject.transform.position = Vector3.Lerp(
                        rp.BodyObject.transform.position, rp.TargetBodyPos, Time.deltaTime * 15f);
                    rp.BodyObject.transform.rotation = Quaternion.Lerp(
                        rp.BodyObject.transform.rotation, rp.TargetBodyRot, Time.deltaTime * 15f);
                }
            }

            // T = czat
            if (Input.GetKeyDown(KeyCode.T) && !showChat)
            {
                showChat = true;
                chatInput = "";
            }
        }

        // ===================== WYSYLANIE =====================

        private void SendCarPosition()
        {
            if (playerCar == null) return;
            try
            {
                Vector3 pos = playerCar.position;
                Vector3 rot = playerCar.eulerAngles;
                byte[] packet = new byte[26];
                packet[0] = 0x02;
                packet[1] = myID;
                Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, packet, 2, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, packet, 6, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, packet, 10, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rot.x), 0, packet, 14, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rot.y), 0, packet, 18, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rot.z), 0, packet, 22, 4);
                udpClient.Send(packet, packet.Length);
            }
            catch { }
        }

        private void SendBodyPosition()
        {
            if (playerBody == null) return;
            try
            {
                Vector3 pos = playerBody.position;
                Vector3 rot = playerBody.eulerAngles;
                byte[] packet = new byte[26];
                packet[0] = 0x04;
                packet[1] = myID;
                Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, packet, 2, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, packet, 6, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, packet, 10, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rot.x), 0, packet, 14, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rot.y), 0, packet, 18, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rot.z), 0, packet, 22, 4);
                udpClient.Send(packet, packet.Length);
            }
            catch { }
        }

        // ===================== SPAWN / REMOVE =====================

        private void SpawnRemotePlayer(byte id, string nick)
        {
            if (remotePlayers.ContainsKey(id)) return;

            RemotePlayer rp = new RemotePlayer();
            rp.ID = id;
            rp.Nick = nick;

            // Auto placeholder
            rp.CarObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rp.CarObject.name = "MSCCar_" + id;
            rp.CarObject.transform.localScale = new Vector3(1.6f, 1.3f, 3.8f);
            Renderer cr = rp.CarObject.GetComponent<Renderer>();
            if (cr != null) cr.material.color = new Color(1f, 0.8f, 0f);
            Collider cc = rp.CarObject.GetComponent<Collider>();
            if (cc != null) UnityEngine.Object.Destroy(cc);

            // Cialo placeholder
            rp.BodyObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rp.BodyObject.name = "MSCBody_" + id;
            rp.BodyObject.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            Renderer br = rp.BodyObject.GetComponent<Renderer>();
            if (br != null) br.material.color = new Color(0.2f, 0.6f, 1f);
            Collider bc = rp.BodyObject.GetComponent<Collider>();
            if (bc != null) UnityEngine.Object.Destroy(bc);

            remotePlayers[id] = rp;
            ModConsole.Log("[MSCMultiplayer] Stworzono gracza: " + nick);
        }

        private void RemoveRemotePlayer(byte id)
        {
            if (remotePlayers.ContainsKey(id))
            {
                var rp = remotePlayers[id];
                if (rp.CarObject != null) UnityEngine.Object.Destroy(rp.CarObject);
                if (rp.BodyObject != null) UnityEngine.Object.Destroy(rp.BodyObject);
                remotePlayers.Remove(id);
                playerList.Remove(id);
            }
        }

        // ===================== RECEIVE =====================

        private void ReceiveLoop()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
            while (connected)
            {
                try
                {
                    byte[] data = udpClient.Receive(ref endpoint);
                    if (data.Length == 0) continue;

                    switch (data[0])
                    {
                        case 0x01: // Welcome
                            myID = data[1];
                            isHost = data.Length >= 3 && data[2] == 1;
                            playerList[myID] = playerNick;
                            statusMsg = "Polaczony! ID: " + myID + (isHost ? " [HOST]" : "");
                            ModConsole.Log("[MSCMultiplayer] Polaczono! ID: " + myID);
                            break;

                        case 0x03: // Gracz dolaczyl
                            if (data.Length >= 3)
                            {
                                byte pid = data[1];
                                int nickLen = data[2];
                                if (data.Length >= 3 + nickLen && pid != myID)
                                {
                                    string nick = System.Text.Encoding.UTF8.GetString(data, 3, nickLen);
                                    playerList[pid] = nick;
                                    if (inGame) SpawnRemotePlayer(pid, nick);
                                    AddChat("» " + nick + " dolaczyl.");
                                }
                            }
                            break;

                        case 0x02: // Pozycja auta
                            if (data.Length >= 26)
                            {
                                byte pid = data[1];
                                if (pid == myID) break;
                                float px = BitConverter.ToSingle(data, 2);
                                float py = BitConverter.ToSingle(data, 6);
                                float pz = BitConverter.ToSingle(data, 10);
                                float rx = BitConverter.ToSingle(data, 14);
                                float ry = BitConverter.ToSingle(data, 18);
                                float rz = BitConverter.ToSingle(data, 22);
                                if (!remotePlayers.ContainsKey(pid))
                                {
                                    string nick = playerList.ContainsKey(pid) ? playerList[pid] : "Gracz" + pid;
                                    SpawnRemotePlayer(pid, nick);
                                }
                                if (remotePlayers.ContainsKey(pid))
                                {
                                    remotePlayers[pid].TargetCarPos = new Vector3(px, py, pz);
                                    remotePlayers[pid].TargetCarRot = Quaternion.Euler(rx, ry, rz);
                                }
                            }
                            break;

                        case 0x04: // Pozycja ciala
                            if (data.Length >= 26)
                            {
                                byte pid = data[1];
                                if (pid == myID) break;
                                float px = BitConverter.ToSingle(data, 2);
                                float py = BitConverter.ToSingle(data, 6);
                                float pz = BitConverter.ToSingle(data, 10);
                                float rx = BitConverter.ToSingle(data, 14);
                                float ry = BitConverter.ToSingle(data, 18);
                                float rz = BitConverter.ToSingle(data, 22);
                                if (remotePlayers.ContainsKey(pid))
                                {
                                    remotePlayers[pid].TargetBodyPos = new Vector3(px, py, pz);
                                    remotePlayers[pid].TargetBodyRot = Quaternion.Euler(rx, ry, rz);
                                }
                            }
                            break;

                        case 0x07: // Chat
                            if (data.Length >= 2)
                            {
                                int nickLen = data[1];
                                if (data.Length >= 2 + nickLen)
                                {
                                    string nick = System.Text.Encoding.UTF8.GetString(data, 2, nickLen);
                                    string msg = System.Text.Encoding.UTF8.GetString(data, 2 + nickLen, data.Length - 2 - nickLen);
                                    AddChat("[" + nick + "]: " + msg);
                                }
                            }
                            break;

                        case 0x08: // Nowy host
                            if (data.Length >= 2 && data[1] == myID)
                            {
                                isHost = true;
                                statusMsg = "Polaczony! ID: " + myID + " [HOST]";
                                AddChat("» Jestes teraz hostem.");
                            }
                            break;

                        case 0xFF:
                            statusMsg = "Serwer pelny!";
                            connected = false;
                            break;

                        case 0x00:
                            if (data.Length >= 2)
                            {
                                byte pid = data[1];
                                string leavingNick = playerList.ContainsKey(pid) ? playerList[pid] : "Gracz " + pid;
                                AddChat("» " + leavingNick + " opuscil gre.");
                                RemoveRemotePlayer(pid);
                            }
                            break;
                    }
                }
                catch { break; }
            }
        }

        // ===================== CONNECT / DISCONNECT =====================

        private void Connect()
        {
            try
            {
                Disconnect();
                Thread.Sleep(100);

                int port = int.Parse(serverPort);
                udpClient = new UdpClient();
                udpClient.Connect(serverIP, port);
                connected = true;

                byte[] nickBytes = System.Text.Encoding.UTF8.GetBytes(playerNick);
                byte[] packet = new byte[2 + nickBytes.Length];
                packet[0] = 0x01;
                packet[1] = (byte)nickBytes.Length;
                Buffer.BlockCopy(nickBytes, 0, packet, 2, nickBytes.Length);
                udpClient.Send(packet, packet.Length);

                receiveThread = new Thread(ReceiveLoop);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                statusMsg = "Laczenie z " + serverIP + ":" + serverPort + "...";
            }
            catch (Exception e)
            {
                statusMsg = "Blad: " + e.Message;
            }
        }

        private void Disconnect()
        {
            connected = false;
            if (udpClient != null)
            {
                try { udpClient.Send(new byte[] { 0x00 }, 1); } catch { }
                try { udpClient.Close(); } catch { }
                udpClient = null;
            }
            myID = 0;
            isHost = false;
            playerList.Clear();
            List<byte> toRemove = new List<byte>(remotePlayers.Keys);
            foreach (byte id in toRemove) RemoveRemotePlayer(id);
            statusMsg = "Rozlaczono.";
        }

        // ===================== CHAT =====================

        private void AddChat(string msg)
        {
            chatMessages.Add(msg);
            if (chatMessages.Count > 20) chatMessages.RemoveAt(0);
            chatFadeTimer = 0f;
        }

        private void SendChat(string msg)
        {
            if (udpClient == null || string.IsNullOrEmpty(msg)) return;
            try
            {
                byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(msg);
                byte[] packet = new byte[1 + msgBytes.Length];
                packet[0] = 0x07;
                Buffer.BlockCopy(msgBytes, 0, packet, 1, msgBytes.Length);
                udpClient.Send(packet, packet.Length);
                AddChat("[" + playerNick + "]: " + msg);
            }
            catch { }
        }

        // ===================== GUI =====================

        private void OnGUIDraw()
{
    // M otwiera/zamyka menu
    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.M)
        showMenu = !showMenu;

    // Maly wskaznik w rogu tylko jak niepodlaczony
    if (!connected)
    {
        GUI.color = new Color(1, 1, 1, 0.5f);
        GUI.Label(new Rect(10, Screen.height - 25, 200, 20), "[M] MSC Multiplayer");
        GUI.color = Color.white;
    }
    else
    {
        // Zielona kropka jak polaczony
        GUI.color = Color.green;
        GUI.Label(new Rect(10, Screen.height - 25, 200, 20), "● MSC Multiplayer [M]");
        GUI.color = Color.white;
    }

    if (showMenu)
        menuRect = GUI.Window(9999, menuRect, DrawMenu, "MSC Multiplayer v" + Version);

    DrawChat();
}

        private void DrawChat()
        {
            if (!inGame) return;

            float chatY = Screen.height - 200f;

            if (chatMessages.Count > 0 && chatFadeTimer < 10f)
            {
                int start = Mathf.Max(0, chatMessages.Count - 6);
                for (int i = start; i < chatMessages.Count; i++)
                {
                    GUI.color = new Color(1, 1, 1, Mathf.Clamp01(1f - (chatFadeTimer - 7f)));
                    GUI.Label(new Rect(10, chatY + (i - start) * 18f, 400f, 20f), chatMessages[i]);
                }
                GUI.color = Color.white;
            }

            if (showChat)
            {
                GUI.SetNextControlName("ChatInput");
                chatInput = GUI.TextField(new Rect(10, chatY + 110f, 300f, 24f), chatInput, 100);
                GUI.FocusControl("ChatInput");

                if (Event.current.type == EventType.KeyDown)
                {
                    if (Event.current.keyCode == KeyCode.Return)
                    {
                        if (!string.IsNullOrEmpty(chatInput.Trim()))
                            SendChat(chatInput.Trim());
                        chatInput = "";
                        showChat = false;
                    }
                    if (Event.current.keyCode == KeyCode.Escape)
                    {
                        chatInput = "";
                        showChat = false;
                    }
                }
            }
            else if (connected)
            {
                GUI.color = new Color(1, 1, 1, 0.4f);
                GUI.Label(new Rect(10, chatY + 110f, 200f, 20f), "[T] Czat");
                GUI.color = Color.white;
            }
        }

        private void DrawMenu(int id)
        {
            GUI.DragWindow(new Rect(0, 0, 600, 20));
            GUILayout.BeginVertical();
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nick:", GUILayout.Width(60));
            playerNick = GUILayout.TextField(playerNick, 20, GUILayout.Width(180));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Serwery publiczne", GUILayout.Width(185))) showPrivate = false;
            if (GUILayout.Button("Serwer prywatny", GUILayout.Width(185))) showPrivate = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            if (!showPrivate)
            {
                GUILayout.Label("Dostepne serwery:");
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUI.color = Color.cyan;
                GUILayout.Label("play.szybkihost.eu:7777", GUILayout.Width(220));
                GUI.color = Color.white;
                GUILayout.Label("0/10", GUILayout.Width(40));
                if (GUILayout.Button("Dolacz", GUILayout.Width(80)))
                {
                    serverIP = "play.szybkihost.eu";
                    serverPort = "7777";
                    Connect();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Serwer prywatny:");
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(40));
                serverIP = GUILayout.TextField(serverIP, GUILayout.Width(240));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port:", GUILayout.Width(40));
                serverPort = GUILayout.TextField(serverPort, GUILayout.Width(80));
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
                if (GUILayout.Button("Dolacz", GUILayout.Width(100))) Connect();
            }

            GUILayout.Space(8);
            GUI.color = connected ? Color.green : Color.gray;
            GUILayout.Label("Status: " + statusMsg);
            GUI.color = Color.white;

            GUILayout.Space(4);
            GUILayout.Label("Gracze (" + playerList.Count + "):");
            foreach (var p in playerList)
            {
                GUILayout.BeginHorizontal();
                string prefix = p.Key == myID ? "★ " : "• ";
                string hostTag = (p.Key == myID && isHost) ? " [HOST]" : "";
                GUILayout.Label(prefix + p.Value + hostTag, GUILayout.Width(200));
                if (p.Key != myID && isHost)
                {
                    if (GUILayout.Button("Kick", GUILayout.Width(50)))
                        ModConsole.Log("[MSCMultiplayer] Kick: " + p.Value);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (connected)
            {
                if (GUILayout.Button("Rozlacz", GUILayout.Width(90))) Disconnect();
            }
            if (GUILayout.Button("Zamknij", GUILayout.Width(90))) showMenu = false;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
    }
}