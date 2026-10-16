using MailClient.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.Security.Credentials;

namespace MailClient.App.Helpers;

public class AccountService
{
    private readonly string _accountsPath;
    private const string CredentialResource = "MailClientApp";

    public AccountService(string dataDir)
    {
        _accountsPath = Path.Combine(dataDir, "accounts.json");
    }

    public List<MailAccount> LoadAccounts()
    {
        if (!File.Exists(_accountsPath)) return [];
        var json = File.ReadAllText(_accountsPath);
        return JsonSerializer.Deserialize<List<MailAccount>>(json) ?? [];
    }

    public void SaveAccounts(List<MailAccount> accounts)
    {
        var json = JsonSerializer.Serialize(accounts,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_accountsPath, json);
    }

    public void SavePassword(Guid accountId, string password)
    {
        var vault = new PasswordVault();
        try
        {
            vault.Remove(vault.Retrieve(CredentialResource, accountId.ToString()));
        }
        catch { /* not yet stored */ }

        vault.Add(new PasswordCredential(
            CredentialResource, accountId.ToString(), password));
    }

    public string? LoadPassword(Guid accountId)
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(CredentialResource, accountId.ToString());
            cred.RetrievePassword();
            return cred.Password;
        }
        catch { return null; }
    }

    public void DeletePassword(Guid accountId)
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(CredentialResource, accountId.ToString()));
        }
        catch { }
    }

    public void DeleteAccount(MailAccount account)
    {
        DeletePassword(account.Id);
        var accounts = LoadAccounts();
        accounts.RemoveAll(a => a.Id == account.Id);
        SaveAccounts(accounts);
    }
}
