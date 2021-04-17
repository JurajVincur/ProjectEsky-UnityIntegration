using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System;
using System.Net;
using System.Text;
using System.Linq;
using System.ComponentModel;
using System.Threading;
using Microsoft.MixedReality.WebRTC;
using System.Threading.Tasks;

namespace ProjectEsky.Networking.Discovery{
    public class PackageManagerHookBehaviour : Microsoft.MixedReality.WebRTC.Unity.Signaler{
        public delegate void BytesReceivedDelegate(byte[] b);
        public BytesReceivedDelegate BytesReceived;
        public virtual void SendBytes(byte[] b){
            
        }
        public override Task SendMessageAsync(SdpMessage message)
        {
            return null;
        }

        /// <inheritdoc/>
        public override Task SendMessageAsync(IceCandidate candidate)
        {
            return null;
        }
    }
    

    [System.Serializable]
    public class IceMessageWebsocket{
        public string candidate;
        public int sdpMlineIndex;
        public string sdpMid;
        public IceMessageWebsocket(string cand, int sdpmline, string sdpmid){
            candidate = cand;
            sdpMlineIndex = sdpmline;
            sdpMid = sdpmid;
        }
    }
    [System.Serializable]
    public class SdpMessageWebsocket{
        public string sdp;
        public string type;
        public SdpMessageWebsocket(string sdpMessage, string sdpType){
            sdp = sdpMessage;
            type = sdpType; 
        }
    }
    
    [System.Serializable]
    public class WebrtcShakeString{
        public List<string> IceMessages;
        public string sdpMessage;
    }
    [System.Serializable]
    public class WebrtcShakeClass{
        public List<IceMessageWebsocket> iceMessages;
        public SdpMessageWebsocket sdpMessage;
    }
    public delegate void CreateOfferDelegate();
    public delegate void CreateResponseDelegate();

    public class UDPAutoDiscovery : PackageManagerHookBehaviour
    {
        BackgroundWorker objWorkerDiscovery;
        AutoDiscoverySender ads;
        AutoDiscoveryReceiver adr;
        public bool hasMessagePrepared = false;
        public WebrtcShakeString shake = new WebrtcShakeString();
        bool createOffer = false;
        bool createAnswer = false;
        public void Start() {
            objWorkerDiscovery = new BackgroundWorker();
            objWorkerDiscovery.WorkerReportsProgress = true;
            objWorkerDiscovery.WorkerSupportsCancellation = true;
            adr = new AutoDiscoveryReceiver(ref objWorkerDiscovery,CreateOffer,this);
            objWorkerDiscovery.DoWork += new DoWorkEventHandler(adr.Start);
            objWorkerDiscovery.ProgressChanged += new ProgressChangedEventHandler(LogProgressChanged);
            objWorkerDiscovery.RunWorkerAsync();
        }
        private void FixedUpdate() {
            if(createOffer){createOffer = false;PeerConnection.StartConnection();}    
            if(createAnswer){createAnswer = false;PeerConnection.Peer.CreateAnswer();}        
        }
        public void StartSender(){
            shake.IceMessages.Clear();
            objWorkerDiscovery.CancelAsync();
            adr.Stop();
            adr = null;
            objWorkerDiscovery = new BackgroundWorker();
            objWorkerDiscovery.WorkerReportsProgress = true;
            objWorkerDiscovery.WorkerSupportsCancellation = true;
            ads = new AutoDiscoverySender(ref objWorkerDiscovery,CreateAnswer,this);
            objWorkerDiscovery.DoWork += new DoWorkEventHandler(ads.Start);
            objWorkerDiscovery.ProgressChanged += new ProgressChangedEventHandler(LogProgressChanged);
            objWorkerDiscovery.RunWorkerAsync();            
        }
        private void LogProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // Report thread messages to Console
            Debug.Log(e.UserState.ToString());
        }

        public void OnDestroy(){
            if(ads != null){
                ads.Stop();
            }
            if(adr != null){

                adr.Stop();
            }
        }
        
        public void CreateOffer(){
            createOffer = true;
        }
        public void CreateAnswer(){
            createAnswer = true;
        }

