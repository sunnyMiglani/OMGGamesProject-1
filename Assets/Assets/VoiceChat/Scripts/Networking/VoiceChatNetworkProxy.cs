﻿using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace VoiceChat.Networking
{
    public class VoiceChatNetworkProxy : NetworkBehaviour
    {
        public delegate void MessageHandler<T>(T data);
        public static event MessageHandler<VoiceChatPacketMessage> VoiceChatPacketReceived;
        public static event System.Action<VoiceChatNetworkProxy> ProxyStarted;

		private const string ProxyPrefabPath = "VoiceChat_NetworkProxy"; //The label for the networkProxy
        private static GameObject proxyPrefab;
        private static int localProxyId;
        private static Dictionary<int, GameObject> proxies = new Dictionary<int, GameObject>();

        public bool isMine { get { return networkId != 0 && networkId == localProxyId; } }
        // 
        
        [SyncVar]
        private int networkId;

        VoiceChatPlayer player = null;

        void Start()
        {
            if (isMine)
            {
                if (LogFilter.logDebug)
                {
                    Debug.Log("Setting VoiceChat recorder NetworkId.");
                }

                VoiceChatRecorder.Instance.NewSample += OnNewSample;
                Debug.Log("THIS IS THE INSTANCE BEING USED IN NETWORK PROXY");
                Debug.Log(VoiceChatRecorder.Instance);
                VoiceChatRecorder.Instance.NetworkId = networkId;
            }
            else
            {
                VoiceChatPacketReceived += OnReceivePacket;
            }

            if (isClient && (!isMine || VoiceChatSettings.Instance.LocalDebug))
            {
               //if(GetComponent<AudioSource>() == null) // if there is no audio component.
                {
                    gameObject.AddComponent<AudioSource>();
                }
                player = gameObject.AddComponent<VoiceChatPlayer>();
            }

            if (ProxyStarted != null)
            {
                ProxyStarted(this);
            }
        }

        void OnDestroy()
        {
            if (VoiceChatRecorder.Instance != null)
                VoiceChatRecorder.Instance.NewSample -= OnNewSample;
            VoiceChatPacketReceived -= OnReceivePacket;
        }

        private void OnReceivePacket(VoiceChatPacketMessage data)
        {

            //if (LogFilter.logDebug)
            //{
            //    Debug.Log("Received a new Voice Sample. Playing!");
            //}

            if (data.proxyId == networkId)
            {
                player.OnNewSample(data.packet);
            }
        }

        void OnNewSample(VoiceChatPacket packet)
        {
            var packetMessage = new VoiceChatPacketMessage {
                proxyId = (short)localProxyId,
                packet = packet,
            };

            //if (LogFilter.logDebug)
            //{
            //    Debug.Log("Got a new Voice Sample. Streaming!");
            //}

            NetworkManager.singleton.client.SendUnreliable(VoiceChatMsgType.Packet, packetMessage);
        }



        void SetNetworkId(int networkId) // Sunny : Found where networkId is set if not already a host or client.
        {
            var netIdentity = GetComponent<NetworkIdentity>();
            if (netIdentity.isServer || netIdentity.isClient)
            {
                Debug.LogWarning("Can only set NetworkId before spawning");
                return;
            }
            Debug.Log("#### Network ID IS :" + networkId);
            this.networkId = networkId;
            //VoiceChatRecorder.Instance.NetworkId = networkId;
        }

      
        #region NetworkManager Hooks
		// Called from OMGNetManager
		// what is the customPrefab, an optional parameter?
        public static void OnManagerStartClient(NetworkClient client, GameObject customPrefab = null)
        {
            client.RegisterHandler(VoiceChatMsgType.Packet, OnClientPacketReceived);
            client.RegisterHandler(VoiceChatMsgType.SpawnProxy, OnProxySpawned);

			//Since OMGNetManager never specifies the optional parameter, customprefab defualts to null
			//The else part of this statement will never run
            if (customPrefab == null) 
            {
				//This is loaded from VoiceChat\Resources\Legacy\
                proxyPrefab = Resources.Load<GameObject>(ProxyPrefabPath); 
            }
            else
            {
                proxyPrefab = customPrefab;
            }
            
            ClientScene.RegisterPrefab(proxyPrefab); //creates the clientside version?
        }

        public static void OnManagerStopClient()
        {
            var client = NetworkManager.singleton.client;
            if (client == null) return;

            client.UnregisterHandler(VoiceChatMsgType.Packet);
            client.UnregisterHandler(VoiceChatMsgType.SpawnProxy);
        }

        public static void OnManagerServerDisconnect(NetworkConnection conn)
        {
            var id = conn.connectionId;

            if (!proxies.ContainsKey(id))
            {
                Debug.LogWarning("Proxy destruction requested for client " + id + " but proxy wasn't registered");
                return;
            }

            var proxy = proxies[id];
            NetworkServer.Destroy(proxy);

            proxies.Remove(id);
        }

		/* Registers a handlers for a specific message, VoiceCharMsgTypes are defined in the corresponding cs script
		 * public static void RegisterHandler(short msgType, Networking.NetworkMessageDelegate handler); 
		 * The handler is the callback invoked when the message is recieved, theoy are defined in this script 
		*/
        public static void OnManagerStartServer()
		{	
            NetworkServer.RegisterHandler(VoiceChatMsgType.Packet, OnServerPacketReceived); 
            NetworkServer.RegisterHandler(VoiceChatMsgType.RequestProxy, OnProxyRequested);
			proxyPrefab = Resources.Load<GameObject>(ProxyPrefabPath); //Server needed a reference to the ProxyPrefab
        }

        public static void OnManagerStopServer()
        {
            NetworkServer.UnregisterHandler(VoiceChatMsgType.Packet);
            NetworkServer.UnregisterHandler(VoiceChatMsgType.RequestProxy);
        }

        public static void OnManagerClientConnect(NetworkConnection connection)
        {
            var client = NetworkManager.singleton.client;
            client.Send(VoiceChatMsgType.RequestProxy, new EmptyMessage());
        }
        
        #endregion

        #region Network Message Handlers

		//When a client requets a proxy
        private static void OnProxyRequested(NetworkMessage netMsg) 
        {
            var id = netMsg.conn.connectionId; // Sunny: Extract NetworkId here and set for the instance using the function below

            if (LogFilter.logDebug)
            {
                Debug.Log("Proxy Requested by " + id);
            }

            // We need to set the "localProxyId" static variable on the client
            // before the "Start" method of the local proxy is called.
            // On Local Clients, the Start method of a spowned obect is faster than
            // Connection.Send() so we will set the "localProxyId" flag ourselves
            // since we are in the same instance of the game.
            if (id == -1)
            {
                if (LogFilter.logDebug)
                {
                    Debug.Log("Local proxy! Setting local proxy id by hand");
                }

                VoiceChatNetworkProxy.localProxyId = id;
            }
            else
            {
                netMsg.conn.Send(VoiceChatMsgType.SpawnProxy, new IntegerMessage(id));
            }
			//proxyprefab defintely exists when a client has joined
			GameObject proxy = Instantiate<GameObject>(proxyPrefab) as GameObject; //TODO this is causing the error for a dedicated server
			//var proxy = Instantiate<GameObject>(proxyPrefab);

            proxy.SendMessage("SetNetworkId", id);

            proxies.Add(id, proxy);
            NetworkServer.Spawn(proxy);

        }

        private static void OnProxySpawned(NetworkMessage netMsg)
        {
            localProxyId = netMsg.ReadMessage<IntegerMessage>().value;

            if (LogFilter.logDebug)
            {
                Debug.Log("Proxy spawned for " + localProxyId + ", setting local proxy id.");
            }

        }

        private static void OnServerPacketReceived(NetworkMessage netMsg)
        {
            var data = netMsg.ReadMessage<VoiceChatPacketMessage>();

            foreach (var connection in NetworkServer.connections)
            {
                if (connection == null || connection.connectionId == data.proxyId)
                    continue;

                connection.SendUnreliable(VoiceChatMsgType.Packet, data);
            }

            foreach (var connection in NetworkServer.localConnections)
            {
                if (connection == null || connection.connectionId == data.proxyId)
                    continue;

                connection.SendUnreliable(VoiceChatMsgType.Packet, data);
            }

        }

        private static void OnClientPacketReceived(NetworkMessage netMsg)
        {
            if (VoiceChatPacketReceived != null)
            {
                var data = netMsg.ReadMessage<VoiceChatPacketMessage>();
                VoiceChatPacketReceived(data);
            }
        }
        
        #endregion
    }
}