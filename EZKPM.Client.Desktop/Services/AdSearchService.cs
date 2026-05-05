using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Services;

public class AdPrincipal
{
    public string DisplayName { get; set; } = "";
    public string Sid { get; set; } = "";
    public string Type { get; set; } = "User"; // "User" or "Group"
    
    public override string ToString() => $"{DisplayName} ({Sid})";
}

public static class AdSearchService
{
    public static AdPrincipal GetCurrentUser()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new AdPrincipal 
            { 
                DisplayName = identity.Name, 
                Sid = identity.User?.Value ?? Environment.UserDomainName + "\\" + Environment.UserName, 
                Type = "User" 
            };
        }
        catch
        {
            return new AdPrincipal 
            { 
                DisplayName = Environment.UserName, 
                Sid = Environment.UserDomainName + "\\" + Environment.UserName, 
                Type = "User" 
            };
        }
    }

    public static async Task<List<AdPrincipal>> SearchAsync(string query)
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
                var users = userSearcher.FindAll().Take(15);
                foreach (var p in users)
                {
                    results.Add(new AdPrincipal { DisplayName = p.DisplayName ?? p.Name, Sid = p.Sid?.Value ?? "", Type = "User" });
                }

                // 2. Search Groups (Limit to 15)
                using var groupPrincipal = new GroupPrincipal(context) { Name = $"*{query}*" };
                using var groupSearcher = new PrincipalSearcher(groupPrincipal);
                var groups = groupSearcher.FindAll().Take(15);
                foreach (var p in groups)
                {
                    results.Add(new AdPrincipal { DisplayName = p.DisplayName ?? p.Name, Sid = p.Sid?.Value ?? "", Type = "Group" });
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
                    var users = userSearcher.FindAll().Take(15);
                    foreach (var p in users)
                    {
                        results.Add(new AdPrincipal { DisplayName = p.DisplayName ?? p.Name, Sid = p.Sid?.Value ?? "", Type = "User" });
                    }
                    
                    using var groupPrincipal = new GroupPrincipal(localContext) { Name = $"*{query}*" };
                    using var groupSearcher = new PrincipalSearcher(groupPrincipal);
                    var groups = groupSearcher.FindAll().Take(15);
                    foreach (var p in groups)
                    {
                        results.Add(new AdPrincipal { DisplayName = p.DisplayName ?? p.Name, Sid = p.Sid?.Value ?? "", Type = "Group" });
                    }
                }
                catch { /* Ignore errors */ }
            }

            return results.OrderBy(r => r.DisplayName).ToList();
        });
    }
}
