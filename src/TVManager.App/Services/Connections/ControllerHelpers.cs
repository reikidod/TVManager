using TVManager.Models;

namespace TVManager.Services.Connections;

internal static class ControllerHelpers
{
    public static ChangeResult Unsupported(string operation, string platform) =>
        new(false, $"{operation} через официальные инструменты {platform} в этой версии недоступно.");

    public static (bool Success, string Message) RequireConnection(TvConnection connection)
    {
        if (connection.State == ConnectionState.Connected)
            return (true, string.Empty);

        return (false, "Сначала подключитесь к устройству.");
    }
}
