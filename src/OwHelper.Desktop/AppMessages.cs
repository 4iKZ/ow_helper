namespace OwHelper.Desktop;

internal static class AppMessages
{
    public static string Latest { get; private set; } = "";

    public static void Write(string message) => Latest = message.Trim();
}
