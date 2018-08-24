using System.IO;
using System.Collections.Generic;

// Loads settings from a configuration file
// This also works in editor, as long as the config file is placed in project root
public class ConfigFileProvider : SettingsProvider
{
    const string fileName = "config.ini";
    
    public override bool IsProvided()
    {
        return File.Exists(fileName);
    }

    public override SettingsData GetSettingsData()
    {
        var arguments = new Dictionary<string, string>();

        string line = "";
        string key, value;
        int splitIndex = -1;

        using (var stream = File.OpenText(fileName))
        {
            while ((line = stream.ReadLine()) != null)
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith(";") || !line.Contains("="))
                    continue;

                splitIndex = line.IndexOf('=');

                // Split the line at the equals mark
                key = line.Substring(0, splitIndex);
                value = line.Substring(splitIndex + 1);

                // Remove leading and trailing quotations
                if (!string.IsNullOrEmpty(value))
                    value = value.Trim('"');

                arguments.Add(key, value);
            }
        }

        return Parse(arguments);
    }
}