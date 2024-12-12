using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using UnityEditor.MemoryProfiler;
using UnityEditor.VersionControl;
using System.IO;
using Unity.Jobs;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 9001;
    const int MaxNumberOfClientConnections = 1000;

    private NetworkConnection currentConnection;

    List<SaveFiles> playerSaves;
    private string filePath;
   
    private Dictionary<NetworkConnection, int> connectionToPlayerId;

    private enum ServerState
    {
        WaitForLogin,
        LoggedIn
    }

    private ServerState currentState;

    void Start()
    {
        
        networkDriver = NetworkDriver.Create();
        reliableAndInOrderPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        nonReliableNotInOrderedPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage));
        NetworkEndpoint endpoint = NetworkEndpoint.AnyIpv4;
        endpoint.Port = NetworkPort;

        int error = networkDriver.Bind(endpoint);
        if (error != 0)
            Debug.Log("Failed to bind to port " + NetworkPort);
        else
            networkDriver.Listen();
        Debug.Log("Successfully was able to bind to port " + NetworkPort);

        networkConnections = new NativeList<NetworkConnection>(MaxNumberOfClientConnections, Allocator.Persistent);

        #region Account File
        playerSaves = new List<SaveFiles>();
        filePath = Application.dataPath + Path.DirectorySeparatorChar + "savedAccountData.txt";
        if (File.Exists(filePath))
        {
            Debug.Log("File Found");
        }

        LoadPlayer();
        #endregion

        connectionToPlayerId = new Dictionary<NetworkConnection, int>();
    }


    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {

        networkDriver.ScheduleUpdate().Complete();

        #region Accept New Connections

        while (AcceptIncomingConnection())
        {
            Debug.Log("Accepted a client connection");
        }

        #endregion

        #region Remove Unused Connections

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
            {
                networkConnections.RemoveAtSwapBack(i);
                i--;
            }
        }

        #endregion

        #region Manage Network Events

        DataStreamReader streamReader;
        NetworkPipeline pipelineUsedToSendEvent;
        NetworkEvent.Type networkEventType;

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
                continue;

            while (PopNetworkEventAndCheckForData(networkConnections[i], out networkEventType, out streamReader,
                       out pipelineUsedToSendEvent))
            {
                if (pipelineUsedToSendEvent == reliableAndInOrderPipeline)
                    Debug.Log("Network event from: reliableAndInOrderPipeline");
                else if (pipelineUsedToSendEvent == nonReliableNotInOrderedPipeline)
                    Debug.Log("Network event from: nonReliableNotInOrderedPipeline");

                switch (networkEventType)
                {
                    case NetworkEvent.Type.Data:
                        int sizeOfDataBuffer = streamReader.ReadInt();
                        NativeArray<byte> buffer = new NativeArray<byte>(sizeOfDataBuffer, Allocator.Persistent);
                        streamReader.ReadBytes(buffer);
                        byte[] byteBuffer = buffer.ToArray();
                        string msg = Encoding.Unicode.GetString(byteBuffer);
                        ProcessReceivedMsg(msg);
                        buffer.Dispose();
                        break;
                    case NetworkEvent.Type.Disconnect:
                        Debug.Log("Client has disconnected from server");
                        networkConnections[i] = default(NetworkConnection);
                        break;
                }
            }
        }

        #endregion
        
    }

    private bool AcceptIncomingConnection()
    {
        NetworkConnection connection = networkDriver.Accept();
        if (connection == default(NetworkConnection))
            return false;

        networkConnections.Add(connection);

        int playerId = networkConnections.Length;
        connectionToPlayerId[connection] = playerId;

        return true;
    }

    private bool PopNetworkEventAndCheckForData(NetworkConnection networkConnection, out NetworkEvent.Type networkEventType, out DataStreamReader streamReader, out NetworkPipeline pipelineUsedToSendEvent)
    {
        networkEventType = networkConnection.PopEvent(networkDriver, out streamReader, out pipelineUsedToSendEvent);

        if (networkEventType == NetworkEvent.Type.Empty)
            return false;
        return true;
    }

    private void ProcessReceivedMsg(string msg)
    {
        string[] charParse = msg.Split(',');
        if (charParse.Length < 2)
        {
            Debug.LogError("Invalid message format");
            return;
        }

        string roomName = charParse[1];  // The room name requested

        // Try parsing the identifier
        int identifier;
        if (!int.TryParse(charParse[0], out identifier))
        {
            Debug.LogError("Failed to parse identifier: " + charParse[0]);
            return;
        }

        

        #region See if they are creating an account or not
        if (identifier == ClientServerSignifiers.CreateAccount)
        {
            string userName = charParse[1];
            string password = charParse[2];
            bool checkIsUsed = false;
            //iterate through all the accounts to check
            foreach (SaveFiles p in playerSaves)
            {
                //if the username matches with one thats already existing
                if (userName == p.username)
                {
                    checkIsUsed = true;
                }
            }

            if (checkIsUsed)
            {
                foreach (NetworkConnection connection in networkConnections)
                {
                    if (connection.IsCreated)
                    {
                        SendMessageToClient(ServerClientSignifiers.AccountCreationFailed + ", username is already in use!", connection);
                        Debug.Log("Failed to create!");
                    }
                }
            }
            else
            {
                foreach (NetworkConnection connection in networkConnections)
                {
                    if (connection.IsCreated)
                    {
                        SaveNewPlayer(new SaveFiles(userName, password));
                        SendMessageToClient(ServerClientSignifiers.AccountCreated + ", the new account has been created", connection);
                        Debug.Log("New user created!");
                    }
                }
            }
        }
        #endregion

        #region See if they are logging in
        else if (identifier == ClientServerSignifiers.Login)
        {
            string userName = charParse[1];
            string password = charParse[2];
            // Handle login logic
            HandleLogin(userName, password);
        }
        #endregion

       // #region Joining Queue

        //else if (identifier == ClientServerSignifiers.JoinQueue)
        //{
        //    //HandleJoinOrCreateRoom(roomName);
        //}

        //#endregion

        //#region Making move

        //else if (identifier == ClientServerSignifiers.MakeMove)
        //{
        //    int row = int.Parse(charParse[1]);
        //    int col = int.Parse(charParse[2]);
        //    Debug.Log("Move made");

        //    // Broadcast move to opponent
        //    NotifyOpponentMove(row, col);
        //}

        //#endregion

        //#region All fails - Send Debug message saying HUH???

        //else
        //{
        //    Debug.Log("Unknown identifier: " + identifier);
        //}


    }

    public void SendMessageToClient(string msg, NetworkConnection networkConnection)
    {
        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);


        //Driver.BeginSend(m_Connection, out var writer);
        DataStreamWriter streamWriter;
        //networkConnection.
        networkDriver.BeginSend(reliableAndInOrderPipeline, networkConnection, out streamWriter);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }

    #region Classes

    public class SaveFiles
    {
        #region Variables

        public string username;
        public string password;

        #endregion

        public SaveFiles(string username, string password)
        {
            this.username = username;
            this.password = password;
        }

    }

    #endregion

    #region Save/Load Player

    private void SaveNewPlayer(SaveFiles newSave)
    {
        playerSaves.Add(newSave);

        StreamWriter streamWriter = new StreamWriter(filePath, true);
        streamWriter.WriteLine(newSave.username + ":" + newSave.password);
        streamWriter.Close();
    }

    private void LoadPlayer()
    {
        if (File.Exists(filePath) == false)
            return;

        string line = "";

        StreamReader streamReader = new StreamReader(filePath);
        while ((line = streamReader.ReadLine()) != null)
        {
            string[] charParse = line.Split(',');
            playerSaves.Add(new SaveFiles(charParse[0], charParse[1]));
        }
        streamReader.Close();
    }

    public bool CheckUserCredentials(string username, string password)
    {
        foreach (var account in playerSaves)
        {
            if (account.username == username && account.password == password)
            {
                return true;
            }
        }
        return false;
    }

    #endregion

    #region Login

    private void HandleLogin(string userName, string password)
    {
        bool loginSuccessful = false;

        foreach (SaveFiles s in playerSaves)
        {
            if (userName == s.username && password == s.password)
            {
                loginSuccessful = true;
                break;
            }
        }
        if (loginSuccessful)
        {
            // If login is successful, send a success message to the client
            foreach (NetworkConnection connection in networkConnections)
            {
                if (connection.IsCreated)
                {
                    SendMessageToClient(ServerClientSignifiers.LoginComplete + "", connection);

                    Debug.Log("Login successful for " + userName);
                }
            }
        }
        else
        {
            // If login fails, send a failure message to the client
            foreach (NetworkConnection connection in networkConnections)
            {
                if (connection.IsCreated)
                {
                    SendMessageToClient(ServerClientSignifiers.LoginFailed + "", connection);

                    Debug.Log("Login failed for " + userName);
                }
            }
        }
    }

    #endregion

    #region Signifiers

    public static class ClientServerSignifiers
    {
        public const int CreateAccount = 1;
        public const int Login = 2;

        public const int JoinQueue = 3;
        public const int MakeMove = 4;

        public const int ChosenAsPlayerOne = 6;
        public const int ChosenAsPlayerTwo = 7;

        public const int OpponentChoseASquare = 8;

    }

    public static class ServerClientSignifiers
    {
        public const int LoginComplete = 1;
        public const int LoginFailed = 2;

        public const int AccountCreated = 3;
        public const int AccountCreationFailed = 4;

        public const int StartGame = 5;

        public const int ChosenAsPlayerOne = 6;
        public const int ChosenAsPlayerTwo = 7;

        public const int OpponentChoseASquare = 8;
        //public const int MoveSelected = 9;
    }

    #endregion

}