using UnityEngine;
using System.Collections.Generic;

public class CommandLineArgsProvider : SettingsProvider
{
    /*
        - Direct connect (as client only)
            MetaFour.exe --config client --address 192.168.1.2 --port 4444

        - Direct host (as server/host)
            MetaFour.exe --config server --port 4444

        - Listen for servers (as client only), listen port is optional (will default if omitted). In this case, since a server connection port is not provided, we will use the default port
            MetaFour.exe --config client --broadcast_port 5555

        - Broadcast to potential clients (as server/host), broadcast port is optional (will default if omitted)
            MetaFour.exe --config host --broadcast_port
    */

    public override bool IsProvided()
    {
        // CommandLineArgs are never valid in Unity Editor
        return !Application.isEditor && System.Environment.GetCommandLineArgs().Length > 1;
    }

    public override SettingsData GetSettingsData()
    {
        var arguments = GetCommandLineArgs();
        return Parse(arguments);
    }

    // Parse configuration options from command-line arguments
    Dictionary<string, string> GetCommandLineArgs()
    {
        var args = System.Environment.GetCommandLineArgs();
        var arguments = new Dictionary<string, string>();

        string lastKey = "";

        Debug.Log("CommandLineArgsProvider : Parsing the following argument string\n " + string.Join(" ", args));

        // Convert the args string[] to a Dictionary, where they key is "-key", and the value is whatever is after it, and before the next "-key"
        // Start from index 1, as 0 will always contain the executable name
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];

            // Add this arg as a key, on the next loop we will add the value (parameter passed with the arg)
            if (arg.StartsWith("-") || arg.StartsWith("--") || arg.StartsWith("/"))
            {
                arg = arg.TrimStart(new char[] {'-', '/'});

                // Ensure that there isn't already an element in the Dictionary with this key
                if (arguments.ContainsKey(arg))
                {
                    Debug.LogError("CommandLineArgsProvider : Argument \"" + arg + "\" is duplicated! Will ignore...");
                    continue;
                }
                else
                {
                    Debug.Log("CommandLineArgsProvider : Added key \"" + arg + "\"");
                    lastKey = arg;
                    arguments.Add(arg, "");
                }
            }

            // Add this arg as a value to the last-added key, if it doesn't already have one
            else if (!string.IsNullOrEmpty(lastKey) && string.IsNullOrEmpty(arguments[lastKey]))
            {
                arguments[lastKey] = arg;
                Debug.Log("CommandLineArgsProvider : Added value \"" + arg + "\" to the key \"" + lastKey + "\"");
            }

            // Unexpected value
            else
            {
                Debug.LogError("CommandLineArgsProvider : Unexpected value \"" + arg + "\" in command-line args should either follow a flag/key, or be prefixed with a '-'");
            }
        }

        return arguments;
    }
}