using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Services;

public enum AdPickerFilterMode
{
    All,
    ActiveOnly,
    DisabledOnly
}

public class AdPrincipal
{
    public string DisplayName { get; set; } = "";
    public string SamAccountName { get; set; } = "";
    public string Sid { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public string Type { get; set; } = "User"; // "User" or "Group"
    public bool IsAccountDisabled { get; set; } = false;
    
    public override string ToString() => $"{DisplayName} ({SamAccountName})";
}

public static class AdSearchService
{
    public static AdPrincipal GetCurrentUser()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            string displayName = identity.Name;
            string samAccountName = identity.Name.Split('\\').LastOrDefault() ?? identity.Name;
            string email = "";
            
            try 
            {
                if (OperatingSystem.IsWindows())
                {
                    // Fast check if the machine is actually joined to a domain
                    bool isDomainJoined = false;
                    try 
                    {
                        using (var winContext = new PrincipalContext(ContextType.Machine)) 
                        {
                            isDomainJoined = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName != string.Empty;
                        }
                    } 
                    catch { }

                    if (isDomainJoined)
                    {
                        using var context = new PrincipalContext(ContextType.Domain);
                        using var user = UserPrincipal.FindByIdentity(context, identity.Name);
                        if (user != null && !string.IsNullOrWhiteSpace(user.DisplayName))
                        {
                            displayName = user.DisplayName;
                            samAccountName = user.SamAccountName;
                            email = user.EmailAddress ?? "";
                        }
                    }
                }
            }
            catch { }

