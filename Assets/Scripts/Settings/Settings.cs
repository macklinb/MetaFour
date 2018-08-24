using System.Linq;
using UnityEngine;

public struct SettingsData
{
    // Determines what network configuration to run as.
    // Valid values are "client", "host". Defaults to "host"
    // (standalone "server" has not been tested, and should not be used).
    public string networkConfig;

    // Specifies the IP address to connect to. This can only be used on a client and will have no effect if present on the server.
    // This will be set to the loopback address (127.0.0.1) if mode is singleplayer
    public string serverAddress;

    // Specifies either the port to be connected to (if client), or the port to host the game on (if host/server).
    // If the value is not present, or is "default" we will use the default SERVER_PORT
    public int serverPort;

    // Specifies whether to broadcast as an open server (if server/host), and what port to broadcast on.
    // Specifies whether to listen for broadcasting servers (if client), and what port to listen on.
    // If the value of this key is not present, or is "default" we will use the default BROADCAST_PORT.
    // If the value of the key is invalid, is is the same as not providing a value.
    // If the key is not present, we will not broadcast or listen (the resulting port will be '0')
    public int broadcastPort;

    // Specifies the type of game/number of players.
    // Valid values are "singleplayer" and "multiplayer".
    // If running in single player mode, another instance of the game will be run as a child process, using the built-in arguments "batchmode" and "nographics". The instance will be connected to the loopback address and the AI will be enabled from start. The process will be closed automatically when the main instance is closed. The headless instance's network config will be set to the opposite of the main instance. This is the same as running two multiplayer instances on the same machine, and enabling AI on one of them.
    public string gameMode;

    // Specifies whether to enable meta-related GameObjects or not. Set this to false to not try to connect to a Meta 2 device. If true, meta objects are only enabled if the device is connected.
    public bool metaEnabled;

    
    public string ExecutablePath
    {
        get { return System.Environment.GetCommandLineArgs()[0]; }
    }

    public string LogFilePath
    {
        get 
        {
            // Get from logFile command line arg
            if (System.Environment.GetCommandLineArgs().Contains("logFile"))
            {
                return System.Environment.GetCommandLineArgs().First(x => x == "logFile");
            }

            // Construct from default location
            // This should be C:\Users\user\AppData\LocalLow\North Metropolitan TAFE\MetaFour\output_log.txt
            else
            {
                string logPath = "%USERPROFILE%\\AppData\\LocalLow\\" + Application.companyName + "\\" + Application.productName + "\\output_log.txt";

                return System.Environment.ExpandEnvironmentVariables(logPath);
            }
        }
    }

    public bool UsesDirectConnect
    {
        get { return !string.IsNullOrEmpty(serverAddress) && serverPort != 0; }
    }

    public bool UsesNetworkDiscovery
    {
        get { return broadcastPort > 0; }
    }

    public override string ToString()
    {
        return string.Format(" networkConfig: {0}\n gameMode: {1}\n serverAddress: {2}\n serverPort: {3}\n broadcastPort: {4}\n metaEnabled: {5}", networkConfig, gameMode, serverAddress, serverPort, broadcastPort, metaEnabled);
    }
}

public static class Settings
{
    public static SettingsData Current { get; private set; }

    // Default Settings values (used by DefaultProvider)
    public const int DEFAULT_SERVER_PORT = 11474;
    public const int DEFAULT_BROADCAST_PORT = 11475;
    public const string DEFAULT_NETWORK_CONFIG = NETWORK_CONFIG_HOST;
    public const string DEFAULT_GAME_MODE = GAME_MODE_MULTIPLAYER;

    // Network config strings
    public const string NETWORK_CONFIG_SERVER = "server";
    public const string NETWORK_CONFIG_HOST = "host";
    public const string NETWORK_CONFIG_CLIENT = "client";

    // Game mode strings
    public const string GAME_MODE_SINGLEPLAYER = "singleplayer";
    public const string GAME_MODE_MULTIPLAYER = "multiplayer";

    static SettingsProvider[] providers = new SettingsProvider[]
    {
        new CommandLineArgsProvider(),
        new ConfigFileProvider(),
        new DefaultProvider()
    };

    // Static ctor
    static Settings()
    {
        // Try to get settings from each available provider
        foreach (var p in providers)
        {
            if (p.IsProvided())
            {
                Debug.Log("Settings : " + p.GetType().FullName + " provided");
                
                // Takes the settings retrieved, setting the Current value
                Current = p.GetSettingsData();

                Debug.Log("Settings : Current settings (using provider " + p.GetType().FullName + "):\n" + Current.ToString());
                break;
            }
            else
            {
                Debug.Log("Settings : " + p.GetType().FullName + " not provided!");
            }
        }
    }
}