        public void ReceiveCompletedOffer(WebrtcShakeClass receivedClass){
            Debug.Log("Received Offer: " + receivedClass.sdpMessage.sdp);
            SdpMessage sdpOffer = new SdpMessage { Type = SdpMessageType.Offer, Content = receivedClass.sdpMessage.sdp};
            PeerConnection.HandleConnectionMessageAsync(sdpOffer).ContinueWith(_ =>
            {
                // If the remote description was successfully applied then immediately send
                // back an answer to the remote peer to acccept the offer.
                _nativePeer.CreateAnswer();
            }, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.RunContinuationsAsynchronously);     
        }
        public void ReceiveCompletedAnswer(WebrtcShakeClass receivedClass){
            Debug.Log("Received Answer: " + receivedClass.sdpMessage.sdp);
            SdpMessage sdpAnswer = new SdpMessage { Type = SdpMessageType.Answer, Content = receivedClass.sdpMessage.sdp};                            
            PeerConnection.HandleConnectionMessageAsync(sdpAnswer).ContinueWith(_ =>
            {
                Debug.Log("Handled Answer");
            }, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.RunContinuationsAsynchronously);
        }
        public void ReceiveIceCandidate(WebrtcShakeClass receivedClass){
            for(int i = 0; i < receivedClass.iceMessages.Count; i++){
                _nativePeer.AddIceCandidate(new IceCandidate{SdpMid = receivedClass.iceMessages[i].sdpMid,SdpMlineIndex = receivedClass.iceMessages[i].sdpMlineIndex,Content = receivedClass.iceMessages[i].candidate});
            }
        }
        
