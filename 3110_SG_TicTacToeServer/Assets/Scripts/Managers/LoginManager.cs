using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Networking.Transport;

public class LoginManager : MonoBehaviour
{
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;
    public Button loginButton;
    public Button createAccountButton;
    public TMP_Text feedbackText;

    private NetworkServer networkServer;
    private NetworkServer connection;

    public void Start()
    {
        networkServer = FindObjectOfType<NetworkServer>();

        loginButton.onClick.AddListener(HandleLogin);
        createAccountButton.onClick.AddListener(HandleAccountCreation);
    }

    public void HandleAccountCreation()
    {
        string username = usernameInput.text;
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(username))
        {
            feedbackText.text = "Please enter a username.";
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            feedbackText.text = "Please enter a password.";
            return;
        }

        NetworkConnection connection = networkServer.GetCurrentConnection();

        if (connection == default(NetworkConnection))
        {
            feedbackText.text = "No active connection found.";
            return;
        }

        // Send account creation request to the server
        networkServer.SendAccountCreationRequest(username, password, connection);
    }


    public void HandleLogin()
    {
        string username = usernameInput.text;
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(username))
        {
            feedbackText.text = "Please enter a username.";
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            feedbackText.text = "Please enter a password.";
            return;
        }

        NetworkConnection connection = networkServer.GetCurrentConnection();

        if (connection == default(NetworkConnection))
        {
            feedbackText.text = "No active connection found.";
            return;
        }

        // Send login request to the server
        networkServer.SendLoginRequest(username, password, connection);
    }

}
