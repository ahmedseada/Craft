// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Craft.Maui.WinUI;

/// <summary>
/// The Windows Shell's own file dialogs (<c>IFileOpenDialog</c>, <c>IFileSaveDialog</c>), through source-generated COM
/// (no runtime COM interop, trim-safe). Unlike the Windows App SDK's pickers they open on a given real folder
/// (<c>SetFolder</c>), so the folder dialog's "Select folder" works at once (the pickers' <c>DocumentsLibrary</c> opened
/// on a virtual location where it stayed disabled, the owner's Windows run), the save dialog starts in the file's own
/// folder, and they run in a process started as administrator too. Each is modal to the owner window.
/// </summary>
internal static partial class ShellFileDialog
{
    private const uint FosOverwritePrompt = 0x2;
    private const uint FosNoChangeDir = 0x8;
    private const uint FosPickFolders = 0x20;
    private const uint FosForceFileSystem = 0x40;
    private const uint FosPathMustExist = 0x800;
    private const uint FosFileMustExist = 0x1000;
    private const uint SigdnFileSysPath = 0x80058000;
    private const uint ClsctxInprocServer = 0x1;
    private const int Cancelled = unchecked((int)0x800704C7);       // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    private static readonly Guid FileOpenDialogClass = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid FileSaveDialogClass = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Asks for a folder, opening on <paramref name="start"/>; its path, or null when cancelled.</summary>
    public static string? PickFolder(nint owner, string start)
    {
        var dialog = Create<IFileOpenDialog>(FileOpenDialogClass, typeof(IFileOpenDialog).GUID);
        dialog.GetOptions(out uint options);
        dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoChangeDir);
        return Show(dialog, owner, start);
    }

    /// <summary>Asks for a file to open, opening on <paramref name="start"/>; its path, or null when cancelled.</summary>
    public static string? PickFile(nint owner, string start)
    {
        var dialog = Create<IFileOpenDialog>(FileOpenDialogClass, typeof(IFileOpenDialog).GUID);
        dialog.GetOptions(out uint options);
        dialog.SetOptions(options | FosForceFileSystem | FosFileMustExist | FosPathMustExist | FosNoChangeDir);
        return Show(dialog, owner, start);
    }

    /// <summary>Asks where to save <paramref name="name"/>, opening on <paramref name="start"/>; the path, or null when cancelled.</summary>
    public static string? PickSave(nint owner, string start, string name)
    {
        var dialog = Create<IFileSaveDialog>(FileSaveDialogClass, typeof(IFileSaveDialog).GUID);
        dialog.GetOptions(out uint options);
        dialog.SetOptions(options | FosForceFileSystem | FosPathMustExist | FosOverwritePrompt | FosNoChangeDir);
        dialog.SetFileName(name);
        if (Path.GetExtension(name) is { Length: > 1 } extension)
        {
            dialog.SetDefaultExtension(extension[1..]);
        }

        return Show(dialog, owner, start);
    }

    private static string? Show(IFileDialog dialog, nint owner, string start)
    {
        if (Item(start) is { } folder)
        {
            dialog.SetFolder(folder);
        }

        int result = dialog.Show(owner);
        if (result == Cancelled)
        {
            return null;
        }

        Marshal.ThrowExceptionForHR(result);
        dialog.GetResult(out var item);
        item.GetDisplayName(SigdnFileSysPath, out nint name);
        try
        {
            return Marshal.PtrToStringUni(name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    // The folder as a Shell item, or null when it does not resolve (the dialog then opens where it last was).
    private static IShellItem? Item(string folder)
    {
        var shellItem = typeof(IShellItem).GUID;
        if (SHCreateItemFromParsingName(folder, 0, in shellItem, out nint pointer) < 0 || pointer == 0)
        {
            return null;
        }

        try
        {
            return (IShellItem)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static T Create<T>(Guid classId, Guid interfaceId)
        where T : class
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(in classId, 0, ClsctxInprocServer, in interfaceId, out nint pointer));
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid interfaceId, out nint item);
}

[GeneratedComInterface]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem
{
    void BindToHandler(nint bindContext, in Guid handler, in Guid interfaceId, out nint result);

    void GetParent(out IShellItem parent);

    void GetDisplayName(uint form, out nint name);

    void GetAttributes(uint mask, out uint attributes);

    void Compare(IShellItem other, uint hint, out int order);
}

[GeneratedComInterface]
[Guid("B4DB1657-70D7-485E-8E3E-6FCB5A5C1802")]
internal partial interface IModalWindow
{
    [PreserveSig]
    int Show(nint owner);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
internal partial interface IFileDialog : IModalWindow
{
    void SetFileTypes(uint count, nint filterSpecs);

    void SetFileTypeIndex(uint index);

    void GetFileTypeIndex(out uint index);

    void Advise(nint events, out uint cookie);

    void Unadvise(uint cookie);

    void SetOptions(uint options);

    void GetOptions(out uint options);

    void SetDefaultFolder(IShellItem folder);

    void SetFolder(IShellItem folder);

    void GetFolder(out IShellItem folder);

    void GetCurrentSelection(out IShellItem item);

    void SetFileName(string name);

    void GetFileName(out nint name);

    void SetTitle(string title);

    void SetOkButtonLabel(string label);

    void SetFileNameLabel(string label);

    void GetResult(out IShellItem item);

    void AddPlace(IShellItem item, int place);

    void SetDefaultExtension(string extension);

    void Close(int result);

    void SetClientGuid(in Guid guid);

    void ClearClientData();

    void SetFilter(nint filter);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
internal partial interface IFileOpenDialog : IFileDialog
{
    void GetResults(out nint items);

    void GetSelectedItems(out nint items);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB")]
internal partial interface IFileSaveDialog : IFileDialog
{
    void SetSaveAsItem(IShellItem item);

    void SetProperties(nint store);

    void SetCollectedProperties(nint list, int appendDefault);

    void GetProperties(out nint store);

    void ApplyProperties(IShellItem item, nint store, nint owner, nint sink);
}
#endif