        protected override void OnIceCandidateReadyToSend(IceCandidate candidate)
        {
          //Data = string.Join(IceSeparatorChar, candidate.Content, candidate.SdpMlineIndex.ToString(), candidate.SdpMid);      
            IceMessageWebsocket imws = new IceMessageWebsocket(candidate.Content,candidate.SdpMlineIndex,candidate.SdpMid);
            string json = JsonUtility.ToJson(imws);  
            shake.IceMessages.Add(json);
        }
        protected override void OnSdpOfferReadyToSend(SdpMessage offer)
        {

            SdpMessageWebsocket sdpws = new SdpMessageWebsocket(offer.Content,"offer");
            Debug.Log("Sending Offer: " + sdpws.sdp);                        
            string json = JsonUtility.ToJson(sdpws);
            shake.sdpMessage = json;
        }
        protected override void OnSdpAnswerReadyToSend(SdpMessage answer)
        {

            SdpMessageWebsocket sdpws = new SdpMessageWebsocket(answer.Content,"answer");
            Debug.Log("Sending Answer: " + sdpws.sdp);
            string json = JsonUtility.ToJson(sdpws);
            shake.sdpMessage = json;            
        }
    }
    public class AutoDiscoveryReceiver
    {
        public CreateOfferDelegate createOfferDelegate;
        public UDPAutoDiscovery hookedAutoDiscovery;
        private System.ComponentModel.BackgroundWorker workerUDP;

        // Port the UDP server will listen to broadcast packets from UDP Clients.
        private int AutoDiscoveryPort = 18500;

        // Sample byte sequency that Identify a Server Address Request. You may change on the client-side also.
        // Implementing other byte sequences for other actions are also valid. You as developer may know that ;)
        byte[] packetBytes = new byte[] { 0x1, 0x2, 0x3 };

        // In the following example code we reply to incoming client an IP Address that
        // Client must use as server for any purpose. (TCP Server not implemented)
        public IPAddress addrDaemonListenIP;
        
        // Which port we will broadcast as TCP Server (not implemented).
        public int BroadCastDaemonPort = 0;

        private bool disposing = false;

        public AutoDiscoveryReceiver(ref BackgroundWorker workerUDP, CreateOfferDelegate offerDelegate,UDPAutoDiscovery hookedDiscovery)
        {
            this.workerUDP = workerUDP;
            this.BroadCastDaemonPort = AutoDiscoveryPort;
            this.addrDaemonListenIP = IPAddress.Parse("0.0.0.0");
            this.createOfferDelegate = offerDelegate;
            this.hookedAutoDiscovery = hookedDiscovery;
        }

        public void Stop()
        {
            workerUDP.CancelAsync();
            workerUDP.Dispose();
            this.disposing = true;
        }
        public string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        /// <summary>
        /// Start the listener.
        /// </summary>
        public void Start(object sender, DoWorkEventArgs e)
        {
            try
            {
                this.workerUDP.ReportProgress(30, "AutoDiscoveryReceiver::Service Listening " + this.AutoDiscoveryPort + "/UDP");
                byte[] ReceivedData = new byte[1024];


                // Local End-Point
                IPEndPoint LocalEP = new IPEndPoint(IPAddress.Any, AutoDiscoveryPort);
                IPEndPoint RemoteEP = new IPEndPoint(IPAddress.Any, 0);

                UdpClient newsock = new UdpClient(LocalEP);

                ReceivedData = newsock.Receive(ref RemoteEP);

                IPEndPoint IncomingIP = (IPEndPoint)RemoteEP;

                while (!disposing)
                {
                    if (ReceivedData.SequenceEqual(packetBytes))
                    {
                        // Use ReportProgress from BackgroundWorker as communication channel between main app and the worker thread.
                        this.workerUDP.ReportProgress(1, "Discovery from " + IncomingIP + "/UDP");

                        // Here we reply the Service IP and Port (TCP).. 
                        // You must point to your server and service port. For example a webserver: sending the correct IP and port 80.
                        createOfferDelegate.Invoke();
                        Thread.Sleep(1000);//wait for the candidates to be generated
                        string s = JsonUtility.ToJson(hookedAutoDiscovery.shake);
                        this.workerUDP.ReportProgress(1, "Got discovered, sending offer: " + s);
                        byte[] packetBytesAck = Encoding.ASCII.GetBytes("ACK=" + s); // Acknowledged
                        newsock.Send(packetBytesAck, packetBytesAck.Length, RemoteEP);
                        this.workerUDP.ReportProgress(1, "Answering(ACK) " + packetBytesAck.Length + " bytes to " + IncomingIP);
                    }
                    else
                    {
                        // Unknown packet type.
                        this.workerUDP.ReportProgress(1, "Answering(NAK) " + packetBytes.Length + " bytes to " + IncomingIP);
                        byte[] packetBytesNak = Encoding.ASCII.GetBytes("NAK"); // Not Acknowledged

                        newsock.Send(packetBytesNak, packetBytesNak.Length, RemoteEP);
                    }
                    ReceivedData = newsock.Receive(ref RemoteEP);
                }

                

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }
    }
    public class AutoDiscoverySender
        {
            public CreateResponseDelegate createAnswerDelegate;
            public UDPAutoDiscovery hookedAutoDiscovery;
            public bool disposing = false;

            // Fixed Port for broadcast.
            // You may change it but CLIENT and SERVER must be configured with the same port.
            private int AutoDiscoveryPort = 18500;

            // Specify timeout since UDP is a state-less protocol
            // 5000ms - 5 seconds.
            private int ServerSyncTimeout = 5000;
            
            private int AutoDiscoveryTimeout = 10000;

            // Sample byte sequency that Identify a Server Address Request. You may change on the client-side also.
            // Implementing other byte sequences for other actions are also valid. You as developer may know that ;)
            byte[] packetBytes = new byte[] { 0x1, 0x2, 0x3 };

            // Do not change:
            // We will set this variable when Auto Discovery server reply with ACK;
            // Do not change. This will be set if Server is found and check for TCP Connections is OK
            public string ServerAddress = String.Empty;
            public int ServerPort = 0;

            private BackgroundWorker worker;

            public AutoDiscoverySender(ref BackgroundWorker worker, CreateResponseDelegate offerDelegate, UDPAutoDiscovery hookedDiscovery)
            {
                this.worker = worker;
                worker.ReportProgress(1, "AutoDiscoverySender::Started at " + AutoDiscoveryPort + "/UDP");
                this.hookedAutoDiscovery = hookedDiscovery;
                this.createAnswerDelegate = offerDelegate;
            }

            public void Start(object sender, DoWorkEventArgs e)
            {
                try
                {
                    while (this.disposing == false)
                    {
                        // Must look for server.. Repeat until configured.
                        if (ServerAddress == String.Empty)
                        {
                            this.worker.ReportProgress(2, "AutoDiscovery::Looking for server..");

                            // Broadcast the query
                            sendBroadcastSearchPacket();
                        }

                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            public void Stop()
            {
                this.disposing = true;
            }

            // Here is to check if the server replied by Auto Discovery is Alive.
            private bool serverIsReachable(string host, int port)
            {
                try
                {
                    TcpClient handle = TCP_Client.Connect(new IPEndPoint(IPAddress.Parse(host), port), ServerSyncTimeout);
                    if (handle.Connected == true)
                    {
                        handle.Close();
                        return (true);
                    }
                    else
                    {
                        return (false);
                    }
                }
                catch (Exception e)
                {
                    worker.ReportProgress(1, "AutoDiscovery::Connect Error:" + e.Message);
                    return (false);
                }
            }

            private bool sendBroadcastSearchPacket()
            {

                bool returnVal = false;
                UdpClient udp = new UdpClient();
                udp.EnableBroadcast = true;

                udp.Client.ReceiveTimeout = AutoDiscoveryTimeout;

                IPEndPoint groupEP = new IPEndPoint(IPAddress.Parse("255.255.255.255"), AutoDiscoveryPort);


                try
                {
                    udp.Send(packetBytes, packetBytes.Length, groupEP);

                    byte[] receiveBytes = udp.Receive(ref groupEP);

                    string returnData = Encoding.ASCII.GetString(receiveBytes, 0, receiveBytes.Length);
                    if (returnData.Substring(0, 3) == "ACK")
                    {
                        Debug.Log("Received Data: " + returnData);
                        string[] splitRcvd = returnData.Split(' ');
                        this.worker.ReportProgress(3, "AutoDiscovery::Response Server is " + splitRcvd[1] + ":" + splitRcvd[2]);

                        // Check if the server is reachable! Try to connect it using TCP.
                        if (serverIsReachable(splitRcvd[1], Convert.ToInt16(splitRcvd[2])))
                        {
                            ServerAddress = splitRcvd[1];
                            ServerPort = Convert.ToInt16(splitRcvd[2]);
                            returnVal = true;
                        }
                        else
                        {
                            this.worker.ReportProgress(3, "AutoDiscovery::WARNING Server found but is unreachable. Retrying..");
                            return (false);
                        }


                    }
                    else if (returnData.Substring(0, 3) == "NAK")
                    {
                        this.worker.ReportProgress(3, "AutoDiscovery::INVALID REQUEST");
                    }
                    else
                    {
                        this.worker.ReportProgress(3, "AutoDiscovery::Garbage Received?");
                    }

                    Console.WriteLine("Sleeping. No work to do.");
                    Thread.Sleep(100);
                }
                catch (SocketException e)
                {
                    this.worker.ReportProgress(1, "AutoDiscovery::Timeout. Retrying "+e.Message);

                }

                udp.Close();

                return (returnVal);

            }

        }
    public class TCP_Client
    {

        private static bool IsConnectionSuccessful = false;
        private static Exception socketexception;
        private static ManualResetEvent TimeoutObject = new ManualResetEvent(false);

        public static TcpClient Connect(IPEndPoint remoteEndPoint, int timeoutMSec)
        {

            TimeoutObject.Reset();
            socketexception = null;

            string serverip = Convert.ToString(remoteEndPoint.Address);
            int serverport = remoteEndPoint.Port;
            TcpClient tcpclient = new TcpClient();

            tcpclient.BeginConnect(serverip, serverport,
                new AsyncCallback(CallBackMethod), tcpclient);

            if (TimeoutObject.WaitOne(timeoutMSec, false))
            {
                if (IsConnectionSuccessful)
                {
                    return tcpclient;
                }
                else
                {
                    throw socketexception;
                }
            }
            else
            {
                tcpclient.Close();
                throw new TimeoutException("TimeOut Exception");
            }

        }
        private static void CallBackMethod(IAsyncResult asyncresult)
        {
            try
            {
                IsConnectionSuccessful = false;
                TcpClient tcpclient = asyncresult.AsyncState as TcpClient;

                if (tcpclient.Client != null)
                {
                    tcpclient.EndConnect(asyncresult);
                    IsConnectionSuccessful = true;
                }
            }
            catch (Exception ex)
            {
                IsConnectionSuccessful = false;
                socketexception = ex;
            }
            finally
            {
                TimeoutObject.Set();
            }
        }

    }

}