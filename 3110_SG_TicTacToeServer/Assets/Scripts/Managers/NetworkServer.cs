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
using System.Linq;

public class NetworkServer : MonoBehaviour
{
    #region Variables

    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 9001;

    const int MaxNumberOfClientConnections = 1000;

    List<Account> savedAccounts;
    string filePath;

    LinkedList<GameRoom> roomList;
    public int playerMatchID = -1;

    // Mapping between NetworkConnection and unique player ID
    private Dictionary<NetworkConnection, int> connectionToPlayerId;

    #endregion

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

        #region Account File Path
        savedAccounts = new List<Account>();
        filePath = Application.dataPath + Path.DirectorySeparatorChar + "savedAccountData.txt";
        if (File.Exists(filePath))
        {
            Debug.Log("File found!");
        }

        LoadOldUser();

        #endregion

        roomList = new LinkedList<GameRoom>();

        // Initialize dictionary for mapping connections to player IDs
        connectionToPlayerId = new Dictionary<NetworkConnection, int>();

    }

    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {
        #region Check Input and Send Msg

        //if (Input.GetKeyDown(KeyCode.A))
        //{
        //    for (int i = 0; i < networkConnections.Length; i++)
        //    {
        //        SendMessageToClient("Hello client's world, sincerely your network server", networkConnections[i]);
        //    }
        //}

        #endregion

        networkDriver.ScheduleUpdate().Complete();

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

        #region Accept New Connections

        while (AcceptIncomingConnection())
        {
            Debug.Log("Accepted a client connection");
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

            while (PopNetworkEventAndCheckForData(networkConnections[i], out networkEventType, out streamReader, out pipelineUsedToSendEvent))
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

    #region Methods
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

    //this need to get reworked
    private void ProcessReceivedMsg(string msg)
    {
        Debug.Log("Msg received = " + msg);

        #region Check to see what message was sent via Signifier

        //process each line from the file
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

        #region Depending on identifier sent back, work accordingly

        #region See if they are creating an account or not
        if (identifier == ClientServerSignifiers.CreateAccount)
        {
            string userName = charParse[1];
            string password = charParse[2];
            bool checkIsUsed = false;
            //iterate through all the accounts to check
            foreach (Account a in savedAccounts)
            {
                //if the username matches with one thats already existing
                if (userName == a.username)
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
                        SaveNewUser(new Account(userName, password));
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

        #region Joining Queue

        else if (identifier == ClientServerSignifiers.JoinQueue)
        {
            HandleJoinOrCreateRoom(roomName);
        }

        #endregion

        #region Making move

        else if (identifier == ClientServerSignifiers.MakeMove)
        {
            int row = int.Parse(charParse[1]);
            int col = int.Parse(charParse[2]);
            Debug.Log("Move made");

            // Broadcast move to opponent
            NotifyOpponentMove(row, col);
        }

        #endregion

        #region All fails - Send Debug message saying HUH???

        else
        {
            Debug.Log("Unknown identifier: " + identifier);
        }

        #endregion

        #endregion

        #endregion
    }

    #region Room join / Creation

    private void HandleJoinOrCreateRoom(string roomName)
    {
        if (playerMatchID == -1)
        {
            playerMatchID = GetNextPlayerMatchID();
        }

        // Check if a room with the given name already exists
        GameRoom existingRoom = roomList.FirstOrDefault(r => r.roomName == roomName);

        if (existingRoom != null && existingRoom.playerID2 == -1)
        {
            // If the room exists and has space, assign the player to this room
            existingRoom.playerID2 = playerMatchID;
            Debug.Log($"Joined existing room: {roomName} with playerID: {playerMatchID}");

            //Notify both players to start the game
            foreach (var connection in networkConnections)
            {
                if (connectionToPlayerId.ContainsKey(connection))
                {
                    int playerId = connectionToPlayerId[connection];

                    #region If the ID matchs player 1 - Assign player 1 role

                    if (playerId == existingRoom.playerID1)
                    {
                        SendMessageToClient(ServerClientSignifiers.StartGame + "", connection);

                        playerMatchID = -1;
                        SendMessageToClient(ServerClientSignifiers.ChosenAsPlayerOne + "", connection);

                        Debug.Log("Player 1 chosen " + playerId + ", to " + connection);
                        Debug.Log(existingRoom.playerID1);
                    }

                    #endregion

                    #region If it matches player 2 - Assign player 2 role

                    if (playerId == existingRoom.playerID2)
                    {
                        SendMessageToClient(ServerClientSignifiers.StartGame + "", connection);
                        Debug.Log("Player 2 chosen " + playerId + ", to " + connection);
                        Debug.Log(existingRoom.playerID2);
                        SendMessageToClient(ServerClientSignifiers.ChosenAsPlayerTwo + "", connection);
                    }

                    #endregion
                }
            }

        }
        else
        {
            // If no available room exists, create a new room
            GameRoom newRoom = new GameRoom(playerMatchID, -1) { roomName = roomName };
            roomList.AddLast(newRoom);
            Debug.Log($"Created new room: {roomName} with playerID: {playerMatchID}");
        }

        playerMatchID = -1;
    }

    #endregion

    private int GetNextPlayerMatchID()
    {
        return networkConnections.Length;
    }

    #region Send message to client

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

    #endregion

    #region Login Methods
    private void SaveNewUser(Account newAccount)
    {
        //add new account
        savedAccounts.Add(newAccount);

        StreamWriter sw = new StreamWriter(filePath, true);
        sw.WriteLine(newAccount.username + "," + newAccount.password);

        sw.Close();
    }

    private void LoadOldUser()
    {
        if (File.Exists(filePath) == false)
            return;

        string line = "";

        StreamReader sr = new StreamReader(filePath);
        while ((line = sr.ReadLine()) != null)
        {
            string[] charParse = line.Split(',');
            savedAccounts.Add(new Account(charParse[0], charParse[1]));
        }
        sr.Close();
    }

    public bool CheckCredentials(string username, string password)
    {
        foreach (var account in savedAccounts)
        {
            if (account.username == username && account.password == password)
            {
                return true;
            }
        }
        return false;
    }

    private void HandleLogin(string userName, string password)
    {
        bool loginSuccess = false;

        // Check if the user exists and the password is correct
        foreach (Account a in savedAccounts)
        {
            if (userName == a.username && password == a.password)
            {
                loginSuccess = true;
                break;
            }
        }

        if (loginSuccess)
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

    private void NotifyOpponentMove(int row, int col)
    {
        foreach (var connection in networkConnections)
        {
            if (connectionToPlayerId.ContainsKey(connection))
            {
                int playerId = connectionToPlayerId[connection];

                if (playerId != playerMatchID)
                {
                    Debug.Log("Sending move made to client...");
                    SendMessageToClient($"{ClientServerSignifiers.MakeMove},{row},{col}", connection);
                }
            }
        }
    }

    #endregion

}

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

#region New Classes
public class Account
{
    #region Variables

    public string username;
    public string password;

    #endregion

    public Account(string username, string password)
    {
        this.username = username;
        this.password = password;
    }

}
public class GameRoom
{
    public int playerID1;
    public int playerID2;
    public string roomName;

    public GameRoom(int playerID1, int playerID2)
    {
        this.playerID1 = playerID1;

        this.playerID2 = playerID2;
    }
}
#endregion

#region ENUM
public enum GameState
{
    Login,
    InGame,
}
#endregion