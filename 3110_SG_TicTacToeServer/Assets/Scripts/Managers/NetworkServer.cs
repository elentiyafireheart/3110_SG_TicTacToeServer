using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using UnityEditor.MemoryProfiler;
using UnityEditor.VersionControl;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 9001;

    const int MaxNumberOfClientConnections = 1000;

    // adding the new managers
    private AccountManager accountManager;
    private StateManager stateManager;

    private NetworkConnection currentConnetion;

    void Start()
    {
        // creating the new managers
        //accountManager = new AccountManager();
       
        accountManager = GameObject.Find("Managers").GetComponent<AccountManager>();
        if (accountManager == null)
        {
            Debug.LogError("AccountManager not found!");
        }

        stateManager = new StateManager();
        Debug.Log("Current State: " + stateManager.GetState());

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

        networkConnections = new NativeList<NetworkConnection>(MaxNumberOfClientConnections, Allocator.Persistent);
    }


    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {
        if (stateManager.GetState() == StateManager.ServerState.WaitingForLogin)
        {

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
    }

   private bool AcceptIncomingConnection()
    {
        NetworkConnection connection = networkDriver.Accept();
        if (connection == default(NetworkConnection))
            return false;

        networkConnections.Add(connection);

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
        Debug.Log("Msg received = " + msg);
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

    private void SendUIUpdateToClient(string message, NetworkConnection connection)
    {
        byte[] msgAsByteArray = Encoding.UTF8.GetBytes(message);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);

        DataStreamWriter streamWriter;
        networkDriver.BeginSend(reliableAndInOrderPipeline, connection, out streamWriter);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }

    private void HandleLogin(string msg, NetworkConnection connection)
    {
        string[] credentials = msg.Split(';');
        if (credentials.Length != 2)
        {
            SendMessageToClient("Invalid login format.", connection);
            return;
        }

        string username = credentials[0];
        string password = credentials[1];

        if (accountManager.ValidateLogin(username, password))
        {
            SendMessageToClient("Login successful!", connection);
            stateManager.SetStateLoggedIn();
        }
        else
        {
            SendMessageToClient("Invalid username or password.", connection);
        }
    }

    private void HandleAccountCreation(string msg, NetworkConnection connection)
    {
        string[] credentials = msg.Split(';');
        if (credentials.Length != 2)
        {
            SendMessageToClient("Invalid account creation format.", connection);
            return;
        }

        string username = credentials[0];
        string password = credentials[1];

        if (accountManager.CreateAccount(username, password))
        {
            SendMessageToClient("Account created successfully!", connection);
            stateManager.SetStateWaitingForLogin();
        }
        else
        {
            SendMessageToClient("Username already exists.", connection);
        }
    }

    private void HandleData(DataStreamReader streamReader, NetworkConnection connection)
    {
        int msgLength = streamReader.ReadInt();
        NativeArray<byte> buffer = new NativeArray<byte>(msgLength, Allocator.Persistent);
        streamReader.ReadBytes(buffer);
        string msg = Encoding.UTF8.GetString(buffer.ToArray());
        buffer.Dispose();

        if (stateManager.GetState() == StateManager.ServerState.WaitingForLogin)
        {
            HandleLogin(msg, connection);
        }
        else if (stateManager.GetState() == StateManager.ServerState.AccountCreation)
        {
            HandleAccountCreation(msg, connection);
        }
    }

    public void SendAccountCreationRequest(string username, string password, NetworkConnection connection)
    {
        bool isCreated = accountManager.CreateAccount(username, password);
        
        if (isCreated)
        {
            // Send success message to client
            SendMessageToClient("Account created successfully!", connection);
        }
        else
        {
            // Send error message to client
            SendMessageToClient("Username already exists.", connection);
        }
    }

    public void SendLoginRequest(string username, string password, NetworkConnection connection)
    {
        bool isValid = accountManager.ValidateLogin(username, password);
        if (isValid)
        {
            // Send success message to client
            SendMessageToClient("Login successful!", connection);
        }
        else
        {
            // Send error message to client
            SendMessageToClient("Invalid username or password.", connection);
        }
    }
}