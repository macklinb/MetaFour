public class DefaultProvider : SettingsProvider
{
    public override bool IsProvided()
    {
        return true;
    }

    public override SettingsData GetSettingsData()
    {
        return new SettingsData()
        {
            networkConfig = Settings.DEFAULT_NETWORK_CONFIG,
            serverPort = Settings.DEFAULT_SERVER_PORT,
            broadcastPort = Settings.DEFAULT_BROADCAST_PORT,
            gameMode = Settings.DEFAULT_GAME_MODE,
            metaEnabled = MetaDisabler.DEFAULT_META_ENABLED,
        };
    }
}