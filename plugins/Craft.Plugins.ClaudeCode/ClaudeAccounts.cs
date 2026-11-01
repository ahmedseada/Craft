// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Plugins.ClaudeCode;

/// <summary>
/// A Claude Code account as Craft keeps it: its name and its own Claude Code configuration folder (the CLI's
/// <c>CLAUDE_CONFIG_DIR</c>, where its own login keeps its sign-in), or none for "default", which is the CLI's own default
/// configuration (an already logged-in CLI works with no setup).
/// </summary>
/// <param name="Name">Its name ("default", "work").</param>
/// <param name="Folder">Its configuration folder, or null for "default".</param>
public sealed record ClaudeAccount(string Name, string? Folder)
{
    /// <summary>The model name the agent's chip shows for it ("Claude Code (work)").</summary>
    public string Model => ClaudeAccounts.ModelOf(Name);

    /// <summary>The variables a run sets for it: its <c>CLAUDE_CONFIG_DIR</c>; none for "default".</summary>
    public IReadOnlyDictionary<string, string> Environment =>
        Folder is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [ClaudeAccounts.ConfigVariable] = Folder };

    /// <summary>
    /// Whether it is logged in, as far as Craft can tell without reading a secret: its folder holds the CLI's credentials
    /// file (true) or not (false); null when Craft cannot tell ("default", whose configuration is the CLI's own; macOS,
    /// where the CLI keeps its sign-in in the keychain). Only the file's presence is checked: nothing in it is read.
    /// </summary>
    public bool? IsLoggedIn
    {
        get
        {
            if (Folder is null || OperatingSystem.IsMacOS())
            {
                return null;
            }

            return File.Exists(System.IO.Path.Combine(Folder, ClaudeAccounts.CredentialsFile));
        }
    }
}

/// <summary>
/// The accounts, Craft's own: "default" (the CLI's own configuration), then one folder per account under the user's data
/// folder (<c>&lt;data&gt;/claude-code/accounts/&lt;name&gt;</c>, never the workspace or git), each its own
/// <c>CLAUDE_CONFIG_DIR</c>. An account is added with a name, then logged in by the CLI's own login with that folder;
/// Craft never sees, stores or logs a password or a token. Nothing on the machine is searched for accounts.
/// </summary>
public static class ClaudeAccounts
{
    /// <summary>The account that uses the CLI's own default configuration.</summary>
    public const string DefaultName = "default";

    /// <summary>The variable that points Claude Code at an account's own configuration (its sign-in).</summary>
    public const string ConfigVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>The file the CLI keeps its sign-in in, in a configuration folder (its presence only is checked).</summary>
    public const string CredentialsFile = ".credentials.json";

    /// <summary>The default account's model at the chip.</summary>
    public static string DefaultModel => ModelOf(DefaultName);

    /// <summary>The default account.</summary>
    public static ClaudeAccount Default { get; } = new(DefaultName, null);

    /// <summary>The model name of account <paramref name="name"/> ("Claude Code (work)").</summary>
    public static string ModelOf(string name) => $"Claude Code ({name})";

    /// <summary>The account a model's name names ("Claude Code (work)" gives "work"), or null for another model.</summary>
    public static string? AccountOf(string? model)
    {
        if (model is null)
        {
            return null;
        }

        var text = model.Trim();
        return text.StartsWith("Claude Code (", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')') ? text["Claude Code (".Length..^1].Trim() : null;
    }

    /// <summary>The folder of the accounts under the user's data folder <paramref name="dataFolder"/>.</summary>
    public static string Root(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        return System.IO.Path.Combine(System.IO.Path.GetFullPath(dataFolder), "claude-code", "accounts");
    }

    /// <summary>The accounts: "default", then each folder under <see cref="Root"/>, by name (off the UI thread: it lists a folder).</summary>
    public static IReadOnlyList<ClaudeAccount> List(string dataFolder)
    {
        var accounts = new List<ClaudeAccount> { Default };
        string root = Root(dataFolder);
        try
        {
            if (Directory.Exists(root))
            {
                foreach (var folder in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
                {
                    string name = System.IO.Path.GetFileName(folder);
                    if (Problem(name) is null)
                    {
                        accounts.Add(new ClaudeAccount(name, folder));
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder lists "default" alone.
        }

        return accounts;
    }

    /// <summary>Why <paramref name="name"/> cannot name an account (empty, "default", too long, a character a folder name should not have), or null.</summary>
    public static string? Problem(string? name)
    {
        var text = name?.Trim() ?? "";
        if (text.Length == 0)
        {
            return "An account needs a name (\"work\", \"personal\").";
        }

        if (text.Equals(DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return "\"default\" is the CLI's own configuration: give the account another name.";
        }

        if (text.Length > 40 || !text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return "An account's name takes letters, digits, '-' and '_' (at most 40).";
        }

        return null;
    }

    /// <summary>
    /// Makes account <paramref name="name"/>: its configuration folder under the data folder, private to the user; refused
    /// inside <paramref name="workspace"/> (never the project, never git). Off the UI thread.
    /// </summary>
    /// <exception cref="ArgumentException">The name cannot name an account.</exception>
    /// <exception cref="InvalidOperationException">The data folder is inside the workspace, or the account exists.</exception>
    public static ClaudeAccount Add(string dataFolder, string? workspace, string name)
    {
        if (Problem(name) is { } problem)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        string folder = System.IO.Path.Combine(Root(dataFolder), name.Trim());
        if (workspace is { Length: > 0 } && McpConfig.IsInside(folder, workspace))
        {
            throw new InvalidOperationException($"Craft's data folder ({dataFolder}) is inside the open folder: an account's sign-in is never kept in a project. Set CRAFT_DATA_FOLDER elsewhere.");
        }

        if (Directory.Exists(folder))
        {
            throw new InvalidOperationException($"The account {name.Trim()} exists: use Claude Code: log in again.");
        }

        Directory.CreateDirectory(folder);
        PrivateFiles.RestrictFolder(folder);
        return new ClaudeAccount(name.Trim(), folder);
    }

    /// <summary>Deletes account <paramref name="account"/>'s folder (its sign-in with it); "default" is never removed. Off the UI thread.</summary>
    /// <exception cref="InvalidOperationException">It is "default".</exception>
    public static void Remove(ClaudeAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Folder is null)
        {
            throw new InvalidOperationException("\"default\" is the CLI's own configuration: Craft does not remove it.");
        }

        if (Directory.Exists(account.Folder))
        {
            Directory.Delete(account.Folder, recursive: true);
        }
    }
}

/// <summary>Files and folders only the user may read (the MCP config's token, an account's sign-in): Unix modes, or a Windows ACL of the user alone.</summary>
public static class PrivateFiles
{
    /// <summary>Makes <paramref name="path"/> (a file) readable and writable by the user only.</summary>
    public static void RestrictFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Windows(path, folder: false);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>Makes <paramref name="path"/> (a folder) usable by the user only (what is made in it inherits that on Windows).</summary>
    public static void RestrictFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Windows(path, folder: true);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Windows(string path, bool folder)
    {
        if (System.Security.Principal.WindowsIdentity.GetCurrent().User is not { } user)
        {
            return;
        }

        if (folder)
        {
            var security = new System.Security.AccessControl.DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        else
        {
            var security = new System.Security.AccessControl.FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
    }
}
