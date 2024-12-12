using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StateManager
{
    // this is going to be responsible for tracking and updating the login
    public enum ServerState
    {
        WaitingForLogin,
        AccountCreation,
        LoggedIn
    }

    // creating the different states
    private ServerState currentState;

    public StateManager()
    {
        currentState = ServerState.WaitingForLogin;
    }

    public ServerState GetState()
    {
        return currentState;
    }

    public void SetStateAccountCreation()
    {
        currentState = ServerState.AccountCreation;
    }

    public void SetStateLoggedIn()
    {
        currentState = ServerState.LoggedIn;
    }

    public void SetStateWaitingForLogin()
    {
        currentState = ServerState.WaitingForLogin;
    }
}