            return new AdPrincipal 
            { 
                DisplayName = displayName, 
                SamAccountName = samAccountName,
                Sid = identity.User?.Value ?? Environment.UserDomainName + "\\" + Environment.UserName, 
                EmailAddress = email,
                Type = "User" 
            };
        }
        catch
        {
            return new AdPrincipal 
            { 
                DisplayName = Environment.UserName, 
                SamAccountName = Environment.UserName,
                Sid = Environment.UserDomainName + "\\" + Environment.UserName, 
                Type = "User" 
            };
        }
    }

    public static async Task<List<AdPrincipal>> SearchAsync(string query, AdPickerFilterMode filterMode = AdPickerFilterMode.All)
    {
        return await Task.Run(() =>
        {
            var results = new List<AdPrincipal>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            try
            {
                // Attempt Domain Context First
                using var context = new PrincipalContext(ContextType.Domain);

                // 1. Search Users (Limit to 15)
                using var userPrincipal = new UserPrincipal(context) { Name = $"*{query}*" };
                using var userSearcher = new PrincipalSearcher(userPrincipal);
                var users = userSearcher.FindAll().OfType<UserPrincipal>();
                
                foreach (var u in users)
                {
                    bool isEnabled = u.Enabled ?? true;
                    if (filterMode == AdPickerFilterMode.ActiveOnly && !isEnabled) continue;
                    if (filterMode == AdPickerFilterMode.DisabledOnly && isEnabled) continue;

                    results.Add(new AdPrincipal 
                    { 
                        DisplayName = u.DisplayName ?? u.Name, 
                        SamAccountName = u.SamAccountName,
                        Sid = u.Sid?.Value ?? "", 
                        EmailAddress = u.EmailAddress ?? "",
                        Type = "User",
                        IsAccountDisabled = !isEnabled
                    });
                    if (results.Count >= 15) break;
                }

                // 2. Search Groups (Limit to 15)
                if (filterMode != AdPickerFilterMode.DisabledOnly)
                {
                    using var groupPrincipal = new GroupPrincipal(context) { Name = $"*{query}*" };
                    using var groupSearcher = new PrincipalSearcher(groupPrincipal);
                    var groups = groupSearcher.FindAll().OfType<GroupPrincipal>().Take(15);
                    foreach (var p in groups)
                    {
                        results.Add(new AdPrincipal 
                        { 
                            DisplayName = p.DisplayName ?? p.Name, 
                            SamAccountName = p.SamAccountName,
                            Sid = p.Sid?.Value ?? "", 
                            Type = "Group" 
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Fallback: Local Machine context if domain is unreachable or user is not in a domain
                try
                {
                    using var localContext = new PrincipalContext(ContextType.Machine);
                    using var userPrincipal = new UserPrincipal(localContext) { Name = $"*{query}*" };
                    using var userSearcher = new PrincipalSearcher(userPrincipal);
                    var users = userSearcher.FindAll().OfType<UserPrincipal>();
                    foreach (var u in users)
                    {
                        bool isEnabled = u.Enabled ?? true;
                        if (filterMode == AdPickerFilterMode.ActiveOnly && !isEnabled) continue;
                        if (filterMode == AdPickerFilterMode.DisabledOnly && isEnabled) continue;

                        results.Add(new AdPrincipal 
                        { 
                            DisplayName = u.DisplayName ?? u.Name, 
                            SamAccountName = u.SamAccountName,
                            Sid = u.Sid?.Value ?? "", 
                            Type = "User",
                            IsAccountDisabled = !isEnabled
                        });
                        if (results.Count >= 15) break;
                    }
                    
                    if (filterMode != AdPickerFilterMode.DisabledOnly)
                    {
                        using var groupPrincipal = new GroupPrincipal(localContext) { Name = $"*{query}*" };
                        using var groupSearcher = new PrincipalSearcher(groupPrincipal);
                        var groups = groupSearcher.FindAll().Take(15);
                        foreach (var p in groups)
                        {
                            results.Add(new AdPrincipal { DisplayName = p.DisplayName ?? p.Name, SamAccountName = p.Name, Sid = p.Sid?.Value ?? "", Type = "Group" });
                        }
                    }
                }
                catch { /* Ignore errors */ }
            }

            return results.OrderBy(r => r.DisplayName).ToList();
        });
    }

    public static async Task<List<AdPrincipal>> GetAllAdUsersAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<AdPrincipal>();
            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                using var userPrincipal = new UserPrincipal(context) { Name = "*" };
                using var userSearcher = new PrincipalSearcher(userPrincipal);
                
                // Wir limitieren hier bewusst NICHT, da dies für den kompletten AD-Abgleich im Admin-Dashboard gedacht ist
                var users = userSearcher.FindAll().OfType<UserPrincipal>();
                
                foreach (var u in users)
                {
                    bool isEnabled = u.Enabled ?? true;
                    results.Add(new AdPrincipal 
                    { 
                        DisplayName = u.DisplayName ?? u.Name, 
                        SamAccountName = u.SamAccountName,
                        Sid = u.Sid?.Value ?? "", 
                        EmailAddress = u.EmailAddress ?? "",
                        Type = "User",
                        IsAccountDisabled = !isEnabled
                    });
                }
            }
            catch (Exception)
            {
                // Fallback: Local Machine
                try
                {
                    using var localContext = new PrincipalContext(ContextType.Machine);
                    using var userPrincipal = new UserPrincipal(localContext) { Name = "*" };
                    using var userSearcher = new PrincipalSearcher(userPrincipal);
                    var users = userSearcher.FindAll().OfType<UserPrincipal>();
                    foreach (var u in users)
                    {
                        bool isEnabled = u.Enabled ?? true;
                        results.Add(new AdPrincipal 
                        { 
                            DisplayName = u.DisplayName ?? u.Name, 
                            SamAccountName = u.SamAccountName,
                            Sid = u.Sid?.Value ?? "", 
                            Type = "User",
                            IsAccountDisabled = !isEnabled
                        });
                    }
                }
                catch { }
            }

            return results.OrderBy(r => r.DisplayName).ToList();
        });
    }

    public static async Task<List<AdPrincipal>> GetGroupMembersAsync(string groupSid)
    {
        return await Task.Run(() =>
        {
            var results = new List<AdPrincipal>();
            if (string.IsNullOrWhiteSpace(groupSid)) return results;

            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
                
                if (group != null)
                {
                    // GetMembers(true) recursively resolves nested groups!
                    foreach (var p in group.GetMembers(true))
                    {
                        if (p is UserPrincipal user)
                        {
                            results.Add(new AdPrincipal 
                            { 
                                DisplayName = user.DisplayName ?? user.Name, 
                                Sid = user.Sid?.Value ?? "", 
                                Type = "User" 
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback local machine
                try
                {
                    using var localContext = new PrincipalContext(ContextType.Machine);
                    var group = GroupPrincipal.FindByIdentity(localContext, IdentityType.Sid, groupSid);
                    if (group != null)
                    {
                        foreach (var p in group.GetMembers(true))
                        {
                            if (p is UserPrincipal user)
                            {
                                results.Add(new AdPrincipal 
                                { 
                                    DisplayName = user.DisplayName ?? user.Name, 
                                    Sid = user.Sid?.Value ?? "", 
                                    Type = "User" 
                                });
                            }
                        }
                    }
                }
                catch { /* Ignore errors */ }
            }

            return results;
        });
    }
}
