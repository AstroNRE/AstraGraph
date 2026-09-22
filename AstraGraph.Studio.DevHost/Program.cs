namespace AstraGraph.Studio.DevHost;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = DevHostOptions.Parse(args);
            await using var server = await AstraStudioDevServer.StartAsync(options);
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                server.RequestShutdown();
            };
            await server.WaitForShutdownAsync();
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
