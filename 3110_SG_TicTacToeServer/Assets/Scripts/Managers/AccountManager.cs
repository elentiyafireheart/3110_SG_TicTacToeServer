using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Networking.Transport;
using UnityEngine;

public class AccountManager : MonoBehaviour
{
    private Dictionary<NetworkConnection, int> connectionToPlayerId;
    List<SaveFiles> playerSaves;
    private string filePath;

    public void Start()
    {
        playerSaves= new List<SaveFiles>();
        filePath = Application.dataPath + Path.DirectorySeparatorChar + "savedAccountData.txt";
        if (File.Exists(filePath))
        {
            Debug.Log("File Found");
        }

        LoadAccountsFromFile();

        connectionToPlayerId = new Dictionary<NetworkConnection, int>();
    }


    //stores account info / checks if it exists
    public bool CreateAccount(string username, string password)
    {
        if (playerSaves.Exists(account => account.username == username))
        {
            return false; // account exists
        }

        SaveFiles newAccount = new SaveFiles(username, password);
        playerSaves.Add(newAccount);
        SaveAccountsToFile();
        return true;
    }

    // validates login
    public bool ValidateLogin(string username, string password)
    {
        SaveFiles account = playerSaves.Find(acc => acc.username == username);
        if (account != null && account.password == password)
        {
            return true; // successful login
        }

        return false; // invalid
    }
    private void SaveAccountsToFile()
    {
        try
        {
            using (StreamWriter streamWriter = new StreamWriter(filePath))
            {
                foreach (var account in playerSaves)
                {
                    streamWriter.WriteLine(account.username + ";" + account.password);
                }
            }
        }
        catch (IOException ex)
        {
            Debug.LogError("Failed to save accounts to file: " + ex.Message);
        }
    }

    private void LoadAccountsFromFile()
    {
        try
        {
            if (File.Exists(filePath))
            {
                string[] lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    string[] accountData = line.Split(';');
                    if (accountData.Length == 2)
                    {
                        playerSaves.Add(new SaveFiles(accountData[0], accountData[1]));
                    }
                }
            }
        }
        catch (IOException ex)
        {
            Debug.LogError("Failed to load accounts from file: " + ex.Message);
        }
    }
}
public class SaveFiles
{
    public string username;
    public string password;

    public SaveFiles(string username, string password)
    {
        this.username = username;
        this.password = password;
    }
}
