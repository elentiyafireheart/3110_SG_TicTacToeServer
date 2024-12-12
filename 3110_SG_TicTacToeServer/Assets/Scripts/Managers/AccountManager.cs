using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public class AccountManager : MonoBehaviour
{
    private Dictionary<string, string> accounts = new Dictionary<string, string>();
    private const string filePath = "SavedData/TicTacSaved.txt";

    public void Start()
    {
        LoadAccountsFromFile();
    }


    //stores account info / checks if it exists
    public bool CreateAccount(string username, string password)
    {
        if (accounts.ContainsKey(username))
        {
            return false; // account exists
        }

        accounts[username] = password;
        SaveAccountsToFile();
        return true;
    }

    // validates login
    public bool ValidateLogin(string username, string password)
    {
        if (accounts.ContainsKey(username) && accounts[username] == password)
        {
            return true; // successful login
        }

        return false; // invalid
    }
    private void SaveAccountsToFile()
    {
        using (StreamWriter streamWriter = new StreamWriter(filePath))
        {
            foreach (var login in accounts)
            {
                streamWriter.WriteLine(login.Key + ";" + login.Value);
            }
        }
    }
    private void LoadAccountsFromFile()
    {
        if (File.Exists(filePath))
        {
            string[] lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                string[] accountData = line.Split(';');
                if (accountData.Length == 2)
                {
                    accounts[accountData[0]] = accountData[1];
                }
            }
        }
    }
}
