using System.ComponentModel;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Tools;

namespace DotCraft.QQ;

public sealed class QQTools(QQBotClient client, IAgentFileSystem? fileSystem = null)
{
    private readonly IAgentFileSystem? _fileSystem = fileSystem;

    private static bool IsRemoteOrInline(string file)
        => file.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || file.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || file.StartsWith("base64://", StringComparison.OrdinalIgnoreCase);

    [Description("Send a voice/audio message to a QQ group chat. The file can be a local absolute path, an HTTP URL, or a base64-encoded string (prefix with 'base64://'). Supported formats depend on the OneBot implementation (typically mp3, amr, silk).")]
    [Tool(Icon = "🎤", DisplayType = typeof(QQToolDisplays), DisplayMethod = nameof(QQToolDisplays.QQSendGroupVoice))]
    public async Task<string> QQSendGroupVoice(
        [Description("The QQ group number to send the voice message to.")] long groupId,
        [Description("Voice file: local absolute path, HTTP URL, or base64-encoded data (prefix with 'base64://').")] string file)
    {
        try
        {
            file = await ResolveVoiceFileAsync(file);
            var resp = await client.SendGroupRecordAsync(groupId, file);
            return resp.IsOk
                ? JsonSerializer.Serialize(new { success = true, message = "Voice message sent." })
                : JsonSerializer.Serialize(new { success = false, error = resp.Message });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [Description("Send a voice/audio message to a QQ private chat. The file can be a local absolute path, an HTTP URL, or a base64-encoded string (prefix with 'base64://').")]
    [Tool(Icon = "🎤", DisplayType = typeof(QQToolDisplays), DisplayMethod = nameof(QQToolDisplays.QQSendPrivateVoice))]
    public async Task<string> QQSendPrivateVoice(
        [Description("The QQ user ID to send the voice message to.")] long userId,
        [Description("Voice file: local absolute path, HTTP URL, or base64-encoded data (prefix with 'base64://').")] string file)
    {
        try
        {
            file = await ResolveVoiceFileAsync(file);
            var resp = await client.SendPrivateRecordAsync(userId, file);
            return resp.IsOk
                ? JsonSerializer.Serialize(new { success = true, message = "Voice message sent." })
                : JsonSerializer.Serialize(new { success = false, error = resp.Message });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [Description("Send a video message to a QQ group chat. The file can be a local absolute path or an HTTP URL.")]
    [Tool(Icon = "🎬", DisplayType = typeof(QQToolDisplays), DisplayMethod = nameof(QQToolDisplays.QQSendGroupVideo))]
    public async Task<string> QQSendGroupVideo(
        [Description("The QQ group number to send the video to.")] long groupId,
        [Description("Video file: local absolute path or HTTP URL.")] string file)
    {
        try
        {
            var resolvedFile = await ResolveVideoFileAsync(file);
            using var handle = resolvedFile;
            var resp = await client.SendGroupVideoAsync(groupId, handle?.HostPath ?? file);
            return resp.IsOk
                ? JsonSerializer.Serialize(new { success = true, message = "Video sent." })
                : JsonSerializer.Serialize(new { success = false, error = resp.Message });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [Description("Send a video message to a QQ private chat. The file can be a local absolute path or an HTTP URL.")]
    [Tool(Icon = "🎬", DisplayType = typeof(QQToolDisplays), DisplayMethod = nameof(QQToolDisplays.QQSendPrivateVideo))]
    public async Task<string> QQSendPrivateVideo(
        [Description("The QQ user ID to send the video to.")] long userId,
        [Description("Video file: local absolute path or HTTP URL.")] string file)
    {
        try
        {
            var resolvedFile = await ResolveVideoFileAsync(file);
            using var handle = resolvedFile;
            var resp = await client.SendPrivateVideoAsync(userId, handle?.HostPath ?? file);
            return resp.IsOk
                ? JsonSerializer.Serialize(new { success = true, message = "Video sent." })
                : JsonSerializer.Serialize(new { success = false, error = resp.Message });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [Description("Upload a file to a QQ group. This uses the extended OneBot API (upload_group_file). The file must be a local absolute path on the server.")]
    [Tool(Icon = "📁", DisplayType = typeof(QQToolDisplays), DisplayMethod = nameof(QQToolDisplays.QQUploadGroupFile))]
    public async Task<string> QQUploadGroupFile(
        [Description("The QQ group number to upload the file to.")] long groupId,
        [Description("Local absolute path of the file to upload.")] string filePath,
        [Description("Display name for the file in the group.")] string fileName,
        [Description("Optional: target folder ID in the group file system.")] string? folder = null)
    {
        try
        {
            using var handle = _fileSystem != null
                ? await _fileSystem.ResolveHostFileAsync(filePath)
                : new HostFileHandle(filePath);
            var resp = await client.UploadGroupFileAsync(groupId, handle.HostPath, fileName, folder);
            return resp.IsOk
                ? JsonSerializer.Serialize(new { success = true, message = "File uploaded to group." })
                : JsonSerializer.Serialize(new { success = false, error = resp.Message });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [Description("Upload a file to a QQ private chat. This uses the extended OneBot API (upload_private_file). The file must be a local absolute path on the server.")]
    [Tool(Icon = "📁", DisplayType = typeof(QQToolDisplays), DisplayMethod = nameof(QQToolDisplays.QQUploadPrivateFile))]
    public async Task<string> QQUploadPrivateFile(
        [Description("The QQ user ID to send the file to.")] long userId,
        [Description("Local absolute path of the file to upload.")] string filePath,
        [Description("Display name for the file.")] string fileName)
    {
        try
        {
            using var handle = _fileSystem != null
                ? await _fileSystem.ResolveHostFileAsync(filePath)
                : new HostFileHandle(filePath);
            var resp = await client.UploadPrivateFileAsync(userId, handle.HostPath, fileName);
            return resp.IsOk
                ? JsonSerializer.Serialize(new { success = true, message = "File sent to user." })
                : JsonSerializer.Serialize(new { success = false, error = resp.Message });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// For voice messages, OneBot supports base64:// inline data,
    /// so we can avoid temp file extraction by reading the file as base64.
    /// </summary>
    private async Task<string> ResolveVoiceFileAsync(string file)
    {
        if (IsRemoteOrInline(file) || _fileSystem == null)
            return file;

        var b64 = await _fileSystem.ReadAsBase64Async(file);
        return "base64://" + b64;
    }

    /// <returns>A <see cref="HostFileHandle"/> for local paths, or null for remote URLs.</returns>
    private async Task<HostFileHandle?> ResolveVideoFileAsync(string file)
    {
        if (IsRemoteOrInline(file))
            return null;

        return _fileSystem != null
            ? await _fileSystem.ResolveHostFileAsync(file)
            : new HostFileHandle(file);
    }
}
