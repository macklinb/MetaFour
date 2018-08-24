using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public abstract class SettingsProvider
{
    // Available argument keys:

    protected const string ARG_NETWORK_CONFIG = "config";
    protected const string ARG_SERVER_ADDRESS = "address";
    protected const string ARG_SERVER_PORT = "port";
    protected const string ARG_BROADCAST_PORT = "broadcast_port";
    protected const string ARG_GAME_MODE = "mode";
    protected const string ARG_META_ENABLED = "meta_enabled";

    // Should return true if this provider can be used, and has data
    public abstract bool IsProvided();

    // Should return a NetworkSettings struct, filled with network settings via whatever acquisition method was used
    public abstract SettingsData GetSettingsData();

    // Should be used by the SettingsProvider derivative to translate key-value pairs into a SettingsData struct
    protected SettingsData Parse(Dictionary<string, string> data)
    {
        Debug.Log(this.GetType().Name + " : Parsing the following key-value pairs:\n" + string.Join("\n", data.Select(d => d.Key + ": " + d.Value).ToArray()));

        var settings = new SettingsData();

        // ARG_NETWORK_CONFIG
        if (data.ContainsKey(ARG_NETWORK_CONFIG))
        {
            var key = ARG_NETWORK_CONFIG;
            var value = data[ARG_NETWORK_CONFIG];

            // Ensure that the value is present
            if (string.IsNullOrEmpty(value))
                Debug.LogError(this.GetType().Name + " : Key \"" + key + "\" requires a value");

            // Ensure that the value is valid
            else if (value != Settings.NETWORK_CONFIG_CLIENT && value != Settings.NETWORK_CONFIG_HOST && value != Settings.NETWORK_CONFIG_SERVER)
                Debug.LogError(this.GetType().Name + " : Value \"" + value + "\" for key \"" + key + "\" is invalid!");
            
            else
                settings.networkConfig = value;
        }

        // Default networkConfig
        if (string.IsNullOrEmpty(settings.networkConfig))
            settings.networkConfig = Settings.DEFAULT_NETWORK_CONFIG;

        // ARG_SERVER_ADDRESS
        if (data.ContainsKey(ARG_SERVER_ADDRESS))
        {
            var key = ARG_SERVER_ADDRESS;
            var value = data[ARG_SERVER_ADDRESS];

            System.Net.IPAddress addr;

            // Address can only be used with networkConfig 'client', and will be ignored if otherwise
            if (settings.networkConfig != Settings.NETWORK_CONFIG_CLIENT)
                Debug.LogWarning(this.GetType().Name + " : Key \"" + key + "\" can only be used in a client configuration (e.g. \"-config client\")");

            // Ensure that the value is present
            else if (string.IsNullOrEmpty(value))
                Debug.LogWarning(this.GetType().Name + " : Key \"" + key + "\" requires a value");

            // Ensure that the value is a valid IP address
            else if (!System.Net.IPAddress.TryParse(value, out addr))
                Debug.LogError(this.GetType().Name + " : Value \"" + value + "\" for key \"" + key + "\" is invalid (must be an IPv4 dotted-quad)");
            
            else
                settings.serverAddress = addr.ToString();
        }

        // ARG_SERVER_PORT
        if (data.ContainsKey(ARG_SERVER_PORT))
        {
            var key = ARG_SERVER_PORT;
            var value = data[ARG_SERVER_PORT];

            int port;

            // Default if the value is empty or if the value is "default"
            if (!string.IsNullOrEmpty(value) && value != "default")
            {
                // Ensure that the value is a valid port
                if (System.Int32.TryParse(value, out port) && (port <= 0 || port > 65535))
                    Debug.LogError(this.GetType().Name + " : Value \"" + value + "\" for key \"" + key + "\" is invalid (must be an integer in the range 0-65535, exclusive)");
                else
                    settings.serverPort = port;
            }
            else
                settings.serverPort = Settings.DEFAULT_SERVER_PORT;
        }

        // Default serverPort
        if (settings.serverPort == 0)
            settings.serverPort = Settings.DEFAULT_SERVER_PORT;

        // ARG_BROADCAST_PORT
        if (data.ContainsKey(ARG_BROADCAST_PORT))
        {
            var key = ARG_BROADCAST_PORT;
            var value = data[ARG_BROADCAST_PORT];

            int port = 0;

            // A value for this key is not required, but if included - make sure it's a valid port
            if (!string.IsNullOrEmpty(value) && value != "default")
            {
                if (!System.Int32.TryParse(value, out port) || (port <= 0 || port > 65535))
                    Debug.LogError(this.GetType().Name + " : Value \"" + value + "\" for key \"" + key + "\" is invalid (must be an integer in the range 0-65535, exclusive)");
                else
                    settings.broadcastPort = port;
            }
            else
                settings.broadcastPort = Settings.DEFAULT_BROADCAST_PORT;

            // Listening in client configuration
            if (settings.networkConfig == Settings.NETWORK_CONFIG_CLIENT)
            {
                // Ignore if we have already specified an address
                if (!string.IsNullOrEmpty(settings.serverAddress))
                {
                    Debug.LogWarning(this.GetType().Name + " : Cannot listen for a server if a direct-connect address is already specified");
                    settings.broadcastPort = 0;
                }
            }

            // Broadcasting in serverhost configuration
            else if (settings.networkConfig == Settings.NETWORK_CONFIG_HOST ||
                     settings.networkConfig == Settings.NETWORK_CONFIG_SERVER)
            {
                // Set the serverAddress to the local one, although this is not taken into account at all
                settings.serverAddress = Network.player.ipAddress;
            }
        }

        // ARG_GAME_MODE
        if (data.ContainsKey(ARG_GAME_MODE))
        {
            var key = ARG_GAME_MODE;
            var value = data[ARG_GAME_MODE];

            // Ensure that the value is present
            if (string.IsNullOrEmpty(value))
                Debug.LogError(this.GetType().Name + " : Key \"" + key + "\" requires a value");

            // Ensure that the value is valid
            else if (value != Settings.GAME_MODE_SINGLEPLAYER && value != Settings.GAME_MODE_MULTIPLAYER)
                Debug.LogError(this.GetType().Name + " : Value \"" + value + "\" for key \"" + key + "\" is invalid! Must be one of the following (\"" + Settings.GAME_MODE_SINGLEPLAYER + "\" or \"" + Settings.GAME_MODE_MULTIPLAYER + "\")");
            else
                settings.gameMode = value;
        }
        else
            settings.gameMode = Settings.DEFAULT_GAME_MODE;

        // ARG_META_ENABLED
        if (data.ContainsKey(ARG_META_ENABLED))
        {
            if (!bool.TryParse(data[ARG_META_ENABLED], out settings.metaEnabled))
            {
                settings.metaEnabled = false;
                Debug.LogError(this.GetType().Name + " : Value \"" + data[ARG_META_ENABLED] + "\" is invalid for meta_enabled. Needs to be true/false");
            }
        }

        // If the port has not been set, default it
        if (settings.serverPort <= 0)
            settings.serverPort = Settings.DEFAULT_SERVER_PORT;

        // If running in single player mode, in a client, set server address to the loopback address
        if (settings.networkConfig == Settings.NETWORK_CONFIG_CLIENT && settings.gameMode == Settings.GAME_MODE_SINGLEPLAYER)
        {
            Debug.Log(this.GetType().Name + " : Setting address to loopback address (127.0.0.1) in single player mode!");
            settings.serverAddress = "127.0.0.1";
        }

        // If we don't have a network address, and we aren't broadcasting - fall back to using network discovery
        if (settings.networkConfig == Settings.NETWORK_CONFIG_CLIENT && !settings.UsesDirectConnect && !settings.UsesNetworkDiscovery)
        {
            settings.broadcastPort = Settings.DEFAULT_BROADCAST_PORT;
        }

        return settings;
    }
}