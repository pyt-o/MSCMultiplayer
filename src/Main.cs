using MSCLoader;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MSCMultiplayer
{
    public class MSCMultiplayer : Mod
    {
        public override string ID => "MSCMultiplayer";
        public override string Name => "MSC Multiplayer";
        public override string Author => "Pyt_o";
        public override string Version => "0.1.0";
        public override string Description => "Multiplayer mod dla My Summer Car";

        private UdpClient udpClient;
        private Thread receiveThread;
        private byte myID = 0;
        private bool inGame = false;

        // Pozycja gracza — Satsuma
        private Transform playerCar;
        private float sendTimer = 0f;
        private const float SEND_INTERVAL = 0.05f; // 20 razy na sekunde

        // Inni gracze
        private Dictionary<byte, GameObject> otherPlayers = new Dictionary<byte, GameObject>();
        private Dictionary<byte, Vector3> otherPositions = new Dictionary<byte, Vector3>();

        // GUI
        private bool showMenu = false;
        private bool showPrivate = false;
        private string serverIP = "192.168.10.159";
        private string serverPort = "7777";
        private string playerNick = "Gracz";
        private Rect menuRect = new Rect(Screen.width / 2 - 300, Screen.height / 2 - 200, 600, 400);
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
            // Znajdz auto gracza
            GameObject satsuma = GameObject.Find("SATSUMA(557kg, 248)");
            if (satsuma != null)
            {
                playerCar = satsuma.transform;
                ModConsole.Log("[MSCMultiplayer] Znaleziono Satsume!");
            }
            else
            {
                ModConsole.LogWarning("[MSCMultiplayer] Nie znaleziono Satsumy.");
            }
        }

        private void OnUpdate()
        {
            if (!inGame || udpClient == null || myID == 0) return;

            sendTimer += Time.deltaTime;
            if (sendTimer >= SEND_INTERVAL)
            {
                sendTimer = 0f;
                SendPosition();
            }

            // Interpolacja pozycji innych graczy
            foreach (var kvp in otherPlayers)
            {
                if (otherPositions.ContainsKey(kvp.Key))
                {
                    kvp.Value.transform.position = Vector3.Lerp(
                        kvp.Value.transform.position,
                        otherPositions[kvp.Key],
                        Time.deltaTime * 10f
                    );
                }
            }
        }

        private void SendPosition()
        {
            if (playerCar == null) return;
            try
            {
                Vector3 pos = playerCar.position;
                Quaternion rot = playerCar.rotation;

                // Pakiet: [0x02][ID][x][y][z][rotY] = 18 bajtów
                byte[] packet = new byte[18];
                packet[0] = 0x02;
                packet[1] = myID;
                Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, packet, 2, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, packet, 6, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, packet, 10, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rot.eulerAngles.y), 0, packet, 14, 4);

                udpClient.Send(packet, packet.Length);
            }
            catch { }
        }

        private void SpawnOtherPlayer(byte id)
        {
            if (otherPlayers.ContainsKey(id)) return;

            // Prosty box jako placeholder dla innego gracza
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = "MSCPlayer_" + id;
            obj.transform.localScale = new Vector3(1.5f, 1.2f, 3.5f);

            // Żółty kolor
            Renderer r = obj.GetComponent<Renderer>();
            if (r != null) r.material.color = Color.yellow;

            // Usuń kolider żeby nie blokował gracza
            Collider col = obj.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            otherPlayers[id] = obj;
            ModConsole.Log("[MSCMultiplayer] Stworzono gracza " + id);
        }

        private void RemoveOtherPlayer(byte id)
        {
            if (otherPlayers.ContainsKey(id))
            {
                UnityEngine.Object.Destroy(otherPlayers[id]);
                otherPlayers.Remove(id);
                otherPositions.Remove(id);
                ModConsole.Log("[MSCMultiplayer] Usunięto gracza " + id);
            }
        }

        private void OnGUIDraw()
        {
            if (GUI.Button(new Rect(10, Screen.height - 50, 150, 40), "MULTIPLAYER"))
                showMenu = !showMenu;

            if (showMenu)
                menuRect = GUI.Window(9999, menuRect, DrawMenu, "MSC Multiplayer v" + Version);
        }

        private void DrawMenu(int id)
        {
            GUI.DragWindow(new Rect(0, 0, 600, 20));
            GUILayout.BeginVertical();
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nick: ", GUILayout.Width(80));
            playerNick = GUILayout.TextField(playerNick, 20, GUILayout.Width(200));
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Serwery publiczne", GUILayout.Width(180))) showPrivate = false;
            if (GUILayout.Button("Serwer prywatny", GUILayout.Width(180))) showPrivate = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (!showPrivate)
            {
                GUILayout.Label("Dostepne serwery publiczne:");
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label("play.szybkihost.eu:7777", GUILayout.Width(250));
                GUILayout.Label("0/10 graczy", GUILayout.Width(100));
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
                GUILayout.Label("Wpisz adres serwera:");
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(80));
                serverIP = GUILayout.TextField(serverIP, GUILayout.Width(250));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port:", GUILayout.Width(80));
                serverPort = GUILayout.TextField(serverPort, GUILayout.Width(100));
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
                if (GUILayout.Button("Dolacz", GUILayout.Width(100))) Connect();
            }

            GUILayout.Space(10);
            GUILayout.Label("Status: " + statusMsg);
            GUILayout.Space(10);
            if (GUILayout.Button("Zamknij", GUILayout.Width(100))) showMenu = false;
            GUILayout.EndVertical();
        }

        private void Connect()
{
    try
    {
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
            myID = 0;
            Thread.Sleep(100);
        }

        int port = int.Parse(serverPort);
        udpClient = new UdpClient();
        udpClient.Connect(serverIP, port);

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

        private void ReceiveLoop()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] data = udpClient.Receive(ref endpoint);
                    if (data.Length == 0) continue;

                    switch (data[0])
                    {
                        case 0x01: // Welcome
                            myID = data[1];
                            statusMsg = "Polaczony! ID: " + myID + " Nick: " + playerNick;
                            ModConsole.Log("[MSCMultiplayer] Polaczono! ID: " + myID);
                            break;

                        case 0x02: // Pozycja innego gracza
                            if (data.Length >= 18)
                            {
                                byte pid = data[1];
                                float x = BitConverter.ToSingle(data, 2);
                                float y = BitConverter.ToSingle(data, 6);
                                float z = BitConverter.ToSingle(data, 10);
                                float ry = BitConverter.ToSingle(data, 14);

                                if (!otherPlayers.ContainsKey(pid))
                                    SpawnOtherPlayer(pid);

                                otherPositions[pid] = new Vector3(x, y, z);

                                if (otherPlayers.ContainsKey(pid))
                                    otherPlayers[pid].transform.rotation = Quaternion.Euler(0, ry, 0);
                            }
                            break;

                        case 0xFF: // Server full
                            statusMsg = "Serwer pelny!";
                            break;

                        case 0x00: // Gracz rozlaczony
                            if (data.Length >= 2)
                                RemoveOtherPlayer(data[1]);
                            break;
                    }
                }
                catch { break; }
            }
        }
    }
}