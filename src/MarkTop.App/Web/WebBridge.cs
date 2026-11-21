using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace MarkTop.App.Web;

/// <summary>Small JSON message bus over CoreWebView2's postMessage channel.</summary>
public sealed class WebBridge
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly CoreWebView2 _core;
    private readonly Dictionary<string, Func<JsonElement, Task>> _handlers = new(StringComparer.Ordinal);

    public WebBridge(CoreWebView2 core)
    {
        _core = core;
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    public bool IsReady { get; private set; }

    public event EventHandler<bool>? ReadyChanged;

    public void MarkReady()
    {
        IsReady = true;
        ReadyChanged?.Invoke(this, true);
    }

    public void On(string type, Action<JsonElement> handler) =>
        _handlers[type] = payload =>
        {
            handler(payload);
            return Task.CompletedTask;
        };

    public void On(string type, Func<JsonElement, Task> handler) => _handlers[type] = handler;

    public void Send(string type, object? payload = null)
    {
        var message = new Dictionary<string, object?> { ["type"] = type };
        if (payload is not null)
        {
            foreach (var property in payload.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = property.GetValue(payload);
                if (value is null)
                {
                    continue;
                }

                message[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = value;
            }
        }

        try
        {
            _core.PostWebMessageAsJson(JsonSerializer.Serialize(message, Options));
        }
        catch (Exception ex)
        {
            Log($"发送 {type} 失败：{ex.Message}");
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            if (type is null)
            {
                return;
            }

            if (type == "ready")
            {
                MarkReady();
            }

            if (_handlers.TryGetValue(type, out var handler))
            {
                await handler(root.Clone());
            }
        }
        catch (Exception ex)
        {
            Log($"处理消息失败：{ex}");
        }
    }

    public static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    public static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkTop", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "app.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignored
        }
    }
}
